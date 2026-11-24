using System;
using EasyUI;

namespace EasySave.Integrations
{
    public static class UIReadingSave
    {
        /// <summary>Register once for the local player. Text size and accessibility use the preferences scope.</summary>
        public static ISaveParticipant Create(WindowManager manager,string key="ui/reading")
        {
            if (!manager) throw new ArgumentNullException(nameof(manager));
            return new JsonSaveParticipant<UIReadingPreferences>(key,SaveScope.Preferences,1,()=>manager.ReadingPreferences,
                p=>manager && p.IsValid ? SaveValidation.Valid : SaveValidation.Invalid("Invalid UI reading preferences."),
                p=>manager.ReadingPreferences=p);
        }
    }
}
