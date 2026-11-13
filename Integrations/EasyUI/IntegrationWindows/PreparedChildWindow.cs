using UnityEngine;
using EasyUI.Integrations.Feedbacks;

namespace EasyUI.Integrations
{
    // A child belongs to one visible parent. Preparing before Open ensures feedback/input callbacks see its bindings.
    internal sealed class PreparedChildWindow
    {
        private UIWindow instance;
        private WindowDefinition definition;
        private bool inheritContext, inheritOpen;
        internal UIWindow Prepare(WindowDefinition source, UIWindow parent)
        {
            var handle = parent ? parent.Handle : null;
            if (!source || handle?.IsOpen != true || !parent.isActiveAndEnabled) return null;
            if (!instance || source != definition)
            {
                Dispose();
                if (!parent || !parent.isActiveAndEnabled || parent.Handle != handle || !handle.IsOpen) return null;
                instance = IntegrationWindowSet.InstantiateWindow(source, parent.transform); definition = source;
                var feedback = instance.GetComponent<WindowFeedbacks>();
                if (feedback) { inheritContext = !feedback.ContextProvider; inheritOpen = !feedback.Open; }
            }
            var childFeedback = instance.GetComponent<WindowFeedbacks>();
            var parentFeedback = parent.GetComponent<WindowFeedbacks>();
            if (childFeedback)
            {
                if (inheritContext) childFeedback.ContextProvider = parentFeedback ? parentFeedback.ContextProvider : null;
                if (inheritOpen) childFeedback.Open = parentFeedback ? parentFeedback.Open : null;
            }
            return instance;
        }
        internal void Dispose()
        {
            var child = instance; instance = null; definition = null;
            if (!child) return;
            var handle = child.Handle;
            if (handle?.Manager) handle.Manager.Close(handle, true);
            if (Application.isPlaying) Object.Destroy(child.gameObject); else Object.DestroyImmediate(child.gameObject);
        }
    }
}
