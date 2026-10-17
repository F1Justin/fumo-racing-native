using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        [Serializable] public sealed class RequiredLeafSample
        {
            public string path, failure, contentKind;
            public bool fieldEligible, active, enabled, culled, canvasEnabled;
            public float effectiveAlpha, viewportFraction;
            public int meshVertices;
            public Rect screenRect;
            public string[] clippingAncestors;
            public bool framebufferReviewed;
        }
        // Field eligibility selects a framebuffer review candidate; it never establishes rendered visibility.
        public RequiredLeafSample ReadRequiredLeaf(string path)
        {
            var sample = new RequiredLeafSample { path = path, clippingAncestors = new string[0] };
            if (!transforms.TryGetValue(path, out var node)) { sample.failure = "missing-node"; return sample; }
            return ReadGraphicRequiredLeaf(path, node.GetComponent<Graphic>());
        }
        public static RequiredLeafSample ReadGraphicRequiredLeaf(string path, Graphic graphic)
        {
            var sample = new RequiredLeafSample { path=path, clippingAncestors=new string[0] };
            if (!graphic) { sample.failure="missing-graphic"; return sample; }
            var node = graphic.rectTransform;
            var canvas = graphic.canvas;
            sample.active = node.gameObject.activeInHierarchy;
            sample.enabled = graphic.enabled;
            sample.culled = graphic.canvasRenderer.cull;
            sample.canvasEnabled = canvas && canvas.isActiveAndEnabled && node.GetComponentsInParent<Canvas>(true).All(c => c.isActiveAndEnabled);
            sample.effectiveAlpha = graphic.color.a * graphic.canvasRenderer.GetAlpha() * graphic.canvasRenderer.GetInheritedAlpha();
            var corners = new Vector3[4]; node.GetWorldCorners(corners);
            var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var points = corners.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
            float left = points.Min(p => p.x), right = points.Max(p => p.x), bottom = points.Min(p => p.y), top = points.Max(p => p.y);
            sample.screenRect = new Rect(left, bottom, right - left, top - bottom);
            float area = sample.screenRect.width * sample.screenRect.height;
            sample.viewportFraction = area > 0 ? Mathf.Max(0, Mathf.Min(right, Screen.width) - Mathf.Max(left, 0)) * Mathf.Max(0, Mathf.Min(top, Screen.height) - Mathf.Max(bottom, 0)) / area : 0;
            sample.clippingAncestors = node.GetComponentsInParent<Mask>(true).Where(m => m.isActiveAndEnabled).Select(m => m.name)
                .Concat(node.GetComponentsInParent<RectMask2D>(true).Where(m => m.isActiveAndEnabled).Select(m => m.name)).ToArray();
            var text = graphic as TMP_Text; var legacyText = graphic as Text; var raw = graphic as RawImage; var image = graphic as Image;
            bool content;
            if (text) { sample.contentKind = "text-mesh"; sample.meshVertices = text.textInfo?.meshInfo?.Sum(m => m.vertexCount) ?? 0; content = text.textInfo != null && text.textInfo.characterCount > 0 && sample.meshVertices > 0; }
            else if (legacyText) { sample.contentKind = "ugui-text-mesh"; sample.meshVertices = legacyText.cachedTextGenerator.vertexCount; content = !string.IsNullOrEmpty(legacyText.text) && sample.meshVertices > 0; }
            else if (raw) { sample.contentKind = "raw-texture-binding"; content = raw.texture; }
            else if (image) { sample.contentKind = "image-geometry"; content = image.type != Image.Type.Filled || image.fillAmount > 0; }
            else { sample.contentKind = "unsupported"; content = false; }
            sample.fieldEligible = sample.active && sample.enabled && !sample.culled && sample.canvasEnabled && sample.effectiveAlpha > .001f && sample.viewportFraction > 0 && content;
            sample.failure = sample.fieldEligible ? "framebuffer-content-clipping-occlusion-review-required" : "field-ineligible";
            return sample;
        }
    }
}
