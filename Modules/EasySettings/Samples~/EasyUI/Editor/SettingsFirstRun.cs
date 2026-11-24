using System.IO;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EasySettings.UI.Editor
{
    public static class SettingsFirstRun
    {
        public const string Folder = "Assets/PackageTests/EasyGameFramework/EasySettings";
        public const string ScenePath = Folder + "/FirstRun.unity";
        [MenuItem("Tools/EasyGameFramework/Settings/Create first-run scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) throw new System.InvalidOperationException("Use a Unity 6.3 URP project and assign its pipeline before creating this scene.");
            if (!TMP_Settings.LoadDefaultSettings()) throw new System.InvalidOperationException("Choose Window > TextMeshPro > Import TMP Essential Resources before creating this scene.");
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save the current scene before creating the EasySettings first-run scene.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(4, 3, -6), Quaternion.Euler(17, -30, 0)); camera.backgroundColor = new Color(.04f, .07f, .1f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(48, -30, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Shadow floor"; floor.transform.localScale = new Vector3(3, 1, 3);
            for (int i = 0; i < 4; i++) { var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Quality reference " + i; cube.transform.position = new Vector3(i * 1.4f - 2, .5f + i * .2f, 0); }
            var defaults = ScriptableObject.CreateInstance<SettingsDefaults>(); AssetDatabase.CreateAsset(defaults, Folder + "/Defaults.asset");
            var volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>(); volumeProfile.Add<ColorAdjustments>(); volumeProfile.Add<Bloom>();
            AssetDatabase.CreateAsset(volumeProfile, Folder + "/Volume.asset"); foreach (var component in volumeProfile.components) AssetDatabase.AddObjectToAsset(component, volumeProfile);
            var volume = new GameObject("Settings Volume").AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 100; volume.sharedProfile = volumeProfile;
            var controller = new GameObject("Graphics settings").AddComponent<SettingsController>(); controller.Defaults = defaults; controller.TargetCamera = camera; controller.TargetVolume = volume;
            var canvas = new GameObject("Settings Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = .5f;
            var manager = canvas.AddComponent<WindowManager>();
            var windowObject = new GameObject("Graphics window", typeof(RectTransform)); windowObject.transform.SetParent(canvas.transform, false); windowObject.SetActive(false);
            windowObject.AddComponent<UIWindow>(); var view = windowObject.AddComponent<SettingsWindow>(); view.Controller = controller;
            var demo = canvas.AddComponent<SettingsDemo>(); demo.Controller = controller; demo.Manager = manager; demo.Window = view;
            var reopen = new GameObject("Open graphics", typeof(RectTransform), typeof(Image), typeof(Button)); reopen.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)reopen.transform; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(12, -12); rect.sizeDelta = new Vector2(165, 36);
            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(reopen.transform, false);
            var text = UIText.Add(label, "Graphics settings", 14, TextAlignmentOptions.Center);
            text.color = Color.black;
            var tr = (RectTransform)label.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(reopen.GetComponent<Button>().onClick, demo.Open);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeObject = controller;
        }
    }
}
