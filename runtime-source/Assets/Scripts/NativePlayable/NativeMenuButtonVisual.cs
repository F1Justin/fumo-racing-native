using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // The prefab owns the idle appearance and layout; callers supply only state.
    [DisallowMultipleComponent]
    public sealed class NativeMenuButtonVisual : MonoBehaviour
    {
        public Sprite selectedSprite;
        public float focusWidth = 40;
        public Color disabledTint = new Color(.78431374f, .78431374f, .78431374f, .5019608f);
        Image image;
        TMP_Text[] labels;
        Color[] labelColors;
        Color idleColor;
        Sprite idleSprite;
        bool captured;

        public void Apply(Vector2 authoredSize, float focus, bool selected, bool disabled)
        {
            if (!captured)
            {
                image = GetComponent<Image>(); idleColor = image.color; idleSprite = image.sprite;
                labels = GetComponentsInChildren<TMP_Text>(true);
                labelColors = new Color[labels.Length];
                for (int i = 0; i < labels.Length; i++) labelColors[i] = labels[i].color;
                captured = true;
            }
            ((RectTransform)transform).sizeDelta = authoredSize + new Vector2(focusWidth * focus, 0);
            image.sprite = selected && selectedSprite ? selectedSprite : idleSprite;
            image.overrideSprite = null;
            image.color = disabled ? idleColor * disabledTint : idleColor;
            for (int i = 0; i < labels.Length; i++)
                labels[i].color = disabled ? labelColors[i] * disabledTint : labelColors[i];
        }
    }
}
