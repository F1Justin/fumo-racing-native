using System.Globalization;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        static bool telemetryAppReady;
        string telemetryInitializeOperation;
        bool telemetryStateObserved;
        Phase telemetryPhase;
        NativeSessionMode telemetryMode;
        int telemetryRaceId = -1;
        float telemetryTimeScale = float.NaN;
        bool telemetrySettingsOpen;
        SettingsPage telemetrySettingsPage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTelemetryReady() { telemetryAppReady = false; }

        void TelemetrySceneStarting()
        {
            NativeTelemetry.SetContext("track", trackId);
            NativeTelemetry.SetContext("settingsPage", null);
            NativeTelemetry.SetContext("raceId", null);
            NativeTelemetry.SetContext("phase", null);
            NativeTelemetry.SetContext("mode", null);
            NativeTelemetry.SetContext("menuPage", null);
            NativeTelemetry.SetContext("ownerStatus", null);
            NativeTelemetry.SetContext("verificationMode", null);
            telemetryInitializeOperation = NativeTelemetry.Begin("scene.initialize", trackId);
        }

        void TelemetrySceneReady()
        {
            NativeTelemetry.SetContext("verificationMode", verificationRun ? "true" : "false");
            if (hud != null) NativeTelemetry.SetContext("menuPage", hud.MenuPage.ToString());
            TelemetryObserveState();
            NativeTelemetry.End(telemetryInitializeOperation, "scene.initialize", "completed");
            telemetryInitializeOperation = null;
            NativeTelemetry.Event("scene.ready", "completed", detail: trackId);
            if (!telemetryAppReady)
            {
                telemetryAppReady = true;
                NativeTelemetry.Event("app.ready", "completed");
            }
        }

        // Only compare cached primitive state here. No input getters, scene scans or per-frame DTOs.
        void TelemetryObserveState()
        {
            if (!telemetryStateObserved || telemetryPhase != phase)
            {
                string previous = telemetryStateObserved ? telemetryPhase.ToString() : "unknown";
                telemetryPhase = phase;
                NativeTelemetry.SetContext("phase", phase.ToString());
                NativeTelemetry.Event("race.phase.changed", "applied", detail: previous + "->" + phase);
            }
            if (!telemetryStateObserved || telemetryMode != selectedMode)
            {
                telemetryMode = selectedMode;
                NativeTelemetry.SetContext("mode", selectedMode.ToString());
            }
            int race = sourceRaceState == null ? -1 : sourceRaceState.RaceId;
            if (!telemetryStateObserved || race != telemetryRaceId)
            {
                telemetryRaceId = race;
                NativeTelemetry.SetContext("raceId", race < 0 ? null : race.ToString(CultureInfo.InvariantCulture));
            }
            if (telemetryTimeScale != Time.timeScale)
            {
                telemetryTimeScale = Time.timeScale;
                NativeTelemetry.SetContext("timeScale", telemetryTimeScale.ToString("R", CultureInfo.InvariantCulture));
            }
            bool settingsJustOpened = settingsOpen && !telemetrySettingsOpen;
            if (telemetrySettingsOpen != settingsOpen)
            {
                telemetrySettingsOpen = settingsOpen;
                NativeTelemetry.Event("settings.visibility.changed", "applied", detail: settingsOpen ? "opened" : "closed");
                if (!settingsOpen) NativeTelemetry.SetContext("settingsPage", null);
            }
            if (settingsOpen && (settingsJustOpened || !telemetryStateObserved || telemetrySettingsPage != settingsPage))
            {
                telemetrySettingsPage = settingsPage;
                NativeTelemetry.SetContext("settingsPage", settingsPage.ToString());
                NativeTelemetry.Event("settings.page.changed", "applied", detail: settingsPage.ToString());
            }
            telemetryStateObserved = true;
        }

        void TelemetrySceneDisposed()
        {
            if (telemetryInitializeOperation != null)
                NativeTelemetry.End(telemetryInitializeOperation, "scene.initialize", "cancelled", "scene-disposed-before-ready");
            telemetryInitializeOperation = null;
            NativeTelemetry.Event("scene.cleanup", "completed", detail: trackId);
        }
    }

    public sealed partial class NativeRaceNetwork
    {
        struct TelemetryCachedInput
        {
            public int source, wireSequence;
            public long localSample;
            public bool hasWireSequence;
        }
        readonly System.Collections.Generic.Dictionary<int, TelemetryCachedInput> telemetryInputCache = new System.Collections.Generic.Dictionary<int, TelemetryCachedInput>();
        readonly float[] telemetryAppliedAt = new float[8];
        static readonly string[] telemetryApplyPrefixes = { "network.apply.seat0", "network.apply.seat1", "network.apply.seat2", "network.apply.seat3", "network.apply.seat4", "network.apply.seat5", "network.apply.seat6", "network.apply.seat7" };
        static readonly string[,] telemetryApplyKeys = BuildTelemetryApplyKeys();
        string telemetryConnectOperation, telemetryRecoveryOperation, telemetrySuppressionReason, telemetryLoadingReason;
        long telemetryLocalSample;
        float telemetryInputMetricsAt, telemetrySnapshotAt, telemetryHealthAt;

        static string[,] BuildTelemetryApplyKeys()
        {
            string[] suffixes = { ".racerIndex", ".source", ".wireSequence", ".localSample", ".throttle", ".steering", ".brake", ".inputAgeSeconds", ".speed", ".driving", ".hitFrozen", ".lastStepFrame", ".appliedAtRealtimeSeconds" };
            var keys = new string[8, suffixes.Length];
            for (int seat = 0; seat < 8; seat++) for (int key = 0; key < suffixes.Length; key++) keys[seat, key] = telemetryApplyPrefixes[seat] + suffixes[key];
            return keys;
        }

        void TelemetryNetworkIdentity()
        {
            NativeTelemetry.SetContext("room", Room);
            NativeTelemetry.SetContext("peerId", playerId);
            NativeTelemetry.SetContext("peerSeat", LocalSeat.ToString(CultureInfo.InvariantCulture));
            NativeTelemetry.SetContext("racerIndex", LocalRacerIndex.ToString(CultureInfo.InvariantCulture));
            NativeTelemetry.SetContext("isHost", IsHost ? "true" : "false");
            NativeTelemetry.SetContext("round", Round.ToString(CultureInfo.InvariantCulture));
        }

        void TelemetryCacheInput(int seat, int source, int wireSequence = 0, bool hasWireSequence = false)
        {
            if (seat < 0 || seat >= 8) return;
            // Source: 1 local sampler, 2 remote peer receive, 3 relay echo, 4 stale neutralization.
            telemetryInputCache[seat] = new TelemetryCachedInput { source = source, wireSequence = wireSequence, hasWireSequence = hasWireSequence, localSample = source == 1 ? telemetryLocalSample : 0 };
        }

        void TelemetrySampleInput(NativeNetworkInput input)
        {
            telemetryLocalSample++;
            NativeTelemetry.Count("network.input.sampled");
            string reason = !sampledInputOwner ? "owner-unavailable" : sampledVerificationInput ? "verification-input" : paused ? "paused" : "none";
            if (reason != telemetrySuppressionReason)
            {
                telemetrySuppressionReason = reason;
                NativeTelemetry.Event("input.suppression.changed", "applied", reasonCode: reason);
            }
            float now = Time.realtimeSinceStartup;
            if (now < telemetryInputMetricsAt) return;
            telemetryInputMetricsAt = now + .2f;
            NativeTelemetry.Metric("network.input.localSample", telemetryLocalSample);
            NativeTelemetry.Metric("network.input.ownerValid", sampledInputOwner ? 1 : 0);
            NativeTelemetry.Metric("network.input.paused", paused ? 1 : 0);
            NativeTelemetry.Metric("network.input.throttle", input.throttle);
            NativeTelemetry.Metric("network.input.steering", input.steering);
            NativeTelemetry.Metric("network.input.brake", input.brake ? 1 : 0);
            NativeTelemetry.Metric("network.input.jumpHeld", input.jumpHeld ? 1 : 0);
        }

        internal void TelemetryAppliedInput(int peerSeat, int racerIndex, NativeNetworkInput input, NativeKartMotor motor)
        {
            NativeTelemetry.Count("host.input.applied");
            if (peerSeat < 0 || peerSeat >= telemetryAppliedAt.Length) return;
            float now = Time.realtimeSinceStartup;
            if (now < telemetryAppliedAt[peerSeat]) return;
            telemetryAppliedAt[peerSeat] = now + .2f;
            TelemetryCachedInput cached;
            telemetryInputCache.TryGetValue(peerSeat, out cached);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 0], racerIndex);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 1], cached.source);
            // -1 means no wire sequence; direct host-local cache must not borrow a received seq.
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 2], cached.hasWireSequence ? cached.wireSequence : -1);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 3], cached.localSample);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 4], input.throttle);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 5], input.steering);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 6], input.brake ? 1 : 0);
            float receivedAt;
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 7], inputReceivedAt.TryGetValue(peerSeat, out receivedAt) ? now - receivedAt : -1);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 8], motor.Speed);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 9], motor.driving ? 1 : 0);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 10], motor.HitFrozen ? 1 : 0);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 11], motor.LastStepFrame);
            NativeTelemetry.Metric(telemetryApplyKeys[peerSeat, 12], now);
        }

        void ResetTelemetryInputCache()
        {
            telemetryInputCache.Clear();
            System.Array.Clear(telemetryAppliedAt, 0, telemetryAppliedAt.Length);
            NativeTelemetry.ClearMetrics("network.apply.");
            NativeTelemetry.ClearMetrics("network.input.");
            NativeTelemetry.ClearMetrics("network.snapshot.");
            telemetrySnapshotAt = 0; telemetryInputMetricsAt = 0; telemetrySuppressionReason = null;
        }

        void TelemetryRecoveryCompleted()
        {
            TelemetryNetworkIdentity();
            NativeTelemetry.End(telemetryRecoveryOperation, "network.recovery", "completed", "resumed");
            telemetryRecoveryOperation = null;
        }

        void TelemetryLoadingGate(string reason)
        {
            if (telemetryLoadingReason == reason) return;
            telemetryLoadingReason = reason;
            NativeTelemetry.Event("network.loading.gate", "observed", detail: reason);
        }

        void TelemetryNetworkHealth()
        {
            float now = Time.realtimeSinceStartup;
            if (now < telemetryHealthAt) return;
            telemetryHealthAt = now + 1f;
            NativeTelemetry.Metric("network.connected", transport != null && transport.IsConnected ? 1 : 0);
            NativeTelemetry.Metric("network.recovering", Recovering ? 1 : 0);
            NativeTelemetry.Metric("network.loading", Loading ? 1 : 0);
            NativeTelemetry.Metric("network.paused", paused ? 1 : 0);
            NativeTelemetry.Metric("network.snapshot.ageSeconds", telemetrySnapshotAt > 0 ? now - telemetrySnapshotAt : -1);
        }
    }
}
