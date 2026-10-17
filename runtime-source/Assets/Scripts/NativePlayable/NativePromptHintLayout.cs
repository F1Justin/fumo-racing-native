using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Responsive layout of an authored hint; changing input devices never creates visuals.
    [ExecuteAlways, DefaultExecutionOrder(1100)]
    public sealed class NativePromptHintLayout : LayoutGroup
    {
        public Image icon;
        public TMP_Text caption;
        public RectTransform captionContainer;
        [Min(1)] public float iconHeight = 28;
        [Min(0)] public float iconTextSpacing = 8;
        [Min(1)] public float lineHeight = 36;
        [Min(0)] public float captionEndPadding = 4;
        Sprite lastSprite;
        string lastText;
        float lastFontSize;
        public override int layoutPriority => 2;
        float IconWidth => icon && icon.sprite ? iconHeight * icon.sprite.rect.width / Mathf.Max(1, icon.sprite.rect.height) : iconHeight;
        float CaptionWidth
        {
            get
            {
                if (!caption) return 0;
                var width = caption.GetPreferredValues(caption.text, Mathf.Infinity, Mathf.Infinity).x;
                return Mathf.Max(0, width - Mathf.Max(0, caption.margin.x) - Mathf.Max(0, caption.margin.z));
            }
        }
        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float width = IconWidth + iconTextSpacing + CaptionWidth + captionEndPadding;
            SetLayoutInputForAxis(width, width, 0, 0);
        }
        public override void CalculateLayoutInputVertical() => SetLayoutInputForAxis(lineHeight, lineHeight, 0, 1);
        public override void SetLayoutHorizontal() => Arrange();
        public override void SetLayoutVertical() => Arrange();
        void Arrange()
        {
            if (!icon || !caption) return;
            var glyph = (RectTransform)icon.transform;
            m_Tracker.Add(this, glyph, DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta);
            glyph.anchorMin = glyph.anchorMax = new Vector2(0, .5f);
            glyph.pivot = new Vector2(0, .5f); glyph.anchoredPosition = Vector2.zero;
            glyph.sizeDelta = new Vector2(IconWidth, iconHeight);
            float inset = IconWidth + iconTextSpacing;
            if (caption.transform == transform)
                caption.margin = new Vector4(inset, 0, 0, 0);
            else if (captionContainer)
            {
                m_Tracker.Add(this, captionContainer, DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta);
                captionContainer.anchorMin = Vector2.zero; captionContainer.anchorMax = Vector2.one;
                captionContainer.offsetMin = new Vector2(inset, 0); captionContainer.offsetMax = Vector2.zero;
            }
        }
        void LateUpdate()
        {
            if (!caption || !icon) return;
            if (lastSprite == icon.sprite && lastText == caption.text && lastFontSize == caption.fontSize) return;
            lastSprite = icon.sprite; lastText = caption.text; lastFontSize = caption.fontSize;
            SetDirty();
        }
    }
}
