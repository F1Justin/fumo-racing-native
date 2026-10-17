using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeLobbyResponsiveWidth : MonoBehaviour
    {
        [SerializeField] RectTransform canvasRect;
        [SerializeField] bool adaptToCanvas = true;
        [SerializeField, Min(1)] float referenceWidth = 960;
        [SerializeField, Min(0)] float maximumExpansion = 36;
        float authoredWidth;
        bool captured;
        void Awake() => Capture();
        void Capture()
        {
            if (captured) return;
            authoredWidth = ((RectTransform)transform).rect.width;
            captured = true;
        }
        void LateUpdate() => Refresh();
        public void Refresh()
        {
            if (!canvasRect || !adaptToCanvas) return;
            Capture();
            var rect = (RectTransform)transform;
            float width = Mathf.Max(1, authoredWidth + Mathf.Min(maximumExpansion, canvasRect.rect.width - referenceWidth));
            if (!Mathf.Approximately(rect.rect.width, width))
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}
