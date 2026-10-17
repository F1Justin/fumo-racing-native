using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        NativeSpectatorInput spectatorInput;
        readonly NativeBroadcastDirector spectatorDirector = new NativeBroadcastDirector();
        const float ManualFocusSeconds = 8f;
        float manualFocusUntil, nextSpectatorDecision;
        string presentedStoryKey;
        int spectatorRound = -1;
        int spectatorCameraMode;
        float ManualFocusRemaining => automaticSpectator ? Mathf.Max(0, manualFocusUntil - Time.unscaledTime) : 0;
        bool SpectatorControlsActive => Race && Open && requestedSpectator && OnlinePage == "Racing";
        void SynchronizeSpectatorInput()
        {
            if (SpectatorControlsActive && spectatorInput == null)
            {
                var source = NativeContent.Load<NativeOnlineSpectatorView>("NativeOnline/Pages/Spectator");
                if (!source) throw new System.InvalidOperationException("Missing authored spectator Prefab");
                spectatorInput = new NativeSpectatorInput(source.controls);
            }
            spectatorInput?.Active(SpectatorControlsActive && !NativeCameraLab.IsActive);
        }
        void TickSpectatorInput()
        {
            // Consume the frame even if nothing was pressed: face buttons never activate menus here.
            onlineInputConsumedFrame = Time.frameCount;
            if (NativeCameraLab.IsActive) return;
            if (spectatorInput.Pressed("Pause")) { RunSpectatorAction("Pause"); return; }
            if (spectatorInput.Pressed("Previous")) RunSpectatorAction("Previous");
            if (spectatorInput.Pressed("Next")) RunSpectatorAction("Next");
            if (spectatorInput.Pressed("View")) RunSpectatorAction("View");
            Race.NetworkSpectatorFocus(observedRacer, spectatorCameraMode);
        }
        void RunSpectatorAction(string action)
        {
            if (!SpectatorControlsActive) return;
            EnsureSpectatorRound();
            if (action == "Pause") paused = true;
            else if (action == "Previous") StepSpectatorRacer(-1);
            else if (action == "Next") StepSpectatorRacer(1);
            else if (action == "View") spectatorCameraMode = (spectatorCameraMode + 1) % NativeOnlineSpectatorView.ModeCount;
            else if (action == "Automatic" && NativeReleaseFeatures.BroadcastEnabled)
            {
                if (ManualFocusRemaining > 0) manualFocusUntil = 0;
                else automaticSpectator = !automaticSpectator;
                nextSpectatorDecision = 0; presentedStoryKey = null;
                spectatorDirector.Rejoin(Time.unscaledTime);
            }
        }
        void EnsureSpectatorRound()
        {
            if (spectatorRound != Round)
            {
                spectatorRound = Round; presentedStoryKey = null; spectatorDirector.Reset(); manualFocusUntil = 0; nextSpectatorDecision = 0;
                Race.ResetNetworkBroadcast();
            }
        }
        void TickSpectatorDirector(float now)
        {
            EnsureSpectatorRound();
            if (!NativeReleaseFeatures.BroadcastEnabled)
            {
                automaticSpectator = false; manualFocusUntil = 0;
                if (Latest?.karts != null && !Latest.karts.Any(k => k.seat == observedRacer && !k.retired))
                {
                    var first = Latest.karts.FirstOrDefault(k => !k.retired && k.seat >= 0);
                    if (first != null) observedRacer = first.seat;
                }
                return;
            }
            if (now < nextSpectatorDecision || Latest?.karts == null) return;
            nextSpectatorDecision = now + .1f;
            var frame = Latest.karts.Select(k => new NativeBroadcastRacer
            {
                id = k.seat, place = k.place, lap = k.lap, checkpoint = k.checkpoint,
                damage = k.damage, itemUses = k.inventory.useCount, finished = k.finished, retired = k.retired,
                x = k.position.x, y = k.position.y, z = k.position.z, vx = k.velocity.x, vy = k.velocity.y, vz = k.velocity.z
            }).ToArray();
            var story = spectatorDirector.Update(frame, Round, Latest.elapsed, now);
            bool valid = Latest.karts.Any(k => k.seat == observedRacer && !k.retired);
            if ((automaticSpectator && now >= manualFocusUntil) || !valid)
                if (story != null)
                {
                    // A counter-pass changes event roles, not the established camera subject.
                    if (!valid || presentedStoryKey != story.key || (observedRacer != story.focus && observedRacer != story.other))
                        observedRacer = story.focus;
                    presentedStoryKey = story.key;
                }
        }
        void StepSpectatorRacer(int direction)
        {
            var seats = Latest?.karts?.Where(k => !k.retired && k.seat >= 0 && k.seat < (Race.racers?.Length ?? 0))
                .OrderBy(k => k.place).ThenBy(k => k.seat).Select(k => k.seat).Distinct().ToArray()
                ?? Enumerable.Range(0, Race.racers?.Length ?? 0).ToArray();
            if (seats.Length == 0) return;
            int index = System.Array.IndexOf(seats, observedRacer);
            observedRacer = seats[index < 0 ? direction > 0 ? 0 : seats.Length - 1 : (index + direction + seats.Length) % seats.Length];
            if (automaticSpectator) manualFocusUntil = Time.unscaledTime + ManualFocusSeconds;
        }
        void OnDestroy() => spectatorInput?.Dispose();
    }
}
