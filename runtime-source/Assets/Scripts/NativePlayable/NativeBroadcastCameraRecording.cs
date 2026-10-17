using System;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class CameraTrace
        {
            public float presentationTime, raceTime, frameSeconds;
            public int frame, focus, other, cuts, width, height;
            public bool cut, fallback, damageVisible, bodyActive, firstPerson;
            public int activeBodyRenderers, disabledBodyRenderers;
            public string story, phase, shot, reason, manualKind;
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView;
            public NativeBroadcastShotCandidate[] candidates;
        }
        float nextCameraTrace;
        int recordingFrame;
        float nextRecordingFrame;
        static readonly bool cameraTraceEnabled = Array.IndexOf(Environment.GetCommandLineArgs(), "--broadcast-trace") >= 0;
        static readonly bool cameraRecordEnabled = Array.IndexOf(Environment.GetCommandLineArgs(), "--broadcast-record") >= 0;
        void RecordBroadcastCamera(NativeBroadcastCameraRig rig, float dt)
        {
            string directory = NativeBroadcastReplayControl.RecordingDirectory ?? Net.BroadcastReportDirectory;
            if ((!cameraTraceEnabled && !cameraRecordEnabled && !NativeBroadcastReplayControl.Recording) || string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            if (!Net.BroadcastComparisonRunning && Net.BroadcastCameraTime >= nextRecordingFrame)
            {
                nextRecordingFrame = Net.BroadcastCameraTime + 1f / NativeBroadcastReplayControl.SampleHz;
                NativeBroadcastReplayControl.Append(directory, JsonUtility.ToJson(ReadCameraTapeFrame()));
            }
            if (cameraTraceEnabled && (rig.LastStepCut || Net.BroadcastCameraTime >= nextCameraTrace))
            {
                nextCameraTrace = Net.BroadcastCameraTime + .1f;
                var shot = rig.Current; var story = Net.BroadcastStory;
                var row = new CameraTrace { presentationTime = Net.BroadcastCameraTime, raceTime = Net.Latest?.elapsed ?? 0,
                    frame = Time.frameCount, frameSeconds = dt, width = Screen.width, height = Screen.height,
                    cut = rig.LastStepCut, fallback = rig.Fallback, cuts = rig.CutCount,
                    focus = Net.BroadcastFocus, other = shot?.other ?? -1, story = story?.key, phase = story?.phase,
                    shot = shot?.key, reason = rig.LastStepCut ? rig.CutReason : "hold", manualKind = rig.ManualKind?.ToString(),
                    position = raceCamera.transform.position, rotation = raceCamera.transform.rotation, fieldOfView = raceCamera.fieldOfView,
                    damageVisible = racers[Net.BroadcastFocus].GetComponent<NativeKartHit>().State.DamageVisible,
                    bodyActive = racers[Net.BroadcastFocus].GetComponent<NativeKartHit>().toHide.activeInHierarchy,
                    firstPerson = cameraRig.FirstPerson,
                    activeBodyRenderers = Array.FindAll(racers[Net.BroadcastFocus].GetComponent<NativeKartHit>().toHide.GetComponentsInChildren<Renderer>(true), r => r.enabled && r.gameObject.activeInHierarchy).Length,
                    disabledBodyRenderers = Array.FindAll(racers[Net.BroadcastFocus].GetComponent<NativeKartHit>().toHide.GetComponentsInChildren<Renderer>(true), r => !r.enabled).Length,
                    candidates = rig.Candidates };
                File.AppendAllText(Path.Combine(directory, "broadcast-shots.jsonl"), JsonUtility.ToJson(row) + "\n");

            }
        }
    }
}
