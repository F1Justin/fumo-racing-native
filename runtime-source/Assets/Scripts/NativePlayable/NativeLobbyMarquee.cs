using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeLobbyMarquee : MonoBehaviour
    {
        [SerializeField] RectTransform viewport;
        [SerializeField] TMP_Text foreground, shadow;
        [SerializeField, Min(0)] float speed = 35, pause = 1.2f;
        Vector2 foregroundPosition, shadowPosition;
        float foregroundWidth, shadowWidth;
        bool captured;
        void Awake() => Capture();
        void Capture()
        {
            if (captured) return;
            foregroundPosition = foreground.rectTransform.anchoredPosition;
            foregroundWidth = foreground.rectTransform.rect.width;
            if (shadow)
            {
                shadowPosition = shadow.rectTransform.anchoredPosition;
                shadowWidth = shadow.rectTransform.rect.width;
            }
            captured = true;
        }
        public void Set(string value)
        {
            foreground.richText = false; if (shadow) shadow.richText = false;
            if (foreground.text != value) foreground.text = value;
            if (shadow && shadow.text != value) shadow.text = value;
        }
        void LateUpdate() => Refresh();
        public void Refresh()
        {
            Capture();
            // Animate relative to the saved text offsets, never a hard-coded text/shadow origin.
            float width = Mathf.Max(viewport.rect.width, foreground.GetPreferredValues(foreground.text).x + 2);
            float travel = Mathf.Max(0, width - viewport.rect.width), offset = 0;
            if (travel > 0 && speed > 0)
            {
                float duration = travel / speed, delay = Mathf.Max(0, pause);
                float cycle = Time.unscaledTime % (duration * 2 + delay * 2);
                offset = cycle < delay ? 0 : cycle < delay + duration ? (cycle - delay) * speed
                    : cycle < delay * 2 + duration ? travel : travel - (cycle - delay * 2 - duration) * speed;
            }
            foreground.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(foregroundWidth, width));
            foreground.rectTransform.anchoredPosition = foregroundPosition + Vector2.left * offset;
            if (shadow)
            {
                shadow.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(shadowWidth, width));
                shadow.rectTransform.anchoredPosition = shadowPosition + Vector2.left * offset;
            }
        }
    }
}
