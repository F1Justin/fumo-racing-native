using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Scroll state only. The viewport, content, row layout and scrollbar live in the prefab.
    public sealed class NativeOnlineRoomScroll : MonoBehaviour
    {
        [SerializeField] ScrollRect scroll;
        [SerializeField, Min(0)] float focusPadding = 4;
        string focusKey;
        RectTransform focusedRow;
        public ScrollRect Scroll => scroll;

        public void Focus(string key, RectTransform row)
        {
            if (!scroll || !row) { focusKey = null; focusedRow = null; return; }
            if (focusKey == key && focusedRow == row) return;
            focusKey = key; focusedRow = row;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; row.GetWorldCorners(corners);
            float bottom = scroll.viewport.InverseTransformPoint(corners[0]).y;
            float top = scroll.viewport.InverseTransformPoint(corners[2]).y;
            var viewport = scroll.viewport.rect;
            float delta = top > viewport.yMax - focusPadding ? viewport.yMax - focusPadding - top
                : bottom < viewport.yMin + focusPadding ? viewport.yMin + focusPadding - bottom : 0;
            if (Mathf.Approximately(delta, 0)) return;
            scroll.StopMovement();
            var position = scroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + delta, 0, Mathf.Max(0, scroll.content.rect.height - viewport.height));
            scroll.content.anchoredPosition = position;
        }
    }
}
