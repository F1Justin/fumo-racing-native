using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public static partial class NativeInputBindings
    {
        static InputDevice promptDevice;
        static int promptOwner = -1;
        public static void SetPromptOwner(int human)
        {
            promptOwner = human;
            if (human >= 0 && PromptPlayer != human) { PromptPlayer = human; promptDevice = null; }
        }
        public static int PromptPlayer { get; private set; }
        static void InitializePrompts()
        {
            InputSystem.onActionChange -= PromptActionChanged;
            InputSystem.onActionChange += PromptActionChanged;
        }
        static void PromptActionChanged(object value, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed || !(value is InputAction action)) return;
            var device = action.activeControl?.device;
            if (device == null) return;
            var asset = action.actionMap?.asset;
            if (sharedMenuInput && asset == menuActions) { RecordPromptDevice(device); return; }
            if (sharedMenuInput) return;
            for (int human = 0; human < assets.Length; human++)
                if (asset == assets[human] && (human == 0 || ActualLocalOwner(human)))
                { characterProfileDevice[human] = device; RecordPromptDevice(device, human); return; }
        }
        public static void RecordPromptDevice(InputDevice device, int human = 0)
        {
            if (device == null || (!sharedMenuInput && promptOwner >= 0 && human != promptOwner)) return;
            // Pointer interaction uses the keyboard family; mouse motion alone never changes prompts.
            promptDevice = device is Mouse ? Keyboard.current : device;
            PromptPlayer = human;
            if (sharedMenuInput) menuDevice = promptDevice;
        }
        static bool AllowedPromptDevice(InputDevice device, int human)
        {
            if (sharedMenuInput || !ValidPlayer(human)) return true;
            var devices = assets[human] ? assets[human].devices : null;
            if (!devices.HasValue) return true;
            foreach (var paired in devices.Value) if (paired == device) return true;
            return false;
        }
        public static InputDevice PromptDevice(int human = 0)
        {
            if (sharedMenuInput && menuDevice != null && menuDevice.added && menuDevice.enabled) return menuDevice;
            if (promptDevice != null && promptDevice.added && promptDevice.enabled && human == PromptPlayer && AllowedPromptDevice(promptDevice, human)) return promptDevice;
            return AppliedProfileDevice(human) ?? (InputDevice)Keyboard.current ?? Gamepad.current;
        }
    }
}
