using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    // The default style lives on TMP. Optional language variants are real authored assets.
    [RequireComponent(typeof(TMP_Text))]
    public sealed class NativeLobbyTypography : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset chineseFont;
        [SerializeField] Material chineseMaterial;
        [SerializeField] bool overrideChineseSizing;
        [SerializeField] bool chineseAutoSize = true;
        [SerializeField] float chineseFontSize = 72;
        [SerializeField] float chineseFontSizeMin = 18;
        [SerializeField] float chineseFontSizeMax = 72;
        [SerializeField] bool overrideChineseMargin;
        [SerializeField] Vector4 chineseMargin;
        TMP_Text text;
        TMP_FontAsset authoredFont;
        Material authoredMaterial;
        bool authoredAutoSize;
        float authoredFontSize, authoredFontSizeMin, authoredFontSizeMax;
        Vector4 authoredMargin;
        bool initialized;
        int language = -1;

        void Awake() => Capture();
        void OnEnable() { Capture(); language = -1; Refresh(); }
        void LateUpdate() => Refresh();
        void Capture()
        {
            if (initialized) return;
            text = GetComponent<TMP_Text>();
            authoredFont = text.font;
            authoredMaterial = text.fontSharedMaterial;
            authoredAutoSize = text.enableAutoSizing;
            authoredFontSize = text.fontSize;
            authoredFontSizeMin = text.fontSizeMin;
            authoredFontSizeMax = text.fontSizeMax;
            authoredMargin = text.margin;
            initialized = true;
        }
        public void Refresh()
        {
            Capture();
            if (language == NativeLocalization.Language) return;
            language = NativeLocalization.Language;
            bool alternate = NativeLocalization.IsChinese && chineseFont;
            text.font = alternate ? chineseFont : authoredFont;
            text.fontSharedMaterial = alternate ? (chineseMaterial ? chineseMaterial : chineseFont.material) : authoredMaterial;
            if (overrideChineseSizing)
            {
                text.enableAutoSizing = alternate ? chineseAutoSize : authoredAutoSize;
                text.fontSize = alternate ? chineseFontSize : authoredFontSize;
                text.fontSizeMin = alternate ? chineseFontSizeMin : authoredFontSizeMin;
                text.fontSizeMax = alternate ? chineseFontSizeMax : authoredFontSizeMax;
            }
            if (overrideChineseMargin) text.margin = alternate ? chineseMargin : authoredMargin;
        }
        public bool UsesAuthoredFont => text && text.font ==
            (NativeLocalization.IsChinese && chineseFont ? chineseFont : authoredFont);
    }
}
