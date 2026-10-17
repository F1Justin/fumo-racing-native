using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeOnlineAuthoredNode : MonoBehaviour
    {
        public NativeAuthoredUiNode source;
        public bool bindLocalizedText = true, allowRuntimeVisibility = true;
        public NativeAuthoredUiNode Read()
        {
            var node = source.Copy();
            var rect = (RectTransform)transform;
            node.position = rect.anchoredPosition; node.size = rect.sizeDelta; node.anchorMin = rect.anchorMin;
            node.anchorMax = rect.anchorMax; node.pivot = rect.pivot; node.rotation = rect.localRotation; node.scale = rect.localScale;
            node.active = gameObject.activeSelf;
            if (TryGetComponent<Image>(out var image))
            { node.sprite = image.sprite; node.color = image.color; node.imageOverride = image.material; }
            if (TryGetComponent<TMP_Text>(out var text))
            {
                node.fontSize = text.fontSize; node.minSize = text.fontSizeMin; node.maxSize = text.fontSizeMax;
                node.autoSize = text.enableAutoSizing; node.fontStyle = (int)text.fontStyle; node.alignment = (int)text.alignment;
                node.margin = text.margin; node.fontColor = text.color; node.textMaterialOverride = text.fontSharedMaterial;
                if (!bindLocalizedText) { node.text = text.text; node.phrase = null; }
            }
            if (TryGetComponent<GridLayoutGroup>(out var grid)) { node.cellSize = grid.cellSize; node.spacing = grid.spacing; }
            if (TryGetComponent<HorizontalLayoutGroup>(out var layout)) node.layoutSpacing = layout.spacing;
            return node;
        }
    }
}
