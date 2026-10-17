using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        public bool LocalBroadcastDemo { get; private set; }

        // Real in-process AI race; shares authority simulation and spectator presentation.
        public void BeginLocalBroadcastDemo(NativeSessionMode mode=NativeSessionMode.QuickRace)
        {
            if (!NativeReleaseFeatures.BroadcastEnabled) throw new InvalidOperationException("Broadcast demo is unavailable in the core release");
            Leave(true);
            LocalBroadcastDemo = true; Open = true; IsHost = true; requestedSpectator = true;
            playerId = "local-spectator"; LocalSeat = 8; Room = "Local AI"; Round++;
            Peers = new[] { new NativeNetPeer { id = playerId, name = PlayerName, seat = 8, spectator = true, host = true, connected = true, returned = true } };
            Config.mode = mode; Config.track = Race.trackId;
            Config.session = Guid.NewGuid().ToString("N"); Config.selections = Array.Empty<NativeNetworkSelection>();
            PrepareRoundConfig(); roundConfig = JsonUtility.FromJson<NativeNetworkConfig>(JsonUtility.ToJson(Config));
            CompletedRounds = 0; roundResults.Clear(); finalSnapshot = null; finalSeat = -1;
            lastResultsRound = lastResultsSequence = -1; resultSent = completionSent = completionReceived = false;
            automaticSpectator = true; spectatorCameraMode = 4; observedRacer = 0;
            Race.NetworkOpenMenu((InputDevice)Keyboard.current ?? Gamepad.current);
            InRace = true; Loading = false; Race.NetworkPrepare(Config);
            preparedTrack = Race.trackId; localRoundPrepared = true;
            Race.NetworkCountdown(); Latest = Race.NetworkSnapshot(); Status = "Local AI race";
            Race.NetworkSpectatorFocus(observedRacer, spectatorCameraMode);
            Debug.Log("NATIVE_LOCAL_BROADCAST_DEMO racers=" + Race.racers.Length + " humans=" + Race.BroadcastHumanRacers);
            BeginBroadcastComparisonIfRequested();
        }

        void TickLocalBroadcastDemo()
        {
            if (!LocalBroadcastDemo || !InRace || Loading || resultSent || Latest?.karts == null || !Latest.karts.All(k => k.finished)) return;
            resultSent = true;
            Receive(new NativeNetMessage { kind = "results", round = Round, sequence = ++resultsSeq, payload = JsonUtility.ToJson(Latest) });
        }
    }

    public sealed partial class NativeRace
    {
        internal int BroadcastHumanRacers => raceConfig?.roster?.Count(seat => seat.isHuman) ?? 0;
        internal int BroadcastAiDrivers => racers?.Count(motor => motor && motor.GetComponent<NativeKartOriginalAI>().active) ?? 0;
    }
}
