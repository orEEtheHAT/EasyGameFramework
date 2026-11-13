using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EasySettings;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace EasySettings.Verification
{
    public sealed class SettingsPlayerVerification : MonoBehaviour
    {
        [Serializable] sealed class Report {
            public bool passed; public string unity, platform, failure, initialDisplay, finalDisplay;
            public List<string> checks = new List<string>();
            public double applyAverageMs, idleTickNanoseconds;
            public long idleFrameGcMaximum, positiveControlGcMaximum;
            public bool gcRecorderValid, positiveControlDetected;
        }
        Report report = new Report(); string reportPath;
        SettingsController controller;
        SettingsState initial;
        float oldTimeScale;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartVerification() {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-easySettingsVerify") >= 0)
                new GameObject("EasySettings verification").AddComponent<SettingsPlayerVerification>();
        }
        void Start() { reportPath = Argument("-easySettingsReport"); StartCoroutine(RunGuarded()); }
        IEnumerator RunGuarded()
        {
            var work = Verify(); bool failed = false;
            while (true) {
                object next;
                try { if (!work.MoveNext()) break; next = work.Current; }
                catch (Exception e) { report.failure = e.ToString(); failed = true; break; }
                yield return next;
            }
            Time.timeScale = oldTimeScale;
            if (controller && controller.Session != null && controller.Session.HasPreview) controller.Session.Revert();
            report.passed = !failed; report.finalDisplay = UrpSettingsBackend.ReadDisplay().ToString();
            if (!string.IsNullOrEmpty(reportPath)) { Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); }
            Debug.Log("EasySettings verification: " + JsonUtility.ToJson(report));
            yield return null;
            Application.Quit(report.passed ? 0 : 1);
        }
        IEnumerator Verify()
        {
            oldTimeScale = Time.timeScale; Application.runInBackground = true;
            report.unity = Application.unityVersion; report.platform = Application.platform.ToString();
            for (int i = 0; i < 8; i++) yield return null;
            controller = FindFirstObjectByType<SettingsController>(); Require(controller && controller.Session != null, "controller ready");
            var session = controller.Session; initial = session.Applied;
            report.initialDisplay = UrpSettingsBackend.ReadDisplay().ToString();
            Require(Screen.fullScreenMode == FullScreenMode.Windowed, "own windowed Player; no monitor switch");
            var capture = session.Capture(); var edited = session.Draft; edited.RenderScale = .75f; edited.VSyncCount = 0; edited.FrameRateLimit = 60;
            session.Draft = edited; Require(session.Apply().Success, "graphics apply");
            Require(Mathf.Approximately(session.Observed.RenderScale, .75f), "runtime URP scale observed");
            var restored = JsonUtility.FromJson<SettingsSnapshot>(JsonUtility.ToJson(capture)); Require(session.Restore(restored).Success, "JSON snapshot round trip");
            Require(session.Applied.RenderScale == initial.RenderScale, "snapshot restored scale");

            // Choose only a window size advertised by the current monitor. Never use fullscreen modes.
            DisplaySettings target = default;
            foreach (var option in session.Capabilities.Resolutions)
                if (option.Width >= 640 && option.Width <= 1280 && option.Height >= 480 && option.Height <= 900 && (option.Width != Screen.width || option.Height != Screen.height)) { target = option; break; }
            Require(target.Width > 0, "alternate windowed resolution available"); target.Mode = FullScreenMode.Windowed;
            edited = session.Draft; edited.Display = target; session.Draft = edited; Require(session.Apply().Success && session.HasPreview, "windowed preview requested");
            for (int i = 0; i < 12; i++) yield return null;
            Require(Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == target.Width && Screen.height == target.Height, "OS accepted preview window dimensions");
            while (session.HasPreview && !session.CanConfirm) yield return null;
            Require(session.Confirm(), "display confirmation"); Require(session.Capture().State.Display.Width == Screen.width, "snapshot stores observed dimensions");
            edited = session.Draft; edited.Display = initial.Display; session.Draft = edited; Require(session.Apply().Success && session.HasPreview, "second windowed preview");
            Time.timeScale = 0;
            float deadline = Time.realtimeSinceStartup + 20;
            while (session.HasPreview && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!session.HasPreview, "unscaled preview timeout while paused");
            Time.timeScale = oldTimeScale;
            for (int i = 0; i < 6; i++) yield return null;
            Require(Screen.width == target.Width && Screen.height == target.Height, "timeout restored confirmed window size");
            edited = session.Draft; edited.Display = initial.Display; session.Draft = edited; session.Apply();
            while (session.HasPreview && !session.CanConfirm) yield return null; Require(session.Confirm(), "return to original window size confirmed");
            Require(session.Restore(capture).Success, "final graphics restore");

            Screen.SetResolution(target.Width, target.Height, FullScreenMode.Windowed, default(RefreshRate));
            for (int i = 0; i < 10; i++) yield return null;
            Require(Screen.width == target.Width, "external window resize simulated");
            Require(session.Restore(capture).Success && !session.HasPreview, "graphics restore does not request a display preview after external resize");
            for (int i = 0; i < 4; i++) yield return null;
            Require(Screen.width == target.Width && Screen.height == target.Height, "graphics restore preserves externally resized window");
            Screen.SetResolution(initial.Display.Width, initial.Display.Height, FullScreenMode.Windowed, default(RefreshRate));
            for (int i = 0; i < 10; i++) yield return null;
            session.Restore(capture);

            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 100000; i++) session.Tick(.016f);
            report.idleTickNanoseconds = (Stopwatch.GetTimestamp() - started) * 1e9 / Stopwatch.Frequency / 100000;
            double total = 0;
            for (int i = 0; i < 12; i++) {
                edited = session.Draft; edited.RenderScale = i % 2 == 0 ? .8f : .9f; session.Draft = edited;
                started = Stopwatch.GetTimestamp(); Require(session.Apply().Success, null);
                total += (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency; yield return null;
            }
            report.applyAverageMs = total / 12; session.Restore(capture);
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1)) {
                report.gcRecorderValid = recorder.Valid;
                for (int i = 0; i < 10; i++) { yield return null; report.idleFrameGcMaximum = Math.Max(report.idleFrameGcMaximum, recorder.LastValue); }
                var allocations = new List<byte[]>();
                for (int i = 0; i < 6; i++) { allocations.Add(new byte[262144]); yield return null; report.positiveControlGcMaximum = Math.Max(report.positiveControlGcMaximum, recorder.LastValue); }
                GC.KeepAlive(allocations);
                report.positiveControlDetected = report.gcRecorderValid && report.positiveControlGcMaximum >= report.idleFrameGcMaximum + 131072;
            }
            Require(report.positiveControlDetected, "GC recorder positive control");
            string screenshot = Argument("-easySettingsScreenshot");
            if (!string.IsNullOrEmpty(screenshot)) { ScreenCapture.CaptureScreenshot(screenshot); for (int i = 0; i < 4; i++) yield return null; }
            var pipeline = QualitySettings.renderPipeline;
            controller.enabled = false; Require(QualitySettings.renderPipeline != pipeline, "controller disable released pipeline override");
            controller.enabled = true; Require(controller.Session != null, "controller re-enabled cleanly");
            report.checks.Add("CPU timings measure API work, not GPU cost or FPS; frame GC includes the whole scene/UI.");
        }
        void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name ?? "Apply failed"); if (name != null) report.checks.Add(name); }
        static string Argument(string key) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    }
}
