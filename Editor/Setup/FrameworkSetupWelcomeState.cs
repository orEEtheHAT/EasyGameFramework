using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    // A persistent ScriptableObject needs its own matching script asset for native reloads.
    [FilePath("UserSettings/EasyGameFrameworkSetup.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FrameworkSetupWelcomeState : ScriptableSingleton<FrameworkSetupWelcomeState>
    {
        [SerializeField] private bool hasBeenShown;
        [SerializeField] private string shownVersion;
        [NonSerialized] private bool legacyChecked;

        internal bool HasBeenShown { get { RestoreLegacyMarker(); return hasBeenShown || !string.IsNullOrEmpty(shownVersion); } }
        internal string ShownVersion { get { RestoreLegacyMarker(); return shownVersion; } }

        internal void Remember(string version)
        {
            legacyChecked = true;
            hasBeenShown = true;
            if (!string.IsNullOrEmpty(version)) shownVersion = version;
            Save(true);
        }

        private void RestoreLegacyMarker()
        {
            if (legacyChecked) return;
            legacyChecked = true;
            if (hasBeenShown || !string.IsNullOrEmpty(shownVersion)) return;
            string path = GetFilePath();
            try
            {
                // The old state had no reloadable MonoScript. Read only its small, known YAML schema.
                if (!File.Exists(path) || new FileInfo(path).Length > 8192) return;
                bool missingScript = false, matchingClass = false, hasNewFlag = false;
                string previousVersion = null;
                foreach (string line in File.ReadAllLines(path))
                {
                    string value = line.Trim();
                    if (value == "m_Script: {fileID: 0}") missingScript = true;
                    else if (value == "m_EditorClassIdentifier: EasyGameFramework.Editor.Setup:EasyGameFramework.Editor:FrameworkSetupWelcomeState") matchingClass = true;
                    else if (value.StartsWith("hasBeenShown:", StringComparison.Ordinal)) hasNewFlag = true;
                    else if (value.StartsWith("shownVersion:", StringComparison.Ordinal)) previousVersion = value.Substring("shownVersion:".Length).Trim().Trim('\'', '"');
                }
                if (!missingScript || !matchingClass || hasNewFlag || string.IsNullOrEmpty(previousVersion)) return;
                string coreVersion = previousVersion.Split('-', '+')[0];
                if (!Version.TryParse(coreVersion, out _)) return;
                Remember(previousVersion);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}