using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyUI
{
    /// <summary>Reveal an already laid-out TMP line in reading order, using unscaled time.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
    [AddComponentMenu("EasyGameFramework/UI/Text Reveal")]
    public sealed class UITextReveal : MonoBehaviour, IPointerClickHandler
    {
        [Range(0, 3)] public float duration = .85f;
        public bool playOnEnable = true;
        [Tooltip("The label must be a raycast target to receive pointer clicks.")] public bool clickToComplete = true;
        public bool IsRevealing { get; private set; }

        private static readonly int ClockId = Shader.PropertyToID("_EasyUIRevealClock");
        private static readonly int EdgeId = Shader.PropertyToID("_RevealEdgeColor");
        private static int clockFrame = -1;
        private static Material paper;
        private TextMeshProUGUI label;
        private WindowManager manager;
        private Coroutine animation;
        private Material original, temporary, lease;
        private bool pending;
        private double start;
        private float length, fade, travel;
        private readonly List<FadeQuad> fallback = new();
        private int[] glyphEnds = System.Array.Empty<int>();
        private struct FadeQuad
        {
            public int material, vertex;
            public float delay;
            public Color32 alpha;
        }
        private TextMeshProUGUI Label => label ? label : label = GetComponent<TextMeshProUGUI>();
        private void OnEnable()
        {
            manager = GetComponentInParent<WindowManager>(true);
            if (manager) manager.TypographyChanged += PreferenceChanged;
            if (pending || playOnEnable) Replay();
        }
        private void OnDisable()
        {
            if (manager) manager.TypographyChanged -= PreferenceChanged;
            manager = null; Complete();
        }
        private void PreferenceChanged()
        {
            if (IsRevealing && Instant()) Complete();
        }
        private bool Instant() => manager && (manager.ReadingPreferences.reducedMotion || manager.ReadingPreferences.highContrast);

        public void Play(string text)
        {
            Label.text = text ?? string.Empty;
            Replay();
        }
        public void Replay()
        {
            Complete();
            if (!isActiveAndEnabled) { pending = true; return; }
            if (!Application.isPlaying || Instant() || !(duration > .01f) || string.IsNullOrEmpty(Label.text)) return;
            length = Mathf.Clamp(duration, .02f, 3);
            fade = Mathf.Min(.22f, length * .60f);
            travel = length - fade;
            start = Time.unscaledTimeAsDouble;
            IsRevealing = true;
            UseRevealMaterial();
            Label.OnPreRenderText += PrepareMesh;
            Label.havePropertiesChanged = true;
            Label.SetVerticesDirty();
            SetClock();
            animation = StartCoroutine(Animate());
        }
        public void Complete()
        {
            pending = false;
            if (animation != null) { StopCoroutine(animation); animation = null; }
            if (!IsRevealing) return;
            IsRevealing = false;
            Label.OnPreRenderText -= PrepareMesh;
            if (temporary && Label.fontSharedMaterial == temporary) Label.fontSharedMaterial = original;
            if (lease) TMP_MaterialManager.ReleaseFallbackMaterial(lease);
            original = temporary = lease = null;
            fallback.Clear();
            Label.havePropertiesChanged = true;
            Label.SetVerticesDirty();
        }
        public void OnPointerClick(PointerEventData e)
        {
            if (clickToComplete && e.button == PointerEventData.InputButton.Left) Complete();
        }
        private void UseRevealMaterial()
        {
            var font = Label.font;
            var current = Label.fontSharedMaterial;
            // Preserve custom appearances, emoji and bitmap fonts. Non-Letterpress materials use vertex alpha.
            if (!font || !current || current.HasProperty(EdgeId) || current != font.material || !current.HasProperty("_GradientScale")) return;
            if (!paper) paper = Resources.Load<Material>("EasyUI/Typography/Reveal Paper");
            if (!paper || !paper.shader.isSupported || !current.mainTexture) return;
            original = current;
            temporary = paper;
            if (current.mainTexture != paper.mainTexture)
            {
                temporary = TMP_MaterialManager.GetFallbackMaterial(paper, current);
                lease = temporary;
                TMP_MaterialManager.AddFallbackMaterialReference(lease);
            }
            Label.fontSharedMaterial = temporary;
            Label.UpdateMeshPadding();
        }
        private static void SetClock()
        {
            if (clockFrame == Time.frameCount) return;
            clockFrame = Time.frameCount;
            Shader.SetGlobalFloat(ClockId, (float)(Time.unscaledTimeAsDouble % 256.0));
        }
        private IEnumerator Animate()
        {
            while (true)
            {
                yield return null;
                float elapsed = (float)(Time.unscaledTimeAsDouble - start);
                SetClock();
                if (elapsed >= length) break;
                if (fallback.Count > 0 && !Label.havePropertiesChanged)
                {
                    FadeFallback(Label.textInfo, elapsed);
                    Label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                }
            }
            animation = null;
            Complete();
        }

        private void PrepareMesh(TMP_TextInfo info)
        {
            fallback.Clear();
            if (glyphEnds.Length < info.materialCount) System.Array.Resize(ref glyphEnds, info.materialCount);
            System.Array.Clear(glyphEnds, 0, info.materialCount);
            float spread = fade * .28f;
            float perCharacter = travel / Mathf.Max(1, info.characterCount - 1);
            float startClock = (float)(start % 256.0);
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                glyphEnds[c.materialReferenceIndex] = Mathf.Max(glyphEnds[c.materialReferenceIndex], c.vertexIndex + 4);
                Stamp(info, c.materialReferenceIndex, c.vertexIndex, i * perCharacter, startClock, spread);
            }
            // Underlines and highlights are separate TMP quads after the glyphs. Fade them at the tail.
            for (int m = 0; m < info.materialCount; m++)
                for (int v = glyphEnds[m]; v + 3 < info.meshInfo[m].vertexCount; v += 4)
                    Stamp(info, m, v, travel, startClock, spread);
            FadeFallback(info, (float)(Time.unscaledTimeAsDouble - start));
        }
        private void Stamp(TMP_TextInfo info, int m, int v, float delay, float startClock, float spread)
        {
            var mesh = info.meshInfo[m];
            if (mesh.material && mesh.material.HasProperty(EdgeId))
            {
                float left = Label.isRightToLeftText ? spread : 0;
                float right = spread - left;
                float x = -4096f - startClock - delay;
                float rate = 1f / (fade - spread);
                mesh.uvs2[v] = new Vector2(x - left, rate);
                mesh.uvs2[v + 1] = new Vector2(x - left, rate);
                mesh.uvs2[v + 2] = new Vector2(x - right, rate);
                mesh.uvs2[v + 3] = new Vector2(x - right, rate);
            }
            else
            {
                var colors = mesh.colors32;
                fallback.Add(new FadeQuad
                {
                    material = m,
                    vertex = v,
                    delay = delay,
                    alpha = new Color32(colors[v].a, colors[v + 1].a, colors[v + 2].a, colors[v + 3].a)
                });
            }
        }
        private void FadeFallback(TMP_TextInfo info, float elapsed)
        {
            foreach (var q in fallback)
            {
                float p = Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - q.delay) / fade));
                var colors = info.meshInfo[q.material].colors32;
                colors[q.vertex].a = (byte)(q.alpha.r * p);
                colors[q.vertex + 1].a = (byte)(q.alpha.g * p);
                colors[q.vertex + 2].a = (byte)(q.alpha.b * p);
                colors[q.vertex + 3].a = (byte)(q.alpha.a * p);
            }
        }
    }
}
