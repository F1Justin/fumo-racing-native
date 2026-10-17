using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeNetworkSelection { public int seat, skin; public string character = "Reimu", vehicle = "Kart"; }
    [Serializable] public sealed class NativeNetworkConfig
    {
        public string track = "forest", session;
        public int laps = 1, racers = 4;
        public bool items = true;
        public NativeSessionMode mode = NativeSessionMode.QuickRace;
        public int itemType, gpStation;
        public NativeCustomRaceRules customRules = new NativeCustomRaceRules();
        // Room identities stay stable; the race grid compacts participating humans before AI slots.
        public int[] humanSeats = new int[0];
        public NativeNetworkSelection[] selections = new NativeNetworkSelection[0];
    }
    [Serializable] public sealed class NativeNetworkInput
    {
        public float throttle, steering, vertical;
        public bool brake, jump, jumpHeld, use, recover, autopilot;
    }
    [Serializable] public sealed class NativeNetworkKart
    {
        public int seat, lap, checkpoint, place, damage, effects, recoveryRequests; public string[] effectIds; public float topSpeed, topSpeedAdd, acceleration, grassSpeed;
        public Vector3 position, velocity;
        public Quaternion rotation;
        public float speed, distance, finish;
        public bool frozen, invulnerable, damageVisible, finished, retired, reversed;
        public NativeKartItemState inventory;
        public NativeSourceLapClock sourceLapClock;
        public NativeNetworkSourceAudioState sourceAudio;
    }
    [Serializable] public sealed class NativeNetworkActor { public int id, owner, mode, visual; public string item; public Vector3 position; public Quaternion rotation; public bool carried; }
    [Serializable] public sealed class NativeNetworkSnapshot
    {
        public bool sourceClockAvailable, sourceClockStopped;
        public double sourceClockElapsed;
        public string phase;
        public float elapsed, countdown;
        public NativeNetworkKart[] karts;
        public NativeNetworkActor[] actors;
    }
    [DefaultExecutionOrder(-110)]
    public sealed partial class NativeRaceNetwork : MonoBehaviour
    {
        public static NativeRaceNetwork Instance;
        public static bool ClientRace => Instance && Instance.InRace && !Instance.IsHost;
        public static bool IsLocalMotor(NativeKartMotor motor) => Instance && Instance.Race && Instance.LocalRacerIndex >= 0
            && Instance.LocalRacerIndex < Instance.Race.racers.Length && Instance.Race.racers[Instance.LocalRacerIndex] == motor;
        public bool Open, InRace, Loading, IsHost;
        internal uint StableLocalOwnerId;
        internal int[] StableLocalOwnerDevices=new int[0];
        internal string StableLocalOwnerScheme;
        internal bool DiagnosticOwnerCreated;
        bool localRoundPrepared;
        int sourceAudioMaximumWireBytes;
        string preparedTrack;
        public int LocalSeat = -1, Round, CompletedRounds;
        public NativeRace Race;
        public NativeNetworkConfig Config = new NativeNetworkConfig();
        public NativeNetPeer[] Peers = new NativeNetPeer[0];
        public NativeNetworkSnapshot Latest;
        public readonly Dictionary<int, NativeNetworkInput> Inputs = new Dictionary<int, NativeNetworkInput>();
        NativeNetTransport transport;
        NativeNetBroker broker;
        readonly HashSet<int> loaded = new HashSet<int>();
        readonly Dictionary<int, int> inputSequences = new Dictionary<int, int>();
        public string Address = "127.0.0.1", Port = "7777", Room = "fumo", PlayerName = "Player", Status = "Choose how to play together.";
        bool serverConnection = true;
        string directAddress = "127.0.0.1", directPort = "7777", serverAddress = "", serverPort = "27777";
        string rooms = "", mode, reportDir, commandFile, lastCommand, playerId;
        string playerNameOverride;
        NativeNetBroker.RoomInfo[] availableRooms = new NativeNetBroker.RoomInfo[0];
        float connectAt, loadAt, nextSend, nextTelemetry, finishedAt, lobbyAt;
        float reportInterval = 1;
        int inputSeq, snapshotSeq, eventsSeq, resultsSeq, latestSeq = -1, targetRounds = 1, finalSeat = -1;
        bool autoReady, autoStart, autoPilot, paused, readySent, startSent, resultSent, initialized;
        NativeNetworkSelection selection = new NativeNetworkSelection();
        Vector2 scroll;
        PlayerInput sampledInputOwner;
        uint sampledInputUser;
        string sampledControlScheme;
        NativeNetworkOwnerDiagnostics.Device[] sampledPairedDevices=new NativeNetworkOwnerDiagnostics.Device[0];
        int sampledInputFrame=-1;
        bool sampledVerificationInput, sampledAutopilot;
        NativeNetworkInput frameInput = new NativeNetworkInput();
        NativeNetworkSnapshot finalSnapshot;
        readonly List<NativeNetworkSnapshot> roundResults = new List<NativeNetworkSnapshot>();
        readonly List<string> errors = new List<string>();
        string lastCommandId;
        bool completionSent, completionReceived;
        float completionAt;
        NativeNetworkInput verificationInput;
        float verificationInputUntil;
        int maximumActors, maximumDamage, maximumEffects;
        [Serializable] sealed class Command { public string id, action, args; }

        [Serializable] sealed class Report
        {
            public bool localBroadcastDemo; public int humanRacers, aiDrivers;
            public string room, track, phase, status, player; public int round, completedRounds, seat, inputsSent, snapshots, errors;
            public NativeNetworkOwnerDiagnostics ownerDiagnostics;
            public int sourceAudioConsumed,sourceAudioPlayed;
            public bool sourceAudioAvailable;
            public int sourceAudioEventsBytes,sourceAudioMaximumSnapshotBytes,sourceAudioMaximumWireBytes,sourceAudioFrameLimit;
            public bool sourceAudioObservedWithinFrameLimit;
            public NativeNetworkAudioInvocation[] sourceAudioInvocations;
            public string actualReceiverTrackId, preparedTrack;
            public bool receiverReady;
            public bool host, passed; public float elapsed, distance; public NativeNetworkKart[] results; public NativeNetworkSnapshot[] roundResults; public string[] errorMessages; public string lastCommandId; public int loadedCount;
            public NativeNetworkActor[] actors;
            public int visualActors, maximumActors, maximumDamage, maximumEffects, cameraSeat;
            public float timeScale;
            public bool paused, itemBoxesEnabled;
            public string connectionRoute, recoveryReason, playerId;
            public int reconnectCount, reconnectAttempts;
            public int readyCount, peerCount;
            public bool recovering;
            public bool sourceLapClockAvailable;public int sourceLapClockImportedCount;public NativeSourceLapClock localSourceLapClock;
            public bool sourcePrepared,sourceStarted,sourceEnded,physicalHumanAccepted,ordinaryAudioAccepted;
            public double sourceGameTime; public int sourceStartCount,sourceCountdownDispatches;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var obj = new GameObject("Native online rooms");
            DontDestroyOnLoad(obj); Instance = obj.AddComponent<NativeRaceNetwork>();
        }
        void Awake()
        {
            Instance = this;
            Application.logMessageReceived += LogError;
            var args = Environment.GetCommandLineArgs();
            Open = args.Contains("--net-open");
            mode = Arg(args, "--net-mode"); Address = Arg(args, "--net-address") ?? Address;
            Port = Arg(args, "--net-port") ?? Port; Room = Arg(args, "--net-room") ?? Room;
            playerNameOverride = Arg(args, "--net-name");
            autoReady = args.Contains("--net-auto-ready"); autoStart = args.Contains("--net-auto-start"); autoPilot = args.Contains("--net-autopilot");
            reportDir = Arg(args, "--net-report-dir"); commandFile = Arg(args, "--net-command-file");
            int parsed; if (int.TryParse(Arg(args, "--net-report-hz"), out parsed)) reportInterval = 1f / Mathf.Clamp(parsed, 1, 60);
            if (int.TryParse(Arg(args, "--net-rounds"), out parsed)) targetRounds = Mathf.Clamp(parsed, 1, 20);
            requestedSpectator = args.Contains("--net-spectator");
            string community = Arg(args, "--community-server");
            if (!string.IsNullOrEmpty(community)) { communityEnabled=true; serverAddress=community; }
            Config.track = Arg(args, "--track") ?? Config.track;
            if (int.TryParse(Arg(args, "--net-laps"), out parsed)) Config.laps = Mathf.Clamp(parsed, 1, 99);
            if (int.TryParse(Arg(args, "--net-racers"), out parsed)) Config.racers = Mathf.Clamp(parsed, 2, 100);
            selection.character = Arg(args, "--net-character") ?? selection.character; selection.vehicle = Arg(args, "--net-vehicle") ?? selection.vehicle;
            if (!string.IsNullOrEmpty(reportDir)) Directory.CreateDirectory(reportDir);
        }
        static string Arg(string[] args, string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        public void Bind(NativeRace race)
        {
            if (Race != race)
            {
                localRoundPrepared=false;
                onlineFields.Clear(); boundOnlineFields.Clear(); onlineVisibleFields.Clear();
                onlineInputRoot=null; onlinePage=null;
            }
            Race = race;
            SyncProfilePlayerName();
            if (race.vehicleRoster)
            {
                if (race.vehicleRoster.CharacterIndexOf(selection.character) < 0) selection.character = race.vehicleRoster.CharacterIdAt(0);
                if (race.vehicleRoster.VehicleIndexOf(selection.vehicle) < 0) selection.vehicle = race.vehicleRoster.VehicleIdAt(0);
            }
            if (!initialized) { LoadOnlinePreferences(); initialized = true; if (!string.IsNullOrEmpty(mode)) Connect(mode); }
            if (Loading) LoadRace();
            BeginProfileInputDiagnostic();
        }
        public void OpenMenu(InputDevice entryDevice=null)
        {
            SyncProfilePlayerName();
            Open = true; connectionStep = connectionReturnStep = OnlineConnectionStep.Menu; onlineFocus = "host-direct";
            if (Race) Race.NetworkOpenMenu(entryDevice);
        }
        public void CloseMenu()
        {
            Leave(true);
        }
        public void Connect(string requestedMode)
        {
            SyncProfilePlayerName();
            Leave(false); Open = true; serverConnection = requestedMode != "host" && requestedMode != "join"; mode = requestedMode; Status = "Connecting"; connectAt = Time.realtimeSinceStartup;
            telemetryConnectOperation = NativeTelemetry.Begin("network.connect", requestedMode);
            int port; if (!int.TryParse(Port, out port) || port < 1 || port > 65535) { Status = "Port must be 1–65535"; NativeTelemetry.End(telemetryConnectOperation, "network.connect", "rejected", "invalid-port"); telemetryConnectOperation=null; return; }
            if (requestedMode != "host")
            {
                string host, addressError; int addressPort;
                if (!NativeNetTransport.ParseAddress(Address, out host, out addressPort, out addressError)) { Status = addressError; NativeTelemetry.End(telemetryConnectOperation, "network.connect", "rejected", "invalid-address"); telemetryConnectOperation=null; return; }
                bool explicitPort = Address.StartsWith("[") ? Address.Contains("]:") : Address.Count(c => c == ':') == 1;
                Address = host;
                if (explicitPort) { port = addressPort; Port = port.ToString(); }
            }
            if (requestedMode == "host")
            {
                broker = new NativeNetBroker(); broker.Start(port);
                if (!string.IsNullOrEmpty(broker.Error)) { Status = broker.Error; NativeTelemetry.End(telemetryConnectOperation, "network.connect", "failed", "broker-start-failed"); telemetryConnectOperation=null; return; }
            }
            ResetConnectionRecovery(requestedMode == "host" ? "127.0.0.1" : Address, port);
            transport = OpenTransport(false);
            readySent = startSent = false; LocalSeat = -1; playerId = null;
            IsHost = false; CompletedRounds = 0; finalSeat = -1; finalSnapshot = Latest = null; roundResults.Clear(); errors.Clear();
            completionSent = completionReceived = paused = false; frameInput = new NativeNetworkInput();
            verificationInput = null; maximumActors = maximumDamage = maximumEffects = 0;
        }
        void SyncProfilePlayerName()
        {
            var profile = NativeInputBindings.Profiles.FirstOrDefault(p => p.profileId == NativeInputBindings.SelectedProfile());
            string savedName = NativeUserSettings.ReadValue(OnlineNameSetting);
            string name = playerNameOverride ?? (!string.IsNullOrWhiteSpace(savedName) ? savedName : profile?.name ?? "Player");
            PlayerName = NativePlayerNamePolicy.Display(name);
        }
        public void Send(string kind, string payload = null, bool ready = true)
        {
            if (transport == null || Recovering) { NativeTelemetry.Count("network.send.suppressed"); return; }
            int sequence = kind == "input" ? ++inputSeq : kind == "snapshot" ? ++snapshotSeq : kind == "events" ? ++eventsSeq : kind == "results" ? ++resultsSeq : 0;
            var message=new NativeNetMessage { kind = kind, content = NativeNetProtocol.Content, room = Room,
                name = PlayerName, player = playerId, seat = LocalSeat, round = Round, sequence = sequence,
                ready = ready, spectator = requestedSpectator, capacity = 8, payload = payload };
            if(kind=="snapshot"||kind=="results")sourceAudioMaximumWireBytes=Math.Max(sourceAudioMaximumWireBytes,System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(message)+"\n"));
            bool enqueued = transport.Send(message);
            NativeTelemetry.Count("network.send.attempted");
            NativeTelemetry.Count(enqueued ? "network.send.enqueued" : "network.send.failed");
            if (kind == "input") { NativeTelemetry.Count(enqueued ? "network.input.enqueued" : "network.input.enqueue-failed"); NativeTelemetry.Metric("network.input.lastAttemptedWireSeq", sequence); }
        }
        void LogError(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) RecordNetworkError(message); }
        void RecordNetworkError(string message)
        {
            // Keep original first-error/test failure semantics while bounding the legacy report list.
            if (errors.Count < 128) errors.Add(message != null && message.Length > 4096 ? message.Substring(0, 4096) : message);
            else NativeTelemetry.Count("network.report.errors-dropped");
        }
        void Update()
        {
            TelemetryNetworkHealth();
            TickOnlineMenuState();
            TickPersonalResults();
            SynchronizeSpectatorInput();
            if ((Open || ProfileFromSettings) && Race) TickNetworkUiInput();
            if (onlineUiFixture) return;
            Commands();
            if(Open && Race && !InRace && !Race.NetworkEnsureActualOwner())
            {
                Status="Press confirm or click to join";
                if(Time.realtimeSinceStartup>=nextTelemetry){nextTelemetry=Time.realtimeSinceStartup+reportInterval;WriteReport(false);}
                return;
            }
            if(Loading&&!localRoundPrepared&&Race)LoadRace();
            if (InRace && !onlineUiFixture)
            {
                var sampled = ReadInput();
                TelemetrySampleInput(sampled);
                sampled.jump |= frameInput.jump; sampled.use |= frameInput.use; sampled.recover |= frameInput.recover;
                frameInput = sampled;
            }
            if (broker != null) broker.Pump();
            TickConnectionRecovery();
            if (transport == null)
            {
                TickLocalBroadcastDemo();
                if (Time.realtimeSinceStartup >= nextTelemetry) { nextTelemetry = Time.realtimeSinceStartup + reportInterval; WriteReport(false); }
                return;
            }
            if (transport.IsConnected && !handshakeSent)
            {
                handshakeSent = true;
                if (Recovering) SendResume();
                else { Status = "Joining room"; Send(mode == "host" || mode == "server-create" ? "create" : mode == "list" ? "list" : "join"); }
            }
            NativeNetMessage msg;
            while (transport != null && transport.TryReceive(out msg))
            { try { Receive(msg); } catch (Exception e) { NativeTelemetry.Event("network.receive", "failed", "invalid-payload", e.GetType().Name); RecordNetworkError("Protocol payload: " + e.Message); Status = "Invalid room payload"; if (IsHost) Send("abort"); else Leave(false); } }
            if (transport != null && (!string.IsNullOrEmpty(transport.Error) || (!Recovering && playerId != null && !transport.IsConnected))) HandleConnectionFailure(transport.Error ?? "Connection closed");
            if (transport != null && playerId == null && Time.realtimeSinceStartup - connectAt > 15f) HandleInitialHandshakeTimeout();
            if (transport != null && playerId != null && !Recovering && Time.realtimeSinceStartup >= nextHeartbeat)
            { nextHeartbeat = Time.realtimeSinceStartup + 2f; Send("ping"); }
            NeutralizeStaleInputs();
            if (Loading && Time.realtimeSinceStartup - loadAt > 60f) { NativeTelemetry.Event("network.loading", "timedOut", "scene-loading-deadline"); if (IsHost) Send("abort"); else Leave(false); Status = "Scene loading timed out"; }
            if (!Recovering && !completionSent && !completionReceived && IsHost && !InRace && !Loading && autoStart && !startSent && RoomReadyToStart
                && Time.realtimeSinceStartup - lobbyAt > 1f) StartRound();
            if (!Recovering && !completionSent && !completionReceived && playerId != null && !InRace && !Loading && autoReady && !readySent) Ready();
            if (InRace && Time.realtimeSinceStartup >= nextSend && !Loading && Race && !Recovering)
            {
                nextSend = Time.realtimeSinceStartup + 1f / 20f;
                var input = new NativeNetworkInput { throttle = frameInput.throttle, steering = frameInput.steering, brake = frameInput.brake,
                    jump = frameInput.jump, jumpHeld = frameInput.jumpHeld, use = frameInput.use, recover = frameInput.recover, vertical = frameInput.vertical, autopilot = frameInput.autopilot };
                if (!requestedSpectator && !LocalFinished) { Inputs[LocalSeat] = input; TelemetryCacheInput(LocalSeat, 1); inputReceivedAt[LocalSeat] = Time.realtimeSinceStartup; Send("input", JsonUtility.ToJson(input)); }
                frameInput.jump = frameInput.use = frameInput.recover = false;
                if (IsHost) Send("snapshot", JsonUtility.ToJson(Race.NetworkSnapshot()));
            }
            if (!Recovering && IsHost && InRace && Latest != null && Latest.karts != null && Latest.karts.All(k => k.finished) && !resultSent)
            { resultSent = true; finishedAt = Time.realtimeSinceStartup; Send("results", JsonUtility.ToJson(Latest)); }
            if (!Recovering && resultSent && autoStart && Time.realtimeSinceStartup - finishedAt > 4f && IsHost)
            {
                if (CompletedRounds < targetRounds) { Send("rematch"); resultSent = false; }
                else if (!completionSent) { completionSent = true; completionAt = Time.realtimeSinceStartup; Send("events", "complete"); WriteReport(true); }
                else if (Time.realtimeSinceStartup - completionAt > 4f) { WriteReport(true); Application.Quit(0); }
            }
            if (Time.realtimeSinceStartup >= nextTelemetry) { nextTelemetry = Time.realtimeSinceStartup + reportInterval; WriteReport(false); }
            if (IsHost && completionSent && Time.realtimeSinceStartup - completionAt > 4f) { WriteReport(true); Application.Quit(0); }
            if (!IsHost && completionReceived && Time.realtimeSinceStartup - completionAt > 2f) { WriteReport(true); Application.Quit(0); }
        }
        NativeNetworkInput ReadInput()
        {
            sampledInputOwner=Race?Race.NetworkActualOwner():null;sampledInputFrame=Time.frameCount;
            sampledInputUser=sampledInputOwner&&sampledInputOwner.user.valid?sampledInputOwner.user.id:0;
            sampledControlScheme=sampledInputOwner?sampledInputOwner.currentControlScheme:null;
            sampledPairedDevices=sampledInputOwner&&sampledInputOwner.user.valid?sampledInputOwner.user.pairedDevices.Select(device=>new NativeNetworkOwnerDiagnostics.Device{id=device.deviceId,layout=device.layout,name=device.name,native=device.native}).ToArray():new NativeNetworkOwnerDiagnostics.Device[0];
            sampledVerificationInput=verificationInput!=null&&Time.realtimeSinceStartup<verificationInputUntil;sampledAutopilot=autoPilot;
            if(!sampledInputOwner){frameInput=new NativeNetworkInput();return frameInput;}
            if (verificationInput != null && Time.realtimeSinceStartup < verificationInputUntil)
                return JsonUtility.FromJson<NativeNetworkInput>(JsonUtility.ToJson(verificationInput));
            return new NativeNetworkInput { throttle = paused ? 0 : NativeInputBindings.GetAxis(NativeInputBindings.Action.Accelerate) * .5f,
                steering = paused ? 0 : NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal),
                brake = !paused && NativeInputBindings.GetAxis(NativeInputBindings.Action.Brake) > .5f,
                jump = !paused && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Jump), jumpHeld = !paused && NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Jump),
                use = !paused && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Use), recover = !paused && Input.GetKeyDown(KeyCode.R), autopilot = autoPilot,
                vertical = paused ? 0 : NativeInputBindings.GetAxis(NativeInputBindings.Action.Vertical) };
        }
        void Receive(NativeNetMessage msg)
        {
            // A legacy or third-party relay must not introduce markup/invisible names into the HUD.
            if (msg.peers != null) foreach (var peer in msg.peers) if (peer != null) peer.name = NativePlayerNamePolicy.Display(peer.name);
            if (msg.kind == "error") { Status = msg.error; serverRejected = true; NativeTelemetry.Event("network.rejected", "rejected", "server-error", msg.error); NativeTelemetry.End(telemetryConnectOperation, "network.connect", "rejected", "server-error"); telemetryConnectOperation=null; return; }
            if (msg.kind == "welcome")
            {
                if (Recovering) { ResumeWelcome(msg); return; }
                playerId = msg.player; LocalSeat = msg.seat; IsHost = msg.host; requestedSpectator = msg.spectator; Room = msg.room; Round = msg.round;
                resumeToken = msg.resumeToken;
                connectionRoute = transport == null ? connectionRoute : transport.RouteDiagnostic;
                Peers = msg.peers ?? new NativeNetPeer[0]; Status = "Room " + Room; lobbyAt = Time.realtimeSinceStartup;
                selection.seat = LocalSeat;
                TelemetryNetworkIdentity();
                NativeTelemetry.End(telemetryConnectOperation, "network.connect", "completed", "welcomed"); telemetryConnectOperation=null;
                NativeTelemetry.Event("network.identity", "applied", detail: requestedSpectator ? "spectator" : "player");
                setupActive = msg.setup; candidateTrack = string.IsNullOrEmpty(msg.candidateTrack)?null:msg.candidateTrack;
                if (!string.IsNullOrEmpty(msg.payload)) Config = JsonUtility.FromJson<NativeNetworkConfig>(msg.payload);
                if (msg.racing) { resumeCatchup=true; Receive(new NativeNetMessage{kind="start",round=msg.round,payload=msg.payload}); }
                else { if(!requestedSpectator)Send("selection", JsonUtility.ToJson(selection)); if (IsHost) BroadcastConfig(); }
                return;
            }
            if (msg.kind == "rooms")
            {
                var list = JsonUtility.FromJson<NativeNetBroker.RoomList>(msg.payload);
                availableRooms = list == null || list.rooms == null ? new NativeNetBroker.RoomInfo[0] : list.rooms;
                NormalizeRoomDisplayNames();
                rooms = availableRooms.Length == 0 ? "No open rooms" : "Available rooms";
                Status = "Server room list"; connectAt = float.PositiveInfinity;
                NativeTelemetry.End(telemetryConnectOperation, "network.connect", "completed", "room-list-received"); telemetryConnectOperation=null;
                transport.Dispose(); transport = null;
                return;
            }
            if (msg.kind == "pong") return;
            if (msg.kind == "return-room") { ReceivePersonalReturn(msg); return; }
            if (msg.kind == "lobby" || msg.kind == "peers") { Peers = msg.peers ?? new NativeNetPeer[0]; setupActive=msg.setup; candidateTrack=string.IsNullOrEmpty(msg.candidateTrack)?null:msg.candidateTrack; if (IsHost && !InRace) BroadcastConfig(); return; }
            if (msg.kind=="notice") {Status=msg.payload;return;}
            if (msg.kind=="role") {LocalSeat=msg.seat;requestedSpectator=msg.spectator;Peers=msg.peers??Peers;selection.seat=LocalSeat;TelemetryNetworkIdentity();NativeTelemetry.Event("network.role.changed","applied",detail:requestedSpectator?"spectator":"player");if(!requestedSpectator)Send("selection",JsonUtility.ToJson(selection));return;}
            if (msg.kind=="retire") {retiredSeats.Add(RacerIndex(msg.seat));Peers=msg.peers??Peers;if(msg.seat==LocalSeat){requestedSpectator=true;paused=false;}NativeTelemetry.Event("network.player.retired","applied",detail:"peerSeat="+msg.seat+";racerIndex="+RacerIndex(msg.seat));return;}
            if (msg.kind == "selection" && IsHost)
            {
                var selected = JsonUtility.FromJson<NativeNetworkSelection>(msg.payload); selected.seat = msg.seat;
                var list = Config.selections.ToList(); list.RemoveAll(s => s.seat == selected.seat); list.Add(selected); Config.selections = list.ToArray(); BroadcastConfig(); return;
            }
            if (msg.kind == "config") { Config = JsonUtility.FromJson<NativeNetworkConfig>(msg.payload); return; }
            if (msg.kind == "closed") { Status = "Host disconnected; room closed"; Leave(false); Open = true; return; }
            if (msg.kind == "abort" || msg.kind == "rematch")
            {
                NativeTelemetry.Event("network.round.closed", "applied", detail: msg.kind);
                onlineChoice = onlineChoiceEntry = OnlineChoice.None; connectionStep = OnlineConnectionStep.Menu;
                Round = msg.round; Peers = msg.peers ?? Peers; InRace = Loading = false; Inputs.Clear(); ResetTelemetryInputCache(); readySent = startSent = resultSent = false;
                TelemetryNetworkIdentity();
                setupActive=setupPage=false; candidateTrack=null; retiredSeats.Clear(); ResetPersonalResults(); Latest = null; paused = false; frameInput = new NativeNetworkInput(); Status = msg.kind == "abort" ? "Race aborted; returned to room" : "Rematch room";
                if (Race) Race.NetworkLobby(); lobbyAt = Time.realtimeSinceStartup; return;
            }
            if (msg.kind == "start")
            {
                onlineChoice = onlineChoiceEntry = OnlineChoice.None; connectionStep = OnlineConnectionStep.Menu;
                Round = msg.round;sourceAudioMaximumWireBytes=0; if (!string.IsNullOrEmpty(msg.payload)) Config = JsonUtility.FromJson<NativeNetworkConfig>(msg.payload);
                setupPage=false; retiredSeats.Clear(); ResetPersonalResults(); roundConfig = JsonUtility.FromJson<NativeNetworkConfig>(JsonUtility.ToJson(Config));
                roundNames.Clear();foreach(var peer in Participants)roundNames[RacerIndex(peer.seat)]=peer.name;
                InRace = Loading = true; networkCountdownPending=false; localRoundPrepared=false; loaded.Clear(); Inputs.Clear(); inputSequences.Clear(); latestSeq = -1; Latest = null;
                ResetTelemetryInputCache(); telemetryLoadingReason=null; TelemetryNetworkIdentity(); NativeTelemetry.Event("network.round.started", "accepted", detail: Config.track);
                resultSent = false; paused = false; frameInput = new NativeNetworkInput(); verificationInput = null; loadAt = Time.realtimeSinceStartup; Status = "Loading " + Config.track;
                if (Race && NativeTrackCatalog.IndexOf(Race.trackId) != NativeTrackCatalog.IndexOf(Config.track)) Race.NetworkChangeTrack(Config.track);
                else LoadRace(); return;
            }
            if (msg.round != Round || !InRace) return;
            if (msg.kind == "loaded" && IsHost)
            {
                loaded.Add(msg.seat);
                if (Loading && Participants.All(p => loaded.Contains(p.seat))) { Loading = false; Race.NetworkCountdown(); Send("events", "countdown"); Status = "Countdown"; } return;
            }
            if (msg.kind == "events" && msg.payload == "complete") { completionReceived = true; completionAt = Time.realtimeSinceStartup; WriteReport(true); return; }
            if (msg.kind == "events" && msg.payload == "countdown") { networkCountdownPending=true;if(localRoundPrepared){Loading = false; Race.NetworkCountdown();networkCountdownPending=false;} Status = "Countdown"; return; }
            if (msg.kind == "input" && IsHost)
            {
                int previous; if (inputSequences.TryGetValue(msg.seat, out previous) && msg.sequence <= previous) return;
                inputSequences[msg.seat] = msg.sequence; inputReceivedAt[msg.seat] = Time.realtimeSinceStartup;
                var input = JsonUtility.FromJson<NativeNetworkInput>(msg.payload);
                input.throttle = FiniteInput(input.throttle, 0f, .5f); input.steering = FiniteInput(input.steering, -1f, 1f); input.vertical = FiniteInput(input.vertical, -1f, 1f);
                Inputs[msg.seat] = input; TelemetryCacheInput(msg.seat, msg.seat == LocalSeat ? 3 : 2, msg.sequence, true); NativeTelemetry.Count("network.input.received"); return;
            }
            if (msg.kind == "snapshot")
            {
                if (msg.sequence <= latestSeq) return; latestSeq = msg.sequence;
                Latest = JsonUtility.FromJson<NativeNetworkSnapshot>(msg.payload);
                telemetrySnapshotAt = Time.realtimeSinceStartup;
                NativeTelemetry.Count("network.snapshot.received"); NativeTelemetry.Metric("network.snapshot.sequence", latestSeq); NativeTelemetry.Metric("network.snapshot.elapsed", Latest == null ? -1 : Latest.elapsed);
                if (!IsHost && Race) { if ((resumeCatchup || requestedSpectator) && localRoundPrepared && Loading && NativeTrackCatalog.IndexOf(Race.trackId) == NativeTrackCatalog.IndexOf(Config.track)) Loading = false; if (!Loading) Race.NetworkApply(Latest); }
                resumeCatchup = false; return;
            }
            if (msg.kind == "results")
            {
                if (msg.round == lastResultsRound && msg.sequence <= lastResultsSequence) return;
                lastResultsRound = msg.round; lastResultsSequence = msg.sequence;
                telemetrySnapshotAt = Time.realtimeSinceStartup;
                Latest = JsonUtility.FromJson<NativeNetworkSnapshot>(msg.payload); finalSnapshot = Latest; finalSeat = LocalRacerIndex; roundResults.Add(Latest); CompletedRounds++; if (IsHost && Config.mode == NativeSessionMode.GrandPrix) Config.gpStation = Mathf.Min(Config.gpStation + 1, NativeModeCatalog.CupTrackIds.Length); Status = "Finished round " + CompletedRounds;
                if (!IsHost && Race) Race.NetworkApply(Latest); Loading = false; TickPersonalResults(); WriteReport(true);
                NativeTelemetry.Event("network.results", "completed", detail: "round=" + Round + ";sequence=" + msg.sequence);
                return;
            }
        }
        void LoadRace()
        {
            if (!Race || !Loading || localRoundPrepared) return;
            if(!Race.NetworkReceiverReady){Status="Waiting for receiver initialization";TelemetryLoadingGate("receiver-initializing");return;}
            int actualTrack=NativeTrackCatalog.IndexOf(Race.trackId),requestedTrack=NativeTrackCatalog.IndexOf(Config.track);
            if(actualTrack<0||requestedTrack<0||actualTrack!=requestedTrack){Status="Waiting for track "+Config.track;TelemetryLoadingGate("track-mismatch");return;}
            if(!Race.NetworkEnsureActualOwner()){Status="Waiting for actual owner rejoin";TelemetryLoadingGate("owner-rejoin");return;}
            Race.NetworkPrepare(Config);preparedTrack=Race.trackId;localRoundPrepared=true;
            TelemetryLoadingGate("prepared"); NativeTelemetry.Event("network.loaded", "applied", detail: preparedTrack);
            if(requestedSpectator && Latest!=null){Loading=false;Race.NetworkApply(Latest);}else if(!requestedSpectator)Send("loaded");
            if(networkCountdownPending){Loading=false;Race.NetworkCountdown();networkCountdownPending=false;}
        }
        void BroadcastConfig() { NormalizeSetupCount(); Config.session = Config.session ?? Guid.NewGuid().ToString("N"); Send("config", JsonUtility.ToJson(Config)); }
        public void Ready() => SetReady(true);
        public void SetReady(bool value) { if(requestedSpectator)return; bool changed=readySent!=value; readySent = value; Send("ready", null, value); if(changed)NativeTelemetry.Event("network.ready.changed","requested",detail:value?"ready":"not-ready"); }
        public void StartRound() { if (!IsHost || !RoomReadyToStart || InRace || Loading) return; startSent = true; PrepareRoundConfig(); BroadcastConfig(); Send("start", JsonUtility.ToJson(Config)); }
        public void Leave(bool close)
        {
            bool telemetryWasConnected = transport != null || playerId != null || InRace || Recovering;
            if (telemetryConnectOperation != null) { NativeTelemetry.End(telemetryConnectOperation, "network.connect", "cancelled", "leave"); telemetryConnectOperation=null; }
            if (telemetryRecoveryOperation != null) { NativeTelemetry.End(telemetryRecoveryOperation, "network.recovery", "cancelled", "leave"); telemetryRecoveryOperation=null; }
            LocalBroadcastDemo = false;
            onlineChoice = onlineChoiceEntry = OnlineChoice.None; connectionStep = OnlineConnectionStep.Menu;
            draftCharacter = draftVehicle = draftSkin = 0;
            onlineFocus = onlinePage = null;
            onlineButtons.Clear();
            if (transport != null) { var closing = transport; transport = null; closing.CloseGracefully(new NativeNetMessage { kind = "leave", content = NativeNetProtocol.Content, room = Room, player = playerId, seat = LocalSeat, round = Round }); }
            if (roomListTransport != null) {roomListTransport.Dispose();roomListTransport=null;}
            if (broker != null) { broker.Dispose(); broker = null; }
            setupActive=setupPage=false; candidateTrack=null; retiredSeats.Clear(); ResetPersonalResults();
            InRace = Loading = false; Latest = null; playerId = null; LocalSeat = -1; Peers = new NativeNetPeer[0]; Inputs.Clear();
            Recovering = false; resumeToken = null; inputReceivedAt.Clear();
            if(telemetryWasConnected)ResetTelemetryInputCache(); telemetrySnapshotAt=0;
            NativeTelemetry.ClearMetrics("network."); telemetryHealthAt=0;
            if(telemetryWasConnected)NativeTelemetry.Event("network.closed","applied",close?"menu-closed":"left-room");
            NativeTelemetry.SetContext("room", null); NativeTelemetry.SetContext("peerId", null); NativeTelemetry.SetContext("peerSeat", null); NativeTelemetry.SetContext("racerIndex", null); NativeTelemetry.SetContext("isHost", null); NativeTelemetry.SetContext("round", null);
            if (close) Open = false;
            if (Race) { Race.NetworkLobby(); if (close) Race.NetworkCloseMenu(); }
            Time.timeScale = 1;
        }
        void OnApplicationQuit() { WriteReport(CompletedRounds > 0); if (transport != null) transport.Dispose(); if (broker != null) broker.Dispose(); }
        void WriteReport(bool final)
        {
            if (string.IsNullOrEmpty(reportDir)) return;
            var reportSnapshot = final && finalSnapshot != null ? finalSnapshot : Latest;
            int reportSeat = final && finalSnapshot != null ? finalSeat : LocalRacerIndex;
            var own = reportSnapshot != null && reportSnapshot.karts != null ? reportSnapshot.karts.FirstOrDefault(k => k.seat == reportSeat) : null;
            if (reportSnapshot != null)
            {
                maximumActors = Mathf.Max(maximumActors, reportSnapshot.actors == null ? 0 : reportSnapshot.actors.Length);
                if (reportSnapshot.karts != null && reportSnapshot.karts.Length > 0)
                {
                    maximumDamage = Mathf.Max(maximumDamage, reportSnapshot.karts.Max(k => k.damage));
                    maximumEffects = Mathf.Max(maximumEffects, reportSnapshot.karts.Max(k => k.effects));
                }
            }
            var ownerDiagnostics=Race?Race.ReadNetworkOwnerDiagnostics(sampledInputOwner,sampledInputUser,sampledControlScheme,sampledPairedDevices,sampledInputFrame,sampledVerificationInput,sampledAutopilot,InRace)
                : new NativeNetworkOwnerDiagnostics{passed=false,failure="Actual NativeRace receiver absent",sampleFrame=Time.frameCount};
            var report = new Report { sourceAudioMaximumWireBytes=sourceAudioMaximumWireBytes,sourceAudioFrameLimit=NativeNetProtocol.MaxFrameBytes,sourceAudioObservedWithinFrameLimit=sourceAudioMaximumWireBytes>0&&sourceAudioMaximumWireBytes<=NativeNetProtocol.MaxFrameBytes,sourceAudioEventsBytes=Race?Race.NetworkAudioEventsBytes:0,sourceAudioMaximumSnapshotBytes=Race?Race.NetworkAudioMaximumSnapshotBytes:0,sourceAudioInvocations=Race?Race.NetworkAudioInvocations:null,sourceAudioAvailable=Race&&Race.NetworkAudioAvailable,sourceAudioConsumed=Race?Race.NetworkAudioConsumed:0,sourceAudioPlayed=Race?Race.NetworkAudioPlayed:0,actualReceiverTrackId=Race?Race.trackId:null,preparedTrack=preparedTrack,receiverReady=Race&&Race.NetworkReceiverReady,ownerDiagnostics=ownerDiagnostics, sourceLapClockAvailable=Race&&Race.NetworkLapClockAvailable,sourceLapClockImportedCount=Race?Race.NetworkLapClockImportedCount:0,localSourceLapClock=Race?Race.NetworkReadLocalLapClock():null,sourcePrepared=Race&&Race.NetworkSourcePrepared,sourceStarted=Race&&Race.NetworkSourceStarted,sourceEnded=Race&&Race.NetworkSourceEnded,sourceGameTime=Race?Race.SourceGameTime:0,sourceStartCount=Race?Race.NetworkSourceStartCount:0,sourceCountdownDispatches=Race?Race.NetworkSourceCountdownDispatches:0,physicalHumanAccepted=false,ordinaryAudioAccepted=false,room = Room, track = Config.track, phase = Loading ? "Loading" : Latest != null ? Latest.phase : "Lobby", status = Status,
                player = PlayerName, round = Round, completedRounds = CompletedRounds, seat = reportSeat, host = IsHost, inputsSent = inputSeq, snapshots = latestSeq,
                elapsed = reportSnapshot == null ? 0 : reportSnapshot.elapsed, distance = own == null ? 0 : own.distance, results = reportSnapshot == null ? null : reportSnapshot.karts,
                roundResults = roundResults.ToArray(), errors = errors.Count, errorMessages = errors.ToArray(), lastCommandId = lastCommandId, loadedCount = loaded.Count,
                actors = reportSnapshot == null ? null : reportSnapshot.actors, visualActors = Race ? Race.NetworkVisualActorCount : 0,
                maximumActors = maximumActors, maximumDamage = maximumDamage, maximumEffects = maximumEffects,
                timeScale = Time.timeScale, paused = paused, cameraSeat = Race ? Race.NetworkCameraSeat : -1,
                connectionRoute = transport == null || string.IsNullOrEmpty(transport.RouteDiagnostic) ? connectionRoute : transport.RouteDiagnostic,
                recoveryReason = recoveryReason, playerId = playerId, reconnectCount = reconnectCount, reconnectAttempts = reconnectAttempts, recovering = Recovering,
                readyCount = Peers.Count(p => p.ready), peerCount = Peers.Length,
                itemBoxesEnabled = Race && FindObjectsOfType<NativeKartItemBox>(true).All(b => b.enabled),
                passed = CompletedRounds >= targetRounds && finalSnapshot != null && finalSnapshot.karts.All(k => k.finished) && errors.Count == 0 };
            report.localBroadcastDemo = LocalBroadcastDemo;
            report.humanRacers = Race ? Race.BroadcastHumanRacers : 0;
            report.aiDrivers = Race ? Race.BroadcastAiDrivers : 0;
            try { File.WriteAllText(Path.Combine(reportDir, final ? "net-result.json" : "net-live.json"), JsonUtility.ToJson(report, true)); }
            catch (IOException e) { Status = e.Message; }
        }
        void Commands()
        {
            if (string.IsNullOrEmpty(commandFile) || !File.Exists(commandFile)) return;
            string rawCommand = File.ReadAllText(commandFile).Trim(); if (rawCommand == lastCommand) return;
            string command = rawCommand, commandId = rawCommand;
            if (command.StartsWith("{")) { var parsed = JsonUtility.FromJson<Command>(command); if (parsed.id == lastCommandId) return; commandId = parsed.id; command = parsed.action + (string.IsNullOrEmpty(parsed.args) ? "" : " " + parsed.args); }
            bool lifecycleCommand = command == "leave" || command == "open" || command == "close" || command == "quit";
            // Leaving the room releases the local owner; closing and quitting must remain reachable.
            if (!lifecycleCommand && Open && Race && !InRace && !Race.NetworkEnsureActualOwner()) return;
            lastCommand = rawCommand; lastCommandId = commandId;
            if (command == "local-demo restart" && LocalBroadcastDemo) BeginLocalBroadcastDemo();
            else if (command == "ready") Ready(); else if (command == "start" && IsHost) StartRound();
            else if (command == "rematch" && IsHost) Send("rematch"); else if ((command == "abort" || command == "lobby") && IsHost) Send("abort");
            else if (command == "leave") Leave(false); else if (command == "open") OpenMenu(); else if (command == "close") CloseMenu(); else if (command == "quit") { WriteReport(true); Application.Quit(0); }
            else if (command == "results-confirm") HandlePersonalResultsConfirm();
            else if (command == "return-room") ReturnToRoom();
            else if (command == "pause") paused = !paused;
            else if (command == "drop-transport" && transport != null) transport.Dispose();
            else if (command.StartsWith("track ") && IsHost && !InRace) { Config.track = command.Substring(6); BroadcastConfig(); }
            else if (command.StartsWith("item ") && IsHost && InRace && Race) Race.NetworkGive(command.Substring(5));
            else if (command.StartsWith("input ") && InRace)
            {
                verificationInput = JsonUtility.FromJson<NativeNetworkInput>(command.Substring(6));
                verificationInputUntil = Time.realtimeSinceStartup + 3f;
            }
            else if (command.StartsWith("capture ") && !string.IsNullOrEmpty(reportDir))
                ScreenCapture.CaptureScreenshot(Path.Combine(reportDir, Path.GetFileName(command.Substring(8))));
            else if (command.StartsWith("connect ")) Connect(command.Substring(8));
            else if (command.StartsWith("ui ")) RunNetworkUiCommand(command.Substring(3));
        }
        static float FiniteInput(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, minimum, maximum);
    }
}
