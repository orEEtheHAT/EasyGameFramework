using System.Collections.Generic;
using EasyCombat.Integrations;
using EasyFeedbacks;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EasyCombat.IntegrationTests
{
    public sealed class RecordingFeedbackContext : MonoBehaviour,IFeedbackContextProvider
    {
        public readonly List<FeedbackContext> Received=new List<FeedbackContext>();
        public FeedbackContext Prepare(FeedbackContext context) { Received.Add(context); return context; }
    }

    public sealed class FeedbackContextTests
    {
        private readonly List<Object> cleanup=new List<Object>();
        private T Asset<T>() where T:ScriptableObject { var asset=ScriptableObject.CreateInstance<T>(); cleanup.Add(asset); return asset; }
        private GameObject Actor() { var actor=new GameObject("Feedback owner"); cleanup.Add(actor); return actor; }
        [TearDown] public void Cleanup() { for(var i=cleanup.Count-1;i>=0;i--) if(cleanup[i]!=null) Object.DestroyImmediate(cleanup[i]); cleanup.Clear(); }

        [Test] public void HealthFeedbacksPreparesDamageHealingAndDeathWithOriginalContext()
        {
            var actor=Actor(); actor.transform.position=new Vector3(4,5,6);
            var health=actor.AddComponent<Health>(); var provider=actor.AddComponent<RecordingFeedbackContext>();
            var feedback=actor.AddComponent<HealthFeedbacks>(); feedback.ContextProvider=provider;
            feedback.Damage=feedback.Death=feedback.Healing=Asset<FeedbackPreset>();
            var impact=new Vector3(1,2,3);
            health.ApplyDamage(new DamageInfo(10,point:impact)); health.Heal(5); health.ApplyDamage(new DamageInfo(500,point:impact));
            Assert.That(provider.Received.Count,Is.EqualTo(3));
            Assert.That(provider.Received[0].WorldPosition,Is.EqualTo(impact));
            Assert.That(provider.Received[1].WorldPosition,Is.EqualTo(actor.transform.position));
            Assert.That(provider.Received[2].Source,Is.EqualTo(actor.transform));
            foreach(var context in provider.Received) { Assert.That(context.Intensity,Is.EqualTo(1)); Assert.That(context.Follow,Is.Null); }
        }

        [Test] public void PresentationFeedbacksPreparesCachedPointAndIntensityWithoutFollowingPooledActor()
        {
            var actor=Actor(); var provider=actor.AddComponent<RecordingFeedbackContext>();
            var hub=actor.AddComponent<CombatPresentationHub>(); hub.AutomaticFlush=false;
            var rules=Asset<CombatPresentationDefinition>(); rules.Rules=new[] { new CombatPresentationRule(CombatPresentationKind.Damage) };
            Assert.That(hub.Configure(rules),Is.True);
            var feedback=actor.AddComponent<PresentationFeedbacks>(); feedback.ContextProvider=provider; feedback.Hub=hub;
            feedback.Bindings=new[] { new PresentationFeedbacks.Binding { Kind=CombatPresentationKind.Damage,Preset=Asset<FeedbackPreset>(),ReferenceAmount=10 } };
            feedback.Rebind(); var point=new Vector3(7,8,9);
            var value=new CombatPresentationEvent(CombatPresentationKind.Damage,default,default,point,20,direction:Vector3.right);
            Assert.That(hub.Submit(in value),Is.True); hub.Flush();
            Assert.That(provider.Received.Count,Is.EqualTo(1)); var context=provider.Received[0];
            Assert.That(context.WorldPosition,Is.EqualTo(point)); Assert.That(context.Intensity,Is.EqualTo(2));
            Assert.That(context.Source,Is.Null); Assert.That(context.Follow,Is.Null);
            Assert.That(Vector3.Distance(context.Rotation*Vector3.forward,Vector3.right),Is.LessThan(.0001f));
        }
    }
}
