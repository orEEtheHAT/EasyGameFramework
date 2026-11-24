using System;
using EasySave;

namespace EasyQuests.Integrations
{
    public static class QuestSave
    {
        public static ISaveParticipant Create(QuestWorld world, string key = "world/quests")
        {
            if (!world) throw new ArgumentNullException(nameof(world));
            return new JsonSaveParticipant<QuestSnapshot>(key, SaveScope.Progress, 1,
                () => world.Session.Capture(), data =>
                {
                    try { world.Session.Validate(data); return SaveValidation.Valid; }
                    catch (ArgumentException e) { return SaveValidation.Invalid(e.Message); }
                }, data => world.Session.Restore(data), 95);
        }
    }
}
