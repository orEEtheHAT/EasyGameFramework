using UnityEngine;
using UnityEngine.UI;

namespace EasyQuests.UI
{
    public enum QuestGlyph { Target, Direction, SearchRing }

    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Marker Graphic")]
    public class QuestMarkerGraphic : MaskableGraphic
    {
        private static Material standard;
        private QuestGlyph shape;
        private float mode;
        public QuestGlyph Shape => shape;
        protected virtual QuestGlyph InitialShape => QuestGlyph.Target;
        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            SetStyle(InitialShape, color, false, false, m_Material);
        }

        public void SetStyle(QuestGlyph glyph, Color tint, bool animate, bool highContrast, Material custom = null)
        {
            if (!standard) standard = Resources.Load<Material>("EasyNarrative/Wayfinder");
            var selected = custom ? custom : standard;
            bool nativeMesh = material == defaultMaterial;
            material = selected && selected.shader.isSupported ? selected : null;
            if (nativeMesh != (material == defaultMaterial)) SetVerticesDirty();
            color = highContrast ? Color.white : tint;
            float next = highContrast ? -1 : animate ? 1 : 0;
            if (shape == glyph && mode == next) return;
            shape = glyph;
            mode = next;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            if (shape == QuestGlyph.SearchRing) { Ring(mesh, rect); return; }
            if (material == defaultMaterial)
            {
                Add(mesh, rect, new Vector2(0, 1)); Add(mesh, rect, new Vector2(-1, -.4f));
                Add(mesh, rect, new Vector2(0, shape == QuestGlyph.Target ? -1 : 0)); Add(mesh, rect, new Vector2(1, -.4f));
            }
            else
            {
                Add(mesh, rect, new Vector2(-1, -1)); Add(mesh, rect, new Vector2(-1, 1));
                Add(mesh, rect, new Vector2(1, 1)); Add(mesh, rect, new Vector2(1, -1));
            }
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3);
        }
        private void Add(VertexHelper mesh, Rect rect, Vector2 p) =>
            Vert(mesh, rect.center + Vector2.Scale(p, rect.size) * .5f, p.x, p.y);
        private void Vert(VertexHelper mesh, Vector2 pos, float u, float v)
        {
            var vert = UIVertex.simpleVert;
            vert.position = pos; vert.color = color; vert.uv0 = new Vector4(u, v, (float)shape, mode);
            mesh.AddVert(vert);
        }
        private void Ring(VertexHelper mesh, Rect rect)
        {
            float radius = Mathf.Min(rect.width, rect.height) * .5f;
            float inner = Mathf.Max(0, radius - (material == defaultMaterial ? 2 : 8));
            int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(radius) * 5), 32, 160);
            for (int i = 0; i <= segments; i++)
            {
                float turn = (float)i / segments;
                float a = turn * Mathf.PI * 2;
                var radial = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vert(mesh, rect.center + radial * radius, 1, turn);
                Vert(mesh, rect.center + radial * inner, 0, turn);
                if (i == 0) continue;
                int at = i * 2;
                mesh.AddTriangle(at - 2, at - 1, at); mesh.AddTriangle(at, at - 1, at + 1);
            }
        }
    }
}
