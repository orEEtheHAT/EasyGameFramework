using System;
using System.IO;
using UnityEngine;

namespace EasySave
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Save Configuration", fileName = "Save Configuration")]
    public sealed class SaveConfiguration : ScriptableObject
    {
        [Tooltip("A single lowercase folder name below Application.persistentDataPath. Never a full path.")]
        public string DirectoryName = "easysave";
        public SaveOptions Options = new SaveOptions();
        [Tooltip("0 disables autosave. Uses unscaled time; overlapping requests are skipped.")]
        [Min(0)] public float AutoSaveSeconds;
        public string AutoSaveSlot = "auto";

        public string ConfigurationError
        {
            get
            {
                try
                {
                    SaveNames.CheckSlot(DirectoryName); SaveNames.CheckSlot(AutoSaveSlot);
                    if (Options == null) throw new ArgumentException("Assign save options.");
                    Options.Copy();
                    if (float.IsNaN(AutoSaveSeconds) || float.IsInfinity(AutoSaveSeconds) || AutoSaveSeconds < 0)
                        throw new ArgumentException("Autosave interval must be finite and nonnegative.");
                    return null;
                }
                catch (ArgumentException e) { return e.Message; }
            }
        }
        public SaveService CreateService()
        {
            string error = ConfigurationError;
            if (error != null) throw new InvalidOperationException(error);
            return new SaveService(new LocalFileStorage(Path.Combine(Application.persistentDataPath, DirectoryName)), Options);
        }
    }
}
