using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Stable binding identity; the actual visuals and geometry are authored on this GameObject.
    public sealed class NativeOnlineElement : MonoBehaviour
    {
        public string key;
        public NativeLobbyLabel label;
        public NativeLobbyButton button;
        public NativeOnlinePauseOption pauseOption;
        public Image image;
        public RawImage picture;
        public TMP_InputField input;
        public bool bindText = true, bindImage;
        [SerializeField] Color selectedTint = new Color(1, .86f, .5f, 1);
        Color authoredTint;
        bool authoredActive, initialized, pressed;
        public RectTransform Rect => (RectTransform)transform;
        public void Initialize()
        {
            if (initialized) return;
            initialized = true; authoredActive = gameObject.activeSelf;
            if (image) authoredTint = image.color;
            if (button) button.Bind(() => pressed = true, () => Hovered?.Invoke());
            if (pauseOption) pauseOption.Bind(() => pressed = true, () => Hovered?.Invoke());
        }
        public Action Hovered;
        public void Visible(bool visible) { Initialize(); gameObject.SetActive(visible && authoredActive); }
        public void SelectRow(bool selected)
        { Initialize(); if (image) image.color = selected ? selectedTint : authoredTint; }
        public bool ConsumeClick() { bool value = pressed; pressed = false; return value; }
        public void Text(string value, bool localize)
        { if (bindText && label) label.Set(value, localize); }
        public bool PresentButton(string value, bool selected, bool enabled)
        {
            if (!authoredActive) return false;
            if (pauseOption)
            {
                pauseOption.Present(bindText ? value : null, selected, enabled);
                return enabled && ConsumeClick();
            }
            if (!button) return false;
            button.Present(bindText ? value : null, selected, enabled);
            return enabled && ConsumeClick();
        }
    }
}
