using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        const float RecoveryWindow = 14f;
        public bool Recovering { get; private set; }
        string resumeToken, connectionHost, connectionRoute, recoveryReason;
        int connectionPort, reconnectCount, reconnectAttempts, lastResultsRound = -1, lastResultsSequence = -1;
        float recoveryAt, reconnectAt;
        bool handshakeSent, serverRejected, initialFallbackTried, resumeCatchup;
        Uri secureFallback;
        readonly Dictionary<int, float> inputReceivedAt = new Dictionary<int, float>();

        void ResetConnectionRecovery(string host, int port)
        {
            connectionHost = host; connectionPort = port; connectionRoute = recoveryReason = "";
            Recovering = handshakeSent = serverRejected = initialFallbackTried = resumeCatchup = false;
            resumeToken = null; reconnectCount = reconnectAttempts = 0;
            lastResultsRound = lastResultsSequence = -1; inputReceivedAt.Clear();
            secureFallback = null;
            var args = Environment.GetCommandLineArgs();
            string endpoint = Arg(args, "--net-secure-endpoint");
            if (serverConnection && mode != "host")
            {
                if (string.IsNullOrEmpty(endpoint) && host.Equals("router.f1justin.com", StringComparison.OrdinalIgnoreCase) && port == 27777)
                    endpoint = "wss://router.f1justin.com:8443/fumo-relay";
                Uri value;
                if (!string.IsNullOrEmpty(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out value) && value.Scheme == "wss") secureFallback = value;
            }
            inputSeq = snapshotSeq = eventsSeq = resultsSeq = 0;
        }

        NativeNetTransport OpenTransport(bool alternate)
        {
            var result = new NativeNetTransport();
            bool preferSecure = alternate || Environment.GetCommandLineArgs().Contains("--net-prefer-secure");
            result.Connect(connectionHost, connectionPort, secureFallback, preferSecure, true);
            handshakeSent = false;
            return result;
        }

        void TickConnectionRecovery()
        {
            if (!Recovering)
            {
                if (transport != null && transport.IsConnected && playerId != null && !serverRejected && transport.SecondsSinceRead > 8)
                    HandleConnectionFailure("Connection stopped receiving");
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (now - recoveryAt >= RecoveryWindow)
            {
                NativeTelemetry.End(telemetryRecoveryOperation,"network.recovery","timedOut","recovery-window"); telemetryRecoveryOperation=null;
                Status = "Connection recovery timed out"; Leave(false); Open = true; return;
            }
            if (transport == null && now >= reconnectAt)
            {
                reconnectAttempts++; transport = OpenTransport(true); connectAt = now;
            }
        }

        void HandleConnectionFailure(string reason)
        {
            NativeTelemetry.Event("network.connection.failure","failed",serverRejected?"server-rejected":"transport-failure",reason);
            if (telemetryConnectOperation != null) { NativeTelemetry.End(telemetryConnectOperation,"network.connect","failed",serverRejected?"server-rejected":"transport-failure");telemetryConnectOperation=null; }
            if (transport != null)
            {
                if (!string.IsNullOrEmpty(transport.RouteDiagnostic)) connectionRoute = transport.RouteDiagnostic;
                transport.Dispose(); transport = null;
            }
            if (serverRejected || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(resumeToken))
            {
                var failedStep=connectionStep;string failedStatus=serverRejected?Status:reason;
                Leave(false);Open=true;Status=failedStatus;connectionStep=failedStep;onlineFocus=failedStep==OnlineConnectionStep.Rooms?"refresh":"connect";return;
            }
            if (!Recovering)
            {
                telemetryRecoveryOperation=NativeTelemetry.Begin("network.recovery","transport-interruption");
                Recovering = true; recoveryAt = Time.realtimeSinceStartup; recoveryReason = reason;
                frameInput.jump = frameInput.use = frameInput.recover = false;
            }
            handshakeSent = false; reconnectAt = Time.realtimeSinceStartup + .15f; Status = "Reconnecting";
        }

        void HandleInitialHandshakeTimeout()
        {
            if (!initialFallbackTried && secureFallback != null && !serverRejected)
            {
                transport.Dispose(); initialFallbackTried = true; transport = OpenTransport(true);
                connectAt = Time.realtimeSinceStartup; Status = "Connecting"; return;
            }
            HandleConnectionFailure("Connection timed out");
        }

        void SendResume()
        {
            transport.Send(new NativeNetMessage { kind = "resume", content = NativeNetProtocol.Content, room = Room,
                player = playerId, seat = LocalSeat, resumeToken = resumeToken });
        }

        void ResumeWelcome(NativeNetMessage message)
        {
            if (message.player != playerId || message.seat != LocalSeat || message.room != Room)
                throw new InvalidOperationException("Recovered identity mismatch");
            Recovering = false; reconnectCount++; serverRejected = false;
            resumeToken = message.resumeToken; connectionRoute = transport.RouteDiagnostic;
            IsHost=message.host; requestedSpectator=message.spectator; setupActive=message.setup; candidateTrack=string.IsNullOrEmpty(message.candidateTrack)?null:message.candidateTrack;
            Peers = message.peers ?? new NativeNetPeer[0];
            if (!string.IsNullOrEmpty(message.payload)) Config = JsonUtility.FromJson<NativeNetworkConfig>(message.payload);
            var own = Peers.FirstOrDefault(p => p.seat == LocalSeat); readySent = own != null && own.ready;
            Status = InRace ? "Race connection restored" : "Room " + Room;
            resumeCatchup = message.racing;
            if (message.completed && message.returned) { ReceivePersonalReturn(message); TelemetryRecoveryCompleted(); return; }
            if (message.racing && (!InRace || message.round != Round))
                Receive(new NativeNetMessage { kind = "start", round = message.round, payload = message.payload });
            else if (message.completed && !message.returned && (!InRace || Round!=message.round))
                Receive(new NativeNetMessage {kind="start",round=message.round,payload=message.payload});
            else if (!message.racing && !message.completed && InRace)
                Receive(new NativeNetMessage { kind = "abort", round = message.round, peers = message.peers });
            else Round = message.round;
            TelemetryNetworkIdentity();
            if (message.racing && Loading && !requestedSpectator) Send("loaded");
            if (!message.racing && !message.completed && !requestedSpectator) { selection.seat = LocalSeat; Send("selection", JsonUtility.ToJson(selection)); if (IsHost) BroadcastConfig(); }
            nextSend = Time.realtimeSinceStartup;
            TelemetryRecoveryCompleted();
        }

        void NeutralizeStaleInputs()
        {
            if (!InRace) return;
            float now = Time.realtimeSinceStartup;
            foreach (int seat in Inputs.Keys.ToArray())
            {
                float readAt;
                if ((Recovering && seat == LocalSeat) || (seat != LocalSeat && (!inputReceivedAt.TryGetValue(seat, out readAt) || now - readAt > .5f)))
                {
                    TelemetryCachedInput cached;
                    if (!telemetryInputCache.TryGetValue(seat,out cached) || cached.source != 4)
                        NativeTelemetry.Event("network.input.neutralized","applied",Recovering&&seat==LocalSeat?"local-recovering":"peer-input-stale","peerSeat="+seat);
                    Inputs[seat] = new NativeNetworkInput { brake = true };
                    TelemetryCacheInput(seat,4);NativeTelemetry.Count("network.input.neutralized");
                }
            }
        }
    }
}
