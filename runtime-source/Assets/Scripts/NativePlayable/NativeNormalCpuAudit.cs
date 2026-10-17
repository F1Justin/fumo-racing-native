using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(100)]
    public sealed class NativeNormalCpuAudit : MonoBehaviour
    {
        [Serializable]
        sealed class Report
        {
            public string format = "normal-cpu-natural-command-poststep-v1";
            public string track, applicationVersion;
            public int racer;
            public bool assisted, accepted = false, originalComparisonPresent = false;
            public bool truncated;
            public NativeKartAINavigationSample[] samples;
        }
        NativeKartOriginalAI driver;
        NativeKartMotor motor;
        readonly List<NativeKartAINavigationSample> samples = new List<NativeKartAINavigationSample>();
        NativeKartAINavigationSample latest;
        string output;
        string recordedTrack;
        int recordedRacer;
        bool recordedAssisted;
        int writtenPostSequence = -1;
        bool truncated;
        const int MaximumSamples = 120000;
        public static void Configure(NativeKartOriginalAI value, string track, string[] args, bool visualTest)
        {
            value.trackId = track;
            bool requested = Array.IndexOf(args, "--normal-cpu-audit") >= 0;
            if (!requested) return;
            if (visualTest || Array.IndexOf(args, "--autodrive") >= 0
                || Array.Exists(args, arg => arg.Contains("self-test")))
                throw new InvalidOperationException("Normal CPU audit requires natural ordinary competition inputs");
            var audit = value.GetComponent<NativeNormalCpuAudit>() ?? value.gameObject.AddComponent<NativeNormalCpuAudit>();
            audit.StopAndSave();
            audit.driver = value;
            audit.motor = value.motor;
            int index = Array.IndexOf(args, "--normal-cpu-audit-output");
            string directory = index >= 0 && index + 1 < args.Length ? args[index + 1]
                : Path.Combine(Application.persistentDataPath, "NormalCpuAudit");
            Directory.CreateDirectory(directory);
            audit.output = Path.Combine(directory, track + "-racer" + value.racerIndex + "-"
                + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json");
            audit.samples.Clear();
            audit.truncated = false;
            audit.writtenPostSequence = -1;
            audit.latest = default;
            audit.recordedTrack = track;
            audit.recordedRacer = value.racerIndex;
            audit.recordedAssisted = value.assistedNavigation;
            value.NavigationObserved += audit.Observe;
        }
        void Observe(NativeKartAINavigationSample sample)
        {
            latest = sample;
            Append(sample);
        }
        void Append(NativeKartAINavigationSample sample)
        {
            if (samples.Count >= MaximumSamples) { truncated = true; return; }
            samples.Add(sample);
        }
        void FixedUpdate()
        {
            if (!driver || !motor || latest.sequence == writtenPostSequence || latest.sequence <= 0) return;
            var post = latest;
            post.kind = "motor-poststep";
            post.position = motor.transform.position;
            post.rotation = motor.transform.rotation;
            post.velocity = motor.PhysicsVelocity;
            post.speed = motor.Speed;
            post.currentTurn = motor.CurrentTurn;
            post.grounded = motor.Grounded;
            post.grass = motor.OnGrass;
            post.water = motor.InWater;
            post.frozen = motor.HitFrozen;
            post.reversed = motor.ControlsReversed;
            post.reverseCount = motor.ControlReverseCount;
            post.drifting = motor.IsDrifting;
            post.flips = motor.SpinState.flips;
            post.actualAirTurn = motor.CurrentAirTurn;
            Append(post);
            writtenPostSequence = latest.sequence;
        }
        public void StopAndSave()
        {
            if (driver) driver.NavigationObserved -= Observe;
            if (string.IsNullOrEmpty(output)) return;
            var report = new Report { track = recordedTrack, applicationVersion = Application.version,
                racer = recordedRacer, assisted = recordedAssisted,
                truncated = truncated, samples = samples.ToArray() };
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_NORMAL_CPU_AUDIT_SAVED " + output + " samples=" + samples.Count);
            output = null;
        }
        void OnApplicationQuit() => StopAndSave();
        void OnDestroy() => StopAndSave();
    }
}
