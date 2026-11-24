using EasyFeedbacks;
using EasyLocalization;
using EasyUI;
using TMPro;
using UnityEngine;

namespace EasyQuests.UI
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Notifications")]
    public sealed class QuestNotifications : MonoBehaviour
    {
        public QuestWorld world;
        public LocalizedCatalogCache translations;
        public TMP_Text label;
        public FeedbackPreset feedback;
        [Min(.1f)] public float seconds = 2.5f;
        private float until;
        private QuestTransition? pending;
        private string interruptedRun;

        private void OnEnable()
        {
            if(!world)return;
            world.Session.Transitioned+=Transition;
            world.Session.Restored+=Clear;
        }
        private void OnDisable()
        {
            if(world){world.Session.Transitioned-=Transition;world.Session.Restored-=Clear;}
            Clear();
        }
        private void Clear(){pending=null;interruptedRun=null;until=0;if(label)label.enabled=false;}
        private string T(string key,string fallback) => translations && !string.IsNullOrEmpty(key)?translations.Get(key,fallback):fallback;
        private void Transition(QuestTransition value)
        {
            if(value.Quest.hidden)return;
            if(value.Kind==QuestTransitionKind.StepInterrupted){interruptedRun=value.Run.Id;return;}
            if(value.Kind==QuestTransitionKind.StepEntered && value.Step.type==QuestStepType.Objectives &&
                System.Array.Exists(value.Step.goals,g=>!g.hidden) || value.Kind==QuestTransitionKind.QuestCompleted)
                pending=value;
        }
        private void LateUpdate()
        {
            if(pending is QuestTransition value)
            {
                pending=null;
                if(!label)
                {
                    var go=new GameObject("Quest notification",typeof(RectTransform));go.layer=gameObject.layer;
                    var rect=(RectTransform)go.transform;rect.SetParent(transform,false);
                    rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,.5f);
                    rect.anchoredPosition=new Vector2(-18,0);rect.sizeDelta=new Vector2(320,92);
                    label=UIText.Add(go,"",UITextRole.Caption,TextAlignmentOptions.MidlineRight);
                    label.raycastTarget=false;
                }
                string prefix=value.Kind==QuestTransitionKind.QuestCompleted ?
                    (value.Step.rewardMode==QuestRewardMode.AtInteraction?T("quests.rewardReady","Reward available"):T("quests.completed","Quest completed")) :
                    interruptedRun==value.Run.Id?T("quests.routeChanged","Route changed"):T("quests.newObjective","New objective");
                string line=value.Kind==QuestTransitionKind.QuestCompleted?
                    T(value.Step.outcomeKey,value.Step.outcome):T(value.Step.nameKey,value.Step.name);
                interruptedRun=null;label.text=prefix+"\n"+line;label.enabled=true;
                until=Time.unscaledTime+seconds;
                if(feedback)FeedbackService.Play(feedback,transform);
            }
            else if(until>0 && Time.unscaledTime>=until)Clear();
        }
    }
}
