using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeAuthoredCanvasState : MonoBehaviour
    {
        Canvas target;
        NativeAuthoredUiNode source;
        int order;
        public void Configure(Canvas canvas, NativeAuthoredUiNode definition, int sortingOrder)
        {
            target = canvas; source = definition; order = sortingOrder;
            Apply();
        }
        void OnEnable()
        {
            Apply();
            Canvas.willRenderCanvases += BeforeRender;
        }
        void OnDisable() { Canvas.willRenderCanvases -= BeforeRender; }
        void BeforeRender()
        {
            if (!target || source == null) return;
            if (target.overrideSorting != source.canvasOverrideSorting || target.sortingOrder != order || target.sortingLayerID != source.canvasSortingLayerId)
                Apply();
        }
        void Apply()
        {
            if (!target || source == null) return;
            target.overridePixelPerfect = source.canvasOverridePixelPerfect;
            target.pixelPerfect = source.canvasPixelPerfect;
            target.additionalShaderChannels = (AdditionalCanvasShaderChannels)source.canvasShaderChannels;
            target.enabled = source.canvasEnabled;
            target.sortingLayerID = source.canvasSortingLayerId;
            target.sortingOrder = order;
            // Canvas activation and mode changes update the inherited canvas state first.
            target.overrideSorting = source.canvasOverrideSorting;
        }
    }
}
