using System;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    public interface INarrativeGraphInspector
    {
        NarrativeGraphPanel CreatePanel(EditorWindow host);
        void DrawSettings();
    }

    public abstract class NarrativeGraphPanel : IDisposable
    {
        private readonly EditorWindow host;
        protected NarrativeGraphPanel(EditorWindow host) => this.host = host;
        protected void Repaint() => host.Repaint();
        protected void ShowNotification(GUIContent content) => host.ShowNotification(content);
        public abstract string SelectedNode { get; }
        public abstract void FocusNode(string id);
        public virtual void FocusReply(string replyId) { }
        public abstract void Draw(Vector2 size);
        public virtual void OnUndo() => Repaint();
        public virtual void LostFocus() { }
        public abstract void Dispose();
    }
}
