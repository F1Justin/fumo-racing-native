using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        internal bool BroadcastView => SpectatorControlsActive && spectatorCameraMode >= 4;
        internal int BroadcastFocus => observedRacer;
        internal NativeBroadcastShotKind? BroadcastManualKind => NativeOnlineSpectatorView.ManualKind(spectatorCameraMode);
        internal string BroadcastReportDirectory => reportDir;
        internal NativeBroadcastStory BroadcastStory
        {
            get
            {
                if (comparisonStory != null) return comparisonStory;
                var story = spectatorDirector.Current;
                if (!automaticSpectator || ManualFocusRemaining > 0 || story == null) return null;
                if (story.focus == observedRacer) return story;
                if (story.other != observedRacer) return null;
                return new NativeBroadcastStory { key = story.key, kind = story.kind, phase = story.phase,
                    focus = observedRacer, other = story.focus, score = story.score };
            }
        }

    }

    public sealed partial class NativeRace
    {
        NativeBroadcastCameraRig broadcastCamera;
        bool broadcastPoseApplied;
        internal bool BroadcastCameraActive => broadcastCamera?.Active ?? false;
        internal bool BroadcastCameraFallback => broadcastCamera?.Fallback ?? false;
        internal NativeBroadcastShotCandidate BroadcastShot => broadcastCamera?.Current;
        internal int BroadcastShotCuts => broadcastCamera?.CutCount ?? 0;
        public string LabDirectorCameraId => broadcastCamera?.Current?.key;
        public string LabDirectorReason => broadcastCamera?.CutReason;
        public NativeBroadcastShotCandidate[] LabDirectorCandidates => broadcastCamera?.Candidates;
        internal string BroadcastProfile => broadcastCamera?.Profile;

        void SetBroadcastView(bool active)
        {
            active &= NativeReleaseFeatures.BroadcastEnabled;
            if (active)
            {
                if (broadcastCamera == null) {NativeBroadcastCameraRegistry.LoadSavedDefinitions(this);broadcastCamera = new NativeBroadcastCameraRig();}
                broadcastCamera.Begin(raceCamera, trackId);
            }
            else if (BroadcastCameraActive)
            {
                broadcastCamera.End(); cameraRig?.SnapToTarget(); broadcastPoseApplied = false;
            }
        }

        internal bool StepNetworkBroadcast(float dt)
        {
            if (!NativeReleaseFeatures.BroadcastEnabled || !Net.BroadcastView) { SetBroadcastView(false); return false; }
            SetBroadcastView(true);
            dt = Net.BroadcastCameraDelta(dt);
            var lab=NativeCameraLab.Active;
            broadcastCamera.PreferredCameraId=lab?lab.AutomaticCameraId:null;
            broadcastCamera.ManualKind = string.IsNullOrEmpty(broadcastCamera.PreferredCameraId)?Net.BroadcastManualKind:null;
            bool applied = broadcastCamera.Step(racers, route, Net.BroadcastStory, lab&&lab.AutomaticCameraId!=null&&lab.TargetIds.Length>0?lab.Seat(lab.TargetIds[0]):Net.BroadcastFocus, Net.BroadcastCameraTime, dt);
            if (!applied && (broadcastPoseApplied || broadcastCamera.LastStepCut))
            {
                cameraRig.SnapToTarget(); cameraRig.ProbeObstruction();
                // Settle the legacy pivots on this cut frame; later frames use normal tracking.
                cameraRig.Step(1f, false);
            }
            RecordBroadcastCamera(broadcastCamera, dt);
            broadcastPoseApplied = applied;
            return applied;
        }
        internal void ResetNetworkBroadcast() => SetBroadcastView(false);
    }
}
