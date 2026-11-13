using EasyUI;
using UnityEngine;
using UnityEngine.UI;

namespace EasySettings.UI
{
    public sealed class SettingsDemo : MonoBehaviour
    {
        public SettingsController Controller;
        public WindowManager Manager;
        public SettingsWindow Window;
        void Start() { Application.runInBackground = true; Manager.Open(Window.GetComponent<UIWindow>()); }
        public void Open() => Manager.Open(Window.GetComponent<UIWindow>());
    }
}
