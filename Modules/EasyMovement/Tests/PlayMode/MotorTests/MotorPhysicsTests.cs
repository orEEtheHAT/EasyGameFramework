using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EasyCombat;
using EasyMovement.Integrations;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyMovement.MotorTests
{
    public sealed class MotorOwner : MonoBehaviour { }

    public sealed class MotorPhysicsTests
    {
        Scene scene;
        PhysicsScene physics;
        RigidbodyMotor motor;
        Rigidbody body;
        Action tick;
        readonly List<Object> assets = new List<Object>();
        float previousStep;
        const float Step = .02f;

        [SetUp] public void SetUp()
        {
            previousStep = Time.fixedDeltaTime; Time.fixedDeltaTime = Step;
            scene = SceneManager.CreateScene("Motor isolated physics " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physics = scene.GetPhysicsScene();
            motor = NewMotor("motor", Vector3.zero); body = motor.Body; tick = Tick(motor);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            Time.fixedDeltaTime = previousStep;
            foreach (var asset in assets) if (asset) Object.DestroyImmediate(asset);
            assets.Clear();
            var unload = SceneManager.UnloadSceneAsync(scene);
            while (!unload.isDone) yield return null;
        }
        T Asset<T>() where T : ScriptableObject { var a = ScriptableObject.CreateInstance<T>(); assets.Add(a); return a; }
        GameObject Go(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        RigidbodyMotor NewMotor(string name, Vector3 position)
        {
            var go = Go(name); go.transform.position = position;
            go.AddComponent<BoxCollider>(); var m = go.AddComponent<RigidbodyMotor>();
            m.Body.useGravity = false; m.Body.linearDamping = 0; m.Body.angularDamping = 0; m.Body.constraints = RigidbodyConstraints.FreezeRotation;
            return m;
        }
        static Action Tick(object component) => (Action)Delegate.CreateDelegate(typeof(Action), component,
            component.GetType().GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance));
        void Advance(int steps, Action before = null)
        {
            for (int i = 0; i < steps; ++i) { before?.Invoke(); tick(); physics.Simulate(Step); }
        }
        [Test] public void ForwardReverseBrakeAndMassUseBoundedAcceleration()
        {
            motor.SetThrottle(1); Advance(1); Assert.That(motor.CurrentSpeed, Is.EqualTo(.12f).Within(.001f));
            Advance(99); Assert.That(motor.CurrentSpeed, Is.EqualTo(8).Within(.01f));
            motor.SetThrottle(-1); Advance(20); Assert.That(motor.CurrentSpeed, Is.EqualTo(4).Within(.02f));
            Advance(80); Assert.That(motor.CurrentSpeed, Is.EqualTo(-4).Within(.02f));
            motor.SetBrake(.5f); Advance(20); Assert.That(motor.CurrentSpeed, Is.EqualTo(-2).Within(.02f));
            Advance(25); Assert.That(motor.CurrentSpeed, Is.EqualTo(0).Within(.001f));
            motor.ResetInput(); body.mass = 37; motor.SetThrottle(1); Advance(1);
            Assert.That(motor.CurrentSpeed, Is.EqualTo(.12f).Within(.001f));
        }
        [Test] public void FractionalThrottleAndLocalDirectionAreRespected()
        {
            motor.LocalDirection = Vector3.right; motor.transform.rotation = Quaternion.Euler(0, 90, 0);
            motor.SetThrottle(.5f); Advance(100);
            Assert.That(motor.CurrentSpeed, Is.EqualTo(4).Within(.01f));
            Assert.That(body.linearVelocity.z, Is.EqualTo(-4).Within(.01f));
            Assert.That(body.linearVelocity.x, Is.EqualTo(0).Within(.001f));
            Assert.Throws<ArgumentException>(() => motor.LocalDirection = Vector3.zero);
        }
        [Test] public void CoastingAndExternalSideImpulseRemainPhysical()
        {
            motor.SetThrottle(1); Advance(35); motor.ResetInput();
            float forward = motor.CurrentSpeed;
            body.AddForce(Vector3.right * 3, ForceMode.VelocityChange); Advance(50);
            Assert.That(motor.CurrentSpeed, Is.EqualTo(forward).Within(.001f));
            Assert.That(body.linearVelocity.x, Is.EqualTo(3).Within(.001f));
            var profile = Asset<RigidbodyMotorProfile>(); profile.CoastingDeceleration = 2;
            motor.Profile = profile; Advance(25);
            Assert.That(motor.CurrentSpeed, Is.EqualTo(forward - 1).Within(.01f));
            Assert.That(body.linearVelocity.x, Is.EqualTo(3).Within(.001f));
        }
        [Test] public void ChangingCapsBrakesGraduallyEvenWhileCoastingAndZeroDoesNotBlockFalling()
        {
            motor.SetThrottle(1); Advance(100); motor.ResetInput();
            var factor = motor.AcquireSpeedMultiplier(Go("slow owner"), .5f);
            Advance(1); Assert.That(motor.CurrentSpeed, Is.EqualTo(7.8f).Within(.01f));
            Advance(25); Assert.That(motor.CurrentSpeed, Is.EqualTo(4).Within(.01f));
            body.useGravity = true; factor.TrySet(0); Advance(1);
            Assert.That(motor.CurrentSpeed, Is.EqualTo(3.8f).Within(.01f));
            Advance(25); Assert.That(motor.CurrentSpeed, Is.EqualTo(0).Within(.01f));
            Assert.That(body.linearVelocity.y, Is.LessThan(-4.5f)); Assert.That(body.position.y, Is.LessThan(-1));
            Assert.IsTrue(factor.TrySet(2)); motor.SetThrottle(1); Advance(160);
            Assert.That(motor.CurrentSpeed, Is.EqualTo(16).Within(.02f));
        }
        [Test] public void CollisionAndGravityAreSolvedByRigidbody()
        {
            var wall = Go("wall"); wall.transform.position = new Vector3(0, 0, 3); wall.transform.localScale = new Vector3(8, 8, 1); wall.AddComponent<BoxCollider>();
            motor.SetThrottle(1); Advance(100);
            Assert.That(body.position.z, Is.LessThan(2.1f));
            var floor = Go("floor"); floor.transform.position = new Vector3(0, -3, 0); floor.transform.localScale = new Vector3(20, 1, 20); floor.AddComponent<BoxCollider>();
            body.useGravity = true; motor.ResetInput(); Advance(100);
            Assert.That(body.position.y, Is.EqualTo(-2).Within(.04f));
        }
        [Test] public void ModifierOwnershipDisableDestroyAndPoolingExpireOnlyTheirOwnHandles()
        {
            var a = Go("a").AddComponent<MotorOwner>(); var b = Go("b").AddComponent<MotorOwner>();
            var first = motor.AcquireSpeedMultiplier(a, .5f); var second = motor.AcquireSpeedMultiplier(b, 2);
            Assert.That(motor.SpeedMultiplier, Is.EqualTo(1).Within(.0001f));
            first.Dispose(); first.Dispose(); Assert.That(motor.SpeedMultiplier, Is.EqualTo(2));
            a.enabled = true; first = motor.AcquireSpeedMultiplier(a, .5f); a.enabled = false;
            Assert.That(motor.SpeedMultiplier, Is.EqualTo(2)); Assert.IsFalse(first.IsActive);
            Object.DestroyImmediate(b); Assert.That(motor.SpeedMultiplier, Is.EqualTo(1)); Assert.IsFalse(second.IsActive);
            a.enabled = true; first = motor.AcquireSpeedMultiplier(a, .3f);
            motor.SetThrottle(1); Advance(10); float speed = motor.CurrentSpeed;
            motor.enabled = false; Assert.IsFalse(first.IsActive); Assert.AreEqual(0, motor.Throttle);
            Assert.That(body.linearVelocity.z, Is.EqualTo(speed)); motor.enabled = true;
            Assert.AreEqual(1, motor.SpeedMultiplier); Assert.AreEqual(0, motor.Throttle);
            motor.gameObject.SetActive(false); Assert.IsFalse(motor.AcquireSpeedMultiplier(a, .5f).IsActive);
        }
        [Test] public void NonfiniteInputInvalidProfileKinematicAndProfileSwapDoNotInjectVelocity()
        {
            motor.SetThrottle(float.NaN); motor.SetBrake(float.PositiveInfinity); Assert.AreEqual(0, motor.Throttle); Assert.AreEqual(0, motor.BrakeInput);
            var owner = Go("owner"); var h = motor.AcquireSpeedMultiplier(owner, .5f);
            Assert.IsFalse(h.TrySet(float.NaN)); Assert.IsFalse(h.TrySet(-1)); Assert.AreEqual(.5f, h.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => motor.AcquireSpeedMultiplier(owner, float.PositiveInfinity));
            body.isKinematic = true; motor.SetThrottle(1); Advance(1); Assert.AreEqual(0, motor.Throttle);
            body.isKinematic = false; Advance(1); Assert.That(body.linearVelocity.sqrMagnitude, Is.EqualTo(0));
            var profile = Asset<RigidbodyMotorProfile>(); profile.Acceleration = float.NaN;
            motor.Profile = profile; motor.SetThrottle(1); Advance(5); Assert.AreEqual(0, motor.Throttle); Assert.AreEqual(Vector3.zero, body.linearVelocity);
            profile.Acceleration = 12; motor.SetThrottle(1); Advance(1); Assert.That(motor.CurrentSpeed, Is.EqualTo(.24f).Within(.001f));
            motor.Profile = null; Assert.AreEqual(0, motor.Throttle); Assert.That(motor.CurrentSpeed, Is.EqualTo(.24f).Within(.001f)); Assert.AreEqual(.5f, motor.SpeedMultiplier);
        }
        [Test] public void CombatStatusSpatialOverlapExpiryDisableAndResetComposeOnce()
        {
            var statuses = motor.gameObject.AddComponent<StatusController>(); var effects = motor.gameObject.AddComponent<MovementInfluences>();
            var adapter = motor.gameObject.AddComponent<RigidbodyCombatSpeed>();
            var stat = Asset<CombatStatDefinition>(); effects.MovementSpeedStat = stat;
            var slow = Asset<MovementInfluenceDefinition>(); slow.SpeedMultiplier = .5f;
            var weaker = Asset<MovementInfluenceDefinition>(); weaker.SpeedMultiplier = .8f;
            var source = Go("field").AddComponent<MotorOwner>(); var other = Go("other").AddComponent<MotorOwner>();
            effects.Set(source, slow); effects.Set(other, weaker); adapter.Refresh(); Assert.AreEqual(.5f, motor.SpeedMultiplier);
            var haste = Asset<StatusDefinition>(); haste.Duration = .4f; haste.StatModifiers = new[] { new StatusStatModifier(stat, 3) };
            Assert.IsTrue(statuses.Apply(haste)); adapter.Refresh(); Assert.That(motor.SpeedMultiplier, Is.EqualTo(1.5f).Within(.001f));
            motor.SetThrottle(1); Advance(120, adapter.Refresh); Assert.That(motor.CurrentSpeed, Is.EqualTo(12).Within(.02f));
            statuses.Advance(.5f); adapter.Refresh(); Assert.AreEqual(.5f, motor.SpeedMultiplier);
            effects.Release(source); adapter.Refresh(); Assert.That(motor.SpeedMultiplier, Is.EqualTo(.8f).Within(.001f));
            effects.enabled = false; adapter.Refresh(); Assert.AreEqual(1, motor.SpeedMultiplier);
            effects.enabled = true; effects.Set(source, slow); adapter.Refresh(); Assert.AreEqual(.5f, motor.SpeedMultiplier);
            statuses.Health.ResetToFull(); adapter.Refresh(); Assert.AreEqual(1, motor.SpeedMultiplier);
            var independent = motor.AcquireSpeedMultiplier(other, .25f);
            effects.Set(source, slow); adapter.Refresh(); Assert.AreEqual(.125f, motor.SpeedMultiplier);
            adapter.enabled = false; Assert.AreEqual(.25f, motor.SpeedMultiplier);
            adapter.enabled = true; Assert.AreEqual(.125f, motor.SpeedMultiplier);
            motor.gameObject.SetActive(false); motor.gameObject.SetActive(true); adapter.Refresh();
            Assert.AreEqual(1, motor.SpeedMultiplier); Assert.IsFalse(independent.IsActive); Assert.AreEqual(0, statuses.Count);
            var stop = Asset<StatusDefinition>(); stop.StatModifiers = new[] { new StatusStatModifier(stat, 0) };
            Assert.IsTrue(statuses.Apply(stop)); adapter.Refresh(); Assert.AreEqual(0, motor.SpeedMultiplier);
        }
        [Test] public void ActualTriggerVolumesReleaseOnExitDisableAndDestruction()
        {
            var effects = motor.gameObject.AddComponent<MovementInfluences>(); var adapter = motor.gameObject.AddComponent<RigidbodyCombatSpeed>();
            MovementSurface Volume(string label, float multiplier)
            {
                var go = Go(label); var collider = go.AddComponent<BoxCollider>(); collider.isTrigger = true; collider.size = Vector3.one * 4;
                var surface = go.AddComponent<MovementSurface>(); var p = Asset<MovementInfluenceDefinition>(); p.SpeedMultiplier = multiplier; surface.Effect = p; return surface;
            }
            var a = Volume("half", .5f); var b = Volume("quarter", .25f);
            Advance(2, adapter.Refresh); adapter.Refresh(); Assert.AreEqual(.25f, motor.SpeedMultiplier);
            b.enabled = false; adapter.Refresh(); Assert.AreEqual(.5f, motor.SpeedMultiplier);
            Object.DestroyImmediate(a.gameObject); adapter.Refresh(); Assert.AreEqual(1, motor.SpeedMultiplier);
            b.enabled = true; Advance(2); adapter.Refresh(); Assert.AreEqual(.25f, motor.SpeedMultiplier);
            body.AddForce(Vector3.right * 10, ForceMode.VelocityChange); Advance(25); adapter.Refresh();
            Assert.That(body.position.x, Is.GreaterThan(3)); Assert.AreEqual(1, motor.SpeedMultiplier);
        }
        [Test] public void CombatRebindingReleasesOnlyItsHandleAndNeverEditsSharedProfile()
        {
            var profile = Asset<RigidbodyMotorProfile>();
            motor.Profile = profile;
            var second = NewMotor("second motor", Vector3.right * 10); second.Profile = profile;
            string authored = JsonUtility.ToJson(profile);
            var adapter = motor.gameObject.AddComponent<RigidbodyCombatSpeed>();
            var effects = motor.GetComponent<MovementInfluences>();
            var slow = Asset<MovementInfluenceDefinition>(); slow.SpeedMultiplier = .5f;
            var source = Go("slow source").AddComponent<MotorOwner>(); effects.Set(source, slow);
            var independent = motor.AcquireSpeedMultiplier(source, 2);
            adapter.Refresh(); Assert.AreEqual(1, motor.SpeedMultiplier);
            adapter.Motor = second;
            Assert.AreEqual(2, motor.SpeedMultiplier); Assert.AreEqual(.5f, second.SpeedMultiplier);
            second.SetThrottle(1); Tick(second)(); physics.Simulate(Step);
            Assert.That(second.CurrentSpeed, Is.EqualTo(.12f).Within(.001f), "Speed caps change, not acceleration.");
            Assert.AreEqual(4, second.EffectiveForwardSpeed);
            Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(authored));
            adapter.enabled = false; Assert.AreEqual(1, second.SpeedMultiplier);
            Assert.IsTrue(independent.IsActive); Assert.AreEqual(2, motor.SpeedMultiplier);
        }
        [Test] public void WarmMotorAndCombatDispatchHaveCalibratedZeroMainThreadAllocationEvents()
        {
            var adapter = motor.gameObject.AddComponent<RigidbodyCombatSpeed>();
            var owners = new[] { Go("slow owner"), Go("haste owner") };
            motor.AcquireSpeedMultiplier(owners[0], .5f); motor.AcquireSpeedMultiplier(owners[1], 2);
            motor.SetThrottle(1); Advance(10, adapter.Refresh);
            Action measured = () => { adapter.Refresh(); tick(); };
            Action control = () => GC.KeepAlive(new byte[512]);
            bool prior = UnityEngine.Profiling.Profiler.enabled; UnityEngine.Profiling.Profiler.enabled = true;
            long positive, idle;
            try { Count(control, 1); Count(measured, 10); positive = Count(control, 1); idle = Count(measured, 1000); }
            finally { UnityEngine.Profiling.Profiler.enabled = prior; }
            Assert.AreEqual(1, positive, "Positive GC control must detect the allocation."); Assert.AreEqual(0, idle, "Warm motor + adapter managed dispatch only.");
        }
        [Test] public void TwoHundredFiftySixBodiesRunBoundedPhysicsBatch()
        {
            const int count = 256; var motors = new RigidbodyMotor[count]; var ticks = new Action[count];
            motors[0] = motor; ticks[0] = tick;
            for (int i = 1; i < count; ++i) { motors[i] = NewMotor("batch", new Vector3((i % 16) * 3, (i / 16) * 3, 0)); ticks[i] = Tick(motors[i]); }
            foreach (var m in motors) m.SetThrottle(1);
            for (int n = 0; n < 20; ++n) { foreach (var t in ticks) t(); physics.Simulate(Step); }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int n = 0; n < 100; ++n) { foreach (var t in ticks) t(); physics.Simulate(Step); }
            timer.Stop(); foreach (var m in motors) Assert.That(m.CurrentSpeed, Is.EqualTo(8).Within(.02f));
            TestContext.WriteLine("256 independent bodies, 100 local physics steps: " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms; Editor observation, not target-device performance.");
            string report = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Audits/EasyMovement/Unification/physics-observation.json"));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(report));
            System.IO.File.WriteAllText(report, JsonUtility.ToJson(new PhysicsObservation { Unity = Application.unityVersion, Bodies = count, Steps = 100, FixedDeltaTime = Step, Milliseconds = timer.Elapsed.TotalMilliseconds }, true));
        }
        [Serializable] sealed class PhysicsObservation
        {
            public string Scope = "Editor observation: 256 independent bodies, managed motor dispatch plus local PhysicsScene.Simulate; not a target-device guarantee or whole-game benchmark.";
            public string Unity; public int Bodies, Steps; public float FixedDeltaTime; public double Milliseconds;
        }
        static long Count(Action action, int count)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (int i = 0; i < count; ++i) action();
            recorder.Stop(); if (!recorder.Valid) throw new InvalidOperationException("GC.Alloc recorder unavailable.");
            return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
        }
    }
}
