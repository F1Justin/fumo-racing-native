using System;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    // Seed affects only explicitly requested recording sessions. A tape, not a fresh
    // simulation with the same seed, is the authoritative input for camera A/B.
    public static class NativeBroadcastReplayControl
    {
        public static string RecordingDirectory { get; private set; }
        public static bool Recording => RecordingDirectory != null;
        public static int Seed { get; private set; }
        public static float SampleHz { get; private set; } = 30;
        static StreamWriter writer;
        static string writerPath;
        public static void ConfigureRecording(string directory, int seed, float sampleHz = 30)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Recording directory required");
            StopRecording(); Directory.CreateDirectory(directory);
            RecordingDirectory = directory; Seed = seed; SampleHz = Mathf.Clamp(sampleHz, 10, 60);
            UnityEngine.Random.InitState(seed);
        }
        public static void StopRecording()
        {
            writer?.Dispose(); writer = null; writerPath = null; RecordingDirectory = null;
        }
        internal static void Append(string directory, string row)
        {
            string path = Path.Combine(directory, "camera-tape.jsonl");
            if (writerPath != path)
            {
                writer?.Dispose(); writer = new StreamWriter(path, true); writer.AutoFlush = true; writerPath = path;
            }
            writer.WriteLine(row);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeExplicitRecording()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--broadcast-record") < 0 && Array.IndexOf(args, "--broadcast-trace") < 0) return;
            int index = Array.IndexOf(args, "--broadcast-seed");
            if (index >= 0 && index + 1 < args.Length)
            {
                Seed = int.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
                UnityEngine.Random.InitState(Seed);
            }
            Application.quitting -= StopRecording; Application.quitting += StopRecording;
        }
    }
}
