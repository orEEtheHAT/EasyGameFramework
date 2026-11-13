using System;
using System.IO;
using EasyBuilding.Cells;
using EasyBuilding.Paths;
using EasyBuilding.Voxels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EasyBuilding.Samples.Editor
{
    public static class BuildingCellSampleBuilder
    {
        [MenuItem("Tools/EasyGameFramework/Building/Create Cells and Paths Scene")]
        public static void Create() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) CreateAt("Assets/EasyBuilding Cells Example"); }
        public static string CreateAt(string folder)
        {
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Contains("..")) throw new ArgumentException("Choose a folder under Assets.");
            string scenePath = folder + "/CellsAndPaths.unity"; if (File.Exists(scenePath)) { EditorSceneManager.OpenScene(scenePath); return scenePath; }
            Directory.CreateDirectory(folder); AssetDatabase.Refresh(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var palette = ScriptableObject.CreateInstance<BuildingCellPalette>();
            palette.Types = new[] { new BuildingCellType { Id = "stone", Name = "Stone", Color = new Color(0.55f, 0.65f, 0.72f) }, new BuildingCellType { Id = "brick", Name = "Brick", Color = new Color(0.8f, 0.35f, 0.2f) }, new BuildingCellType { Id = "roof", Name = "Roof", Color = new Color(0.18f, 0.42f, 0.45f) } };
            AssetDatabase.CreateAsset(palette, folder + "/Cell Palette.asset");
            var cells = new GameObject("Cell World").AddComponent<BuildingCellWorld>(); cells.Palette = palette;
            var voxels = cells.gameObject.AddComponent<BuildingVoxelRenderer>(); voxels.World = cells;
            var camera = new GameObject("View Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.transform.position = new Vector3(16, 17, -22); camera.transform.LookAt(Vector3.zero); camera.fieldOfView = 50; camera.backgroundColor = new Color(0.09f, 0.12f, 0.16f); camera.clearFlags = CameraClearFlags.SolidColor;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")); material.color = new Color(0.2f, 0.24f, 0.3f); AssetDatabase.CreateAsset(material, folder + "/Ground.mat");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Ground"; ground.transform.position = Vector3.down * 0.1f; ground.transform.localScale = new Vector3(60, 0.2f, 60); ground.GetComponent<Renderer>().sharedMaterial = material;
            BuildingPathMesh Path(string name, Vector3 offset, BuildingPathShape shape, Color color)
            {
                var path = new GameObject(name).AddComponent<BuildingPathMesh>(); path.transform.position = offset; path.Settings.Shape = shape; path.Settings.Smooth = true; path.Settings.Width = shape == BuildingPathShape.Road ? 2 : 0.5f;
                var authored = new SerializedObject(path); var points = authored.FindProperty("points"); points.arraySize = 4;
                points.GetArrayElementAtIndex(0).vector3Value = Vector3.zero;
                points.GetArrayElementAtIndex(1).vector3Value = new Vector3(1, 0, 2);
                points.GetArrayElementAtIndex(2).vector3Value = new Vector3(0, shape == BuildingPathShape.Pipe ? 2 : 0, 4);
                points.GetArrayElementAtIndex(3).vector3Value = new Vector3(-1, shape == BuildingPathShape.Pipe ? 2 : 0, 6); authored.ApplyModifiedPropertiesWithoutUndo();
                var tint = new Material(material); tint.color = color; AssetDatabase.CreateAsset(tint, folder + "/" + name + ".mat"); path.GetComponent<MeshRenderer>().sharedMaterial = tint;
                return path;
            }
            var demo = new GameObject("Example Controls").AddComponent<BuildingCellPlayground>(); demo.View = camera; demo.Cells = cells; demo.Voxels = voxels;
            demo.Road = Path("Road", new Vector3(8, 0.03f, 0), BuildingPathShape.Road, new Color(0.3f, 0.34f, 0.4f));
            demo.Pipe = Path("Pipe", new Vector3(-8, 1, 0), BuildingPathShape.Pipe, new Color(0.9f, 0.65f, 0.25f));
            EditorSceneManager.SaveScene(scene, scenePath); AssetDatabase.SaveAssets(); return scenePath;
        }
    }
}
