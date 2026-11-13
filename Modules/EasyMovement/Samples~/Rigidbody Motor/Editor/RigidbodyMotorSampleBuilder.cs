using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyMovement.Samples.Editor
{
    public static class RigidbodyMotorSampleBuilder
    {
        [MenuItem("Tools/EasyGameFramework/Movement/Create Rigidbody Motor Scene")]
        public static void CreateFromMenu() => Create("Assets/EasyGameFramework Examples/Movement/Rigidbody", true);

        public static string Create(string folder, bool open = false)
        {
            if (string.IsNullOrWhiteSpace(folder) || !folder.Replace('\\', '/').StartsWith("Assets/"))
                throw new System.ArgumentException("Use a project-owned Assets subfolder.", nameof(folder));
            folder = folder.Replace('\\', '/').TrimEnd('/');
            string fullFolder = Path.GetFullPath(folder);
            if (!fullFolder.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
                throw new System.ArgumentException("Keep sample output inside Assets.", nameof(folder));
            string scenePath = folder + "/RigidbodyMotor_Sample.unity";
            if (File.Exists(scenePath))
            {
                if (open)
                {
                    var loaded = SceneManager.GetSceneByPath(scenePath);
                    if (loaded.IsValid() && loaded.isLoaded) SceneManager.SetActiveScene(loaded);
                    else EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                }
                return scenePath;
            }
            var profile = AssetDatabase.LoadAssetAtPath<RigidbodyMotorProfile>("Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/Rigidbody/Profiles/CoastingMotor_Sample.asset");
            if (!profile) throw new System.InvalidOperationException("EasyGameFramework's CoastingMotor_Sample profile is missing. Check the framework installation before creating the example.");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var floor = Cube("Floor", new Vector3(0, -.5f, 3), new Vector3(24, 1, 36)); floor.isStatic = true;
                Cube("Wall — collision stop", new Vector3(0, 1, 12), new Vector3(12, 2, 1)).isStatic = true;
                var body = Cube("Rigidbody Motor", new Vector3(0, .55f, -6), Vector3.one);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader)
                {
                    var floorMaterial = new Material(shader) { name = "Floor_Sample", color = new Color(.16f, .22f, .29f) };
                    var motorMaterial = new Material(shader) { name = "Motor_Sample", color = new Color(.95f, .44f, .12f) };
                    AssetDatabase.CreateAsset(floorMaterial, folder + "/Floor_Sample.mat"); AssetDatabase.CreateAsset(motorMaterial, folder + "/Motor_Sample.mat");
                    floor.GetComponent<Renderer>().sharedMaterial = floorMaterial; body.GetComponent<Renderer>().sharedMaterial = motorMaterial;
                }
                var motor = body.AddComponent<RigidbodyMotor>(); motor.Body.mass = 2;
                motor.Body.constraints = RigidbodyConstraints.FreezeRotation;
                motor.Profile = profile;
                body.AddComponent<RigidbodyMotorSample>().Motor = motor;
                var friction = new PhysicsMaterial("Sample — no sliding friction") { dynamicFriction = 0, staticFriction = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
                AssetDatabase.CreateAsset(friction, folder + "/Surface_Sample.physicMaterial");
                floor.GetComponent<Collider>().sharedMaterial = friction; body.GetComponent<Collider>().sharedMaterial = friction;
                var camera = new GameObject("Example Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.transform.position = new Vector3(13, 12, -16); camera.transform.LookAt(new Vector3(0, 0, 3));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f, .1f, .15f);
                var light = new GameObject("Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(50, -25, 0);
                EditorSceneManager.SaveScene(scene, scenePath); AssetDatabase.SaveAssets();
                return scenePath;
            }
            finally
            {
                if (!open && previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (!open) EditorSceneManager.CloseScene(scene, true);
            }
        }
        static GameObject Cube(string label, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = label; go.transform.position = position; go.transform.localScale = scale; return go;
        }
    }
}
