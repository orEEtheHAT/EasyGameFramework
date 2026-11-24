using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EasyNarrative.UI
{
    [AddComponentMenu("")]
    public sealed class DialogueReplyFocus : MonoBehaviour, ISelectHandler
    {
        internal ScrollRect Scroll { get; set; }
        public void OnSelect(BaseEventData eventData) =>
            EasyUI.UIScrollFocus.Reveal(Scroll, (RectTransform)transform);
    }
}
