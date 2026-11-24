using System;

namespace EasyQuests
{
    public sealed partial class QuestSession
    {
        /// <summary>Check that a plain Start Quest action can start its definition without instance role bindings.</summary>
        public static void ValidateDialogueQuestStart(QuestDefinition quest)
        {
            if (!quest) throw new ArgumentNullException(nameof(quest));
            ValidateRoles(new QuestRun(quest, quest.id, null, null));
        }

        /// <summary>Commit a validated narrative payment and its built-in consequences, then publish settled quest notifications.</summary>
        public bool TryCommitDialogue(Func<bool> pay, Action apply)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));
            if (changing || claiming) return false;
            bool wasNotifying = notifying;
            notifying = true;
            try
            {
                if (pay != null)
                {
                    claiming = true;
                    try { if (!pay()) return false; }
                    finally { EndWalletOperation(); }
                }
                apply();
                return true;
            }
            finally
            {
                notifying = wasNotifying;
                if (!wasNotifying) Notify();
            }
        }
    }
}
