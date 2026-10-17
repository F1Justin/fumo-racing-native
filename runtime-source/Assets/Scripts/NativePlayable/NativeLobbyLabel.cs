using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeLobbyLabel : MonoBehaviour
    {
        [SerializeField] TMP_Text foreground, shadow;
        Color authoredColor;
        bool captured;
        void Awake() => CaptureColor();
        void CaptureColor()
        {
            if (captured || !foreground) return;
            authoredColor = foreground.color; captured = true;
        }
        public void Set(string value, bool localize = true)
        {
            string text = localize ? NativeLocalization.TranslateText(value) : value;
            if (!localize)
            { if (foreground) foreground.richText = false; if (shadow) shadow.richText = false; }
            if (foreground && foreground.text != text) foreground.text = text;
            if (shadow && shadow.text != text) shadow.text = text;
        }
        public void Tint(Color multiplier)
        { CaptureColor(); if (foreground) foreground.color = authoredColor * multiplier; }
        public void StateColor(Color? color)
        { CaptureColor(); if (foreground) foreground.color = color ?? authoredColor; }
    }
}
