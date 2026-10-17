using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Fumo.NativePlayable
{
    // Pointer events come from the authored Canvas; hit regions are read from saved RectTransforms.
    public sealed class NativeOnlinePointer : MonoBehaviour, IPointerClickHandler
    {
        public int authoringVersion;
        public NativeOnlineMotionSettings motion;
        Vector2 position;
        int frame = -100;
        bool consumed;
        void OnEnable()
        {
            if (Application.isPlaying) EnsureEventSystem();
        }
        // Hover is read by menu logic; clicks and text fields need a UI module on every page.
        public static void EnsureEventSystem()
        {
            var system = EventSystem.current;
            if (!system)
            {
                var events = new GameObject("Native menu pointer input", typeof(EventSystem));
                DontDestroyOnLoad(events); system = events.GetComponent<EventSystem>();
            }
            var module = system.GetComponent<InputSystemUIInputModule>();
            if (!module)
            {
                module = system.gameObject.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
            foreach (var other in system.GetComponents<BaseInputModule>())
                if (other != module) other.enabled = false;
            module.enabled = true;
            NativeTextInputBridge.Attach(module);
            // Menu actions own keyboard/gamepad navigation and confirmation.
            // Letting the UI module submit too would execute the focused button twice.
            system.sendNavigationEvents = false;
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            NativeInputBindings.RecordPromptDevice(UnityEngine.InputSystem.Mouse.current);
            position = new Vector2(eventData.position.x, Screen.height - eventData.position.y) * (600f / Screen.height);
            frame = Time.frameCount; consumed = false;
        }
        public bool Consume(Rect rect)
        {
            if (consumed || Time.frameCount - frame > 2 || !rect.Contains(position)) return false;
            consumed = true; return true;
        }
    }
}
