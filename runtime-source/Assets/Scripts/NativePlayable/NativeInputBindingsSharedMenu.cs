using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public static partial class NativeInputBindings
    {
        static bool sharedMenuInput = true;
        internal static bool SharedMenuInputActive => sharedMenuInput;
        static InputActionAsset menuActions;
        static InputDevice menuDevice;
        static Vector2 menuNavigateVector;
        static int menuNavigateFrame = -1;
        public static void SetSharedMenuInput(bool active)
        {
            sharedMenuInput = active;
            if (active) { SetPromptOwner(-1); PromptPlayer = 0; MenuActionAsset(); }
        }
        static void InvalidateSharedMenu()
        {
            if (!menuActions) return;
            menuActions.Disable(); Object.Destroy(menuActions); menuActions = null;
        }
        static InputActionAsset MenuActionAsset()
        {
            if (menuActions) return menuActions;
            menuActions = InputActionAsset.FromJson(NativeContent.Load<TextAsset>("NativeSettings/controls-runtime").text);
            EnsureGamepadBindings(menuActions);
            // Public menus accept every connected controller without changing any race owner's pairing.
            menuActions.devices = null;
            var profile = Find(selected[0]);
            var profileDevice = profile == null ? null : CompatibleDevice(profile.devicePath);
            if (profileDevice != null)
                foreach (var pair in profile.overrides) ApplyOverride(menuActions, pair.actionId, pair.path, profileDevice);
            foreach (var action in menuActions.FindActionMap("Normal", true).actions)
                action.started += context =>
                {
                    if (!sharedMenuInput) return;
                    menuDevice = context.control.device;
                    if (context.action.name == "Navigate")
                    { menuNavigateVector = context.ReadValue<Vector2>(); menuNavigateFrame = Time.frameCount; }
                };
            menuActions.FindActionMap("Normal", true).Enable();
            return menuActions;
        }
        public static InputActionAsset ActiveActionAsset(int player = 0) =>
            player == 0 && sharedMenuInput ? MenuActionAsset() : RaceActionAsset(player);
    }
    public sealed partial class NativeRace
    {
        void SynchronizeSharedMenuInput() => NativeInputBindings.SetSharedMenuInput(
            phase == Phase.Menu && !localSelectionActive && !CharacterProfilePage);
    }
}
