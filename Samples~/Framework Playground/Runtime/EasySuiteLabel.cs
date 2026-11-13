using EasyLocalization;
using UnityEngine;
using UnityEngine.UI;

namespace EasySuite.Example
{
    [RequireComponent(typeof(LocalizedText))]
    public sealed class EasySuiteLabel : MonoBehaviour
    {
        public Vector2 MeshBounds;
        private LocalizedText source;
        private Text text;
        private TextMesh mesh;
        private bool fitMesh;
        private void OnEnable()
        {
            source = GetComponent<LocalizedText>(); text = GetComponent<Text>(); mesh = GetComponent<TextMesh>();
            source.textChanged.AddListener(Render); Render(source.Value);
        }
        private void OnDisable() { if (source) source.textChanged.RemoveListener(Render); }
        private void Render(string value) { if (text) text.text = value; if (mesh) { mesh.text = value; fitMesh = true; } }
        private void LateUpdate()
        {
            if (!fitMesh || !mesh || MeshBounds.x <= 0 || MeshBounds.y <= 0) return;
            var size = mesh.GetComponent<Renderer>().localBounds.size;
            if (size.x <= 0 || size.y <= 0) return;
            transform.localScale = Vector3.one * Mathf.Min(1, MeshBounds.x / size.x, MeshBounds.y / size.y);
            fitMesh = false;
        }
    }
}
