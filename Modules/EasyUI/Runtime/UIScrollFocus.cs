using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EasyUI
{
    /// <summary>Keeps a selected keyboard/gamepad control in view inside a vertical ScrollRect.</summary>
    [DisallowMultipleComponent,AddComponentMenu("EasyGameFramework/UI/Keep Selection Visible")]
    public sealed class UIScrollFocus : MonoBehaviour, ISelectHandler
    {
        public void OnSelect(BaseEventData eventData) =>
            Reveal(GetComponentInParent<ScrollRect>(),transform as RectTransform);

        public static void Reveal(ScrollRect scroll,RectTransform target)
        {
            if (!target || !scroll || !scroll.isActiveAndEnabled || !scroll.vertical || !scroll.content) return;
            var viewport=scroll.viewport ? scroll.viewport : scroll.transform as RectTransform;
            if (!viewport) return;
            Canvas.ForceUpdateCanvases();
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(viewport,target);
            var rect=viewport.rect;
            float delta=bounds.max.y>rect.yMax ? rect.yMax-bounds.max.y : Mathf.Max(0,rect.yMin-bounds.min.y);
            if (Mathf.Abs(delta)<.25f) return;
            var offset=scroll.content.anchoredPosition;
            offset.y=Mathf.Clamp(offset.y+delta,0,Mathf.Max(0,scroll.content.rect.height-rect.height));
            scroll.StopMovement();
            scroll.content.anchoredPosition=offset;
        }
    }
}
