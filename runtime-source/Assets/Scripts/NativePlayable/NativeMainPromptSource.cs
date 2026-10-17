using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Fumo.NativePlayable
{
    public static class NativeMainPromptSource
    {
        [Serializable] public sealed class State
        {
            public string action, actionId, device, scheme, bindingGroup, displayBinding, dictionaryKey, spriteSourcePath, unknownReason;
            public int productionFrame, spriteInstanceId, textureInstanceId;
            public string sourcePath;
            public bool closed, spriteFound, originalPixelsReviewed, hardwareInputReviewed;
            [NonSerialized] public Sprite sprite;
        }
        static NativePromptSpriteCatalog catalog;
        public static string Display(NativeInputBindings.Action action)
        {
            var state = Read(action);
            switch (state.dictionaryKey)
            {
                case "playstation.cross": return "×";
                case "playstation.circle": return "○";
                case "playstation.square": return "□";
                case "playstation.triangle": return "△";
                case "xboxcontroller.a": return "A";
                case "xboxcontroller.b": return "B";
                case "xboxcontroller.x": return "X";
                case "xboxcontroller.y": return "Y";
                default: return state.displayBinding ?? "";
            }
        }
        public static State Read(NativeInputBindings.Action requested, int human = -1) =>
            Read(requested.ToString(), human < 0 ? NativeInputBindings.PromptPlayer : human);
        public static State Read(string requested, int human = 0, InputDevice device = null, NativePromptSpriteCatalog sprites = null, string keyboardControl = null)
        {
            var state = new State { action = requested, productionFrame = Time.frameCount };
            var asset = NativeInputBindings.ActiveActionAsset(human);
            device = device ?? NativeInputBindings.PromptDevice(human);
            if (!asset || device == null) { state.unknownReason = "prompt-device-unresolved"; return state; }
            var action = asset.FindAction("Normal/" + requested, false);
            if (action == null) { state.unknownReason = "prompt-action-unresolved"; return state; }
            state.actionId = action.id.ToString();
            state.device = device.deviceId + ":" + device.layout + ":" + device.displayName;
            state.scheme = state.bindingGroup = device is Gamepad ? "GamePadControls" : "KeyboardControls";
            if (!catalog) catalog = NativeContent.Load<NativePromptSpriteCatalog>("NativeMenu/Prompts/Catalog");
            sprites = sprites ? sprites : catalog;
            State PresentControl(InputControl control, string display)
            {
                state.displayBinding = display;
                state.dictionaryKey = NativeControllerPromptSprites.Key(control);
                state.sprite = sprites ? sprites.Find(state.dictionaryKey) : null;
                state.spriteFound = state.sprite;
                if (state.spriteFound)
                    foreach (var entry in sprites.entries) if (entry.spriteName == state.dictionaryKey) { state.spriteSourcePath = entry.sourcePath; break; }
                state.spriteInstanceId = state.sprite ? state.sprite.GetInstanceID() : 0;
                state.textureInstanceId = state.sprite && state.sprite.texture ? state.sprite.texture.GetInstanceID() : 0;
                state.closed = true; return state;
            }
            // Text editing reserves literal keys independently of gameplay/profile remapping.
            if (device is Keyboard && !string.IsNullOrEmpty(keyboardControl))
            {
                var control = device.TryGetChildControl(keyboardControl);
                if (control != null) return PresentControl(control, control.displayName);
                state.unknownReason = "prompt-text-control-unresolved"; return state;
            }
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite || string.IsNullOrEmpty(binding.effectivePath)) continue;
                foreach (var control in action.controls)
                {
                    if (control.device != device || !InputControlPath.Matches(binding.effectivePath, control)) continue;
                    return PresentControl(control, action.GetBindingDisplayString(i));
                }
            }
            state.unknownReason = "prompt-device-has-no-binding";
            return state;
        }
    }
}
