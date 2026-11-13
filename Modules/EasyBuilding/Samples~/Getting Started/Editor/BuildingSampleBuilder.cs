using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EasyResources;
using EasyBuilding.Integrations;

namespace EasyBuilding.Samples.Editor
{
    public static class BuildingSampleBuilder
    {
        [MenuItem("Tools/EasyGameFramework/Building/Create Getting Started Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateAt("Assets/EasyGameFramework Examples/Building");
        }

        /// <summary>Public repeatable setup used both by the menu and automated installation verification.</summary>
        public static string CreateAt(string folder)
        {
            if (!folder.StartsWith("Assets/", System.StringComparison.Ordinal) || folder.Contains(".."))
                throw new System.ArgumentException("Choose a folder under Assets.");
            string scenePath = folder + "/GettingStarted.unity";
            if (File.Exists(scenePath)) { EditorSceneManager.OpenScene(scenePath); return scenePath; }
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var blockMaterial = Material(folder, "Block", new Color(0.8f, 0.59f, 0.31f));
            var groundMaterial = Material(folder, "Ground", new Color(0.12f, 0.16f, 0.21f));
            var plotMaterial = Material(folder, "Plot", new Color(0.15f, 0.35f, 0.55f));
            var socketMaterial = Material(folder, "SocketMarker", new Color(0.1f, 0.8f, 0.85f));
            var root = new GameObject("Block");
            var mesh = Cube("Mesh", new Vector3(0, 0.5f, 0), Vector3.one, blockMaterial);
            mesh.transform.SetParent(root.transform, false);
            var topSocket = new GameObject("Top Socket"); topSocket.transform.SetParent(root.transform, false);
            topSocket.transform.localPosition = Vector3.up; topSocket.AddComponent<BuildingSocket>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Block.prefab"); Object.DestroyImmediate(root);
            var free = Definition(folder, "Free Block", prefab, PlacementMode.Free);
            var freeInZone = Definition(folder, "Free In Zone Block", prefab, PlacementMode.Free);
            freeInZone.RequireZone = true; freeInZone.ZoneId = "Plot"; EditorUtility.SetDirty(freeInZone);
            var grid = Definition(folder, "Grid In Zone Block", prefab, PlacementMode.Grid);
            grid.RequireZone = true; grid.ZoneId = "Plot"; EditorUtility.SetDirty(grid);
            var socket = Definition(folder, "Socket Block", prefab, PlacementMode.Socket);
            var paid = Definition(folder, "Paid Block", prefab, PlacementMode.Free);
            var wood = ResourceDefinition.Create("Wood"); AssetDatabase.CreateAsset(wood, folder + "/Wood.asset");
            paid.Payment = BuildingPayment.Paid; paid.Costs = new[] { new BuildingCost(wood.Id, 3) }; EditorUtility.SetDirty(paid);
            Cube("Ground", new Vector3(0, -0.25f, 0), new Vector3(24, 0.5f, 18), groundMaterial);
            var plot = new GameObject("Blue Plot"); plot.transform.position = new Vector3(-4, 0, 0);
            var zone = plot.AddComponent<BuildingZone>(); zone.Size = new Vector3(6, 5, 6); zone.Center = new Vector3(0, 2.5f, 0);
            // Colored ground is recessed to keep its top exactly at the placement plane.
            Cube("Plot Surface", new Vector3(-4, -0.003f, 0), new Vector3(6, 0.01f, 6), plotMaterial);
            var socketRoot = new GameObject("Scene Socket"); socketRoot.transform.position = new Vector3(4, 0, 0); socketRoot.AddComponent<BuildingSocket>();
            Cube("Socket Marker", new Vector3(4, -0.003f, 0), new Vector3(1.5f, 0.01f, 1.5f), socketMaterial);
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 14, -13); camera.transform.rotation = Quaternion.Euler(48, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.055f, 0.07f, 0.11f);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var controls = new GameObject("Building Controls");
            var system = controls.AddComponent<BuildingSystem>(); var placer = controls.AddComponent<BuildingPlacer>();
            placer.StartingBuilding = free; placer.ViewCamera = camera; placer.Reach = 50;
            var wallet = controls.AddComponent<ResourceContainer>();
            var walletData = new SerializedObject(wallet); var initial = walletData.FindProperty("initialContents").FindPropertyRelative("items"); initial.arraySize = 1;
            initial.GetArrayElementAtIndex(0).FindPropertyRelative("resource").objectReferenceValue = wood;
            initial.GetArrayElementAtIndex(0).FindPropertyRelative("amount").longValue = 30; walletData.ApplyModifiedPropertiesWithoutUndo();
            var economy = controls.AddComponent<BuildingResources>(); economy.Resources = new[] { wood }; economy.Containers = new[] { wallet };
            var systemData = new SerializedObject(system); systemData.FindProperty("costProvider").objectReferenceValue = economy; systemData.ApplyModifiedPropertiesWithoutUndo();
            var sample = controls.AddComponent<BuildingSample>(); sample.System = system; sample.Wallet = wallet; sample.Wood = wood;
            sample.Free = free; sample.FreeInZone = freeInZone; sample.GridInZone = grid; sample.Socket = socket; sample.Paid = paid;
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = controls;
            return scenePath;
        }

        [MenuItem("Tools/EasyGameFramework/Building/Create Benchmark Scene")]
        public static void CreateBenchmarkMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateBenchmarkAt("Assets/EasyGameFramework Examples/Building Benchmark");
        }
        public static string CreateBenchmarkAt(string folder)
        {
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Building Benchmark").AddComponent<BuildingBenchmark>();
            string path = folder + "/Benchmark.unity"; EditorSceneManager.SaveScene(scene, path); return path;
        }
        private static BuildingDefinition Definition(string folder, string name, GameObject prefab, PlacementMode placement)
        {
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = prefab; definition.Placement = placement;
            AssetDatabase.CreateAsset(definition, folder + "/" + name + ".asset");
            // The ID postprocessor persists the asset GUID, including when a designer duplicates the definition.
            EditorUtility.SetDirty(definition); return definition;
        }
        private static Material Material(string folder, string name, Color color)
        {
            var shader = Shader.Find("EasyBuilding/Sample Surface");
            var value = new Material(shader); value.color = color; AssetDatabase.CreateAsset(value, folder + "/" + name + ".mat"); return value;
        }
        private static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name; cube.transform.position = position; cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material; return cube;
        }
    }
}
