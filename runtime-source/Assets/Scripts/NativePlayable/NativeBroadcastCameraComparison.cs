using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeBroadcastPoseSample
    {
        public Vector3 position, velocity;
        public Quaternion rotation;
        public float speed;
        public bool damageVisible, frozen, invulnerable, bodyActive, damageActive;
        public bool[] rendererEnabled, bodyObjectActive;
        public string identity;
    }
    [Serializable] public sealed class NativeBroadcastCameraTapeFrame
    {
        public int schema = 2, seed;
        public string track;
        public float time, raceTime;
        public int focus;
        public NativeBroadcastStory story;
        public NativeBroadcastPoseSample[] racers;
    }
    public sealed partial class NativeRaceNetwork
    {
        NativeBroadcastStory comparisonStory;
        internal bool BroadcastComparisonRunning { get; private set; }
        float comparisonTime;
        float comparisonDelta = 1f / 30;
        bool comparisonPaused, comparisonStop, comparisonExport;
        public bool BroadcastReplayRunning => BroadcastComparisonRunning;
        public float BroadcastReplayRaceTime => BroadcastComparisonRunning ? Latest?.elapsed ?? 0 : 0;
        public void StopBroadcastReplay() { comparisonStop = true; comparisonPaused = false; }
        public void SwitchBroadcastReplayMode(int mode)
        {
            if (!BroadcastComparisonRunning) throw new InvalidOperationException("No replay running");
            if (comparisonExport) throw new InvalidOperationException("Export requires one camera mode per run");
            if (mode < 0 || mode >= NativeOnlineSpectatorView.ModeCount) throw new ArgumentOutOfRangeException(nameof(mode));
            spectatorCameraMode = mode; Race.ResetNetworkBroadcast();
            comparisonSeek = Latest?.elapsed ?? 0;
        }
        float? comparisonSeek;
        public bool BroadcastReplayPaused => comparisonPaused;
        public void PauseBroadcastReplay(bool paused) => comparisonPaused = paused;
        public void SeekBroadcastReplay(float raceTime) { comparisonSeek = raceTime; comparisonPaused = true; }
        public void StepBroadcastReplay(int frames) { comparisonSeek = (Latest?.elapsed ?? 0) + frames * comparisonDelta; comparisonPaused = true; }
        public void StartBroadcastReplay(string path, float from, float to, int mode, string exportDirectory = null, int fps = 30)
        {
            if (BroadcastComparisonRunning) throw new InvalidOperationException("Replay already running");
            Race.StartCoroutine(ReplayBroadcastCamera(path, from, to, mode, exportDirectory, fps, false));
        }
        internal float BroadcastCameraTime => BroadcastComparisonRunning ? comparisonTime : Time.unscaledTime;
        internal float BroadcastCameraDelta(float dt) => BroadcastComparisonRunning ? (comparisonPaused ? 0 : comparisonDelta) : dt;
        void BeginBroadcastComparisonIfRequested()
        {
            var args = Environment.GetCommandLineArgs(); string path = Arg(args, "--broadcast-compare-tape");
            if (string.IsNullOrEmpty(path)) return;
            if (string.IsNullOrEmpty(reportDir)) throw new InvalidOperationException("Camera comparison requires an isolated report directory");
            Race.StartCoroutine(CompareBroadcastCameras(path, args));
        }
        IEnumerator CompareBroadcastCameras(string path, string[] args)
        {
            float from = float.Parse(Arg(args, "--broadcast-compare-start") ?? "0", CultureInfo.InvariantCulture);
            float to = float.Parse(Arg(args, "--broadcast-compare-end") ?? "120", CultureInfo.InvariantCulture);
            int mode = int.Parse(Arg(args, "--broadcast-compare-mode") ?? "4", CultureInfo.InvariantCulture);
            return ReplayBroadcastCamera(path, from, to, mode, reportDir, 30, true);
        }
        IEnumerator ReplayBroadcastCamera(string path, float from, float to, int mode, string exportDirectory, int fps, bool quit)
        {
            var tape = File.ReadLines(path).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => JsonUtility.FromJson<NativeBroadcastCameraTapeFrame>(s)).ToArray();
            if (tape.Length < 2 || fps < 1 || fps > 60 || from >= to) throw new InvalidOperationException("Invalid replay interval or tape");
            for (int n = 0; n < tape.Length; n++)
            {
                var sample = tape[n];
                if (sample.schema != 2 || sample.track != Race.trackId || sample.racers == null || sample.racers.Length != Race.racers.Length
                    || sample.focus < 0 || sample.focus >= Race.racers.Length || sample.racers.Any(r => r == null || r.rendererEnabled == null || r.bodyObjectActive == null)
                    || (n > 0 && (sample.time <= tape[n - 1].time || sample.raceTime < tape[n - 1].raceTime)))
                    throw new InvalidOperationException("Tape lacks trustworthy visibility, track, roster, or monotonic timing; record schema 2 again");
            }
            if (from < tape[0].raceTime || to > tape[tape.Length - 1].raceTime) throw new InvalidOperationException("Requested interval is not fully recorded");
            if (mode < 0 || mode >= NativeOnlineSpectatorView.ModeCount) throw new InvalidOperationException("Invalid comparison camera");
            if (Latest == null) Latest = new NativeNetworkSnapshot();
            int previousMode = spectatorCameraMode, previousFocus = observedRacer;
            bool previousFixture = onlineUiFixture, previousAutomatic = automaticSpectator;
            spectatorCameraMode = mode; onlineUiFixture = true; BroadcastComparisonRunning = true; automaticSpectator = false;
            comparisonDelta = 1f / fps; comparisonPaused = false; comparisonStop = false; comparisonExport = exportDirectory != null;
            float oldTimeScale = Time.timeScale;
            Race.PrepareBroadcastComparison();
            string frames = exportDirectory == null ? null : Path.Combine(exportDirectory, "frames");
            if (frames != null) Directory.CreateDirectory(frames);
            int frame = 0, cursor = 0; float raceTime = from;
            try
            {
                while (!comparisonStop && raceTime <= to + .0001f)
                {
                    if (comparisonSeek.HasValue) { raceTime = Mathf.Clamp(comparisonSeek.Value, from, to); comparisonSeek = null; cursor = 0; Race.ResetNetworkBroadcast(); }
                    while (cursor + 1 < tape.Length - 1 && tape[cursor + 1].raceTime <= raceTime) cursor++;
                    var a = tape[cursor]; var b = tape[cursor + 1]; float t = Mathf.InverseLerp(a.raceTime, b.raceTime, raceTime);
                    comparisonTime = Mathf.Lerp(a.time, b.time, t); comparisonStory = a.story; observedRacer = a.focus;
                    Race.ApplyBroadcastTape(a, b, t);
                    Latest.phase = "Racing"; Latest.elapsed = raceTime;
                    Physics.SyncTransforms(); Race.NetworkSpectatorFocus(observedRacer, spectatorCameraMode);
                    if (spectatorCameraMode < 4) Race.StepLegacyBroadcastReplay(comparisonDelta);
                    yield return new WaitForEndOfFrame();
                    if (frames != null) {
                        ScreenCapture.CaptureScreenshot(Path.Combine(frames, frame.ToString("D06") + ".png"));
                        File.AppendAllText(Path.Combine(exportDirectory, "frame-times.csv"), frame + "," + raceTime.ToString("R", CultureInfo.InvariantCulture) + "\n");
                    }
                    frame++;
                    while (!comparisonStop && comparisonPaused && !comparisonSeek.HasValue) yield return null;
                    if (!comparisonSeek.HasValue) raceTime += comparisonDelta;
                }
                if (exportDirectory != null && !comparisonStop) File.WriteAllText(Path.Combine(exportDirectory, "comparison.json"),
                    "{\"completed\":true,\"visualQualityVerified\":false,\"frames\":" + frame + ",\"fps\":" + fps + ",\"cameraMode\":" + mode + ",\"scope\":\"Recorded rendered car poses, body renderer visibility, damage state and story only; not full item, particle, physics or audio replay.\"}");
            }
            finally { BroadcastComparisonRunning = false; comparisonStory = null; comparisonPaused = false; Time.timeScale = oldTimeScale; spectatorCameraMode = previousMode; observedRacer = previousFocus; onlineUiFixture = previousFixture; automaticSpectator = previousAutomatic; Race.RestoreBroadcastReplay(); }
            if (quit) Application.Quit(0);
        }
    }

    public sealed partial class NativeRace
    {
        bool[] replayMotorEnabled, replayAIActive;
        internal void StepLegacyBroadcastReplay(float dt) => cameraRig.Step(dt, false);
        internal void RestoreBroadcastReplay()
        {
            for (int i = 0; i < racers.Length; i++) { racers[i].enabled = replayMotorEnabled[i]; racers[i].GetComponent<NativeKartOriginalAI>().active = replayAIActive[i]; }
            ResetNetworkBroadcast();
        }
        internal void ApplyBroadcastTape(NativeBroadcastCameraTapeFrame a, NativeBroadcastCameraTapeFrame b, float t)
        {
            for (int i = 0; i < racers.Length; i++)
            {
                var p = a.racers[i]; var q = b.racers[i]; var motor = racers[i];
                if (p.identity != motor.name) throw new InvalidOperationException("Replay racer identity mismatch");
                motor.ApplyNetworkPose(Vector3.Lerp(p.position, q.position, t), Quaternion.Slerp(p.rotation, q.rotation, t), Vector3.Lerp(p.velocity, q.velocity, t), Mathf.Lerp(p.speed, q.speed, t), 0, true);
                var hit = motor.GetComponent<NativeKartHit>(); hit.ApplyNetworkHit(p.frozen, p.invulnerable, p.damageVisible, 0);
                if (hit.toHide) hit.toHide.SetActive(p.bodyActive);
                if (hit.damageFX) hit.damageFX.SetActive(p.damageActive);
                var renderers = hit.toHide ? hit.toHide.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                if (renderers.Length != p.rendererEnabled.Length) throw new InvalidOperationException("Replay body renderer layout mismatch");
                for (int j = 0; j < renderers.Length; j++) renderers[j].enabled = p.rendererEnabled[j];
                var objects = hit.toHide ? hit.toHide.GetComponentsInChildren<Transform>(true) : new Transform[0];
                if (objects.Length != p.bodyObjectActive.Length) throw new InvalidOperationException("Replay body hierarchy mismatch");
                for (int j = 0; j < objects.Length; j++) objects[j].gameObject.SetActive(p.bodyObjectActive[j]);
            }
        }
        internal void PrepareBroadcastComparison()
        {
            Time.timeScale = 0; phase = Phase.Racing; hud.Countdown("");
            replayMotorEnabled = racers.Select(m => m.enabled).ToArray();
            replayAIActive = racers.Select(m => m.GetComponent<NativeKartOriginalAI>().active).ToArray();
            foreach (var motor in racers)
            {
                motor.GetComponent<NativeKartOriginalAI>().active = false;
                motor.enabled = false;
            }
        }
        NativeBroadcastCameraTapeFrame ReadCameraTapeFrame() => new NativeBroadcastCameraTapeFrame
        {
            seed = NativeBroadcastReplayControl.Seed, track = trackId, time = Time.unscaledTime, raceTime = Net.Latest?.elapsed ?? 0, focus = Net.BroadcastFocus, story = Net.BroadcastStory,
            racers = racers.Select(m => new NativeBroadcastPoseSample { position = m.transform.position, rotation = m.transform.rotation,
                velocity = m.DriveVelocity, speed = m.Speed, identity = m.name,
                bodyObjectActive = m.GetComponent<NativeKartHit>().toHide ? m.GetComponent<NativeKartHit>().toHide.GetComponentsInChildren<Transform>(true).Select(o => o.gameObject.activeSelf).ToArray() : new bool[0],
                damageVisible = m.GetComponent<NativeKartHit>().State.DamageVisible,
                frozen = m.GetComponent<NativeKartHit>().Frozen, invulnerable = m.GetComponent<NativeKartHit>().Invulnerable,
                bodyActive = m.GetComponent<NativeKartHit>().toHide && m.GetComponent<NativeKartHit>().toHide.activeSelf,
                damageActive = m.GetComponent<NativeKartHit>().damageFX && m.GetComponent<NativeKartHit>().damageFX.activeSelf,
                rendererEnabled = m.GetComponent<NativeKartHit>().toHide ? m.GetComponent<NativeKartHit>().toHide.GetComponentsInChildren<Renderer>(true).Select(r => r.enabled).ToArray() : new bool[0] }).ToArray()
        };
    }
}
