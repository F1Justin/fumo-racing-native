using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace Fumo.NativePlayable
{
    public static class NativeControllerPromptSprites
    {
        public static string Key(InputControl control)
        {
            string name = control.name.ToLowerInvariant();
            if (control.device is Gamepad)
            {
                string button = name == "buttonsouth" ? "a" : name == "buttoneast" ? "b"
                    : name == "buttonwest" ? "x" : name == "buttonnorth" ? "y"
                    : name == "leftshoulder" ? "lb" : name == "rightshoulder" ? "rb"
                    : name == "lefttrigger" ? "lt" : name == "righttrigger" ? "rt"
                    : name == "leftstickpress" ? "lstick" : name == "rightstickpress" ? "rstick"
                    : name == "select" ? "back" : name;
                if (control.device is DualShockGamepad)
                {
                    string sony = button == "a" ? "cross" : button == "b" ? "circle"
                        : button == "x" ? "square" : button == "y" ? "triangle" : button;
                    return "playstation." + sony;
                }
                return "xboxcontroller." + button;
            }
            if (name == "space") name = "spacebar";
            else if (name.EndsWith("arrow")) name = name.Substring(0, name.Length - 5);
            else if (name == "numpadenter") name = "enter2";
            else if (name == "leftctrl" || name == "rightctrl") name = "ctrl";
            else if (name == "leftshift" || name == "rightshift") name = "shift";
            else if (name == "leftalt" || name == "rightalt") name = "alt";
            return "keyboard." + name;
        }
        public static Sprite Find(InputAction action, InputDevice device, NativePromptSpriteCatalog catalog, out string key)
        {
            key = null;
            foreach (var control in action.controls)
            {
                if (control.device != device) continue;
                key = Key(control);
                var sprite = catalog.Find(key);
                if (sprite) return sprite;
            }
            return null;
        }
    }
}
