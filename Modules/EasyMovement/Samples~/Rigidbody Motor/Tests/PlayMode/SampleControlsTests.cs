using System;
using System.Reflection;
using EasyMovement.Samples;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace EasyMovement.Samples.Tests
{
    public sealed class SampleControlsTests
    {
        [Test] public void SampleUsesNewInputForThrottleBrakeAndClearsItsIntentOnDisable()
        {
            var previousKeyboard = Keyboard.current;
            var previousMode = InputSystem.settings.updateMode;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var previousEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            Keyboard keyboard = null; GameObject root = null;
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard = InputSystem.AddDevice<Keyboard>();
                root = new GameObject("Scoped sample input"); var sample = root.AddComponent<RigidbodyMotorSample>();
                sample.Motor.Body.isKinematic = true;
                foreach (var fieldName in new[] { "throttle", "brake", "impulse" })
                {
                    var action = (InputAction)typeof(RigidbodyMotorSample).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sample);
                    for (int i = 0; i < action.bindings.Count; ++i)
                    {
                        var binding = action.bindings[i]; if (binding.isComposite) continue;
                        action.ApplyBindingOverride(i, binding.path.StartsWith("<Keyboard>", StringComparison.Ordinal)
                            ? keyboard.path + binding.path.Substring("<Keyboard>".Length) : string.Empty);
                    }
                }
                var update = (Action)Delegate.CreateDelegate(typeof(Action), sample, typeof(RigidbodyMotorSample).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update(); update(); Assert.AreEqual(1, sample.Motor.Throttle);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S)); InputSystem.Update(); update(); Assert.AreEqual(-1, sample.Motor.Throttle);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update(); update(); Assert.AreEqual(1, sample.Motor.BrakeInput);
                sample.enabled = false; Assert.AreEqual(0, sample.Motor.Throttle); Assert.AreEqual(0, sample.Motor.BrakeInput);
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                InputSystem.settings.updateMode = previousMode;
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditor;
            }
        }
    }
}
