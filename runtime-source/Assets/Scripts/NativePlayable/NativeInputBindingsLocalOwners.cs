using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace Fumo.NativePlayable
{
    public static partial class NativeInputBindings
    {
        static readonly PlayerInput[] localOwners = new PlayerInput[4];
        static readonly Dictionary<uint, string> localOwnerProfiles = new Dictionary<uint, string>();
        static readonly System.Action<InputAction.CallbackContext>[] localNavigateHandlers = new System.Action<InputAction.CallbackContext>[4];
        public static PlayerInput ActualLocalOwner(int slot) => ValidPlayer(slot) ? localOwners[slot] : null;
        public static bool LocalOwnerReplay(Action action, int slot, InputControl control) => CharacterProfileReplay(action, slot, control);
        public static void RecordLocalOwnerDevice(int slot, InputDevice device)
        {
            var owner = ActualLocalOwner(slot);
            if (owner && owner.user.valid && owner.user.pairedDevices.Contains(device)) characterProfileDevice[slot] = device;
        }
        public static void SetActualLocalOwners(PlayerInput[] players)
        {
            if (players == null || players.Length > 4) throw new ArgumentException("Original local owner count differs from four");
            bool unchanged=true;
            for(int slot=0;slot<4;slot++){var owner=slot<players.Length?players[slot]:null;if(localOwners[slot]!=owner||assets[slot]!=(owner?owner.actions:null)){unchanged=false;break;}}
            if(unchanged)return;
            for (int slot = 0; slot < 4; slot++)
            {
                if (localOwners[slot] && assets[slot] != null && localNavigateHandlers[slot] != null)
                {
                    var navigate = assets[slot].FindAction("Normal/Navigate", false);
                    if (navigate != null) navigate.started -= localNavigateHandlers[slot];
                }
            }
            var borrowed = new HashSet<InputActionAsset>();
            foreach (var owner in localOwners) if (owner && owner.actions) borrowed.Add(owner.actions);
            for (int slot = 0; slot < 4; slot++)
            {
                if (assets[slot] && !borrowed.Contains(assets[slot])) { assets[slot].Disable(); UnityEngine.Object.Destroy(assets[slot]); }
                var owner = slot < players.Length ? players[slot] : null;
                if (owner && (!owner.user.valid || owner.user.pairedDevices.Count == 0)) throw new InvalidOperationException("Local owner lacks actual paired devices");
                if (localOwners[slot] != owner)
                    NativeTelemetry.Event("input.owner.bound", owner ? "accepted" : "applied", detail: "slot=" + slot + ";user=" + (owner && owner.user.valid ? owner.user.id.ToString() : "unknown"));
                localOwners[slot] = owner; assets[slot] = owner ? owner.actions : null;
                bool newOwner = owner && !localOwnerProfiles.ContainsKey(owner.user.id);
                selected[slot] = owner && localOwnerProfiles.TryGetValue(owner.user.id, out var profile) ? profile : owner ? NativeUserSettings.ReadValue("native.inputProfile."+slot) : "";
                characterProfileDevice[slot] = owner && owner.user.pairedDevices.Count == 1 ? owner.user.pairedDevices[0] : null;
                characterProfileConsumedPress[slot] = null; navigateStartedFrame[slot] = -1;
                if (owner)
                {
                    BindLocalNavigate(slot);
                    if(newOwner)
                    {
                        var wanted=Find(selected[slot]); var actual=characterProfileDevice[slot];
                        if(wanted==null || actual==null || !InputControlPath.Matches(wanted.devicePath,actual))selected[slot]="";
                        localOwnerProfiles[owner.user.id]=selected[slot]??"";
                        if(!string.IsNullOrEmpty(selected[slot]))TryRebuildLocalOwner(slot,actual);
                    }
                }
            }
        }
        static void BindLocalNavigate(int slot)
        {
            int captured = slot;
            localNavigateHandlers[slot] = context => { navigateStartedVector[captured] = context.ReadValue<Vector2>(); navigateStartedFrame[captured] = Time.frameCount; RecordLocalOwnerDevice(captured, context.control.device); };
            assets[slot].FindAction("Normal/Navigate", true).started += localNavigateHandlers[slot];
        }
        static bool TryRebuildLocalOwner(int slot, InputDevice forcedDevice)
        {
            var owner = ActualLocalOwner(slot);
            if (!owner) return false;
            string telemetryOperation = NativeTelemetry.Begin("input.owner.profile-rebuild", "slot=" + slot);
            bool telemetrySucceeded = false;
            try
            {
            if (!owner.user.valid || owner.user.pairedDevices.Count == 0) throw new InvalidOperationException("Local profile owner lost pairing");
            var pairedDevices = owner.user.pairedDevices.ToArray();
            uint userId = owner.user.id;
            string controlScheme = owner.currentControlScheme;
            var profile = Find(selected[slot]);
            var device = forcedDevice ?? (profile==null ? pairedDevices[0] : AppliedProfileDevice(slot));
            if ((device == null && profile != null) || (device != null && !owner.user.pairedDevices.Contains(device))) throw new InvalidOperationException("Profile device belongs to a different InputUser");
            if (profile != null && !InputControlPath.Matches(profile.devicePath, device)) throw new InvalidOperationException("Profile device is incompatible");
            var json = NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");
            if (!json) throw new InvalidOperationException("Original local Controls resource missing");
            var replacement = InputActionAsset.FromJson(json.text);
            EnsureGamepadBindings(replacement);
            replacement.devices = pairedDevices;
            if (profile != null) foreach (var pair in profile.overrides) ApplyOverride(replacement, pair.actionId, pair.path, device);
            var previous = owner.actions;
            if (localNavigateHandlers[slot] != null) previous.FindAction("Normal/Navigate", true).started -= localNavigateHandlers[slot];
            // PlayerInput.actions rebuilds its pairing; retain the selected human's devices across stations.
            owner.actions = replacement;
            if (!owner.user.valid || owner.user.id != userId) throw new InvalidOperationException("Local profile rebuild changed InputUser identity");
            owner.user.UnpairDevices();
            foreach (var pairedDevice in pairedDevices) InputUser.PerformPairingWithDevice(pairedDevice, owner.user);
            owner.user.ActivateControlScheme(controlScheme);
            owner.user.AssociateActionsWithUser(owner.actions);
            if (!owner.user.pairedDevices.SequenceEqual(pairedDevices)) throw new InvalidOperationException("Local profile rebuild changed paired devices");
            if (owner.currentControlScheme != controlScheme) throw new InvalidOperationException("Local profile rebuild changed control scheme");
            assets[slot] = owner.actions;
            Debug.Log("NATIVE_LOCAL_OWNER_PROFILE_REBUILD slot=" + slot + " user=" + userId + " devices=" + string.Join(",", pairedDevices.Select(value => value.deviceId)));
            if (replacement != owner.actions) UnityEngine.Object.Destroy(replacement);
            localOwnerProfiles[owner.user.id] = selected[slot] ?? "";
            BindLocalNavigate(slot);
            if (previous && previous != owner.actions && !(NativeLocalSelectionOwners.Instance && NativeLocalSelectionOwners.Instance.IsTemplate(previous))) UnityEngine.Object.Destroy(previous);
            return telemetrySucceeded = true;
            }
            finally { NativeTelemetry.End(telemetryOperation, "input.owner.profile-rebuild", telemetrySucceeded ? "completed" : "failed", telemetrySucceeded ? null : "owner-invariant-or-resource"); }
        }
    }
}
