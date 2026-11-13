using System.IO;
using EasyFeedbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasySuite.Example.Editor
{

/// <summary>Idempotent sample-only factory. Creates original assets in an explicit Assets subfolder; never modifies the open scene.</summary>
public static class FrameworkFeedbackSampleFactory
{
    public static FeedbackPreset Create(string outputFolder, bool includeVibration = true)
    {
        var folder = EnsureOutputFolder(outputFolder);
        var presetPath = folder + (includeVibration ? "/Integration Confirm With Haptics.asset" : "/Integration Confirm.asset");
        var existing = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(presetPath);
        if (existing != null) return existing;
        var clipPath = folder + "/Integration Confirm.wav";
        if (!File.Exists(clipPath))
        {
            WriteTone(clipPath);
            AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceSynchronousImport);
        }

        var materialPath = folder + "/Integration Particle.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new System.InvalidOperationException("A particle shader is required for the demo.");
            material = new Material(shader) { name = "Integration Particle" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.1f, 0.8f, 1f));
            AssetDatabase.CreateAsset(material, materialPath);
        }

        var prefabPath = folder + "/Integration Confirm.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Integration Confirm");
                SceneManager.MoveGameObjectToScene(root, preview);
                var particles = root.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.12f;
                main.startLifetime = 0.25f;
                main.startSpeed = 0.8f;
                main.startSize = 0.07f;
                main.startColor = new Color(0.2f, 0.9f, 1f);
                var emission = particles.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
                particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        var profilePath = folder + "/Integration Confirm Haptics.asset";
        var profile = AssetDatabase.LoadAssetAtPath<GamepadVibrationProfile>(profilePath);
        if (includeVibration && profile == null)
        {
            profile = ScriptableObject.CreateInstance<GamepadVibrationProfile>();
            var profileData = new SerializedObject(profile);
            profileData.FindProperty("lowFrequencyMotor").floatValue = 0.25f;
            profileData.FindProperty("highFrequencyMotor").floatValue = 0.4f;
            profileData.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        if (AssetDatabase.LoadAssetAtPath<FeedbackPreset>(presetPath) == null)
        {
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            var data = new SerializedObject(preset);
            var audio = data.FindProperty("audioEntries");
            audio.arraySize = 1;
            var layer = audio.GetArrayElementAtIndex(0);
            layer.FindPropertyRelative("isEnabled").boolValue = true;
            layer.FindPropertyRelative("clips").arraySize = 1;
            layer.FindPropertyRelative("clips").GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            layer.FindPropertyRelative("minVolume").floatValue = 0.5f;
            layer.FindPropertyRelative("maxVolume").floatValue = 0.5f;
            layer.FindPropertyRelative("minPitch").floatValue = 1f;
            layer.FindPropertyRelative("maxPitch").floatValue = 1f;
            layer.FindPropertyRelative("minDistance").floatValue = 1f;
            layer.FindPropertyRelative("maxDistance").floatValue = 500f;
            var visuals = data.FindProperty("visualEntries");
            visuals.arraySize = 1;
            var visual = visuals.GetArrayElementAtIndex(0);
            visual.FindPropertyRelative("isEnabled").boolValue = true;
            visual.FindPropertyRelative("particleSystemPrefab").objectReferenceValue = prefab.GetComponent<ParticleSystem>();
            visual.FindPropertyRelative("scaleMultiplier").vector3Value = Vector3.one;
            visual.FindPropertyRelative("prewarmCount").intValue = 1;
            var haptics = data.FindProperty("gamepadVibrationEntries");
            haptics.arraySize = includeVibration ? 1 : 0;
            if (includeVibration)
            {
                var haptic = haptics.GetArrayElementAtIndex(0);
                haptic.FindPropertyRelative("isEnabled").boolValue = true;
                haptic.FindPropertyRelative("strengthMultiplier").floatValue = 1f;
                haptic.FindPropertyRelative("profile").objectReferenceValue = profile;
                haptic.FindPropertyRelative("duration").floatValue = 0.12f;
                haptic.FindPropertyRelative("fadeOut").floatValue = 0.05f;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(preset, presetPath);
        }

        return AssetDatabase.LoadAssetAtPath<FeedbackPreset>(presetPath);
    }

    private static string EnsureOutputFolder(string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new System.ArgumentException("Supply a generated-assets folder below Assets.", nameof(outputFolder));
        var path = outputFolder.Replace('\\', '/').TrimEnd('/');
        var segments = path.Split('/');
        if (segments.Length < 2 || segments[0] != "Assets")
            throw new System.ArgumentException("The output folder must be below Assets.", nameof(outputFolder));
        for (var i = 1; i < segments.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(segments[i]) || segments[i] == "." || segments[i] == ".."
                || segments[i].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new System.ArgumentException("The output folder contains an invalid segment.", nameof(outputFolder));
        }

        var parent = "Assets";
        for (var i = 1; i < segments.Length; i++)
        {
            var child = parent + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(child) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, segments[i])))
                throw new IOException("Could not create sample output folder: " + child);
            parent = child;
        }
        return path;
    }
    private static void WriteTone(string path)
    {
        const int sampleRate = 44100;
        const int samples = 5292; // 120 ms, mono PCM16. Generated here; no external source.
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + samples * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(samples * 2);
            for (var i = 0; i < samples; i++)
            {
                var t = (float)i / samples;
                var envelope = Mathf.Sin(t * Mathf.PI) * (1f - t);
                var tone = Mathf.Sin(2f * Mathf.PI * 660f * i / sampleRate);
                writer.Write((short)(tone * envelope * 0.2f * short.MaxValue));
            }
        }
    }
}
}
