#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using EasyMovement.Samples;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EasyMovement.Samples.Tests
{
    public sealed class SampleSceneTests
    {
        [UnityTest] public IEnumerator FirstRunSceneMovesWithRealInputAndPhysics()
        {
            // Create the editable example through RigidbodyMotorSampleBuilder before this smoke test.
            const string path = "Assets/EasyGameFramework Examples/Movement/Rigidbody/RigidbodyMotor_Sample.unity";
            var previousKeyboard = Keyboard.current;
            var mode = InputSystem.settings.updateMode;
            var background = InputSystem.settings.backgroundBehavior;
            var editor = InputSystem.settings.editorInputBehaviorInPlayMode;
            Keyboard keyboard = null; Scene scene = default;
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
                scene = SceneManager.GetSceneByPath(path);
                var sample = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<RigidbodyMotorSample>()).Single();
                foreach (var fieldName in new[] { "throttle", "brake", "impulse" })
                {
                    var action = (InputAction)typeof(RigidbodyMotorSample).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sample);
                    for (int i = 0; i < action.bindings.Count; ++i)
                    {
                        var binding = action.bindings[i]; if (binding.isComposite) continue;
                        action.ApplyBindingOverride(i, binding.path.StartsWith("<Keyboard>") ? keyboard.path + binding.path.Substring("<Keyboard>".Length) : string.Empty);
                    }
                }
                float start = sample.Motor.Body.position.z;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
                for (int i = 0; i < 20; ++i) { yield return new WaitForFixedUpdate(); InputSystem.Update(); }
                Assert.That(sample.Motor.Body.position.z, Is.GreaterThan(start + .15f)); Assert.That(sample.Motor.CurrentSpeed, Is.GreaterThan(1));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
                for (int i = 0; i < 30; ++i) { yield return new WaitForFixedUpdate(); InputSystem.Update(); }
                Assert.That(Mathf.Abs(sample.Motor.CurrentSpeed), Is.LessThan(.05f));
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                InputSystem.settings.updateMode = mode; InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editor;
            }
            yield return null;
        }
    }
}
#endif
