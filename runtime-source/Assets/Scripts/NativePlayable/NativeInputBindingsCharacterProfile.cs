using System;
using System.Collections.Generic;
using UnityEngine.InputSystem.Controls;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Fumo.NativePlayable
{
    public static partial class NativeInputBindings
    {
        static readonly int[,] characterProfileStarted = new int[4,3];
        static readonly InputDevice[] characterProfileDevice = new InputDevice[4];
        static readonly List<ButtonControl>[] characterProfileConsumedPress = new List<ButtonControl>[4];
        static void CaptureCharacterProfilePress(int human)
        {
            var held = new List<ButtonControl>();
            foreach (var action in new[] { Action.Use, Action.Ok })
            {
                var input = Get(action,human);
                if (input == null) continue;
                foreach (var control in input.controls)
                    if (control is ButtonControl button && button.isPressed && !held.Contains(button)) held.Add(button);
            }
            characterProfileConsumedPress[human] = held;
        }
        static bool CharacterProfileReplay(Action action, int human, InputControl control = null)
        {
            if (!ValidPlayer(human)) return false;
            var held = characterProfileConsumedPress[human];
            if (held == null) return false;
            held.RemoveAll(button => !button.device.added || !button.device.enabled || !button.isPressed);
            if (held.Count == 0) { characterProfileConsumedPress[human] = null; return false; }
            var actual = control ?? Get(action,human)?.activeControl;
            return actual is ButtonControl button && held.Contains(button);
        }
        static int CharacterProfileActionIndex(Action action) => action == Action.Use ? 0 : action == Action.Ok ? 1 : action == Action.Cancel ? 2 : -1;
        static void TrackCharacterProfileActions(InputActionAsset asset, int human)
        {
            for (int index = 0; index < 3; index++) characterProfileStarted[human,index] = -1;
            foreach (var action in new[] { Action.Use, Action.Ok, Action.Cancel })
            {
                var input = asset.FindAction("Normal/" + action, false);
                if (input == null) continue;
                int index = CharacterProfileActionIndex(action);
                input.started += context =>
                {
                    if (CharacterProfileReplay(action,human,context.control)) return;
                    characterProfileStarted[human,index] = Time.frameCount; characterProfileDevice[human] = context.control?.device;
                };
                input.canceled += context =>
                {
                    var held = characterProfileConsumedPress[human];
                    held?.RemoveAll(button => !button.device.added || !button.device.enabled || !button.isPressed);
                };
            }
            var navigate = asset.FindAction("Normal/Navigate", false);
            if (navigate != null) navigate.started += context => characterProfileDevice[human] = context.control?.device;
        }
        public static bool GetCharacterProfileStarted(Action action, int human = 0)
        {
            int index = CharacterProfileActionIndex(action);
            return ValidPlayer(human) && index >= 0 && !CharacterProfileReplay(action,human) && characterProfileStarted[human,index] == Time.frameCount;
        }
        public static InputDevice AppliedProfileDevice(int human = 0)
        {
            if (!ValidPlayer(human)) return null;
            var actual = human == 0 && sharedMenuInput ? menuDevice : characterProfileDevice[human];
            if (actual != null && actual.added && actual.enabled) return actual;
            var input = Get(Action.Use,human);
            var devices = input?.actionMap.asset.devices;
            return devices.HasValue && devices.Value.Count == 1 ? devices.Value[0] : null;
        }
        public static string AppliedDeviceIdentity(int human = 0)
        {
            var device = AppliedProfileDevice(human);
            return device == null ? "" : device.deviceId + ":" + device.layout + ":" + device.description.interfaceName;
        }
        public static bool CanSelectProfile(string profileId, int human = 0)
        {
            if (!ValidPlayer(human)) return false;
            var device = AppliedProfileDevice(human);
            if (device == null) return false;
            if (string.IsNullOrEmpty(profileId)) return true;
            var profile = Find(profileId);
            return profile != null && InputControlPath.Matches(profile.devicePath,device);
        }
        public static bool SelectCharacterProfile(string profileId, int human = 0)
        {
            if (!CanSelectProfile(profileId,human)) return false;
            var device = AppliedProfileDevice(human);
            if (!NativeUserSettings.WriteValue("native.inputProfile."+human,profileId??"","string")) return Fail(NativeUserSettings.LastError);
            CaptureCharacterProfilePress(human);
            selected[human] = profileId ?? "";
            Rebuild(human,device);
            characterProfileDevice[human] = device; Changed?.Invoke();
            return true;
        }
    }
}
