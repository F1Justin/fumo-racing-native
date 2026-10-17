using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // The original pause TMP is the visible control. Only interaction state changes.
    public sealed class NativeOnlinePauseOption : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        [SerializeField] Button button;
        [SerializeField] NativeLobbyLabel label;
        [SerializeField] Color selectedColor = new Color(1, .77873516f, 0, 1);
        [SerializeField] Color disabledTint = new Color(.55f, .55f, .55f, 1);
        Action hovered;
        public void Bind(Action click, Action hover)
        {
            button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => click?.Invoke());
            hovered = hover;
        }
        public void Present(string caption, bool selected, bool enabled)
        {
            button.interactable = enabled;
            if (caption != null) label.Set(caption);
            label.StateColor(enabled && selected ? selectedColor : (Color?)null);
            if (!enabled) label.Tint(disabledTint);
        }
        public void OnPointerEnter(PointerEventData data) { if (button.interactable) hovered?.Invoke(); }
        public void OnPointerDown(PointerEventData data)
        {
            if (button.interactable && data.button == PointerEventData.InputButton.Left)
                NativeInputBindings.RecordPromptDevice(UnityEngine.InputSystem.Mouse.current);
        }
    }
}
