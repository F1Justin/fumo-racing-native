using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // All geometry, typography, glyphs and bindings belong to the authored Prefab/assets.
    public sealed class NativeOnlineSpectatorView : MonoBehaviour
    {
        [Serializable] public sealed class Prompt
        {
            public string action, caption;
            public Image icon;
            public NativeLobbyLabel label;
            public Sprite keyboard, xbox, playstation;
            public Sprite Glyph(InputDevice device) => device is DualShockGamepad ? playstation
                : device is Gamepad ? xbox : keyboard;
        }
        public InputActionAsset controls;
        public Prompt[] prompts = Array.Empty<Prompt>();
        public void Present(InputDevice device, bool automatic, int cameraMode, float manualFocusRemaining = 0, bool cameraFallback = false)
        {
            foreach (var prompt in prompts)
            {
                if (prompt.action == "Automatic" && !NativeReleaseFeatures.BroadcastEnabled)
                {
                    if (prompt.icon) prompt.icon.gameObject.SetActive(false);
                    if (prompt.label) prompt.label.gameObject.SetActive(false);
                    continue;
                }
                if (prompt.icon) prompt.icon.sprite = prompt.Glyph(device);
                string caption = prompt.action == "Automatic" ? automatic ? manualFocusRemaining > 0 ? "Resume Auto" : "Auto Camera: On" : "Auto Camera: Off"
                    : prompt.action == "View" ? cameraFallback && cameraMode >= 4 ? "Follow (no coverage)" : ViewCaption(cameraMode) : prompt.caption;
                if (prompt.label) prompt.label.Set(caption);
            }
        }
        public static int ModeCount => NativeReleaseFeatures.BroadcastEnabled ? 11 : 4;
        static readonly string[] captions = { "3rd Person / Front", "3rd Person / Rear", "1st Person / Front", "1st Person / Rear",
            "Director View", "Fixed Wide", "Trackside Zoom", "Aerial Tele", "Aerial Wide", "FPV Flight", "Onboard Camera" };
        static readonly NativeBroadcastShotKind[] manualKinds = { NativeBroadcastShotKind.FixedWide, NativeBroadcastShotKind.Trackside,
            NativeBroadcastShotKind.AerialTele, NativeBroadcastShotKind.AerialWide, NativeBroadcastShotKind.Fpv, NativeBroadcastShotKind.Mounted };
        public static string ViewCaption(int mode) => captions[Mathf.Clamp(mode, 0, ModeCount - 1)];
        public static NativeBroadcastShotKind? ManualKind(int mode) => mode >= 5 && mode < ModeCount ? manualKinds[mode - 5] : (NativeBroadcastShotKind?)null;

    }

    // A private action instance prevents spectator A/B/X/Y from sharing menu confirm/cancel.
    internal sealed class NativeSpectatorInput : IDisposable
    {
        readonly InputActionAsset actions;
        readonly System.Collections.Generic.HashSet<string> pressed = new System.Collections.Generic.HashSet<string>();
        public InputDevice Device { get; private set; }
        public NativeSpectatorInput(InputActionAsset source)
        {
            if (!source) throw new InvalidOperationException("Missing authored Spectator Controls input asset");
            actions = UnityEngine.Object.Instantiate(source);
            foreach (var action in actions.FindActionMap("Spectator", true).actions)
                action.performed += context => { Device = context.control.device; NativeInputBindings.RecordPromptDevice(Device); pressed.Add(context.action.name); };
        }
        public void Active(bool active)
        {
            if (actions.enabled == active) return;
            pressed.Clear();
            if (active) actions.Enable(); else actions.Disable();
        }
        public bool Pressed(string action) => pressed.Remove(action);
        public void Dispose() { actions.Disable(); UnityEngine.Object.Destroy(actions); }
    }
}
