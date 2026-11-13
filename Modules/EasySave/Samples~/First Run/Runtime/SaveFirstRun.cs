using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasySave.Samples
{
    public sealed class SaveFirstRun : MonoBehaviour
    {
        [Serializable] public sealed class Progress { public int Coins; }
        [Serializable] public sealed class Preferences { public int TextSize; }
        private SaveService save;
        private string directory, message = "Ready. Save the current values, change them, then load.";
        private int coins = 10, textSize = 20;
        private InputAction add, saveSlot, loadSlot, savePreferences, loadPreferences;
        private bool verifying;
        private void OnEnable()
        {
            directory = Path.Combine(Application.temporaryCachePath, "EasySaveFirstRun-" + Guid.NewGuid().ToString("N"));
            save = new SaveService(new LocalFileStorage(directory));
            save.Register(new JsonSaveParticipant<Progress>("player", SaveScope.Progress, 1, () => new Progress { Coins = coins },
                state => state.Coins >= 0 ? SaveValidation.Valid : SaveValidation.Invalid("Coins cannot be negative."), state => coins = state.Coins));
            save.Register(new JsonSaveParticipant<Preferences>("display", SaveScope.Preferences, 1, () => new Preferences { TextSize = textSize },
                state => state.TextSize >= 10 && state.TextSize <= 40 ? SaveValidation.Valid : SaveValidation.Invalid("Text size must be 10..40."), state => textSize = state.TextSize));
            add = new InputAction("Change values", binding: "<Keyboard>/space"); add.performed += _ => { coins++; textSize = textSize == 20 ? 24 : 20; };
            saveSlot = new InputAction("Save slot", binding: "<Keyboard>/f5"); saveSlot.performed += context => { _ = Show(save.QuickSaveAsync()); };
            loadSlot = new InputAction("Load slot", binding: "<Keyboard>/f9"); loadSlot.performed += context => { _ = Show(save.QuickLoadAsync()); };
            savePreferences = new InputAction("Save preferences", binding: "<Keyboard>/f6"); savePreferences.performed += context => { _ = Show(save.SavePreferencesAsync()); };
            loadPreferences = new InputAction("Load preferences", binding: "<Keyboard>/f10"); loadPreferences.performed += context => { _ = Show(save.LoadPreferencesAsync()); };
            add.Enable(); saveSlot.Enable(); loadSlot.Enable(); savePreferences.Enable(); loadPreferences.Enable();
        }
        private async void Start()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++) if (arguments[i] == "-easySaveVerify")
            {
                verifying = true;
                string report = Path.GetFullPath(arguments[i + 1]);
                var result = await SaveVerification.RunAsync(Path.Combine(Path.GetDirectoryName(report), "IsolatedStorage"), report);
                Application.Quit(result.Success ? 0 : 1); return;
            }
        }
        private async Task Show(Task<SaveResult> task) { var result = await task; if (this && isActiveAndEnabled) message = result.ToString(); }
        private void OnGUI()
        {
            if (verifying) return;
            var previousMatrix = GUI.matrix;
            float scale = Mathf.Max(.1f, Mathf.Min(Screen.width / 960f, Screen.height / 640f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale - 64;
            try
            {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true };
            GUI.Label(new Rect(32, 24, width, 60), "EasySave — isolated first run", style);
            style.fontSize = textSize;
            GUI.Label(new Rect(32, 90, width, 55), "Progress: " + coins + " coins     Preferences: text size " + textSize, style);
            style.fontSize = 18;
            GUI.Label(new Rect(32, 160, width, 120), "Space: change both values\nF5 / F9: save / load progress\nF6 / F10: save / load preferences\nLoading progress leaves text size unchanged.", style);
            GUI.Label(new Rect(32, 310, width, 110), message, style);
            style.fontSize = 14;
            GUI.Label(new Rect(32, 440, width, 140), "This demo uses a new temporary folder per Play session.\n" + directory, style);
            }
            finally { GUI.matrix = previousMatrix; }
        }
        private void OnDisable()
        {
            add?.Dispose(); saveSlot?.Dispose(); loadSlot?.Dispose(); savePreferences?.Dispose(); loadPreferences?.Dispose(); save?.Dispose(); save = null;
        }
    }
}
