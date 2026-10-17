using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeLobbyButton : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        [SerializeField] Button button;
        [SerializeField] Image surface;
        [SerializeField] Sprite normal, focused;
        [SerializeField] Material focusedMaterial;
        [SerializeField] NativeLobbyLabel normalLabel, focusedLabel;
        [SerializeField] GameObject arrow;
        [SerializeField] Color disabledSurfaceTint = new Color(1, 1, 1, .4f);
        [SerializeField] Color disabledTextTint = new Color(.72f, .72f, .72f);
        Color authoredSurfaceColor;
        Material authoredSurfaceMaterial;
        Action clicked, hovered;
        void Awake() { authoredSurfaceColor = surface.color; authoredSurfaceMaterial = surface.material; }
        public RectTransform Rect => (RectTransform)transform;
        public void Bind(Action click, Action hover)
        {
            clicked = click; hovered = hover;
            button.onClick.RemoveListener(Click);
            button.onClick.AddListener(Click);
        }
        void Click()
        {
            EventSystem.current?.SetSelectedGameObject(null);
            clicked?.Invoke();
        }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (button.interactable && eventData.button == PointerEventData.InputButton.Left)
                NativeInputBindings.RecordPromptDevice(UnityEngine.InputSystem.Mouse.current);
        }
        public void OnPointerEnter(PointerEventData eventData) { if (button.interactable) hovered?.Invoke(); }
        public void Present(string caption, bool selected, bool enabled)
        {
            button.interactable = enabled;
            surface.sprite = enabled && selected ? focused : normal;
            surface.material = enabled && selected && focusedMaterial ? focusedMaterial : authoredSurfaceMaterial;
            surface.color = authoredSurfaceColor * (enabled ? Color.white : disabledSurfaceTint);
            if (normalLabel) { if (caption != null) normalLabel.Set(caption); normalLabel.Tint(enabled ? Color.white : disabledTextTint); }
            if (focusedLabel) { if (caption != null) focusedLabel.Set(caption); focusedLabel.Tint(enabled ? Color.white : disabledTextTint); }
            if (normalLabel) normalLabel.gameObject.SetActive(!selected || !enabled);
            if (focusedLabel) focusedLabel.gameObject.SetActive(selected && enabled);
            if (arrow) arrow.SetActive(selected && enabled);
        }
    }
}
