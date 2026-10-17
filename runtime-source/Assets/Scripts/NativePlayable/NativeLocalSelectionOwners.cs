using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed class NativeLocalSelectionOwners : MonoBehaviour
    {
        public static NativeLocalSelectionOwners Instance { get; private set; }
        public bool DiagnosticOwner { get; private set; }
        public void MarkDiagnosticOwner(bool value) => DiagnosticOwner=value;
        readonly List<PlayerInput> owners = new List<PlayerInput>();
        readonly Dictionary<PlayerInput,int> pendingDeviceLost = new Dictionary<PlayerInput,int>();
        PlayerInputManager manager;
        GameObject prototype;
        InputActionAsset template;
        bool accepting;
        int limit;
        public event Action<PlayerInput> Joined, Left;
        public sealed class Choice { public int Character, Vehicle, Skin; }
        readonly Dictionary<uint, Choice> choices = new Dictionary<uint, Choice>();
        public Choice SavedChoice(PlayerInput player) => player && player.user.valid && choices.TryGetValue(player.user.id, out var choice) ? choice : null;
        public void SaveChoice(PlayerInput player, NativeLocalSelectionState.Box box)
        { if (player && player.user.valid && box != null) choices[player.user.id] = new Choice { Character=box.Character,Vehicle=box.Vehicle,Skin=box.Skin }; }
        public event Action<PlayerInput, InputAction.CallbackContext> ActionTriggered;
        public event Action<PlayerInput,string,int> StartedObserved;
        public PlayerInput[] Players => owners.Where(player => player && player.user.valid && !pendingDeviceLost.ContainsKey(player)).OrderBy(player => player.playerIndex).ToArray();
        public PlayerInput FindOwner(uint stableUserId) => Players.FirstOrDefault(player => player.user.id == stableUserId);
        public PlayerInput AtSlot(int slot) { var players = Players; return slot >= 0 && slot < players.Length ? players[slot] : null; }
        public bool IsTemplate(InputActionAsset asset) => asset == template;
        public bool Paired(PlayerInput player, InputDevice device) => player && player.user.valid && player.user.pairedDevices.Contains(device);
        public bool Owns(InputDevice device) => Players.Any(player => Paired(player, device));
        public static NativeLocalSelectionOwners Create()
        {
            if (Instance) return Instance;
            var root = new GameObject("Original local PlayerInput owners"); root.SetActive(false);
            var service = root.AddComponent<NativeLocalSelectionOwners>(); service.Configure();
            DontDestroyOnLoad(root); root.SetActive(true); Instance = service; return service;
        }
        void Configure()
        {
            var json = NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");
            if (!json) throw new InvalidOperationException("Original local Controls resource missing");
            template = InputActionAsset.FromJson(json.text);
            prototype = new GameObject("Original local PlayerInput prototype"); prototype.SetActive(false);
            prototype.transform.SetParent(transform, false);
            var input = prototype.AddComponent<PlayerInput>(); input.actions = template;
            input.defaultActionMap = "Normal"; input.neverAutoSwitchControlSchemes = true;
            input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            manager = gameObject.AddComponent<PlayerInputManager>();
            manager.DisableJoining();
            manager.playerPrefab = prototype; manager.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            manager.joinBehavior = PlayerJoinBehavior.JoinPlayersWhenJoinActionIsTriggered;
            manager.joinAction = new InputActionProperty(template.FindAction("Normal/Ok", true).Clone());
            // Input System exposes this original serialized field through a getter only.
            var field = typeof(PlayerInputManager).GetField("m_MaxPlayerCount", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int)) throw new InvalidOperationException("PlayerInputManager max-player serialized contract missing");
            field.SetValue(manager, 4);
            if (manager.maxPlayerCount != 4) throw new InvalidOperationException("Original local maximum differs from four");
            manager.onPlayerJoined += PlayerJoined; manager.onPlayerLeft += PlayerLeft; manager.DisableJoining();
        }
        public bool Begin(bool timeTrials, InputDevice initialDevice)
        {
            limit = timeTrials ? 1 : 4;
            accepting = true;
            if (Players.Length == 0)
            {
                if (initialDevice == null || !initialDevice.enabled || Owns(initialDevice)) return false;
                manager.EnableJoining();
                var initial = manager.JoinPlayer(pairWithDevice: initialDevice);
                if (!initial || !Paired(initial, initialDevice)) throw new InvalidOperationException("Initial human did not receive its actual input device");
            }
            if (timeTrials) manager.DisableJoining(); else manager.EnableJoining();
            return true;
        }
        public PlayerInput JoinConfirmedNetworkOwner(InputDevice actualDevice)
        {
            if(actualDevice==null||!actualDevice.enabled||Owns(actualDevice)||Players.Length>=4)return null;
            bool previousAccepting=accepting,previousJoining=manager.joiningEnabled;int previousLimit=limit;
            accepting=true;limit=4;manager.EnableJoining();
            try
            {
                var joined=manager.JoinPlayer(pairWithDevice:actualDevice);
                return joined&&joined.user.valid&&Paired(joined,actualDevice)?joined:null;
            }
            finally
            {
                accepting=previousAccepting;limit=previousLimit;
                if(previousJoining)manager.EnableJoining();else manager.DisableJoining();
            }
        }
        public void End() { accepting = false; manager.DisableJoining(); }
        void PlayerJoined(PlayerInput player)
        {
            if (!player || player.gameObject == prototype) return;
            if (!accepting || owners.Count >= limit || !player.user.valid || player.user.pairedDevices.Count == 0
                || player.user.pairedDevices.Any(device => Owns(device)))
            { Destroy(player.gameObject); return; }
            DontDestroyOnLoad(player.gameObject); owners.Add(player);
            NativeTelemetry.Event("input.owner.joined", "accepted", detail: "slot=" + player.playerIndex + ";user=" + player.user.id + ";scheme=" + player.currentControlScheme + ";devices=" + player.user.pairedDevices.Count);
            player.onActionTriggered += context =>
            {
                if (pendingDeviceLost.ContainsKey(player)) return;
                // Profile application replaces actions during dispatch; snapshot producer identity first.
                if (context.started) StartedObserved?.Invoke(player,context.action.name,context.control.device.deviceId);
                ActionTriggered?.Invoke(player, context);
            };
            player.onDeviceLost += DeviceLost;
            Joined?.Invoke(player);
        }
        void DeviceLost(PlayerInput player)
        {
            if (player && owners.Contains(player) && !pendingDeviceLost.ContainsKey(player))
            {
                pendingDeviceLost.Add(player, Time.frameCount);
                NativeTelemetry.Event("input.owner.lost", detail: "slot=" + player.playerIndex);
            }
        }
        void Update()
        {
            // PlayerInput disables synchronously on Destroy; leave InputUser device notification first.
            foreach (var entry in pendingDeviceLost.ToArray())
                if (Time.frameCount > entry.Value && entry.Key) Destroy(entry.Key.gameObject);
        }
        void PlayerLeft(PlayerInput player)
        {
            pendingDeviceLost.Remove(player);
            if (!owners.Remove(player)) return;
            NativeTelemetry.Event("input.owner.left", detail: "slot=" + (player ? player.playerIndex.ToString() : "unknown"));
            Left?.Invoke(player);
        }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (manager) { manager.onPlayerJoined -= PlayerJoined; manager.onPlayerLeft -= PlayerLeft; }
            foreach (var player in owners.ToArray()) if (player) Destroy(player.gameObject);
            owners.Clear(); if (template) Destroy(template);
        }
    }
}
