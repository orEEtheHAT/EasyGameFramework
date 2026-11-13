using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using EasyCombat;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;
#pragma warning disable CS0618 // Intentionally pin autoSync for the same batched physics path in both versions.

// Identical CLI eval source for the pre-cutover baseline and framework final.
// Run synchronously in Play mode in an empty scene. This never saves project assets.
if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode in an empty scene first.");
if (Object.FindObjectsByType<Health>(FindObjectsSortMode.None).Length != 0 ||
    Object.FindObjectsByType<DamageProjectile>(FindObjectsSortMode.None).Length != 0 ||
    Object.FindObjectsByType<DamageArea>(FindObjectsSortMode.None).Length != 0)
    throw new InvalidOperationException("Use an empty scene: the shared combat scheduler must contain only this fixture.");
const int warmup = 60, steps = 180, repeats = 3;
const float dt = 1f / 60f;
var rows = new List<object>();
bool originalSync = Physics.autoSyncTransforms, originalProfiler = Profiler.enabled;
try
{
    Physics.autoSyncTransforms = false;
    foreach (bool large in new[] { false, true })
    for (int repetition = 0; repetition < repeats; repetition++)
    // Alternate order to expose scheduling/JIT/order bias.
    foreach (var mode in repetition % 2 == 0
        ? new[] { ProjectileExecutionMode.SingleThread, ProjectileExecutionMode.Parallel }
        : new[] { ProjectileExecutionMode.Parallel, ProjectileExecutionMode.SingleThread })
    {
        int lanes = large ? 16 : 2, ranks = large ? 4 : 2, perStep = large ? 64 : 4;
        var cleanup = new List<Object>();
        var priorScheduler = Object.FindFirstObjectByType<ProjectileBatchScheduler>();
        ProjectileBatchScheduler scheduler = null;
        var center = new Vector3(0, -10000, 0);
        GameObject Go(string name) { var go = new GameObject(name); cleanup.Add(go); return go; }
        T So<T>() where T : ScriptableObject { var so = ScriptableObject.CreateInstance<T>(); cleanup.Add(so); return so; }
        long damageEvents=0, periodicEvents=0, splashEvents=0, fieldEvents=0, acceptedShots=0;
        try
        {
            var clock = new ManualCombatClock();
            using var scope = new CombatExecutionScope(new SeededCombatRandom(3263),clock,true);
            var dot = So<PeriodicDamageEffect>(); dot.DamagePerStack = .25f;
            var status = So<StatusDefinition>(); status.Duration = .5f; status.TickInterval = .1f;
            status.MaximumStacks = 2; status.Stacking = StatusStacking.RefreshDuration; status.TickEffects = new StatusTickEffect[] { dot };
            var shape = So<AreaDefinition>(); shape.Radius = 2.5f; shape.TargetMask = 1 << 10;
            var projectile = So<AttackDefinition>(); projectile.Delivery = DamageDelivery.Projectile;
            projectile.Damage = 1; projectile.RoundsPerMinute = 0; projectile.ProjectileSpeed = 60;
            projectile.ProjectileLifetime = 1; projectile.Range = 60; projectile.HitMask = 1 << 10;
            projectile.OnHitStatuses = new[] { new StatusApplication(status) };
            var hitscan = So<AttackDefinition>(); hitscan.Damage=1; hitscan.RoundsPerMinute=0; hitscan.Range=30; hitscan.HitMask=1<<10;
            hitscan.OnHitStatuses = new[] { new StatusApplication(status) };
            var splash = So<AttackDefinition>(); splash.Delivery=DamageDelivery.Area; splash.Damage=.5f;
            splash.RoundsPerMinute=0; splash.Area=shape; splash.OnHitStatuses=new[] { new StatusApplication(status) };
            var fieldAttack = So<AttackDefinition>(); fieldAttack.Delivery=DamageDelivery.Area; fieldAttack.Damage=.2f; fieldAttack.Area=shape;
            fieldAttack.OnHitStatuses=new[] { new StatusApplication(status) };
            var pulses = So<AreaPulseDefinition>(); pulses.Attack=fieldAttack; pulses.Duration=10000; pulses.Interval=.1f; pulses.ApplyImmediately=false;
            var targets=new Health[lanes*ranks]; var statuses=new StatusController[targets.Length];
            var positions=new Vector3[targets.Length];
            for(int i=0;i<targets.Length;i++)
            {
                var target=Go("Mixed benchmark target"); target.layer=10;
                positions[i]=center+new Vector3(i%lanes*4,0,3+i/lanes*2); target.transform.position=positions[i];
                target.AddComponent<BoxCollider>().size=new Vector3(.8f,1,.8f);
                targets[i]=target.AddComponent<Health>(); targets[i].SetMaximum(1000000,true);
                statuses[i]=target.AddComponent<StatusController>();
                targets[i].Damaged += result => { damageEvents++; if(result.Damage.IsPeriodic) periodicEvents++; if(result.Damage.Origin.Attack==splash) splashEvents++; if(result.Damage.Origin.Attack==fieldAttack) fieldEvents++; };
            }
            var fields=new DamageArea[lanes];
            for(int i=0;i<fields.Length;i++) { fields[i]=Go("Mixed benchmark field").AddComponent<DamageArea>(); fields[i].transform.position=positions[i]; if(!fields[i].Begin(pulses)) throw new InvalidOperationException("Invalid field fixture."); }
            var emitter=Go("Mixed benchmark emitter").AddComponent<DamageEmitter>();
            int capacity=perStep*8;
            var shots=new DamageProjectile[capacity];
            for(int i=0;i<capacity;i++) shots[i]=Go("Mixed benchmark projectile").AddComponent<DamageProjectile>();
            Action<DamageProjectile> release = shot => { };
            shots[0].Launch(center,Vector3.forward,projectile.RollRandom(null,center,Vector3.forward),null,projectile,null,release);
            scheduler=Object.FindFirstObjectByType<ProjectileBatchScheduler>();
            if(!scheduler) throw new InvalidOperationException("Projectile scheduler was not registered.");
            var method=typeof(ProjectileBatchScheduler).GetMethod("Advance",BindingFlags.Instance|BindingFlags.NonPublic);
            var advance=(Action<float,ProjectileExecutionMode>)method.CreateDelegate(typeof(Action<float,ProjectileExecutionMode>),scheduler);
            shots[0].Despawn(); int frame=0, cursor=0;
            Action work = () =>
            {
                frame++; clock.Advance(dt);
                for(int i=0;i<targets.Length;i++) targets[i].transform.position=positions[i]+Vector3.right*(.15f*Mathf.Sin(frame*.07f+i));
                Physics.SyncTransforms();
                for(int i=0;i<perStep;i++)
                {
                    var at=center+new Vector3(i%lanes*4,0,0);
                    var shot=shots[cursor++%capacity];
                    if(shot.IsFlying) throw new InvalidOperationException("Fixture projectile capacity exceeded.");
                    shot.Launch(at,Vector3.forward,projectile.RollRandom(null,at,Vector3.forward),null,projectile,null,release); acceptedShots++;
                    emitter.Attack=hitscan; if(!emitter.TryFire(at,Vector3.forward)) throw new InvalidOperationException("Hitscan rejected."); acceptedShots++;
                }
                emitter.Attack=splash;
                for(int i=0;i<lanes;i++) if(!emitter.TryFire(positions[i],Vector3.forward)) throw new InvalidOperationException("Splash rejected.");
                advance(dt,mode);
                foreach(var field in fields) field.Advance(dt);
                foreach(var controller in statuses) controller.Advance(dt);
            };
            for(int i=0;i<warmup;i++) work();
            damageEvents=periodicEvents=splashEvents=fieldEvents=acceptedShots=0;
            var timings=new double[steps];
            for(int i=0;i<steps;i++) { long start=Stopwatch.GetTimestamp(); work(); timings[i]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency; }
            double total=0; foreach(var ms in timings) total+=ms; Array.Sort(timings);
            long measuredDamage=damageEvents, measuredTicks=periodicEvents, measuredSplash=splashEvents, measuredFields=fieldEvents, measuredShots=acceptedShots;
            if(measuredTicks==0 || measuredSplash==0 || measuredFields==0 || measuredDamage<=measuredTicks+measuredSplash+measuredFields) throw new InvalidOperationException("A workload channel did no damage.");
            double healthChecksum=0; foreach(var target in targets) healthChecksum+=target.Current;
            Profiler.enabled=true;
            var options=ProfilerRecorderOptions.WrapAroundWhenCapacityReached|ProfilerRecorderOptions.SumAllSamplesInFrame|ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            long empty=-1, positive=-1, allocations=-1; var keep=new object[4];
            using(var recorder=new ProfilerRecorder(ProfilerCategory.Memory,"GC.Alloc",1,options))
            {
                long Sample(Action action) { recorder.Reset(); recorder.Start(); try { action(); } finally { recorder.Stop(); } return recorder.Count>0?recorder.GetSample(0).Count:0; }
                Action control=()=> { for(int i=0;i<keep.Length;i++) keep[i]=new byte[256+i]; };
                Action measured=()=> { for(int i=0;i<steps;i++) work(); };
                if(recorder.Valid) { Sample(control); empty=Sample(()=>{}); positive=Sample(control); if(empty==0 && positive==4) allocations=Sample(measured); }
            }
            rows.Add(new { workload=large?"mixed-large":"mixed-small",repetition,requestedMode=mode.ToString(),lanes,ranks,targets=targets.Length,projectilesPerStep=perStep,hitscansPerStep=perStep,splashesPerStep=lanes,fields=lanes,capacity,warmup,steps,dt,
                meanMilliseconds=total/steps,p95Milliseconds=timings[(int)Math.Ceiling(steps*.95)-1],healthChecksum,acceptedShots=measuredShots,damageEvents=measuredDamage,periodicEvents=measuredTicks,splashEvents=measuredSplash,fieldEvents=measuredFields,
                gcAllocationEvents=allocations,gcEmptyControl=empty,gcPositiveControl=positive,gcValid=allocations>=0 });
        }
        finally
        {
            for(int i=cleanup.Count-1;i>=0;i--) if(cleanup[i]) Object.DestroyImmediate(cleanup[i]);
            if(!priorScheduler && scheduler) Object.DestroyImmediate(scheduler.gameObject);
        }
    }
}
finally { Physics.autoSyncTransforms=originalSync; Profiler.enabled=originalProfiler; }
var burstType=Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst");
var burstOptions=burstType?.GetField("Options",BindingFlags.Public|BindingFlags.Static)?.GetValue(null)
    ?? burstType?.GetProperty("Options",BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
var burstEnabled=burstOptions?.GetType().GetProperty("EnableBurstCompilation")?.GetValue(burstOptions);
return new { fixture="CombatMixed-v1",unity=Application.unityVersion,device=SystemInfo.processorType,workers=Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
    burstEnabled,utc=DateTime.UtcNow.ToString("O"),seed=3263,
    scope="Play mode, synchronous bounded main-thread CPU steps with actual 3D physics queries, moving targets, scheduler jobs, hitscan, splash, status ticks and fields. Excludes rendering/UI/real frame pacing; not FPS. GC includes main-thread workload only.",rows };
