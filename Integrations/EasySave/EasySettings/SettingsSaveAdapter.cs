using System;
using EasySettings;

namespace EasySave.Integrations
{
    /// <summary>Persists confirmed URP graphics preferences. Restore deliberately keeps the current display mode;
    /// display changes belong to the owner's separate preview/confirmation flow.</summary>
    public static class SettingsSaveAdapter
    {
        public static ISaveParticipant Create(SettingsController controller, string key = "graphics")
        {
            if (!controller) throw new ArgumentNullException(nameof(controller));
            return new JsonSaveParticipant<SettingsSnapshot>(key, SaveScope.Preferences, 1, controller.Capture,
                snapshot =>
                {
                    if (!controller || !controller.isActiveAndEnabled || controller.Session == null || controller.Session.IsDisposed || controller.Session.IsFaulted || controller.Session.HasPreview)
                        return SaveValidation.Invalid("Enable and initialize EasySettings before restoring graphics preferences.");
                    var result = controller.Validate(snapshot);
                    return result.Success ? SaveValidation.Valid : SaveValidation.Invalid(string.Join("; ", result.Messages));
                }, snapshot =>
                {
                    if (!controller) throw new InvalidOperationException("Settings controller was destroyed.");
                    var result = controller.Restore(snapshot, restoreDisplay: false);
                    if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Messages));
                });
        }

        public static ISaveParticipant Create(SettingsSession session, string key = "graphics")
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return new JsonSaveParticipant<SettingsSnapshot>(key, SaveScope.Preferences, 1, session.Capture,
                snapshot =>
                {
                    if (session.IsDisposed || session.IsFaulted || session.HasPreview) return SaveValidation.Invalid("Finish the settings preview and use a healthy, active settings session.");
                    var result = session.Validate(snapshot); return result.Success ? SaveValidation.Valid : SaveValidation.Invalid(string.Join("; ", result.Messages));
                }, snapshot =>
                {
                    var result = session.Restore(snapshot, restoreDisplay: false);
                    if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Messages));
                });
        }
    }
}
