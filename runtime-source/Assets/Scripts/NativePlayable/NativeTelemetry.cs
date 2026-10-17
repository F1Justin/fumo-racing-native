using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;

namespace Fumo.NativePlayable
{
    // Observation only. No input, game, save or network control depends on this service.
    public static class NativeTelemetry
    {
        const int QueueLimit = 512, QueueBytesLimit = 1024 * 1024, FieldLimit = 160;
        const long SegmentLimit = 4 * 1024 * 1024;
        static readonly object gate = new object();
        static readonly Queue<Record> queue = new Queue<Record>();
        static readonly Dictionary<string, string> context = new Dictionary<string, string>();
        static readonly Dictionary<string, long> counts = new Dictionary<string, long>();
        static readonly Dictionary<string, double> metrics = new Dictionary<string, double>();
        static readonly Dictionary<string, ErrorCount> errors = new Dictionary<string, ErrorCount>();
        static readonly Dictionary<string, double> operations = new Dictionary<string, double>();
        static readonly Dictionary<string, double> uploadWindow = new Dictionary<string, double>();
        static readonly Dictionary<string, string> metadata = new Dictionary<string, string>();
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static readonly Stopwatch clock = new Stopwatch();
        static readonly Regex secrets = new Regex(@"(?i)\b(authorization|resumeToken|sessionToken|password|secret|cookie|token)\b\s*[""']?\s*[:=]", RegexOptions.CultureInvariant);
        static readonly Regex ip = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b|(?i)\b(?:[0-9a-f]{1,4}:){2,}[0-9a-f:%]*\b", RegexOptions.CultureInvariant);
        static readonly Regex userPaths = new Regex(@"(?i)(?:[a-z]:[\\/](?:users|documents and settings)[\\/][^\\/\r\n]+|/(?:Users|home)/[^/\r\n]+)", RegexOptions.CultureInvariant);
        static Thread worker;
        static bool started, stopping, disabled;
        static int queueBytes;
        static long sequence, dropped;
        static int healthWindows;
        static double uploadWindowStarted;
        static string session, build, directory, home, dataPath;
        static KeyValuePair<string, string>[] contextSnapshot = new KeyValuePair<string, string>[0];
        internal static string DirectoryPath { get { return directory; } }
        internal static string SessionId { get { return session; } }
        internal static bool Available { get { return started && !disabled && !stopping; } }

        sealed class Record
        {
            public string code, outcome, reason, detail, operation, stack, signature;
            public string utc;
            public double mono;
            public long seq;
            public int bytes;
            public bool upload;
            public KeyValuePair<string, string>[] context;
            public KeyValuePair<string, double>[] values;
            public KeyValuePair<string, string>[] metadata;
        }
        sealed class ErrorCount { public long count, reported; public string code; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Stop();
            if (worker != null && worker.IsAlive) { disabled = true; return; }
            lock (gate)
            {
                queue.Clear(); context.Clear(); counts.Clear(); metrics.Clear(); errors.Clear(); operations.Clear(); uploadWindow.Clear(); metadata.Clear(); healthWindows = 0; uploadWindowStarted = 0;
                queueBytes = 0; sequence = dropped = 0; contextSnapshot = new KeyValuePair<string, string>[0];
                started = stopping = disabled = false; session = build = directory = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Install()
        {
            if (started) return;
            try
            {
                var args = Environment.GetCommandLineArgs();
                foreach (string arg in args) if (arg == "--telemetry-disabled") { disabled = true; return; }
                string root = Application.persistentDataPath;
                bool explicitSave = false, verification = false; string reportDirectory = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--save-directory" && i + 1 < args.Length) { root = args[++i]; explicitSave = true; }
                    else if (args[i].StartsWith("--save-directory=", StringComparison.Ordinal)) { root = args[i].Substring(17); explicitSave = true; }
                    else if (args[i] == "--report-dir" && i + 1 < args.Length) reportDirectory = args[++i];
                    else if (args[i].EndsWith("-self-test", StringComparison.Ordinal) || args[i].StartsWith("--check-", StringComparison.Ordinal)
                        || args[i] == "--verify-item-character-emblems" || args[i] == "--local-selection-nonrendered-diagnostic"
                        || args[i] == "--item-prefab-structure-diagnostic" || args[i] == "--ready-structure-diagnostic"
                        || args[i] == "--tree-lod-test" || args[i] == "--menu-display-test" || args[i] == "--winter-runtime-audit"
                        || args[i] == "--winter-nonrendered-diagnostic" || args[i] == "--winter-muted-settings-audit") verification = true;
                }
                if (verification && !explicitSave) root = Path.Combine(reportDirectory ?? Path.Combine(Application.persistentDataPath, "verification"), "isolated-save");
                directory = Path.Combine(Path.GetFullPath(root), "Support", "Telemetry");
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                session = LaunchSession();
                build = Application.version + ":" + Application.buildGUID;
                dataPath = Application.dataPath;
                clock.Restart(); started = true;
                SetContext("verificationMode", verification ? "true" : "false");
                Application.logMessageReceivedThreaded += ReceiveLog;
                worker = new Thread(WriteLoop) { IsBackground = true, Name = "Fumo telemetry" }; worker.Start();
                Event("app.boot", "accepted");
                NativeTelemetryUpload.Configure(directory, Environment.GetEnvironmentVariable("FUMO_TELEMETRY_ENDPOINT"));
            }
            catch { disabled = true; Application.logMessageReceivedThreaded -= ReceiveLog; }
        }
        static string LaunchSession()
        {
            string value = Environment.GetEnvironmentVariable("FUMO_LAUNCH_SESSION_ID");
            if (value != null && value.Length == 32)
            {
                bool valid = true; foreach (char c in value) if (!Uri.IsHexDigit(c)) { valid = false; break; }
                if (valid) return value.ToLowerInvariant();
            }
            return Guid.NewGuid().ToString("N");
        }

        internal static void Metadata(string key, string value)
        {
            if (!Available) return;
            try { lock (gate) { if (metadata.ContainsKey(key) || metadata.Count < 16) metadata[key] = Bound(value, 256); } } catch { }
        }
        internal static void ConfigureUpload(string endpoint) { if (Available) NativeTelemetryUpload.Configure(directory, endpoint); }

        public static void SetContext(string key, string value)
        {
            if (!Available || string.IsNullOrEmpty(key) || key.Length > 64) return;
            try
            {
                value = Bound(value, 256);
                lock (gate)
                {
                    string previous;
                    if (context.TryGetValue(key, out previous) && previous == value) return;
                    if (key == "sceneId" || key == "sceneGeneration" || key == "track" || key == "phase" || key == "room" || key == "round" || key == "raceId") FlushUploadWindow(true);
                    if (value == null) { if (context.Remove(key)) { var remaining = new KeyValuePair<string, string>[context.Count]; int slot = 0; foreach (var pair in context) remaining[slot++] = pair; contextSnapshot = remaining; } return; }
                    if (!context.ContainsKey(key) && context.Count >= 24) { dropped++; return; }
                    context[key] = value;
                    var copy = new KeyValuePair<string, string>[context.Count]; int i = 0;
                    foreach (var pair in context) copy[i++] = pair;
                    contextSnapshot = copy;
                }
            }
            catch { }
        }
        public static void Count(string counter, int amount = 1)
        {
            if (!Available || string.IsNullOrEmpty(counter) || counter.Length > 64) return;
            try { lock (gate) { long value; if (counts.TryGetValue(counter, out value)) counts[counter] = value + amount; else if (counts.Count < FieldLimit) counts[counter] = amount; else dropped++; } } catch { }
        }
        public static void Metric(string key, double value)
        {
            if (!Available || string.IsNullOrEmpty(key) || key.Length > 64) return;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                try { lock (gate) { metrics.Remove(key); uploadWindow.Remove(key); } Count("telemetry.measurement.nonFinite"); } catch { }
                return;
            }
            try { lock (gate) { if (metrics.ContainsKey(key) || metrics.Count < FieldLimit) metrics[key] = value; else dropped++; } } catch { }
        }
        public static void ClearMetrics(string prefix)
        {
            if (!Available || string.IsNullOrEmpty(prefix)) return;
            try
            {
                lock (gate)
                {
                    bool hasWindow = false; foreach (var pair in uploadWindow) if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) { hasWindow = true; break; }
                    if (hasWindow) FlushUploadWindow(true);
                    var removed = new List<string>(); foreach (var pair in metrics) if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) removed.Add(pair.Key);
                    foreach (string key in removed) { metrics.Remove(key); uploadWindow.Remove(key); }
                }
            }
            catch { }
        }
        public static void Event(string eventCode, string outcome = "observed", string reasonCode = null, string detail = null)
        {
            Enqueue(eventCode, outcome, reasonCode, detail, null, null, null, null);
        }
        public static string Begin(string eventCode, string detail = null)
        {
            if (!Available) return null;
            try { string id = Guid.NewGuid().ToString("N"); lock (gate) { if (operations.Count < 128) operations[id] = clock.Elapsed.TotalMilliseconds; } Enqueue(eventCode + ".begin", "requested", null, detail, id, null, null, null); return id; } catch { return null; }
        }
        public static void End(string operationId, string eventCode, string outcome, string reasonCode = null, string detail = null)
        {
            try
            {
                KeyValuePair<string, double>[] duration = null;
                lock (gate) { double began; if (operationId != null && operations.TryGetValue(operationId, out began)) { operations.Remove(operationId); duration = new[] { new KeyValuePair<string, double>("durationMs", clock.Elapsed.TotalMilliseconds - began) }; } }
                Enqueue(eventCode + ".result", outcome, reasonCode, detail, operationId, null, null, duration);
            }
            catch { }
        }
        internal static void Health()
        {
            if (!Available) return;
            try
            {
                KeyValuePair<string, double>[] values;
                lock (gate)
                {
                    var list = new List<KeyValuePair<string, double>>(FieldLimit * 2 + 1);
                    foreach (var pair in counts) { list.Add(new KeyValuePair<string, double>(pair.Key, pair.Value)); Aggregate(pair.Key, pair.Value, true); }
                    counts.Clear();
                    foreach (var pair in metrics) { list.Add(pair); Aggregate(pair.Key, pair.Value, false); }
                    list.Add(new KeyValuePair<string, double>("telemetry.dropped", dropped));
                    values = list.ToArray();
                    if (++healthWindows >= 10) FlushUploadWindow(false);
                    foreach (var pair in errors)
                    {
                        var error = pair.Value; if (error.reported == error.count) continue;
                        Enqueue(error.code == "Warning" ? "runtime.warning.summary" : "runtime.error.summary", error.code == "Warning" ? "observed" : "failed", error.code, null, null, null, pair.Key,
                            new[] { new KeyValuePair<string, double>("count", error.count) });
                        error.reported = error.count;
                    }
                }
                // Full local summary avoids duplicating context for every small metric group.
                Enqueue("health.summary", "observed", null, null, null, null, null, values, false);
            }
            catch { }
        }
        // Called under gate; a transition flushes using the previous immutable context.
        static void Aggregate(string key, double value, bool sum)
        {
            double prior;
            if (!uploadWindow.TryGetValue(key, out prior) && uploadWindow.Count >= FieldLimit * 2) { dropped++; return; }
            uploadWindow[key] = sum ? prior + value : key == "frame.maxMs" ? Math.Max(prior, value) : value;
        }
        static void FlushUploadWindow(bool interrupted)
        {
            if (uploadWindow.Count == 0) return;
            var aggregated = new List<KeyValuePair<string, double>>(uploadWindow.Count + 4);
            foreach (var pair in uploadWindow) aggregated.Add(pair);
            aggregated.Add(new KeyValuePair<string, double>("windowSamples", healthWindows));
            aggregated.Add(new KeyValuePair<string, double>("windowDurationMs", clock.Elapsed.TotalMilliseconds - uploadWindowStarted));
            aggregated.Add(new KeyValuePair<string, double>("windowInterrupted", interrupted ? 1 : 0));
            aggregated.Add(new KeyValuePair<string, double>("telemetry.dropped", dropped));
            var values = aggregated.ToArray(); uploadWindow.Clear(); healthWindows = 0; uploadWindowStarted = clock.Elapsed.TotalMilliseconds;
            for (int offset = 0; offset < values.Length; offset += 8)
            {
                int length = Math.Min(8, values.Length - offset); var part = new KeyValuePair<string, double>[length]; Array.Copy(values, offset, part, 0, length);
                Enqueue("health.aggregate", "observed", null, null, null, null, null, part);
            }
        }
        static void ReceiveLog(string message, string stack, LogType type)
        {
            if (!Available || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert && type != LogType.Warning)) return;
            try
            {
                // Bounded fingerprint work in the callback, normalization/redaction occurs on the worker.
                string signature = Fingerprint(Bound(message, 512) + Bound(stack, 256));
                lock (gate)
                {
                    ErrorCount entry;
                    if (errors.TryGetValue(signature, out entry)) { entry.count++; return; }
                    if (errors.Count >= 128) { dropped++; return; }
                    errors[signature] = new ErrorCount { count = 1, reported = 1, code = type.ToString() };
                }
                Enqueue(type == LogType.Warning ? "runtime.warning" : "runtime.error", type == LogType.Warning ? "observed" : "failed", type.ToString(), message, null, stack, signature, null);
            }
            catch { }
        }
        static void Enqueue(string code, string outcome, string reason, string detail, string operation, string stack, string signature, KeyValuePair<string, double>[] values, bool upload = true)
        {
            if (!Available || string.IsNullOrEmpty(code)) return;
            try
            {
                detail = Bound(detail, 1024); stack = Bound(stack, 4096);
                int bytes = 512 + (detail == null ? 0 : detail.Length * 2) + (stack == null ? 0 : stack.Length * 2) + (values == null ? 0 : values.Length * 128);
                lock (gate)
                {
                    if (queue.Count >= QueueLimit || queueBytes + bytes > QueueBytesLimit) { dropped++; return; }
                    KeyValuePair<string, string>[] identity = null;
                    if (code == "app.identity") { identity = new KeyValuePair<string, string>[metadata.Count]; int index = 0; foreach (var pair in metadata) identity[index++] = pair; }
                    queue.Enqueue(new Record { code = Bound(code, 96), outcome = Bound(outcome, 32), reason = Bound(reason, 96), detail = detail,
                        operation = Bound(operation, 128), stack = stack, signature = signature, utc = DateTime.UtcNow.ToString("O"), mono = clock.Elapsed.TotalMilliseconds,
                        seq = ++sequence, bytes = bytes, context = contextSnapshot, values = values, upload = upload, metadata = identity });
                    queueBytes += bytes;
                }
                wake.Set();
            }
            catch { }
        }

        static void WriteLoop()
        {
            StreamWriter writer = null; string marker = null;
            try
            {
                Directory.CreateDirectory(directory);
                // Unique session segments allow two instances without shared-writer locks.
                string output = Path.Combine(directory, "events-" + session + ".jsonl");
                Prune(directory, 16 * 1024 * 1024, "events-*.jsonl");
                marker = Path.Combine(directory, "run-" + session + ".open");
                foreach (string previous in Directory.GetFiles(directory, "run-*.open"))
                {
                    int pid; string[] markerParts = ReadMarker(previous);
                    if (markerParts.Length > 0 && int.TryParse(markerParts[0], out pid))
                    {
                        try { if (!Process.GetProcessById(pid).HasExited) continue; } catch { }
                    }
                    Event("app.previousRun.unclean", "unknown", "missingCleanExit", Path.GetFileName(previous));
                    try { File.Delete(previous); } catch { }
                }
                WriteMarker(marker, output);
                writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                long size = 0; int segment = 0; var flushAt = Stopwatch.StartNew();
                while (true)
                {
                    Record record = null;
                    lock (gate) { if (queue.Count > 0) { record = queue.Dequeue(); queueBytes -= record.bytes; } else if (stopping) break; }
                    if (record == null) wake.WaitOne(250);
                    else
                    {
                        if (record.code == "app.identity")
                        {
                            var identity = new List<KeyValuePair<string, string>>(record.metadata ?? new KeyValuePair<string, string>[0]);
                            identity.Add(new KeyValuePair<string, string>("sourceManifestSHA256", SourceHash())); record.metadata = identity.ToArray();
                        }
                        string line = Serialize(record, false); writer.WriteLine(line); size += Encoding.UTF8.GetByteCount(line) + 1;
                        if (record.code == "app.identity") { File.WriteAllText(Path.Combine(directory, "build-" + session + ".json"), line); Prune(directory, 4 * 1024 * 1024, "build-*.json"); }
                        if (record.upload) NativeTelemetryUpload.Add(Serialize(record, true));
                    }
                    if (flushAt.ElapsedMilliseconds >= 1000) { writer.Flush(); flushAt.Restart(); long evicted = Prune(directory, 16 * 1024 * 1024, "events-*.jsonl", output); if (evicted > 0) Count("telemetry.history.evictedFiles", (int)evicted); }
                    if (size >= SegmentLimit)
                    {
                        writer.Dispose(); output = Path.Combine(directory, "events-" + session + "-" + (++segment) + ".jsonl");
                        WriteMarker(marker, output);
                        writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)); size = 0;
                    }
                }
                writer.Flush(); writer.Dispose(); writer = null;
                if (marker != null) File.Delete(marker);
            }
            catch { disabled = true; lock (gate) { queue.Clear(); queueBytes = 0; } }
            finally
            {
                try { if (writer != null) writer.Dispose(); } catch { }
                NativeTelemetryUpload.Stop(); NativeTelemetryUpload.WaitForFlush(100);
            }
        }
        static string[] ReadMarker(string path)
        {
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (input.Length > 256) return new string[0]; byte[] data = new byte[(int)input.Length]; int used = 0, count;
                    while (used < data.Length && (count = input.Read(data, used, data.Length - used)) > 0) used += count;
                    return Encoding.UTF8.GetString(data, 0, used).Split('\n');
                }
            }
            catch { return new string[0]; }
        }
        static void WriteMarker(string marker, string output)
        {
            string temporary = marker + ".tmp";
            File.WriteAllText(temporary, Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "\n" + Path.GetFileName(output));
            if (File.Exists(marker)) File.Replace(temporary, marker, null); else File.Move(temporary, marker);
        }
        internal static long Prune(string path, long budget, string pattern, string active = null)
        {
            var protectedFiles = new HashSet<string>(StringComparer.Ordinal);
            if (active != null) protectedFiles.Add(active);
            foreach (string marker in Directory.EnumerateFiles(path, "run-*.open"))
            {
                string[] parts = ReadMarker(marker); int pid;
                if (parts.Length < 2 || !int.TryParse(parts[0], out pid) || Path.GetFileName(parts[1]) != parts[1] || !parts[1].StartsWith("events-", StringComparison.Ordinal) || !parts[1].EndsWith(".jsonl", StringComparison.Ordinal)) continue;
                try { if (!Process.GetProcessById(pid).HasExited) protectedFiles.Add(Path.Combine(path, parts[1])); }
                catch { }
            }
            var files = new DirectoryInfo(path).GetFiles(pattern); Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            long total = 0; foreach (var file in files) total += file.Length;
            long removed = 0;
            foreach (var file in files) { if (total <= budget) break; if (protectedFiles.Contains(file.FullName)) continue; try { long length = file.Length; file.Delete(); total -= length; removed++; } catch { } }
            return removed;
        }
        internal static string Bound(string value, int max) { return value == null || value.Length <= max ? value : value.Substring(0, max); }
        static string Sanitize(string value)
        {
            if (value == null) return null;
            if (secrets.IsMatch(value)) return "<sensitive text redacted>";
            if (!string.IsNullOrEmpty(home)) value = value.Replace(home, "<home>");
            value = userPaths.Replace(value, "<user>");
            return ip.Replace(value, "<ip>");
        }
        static string Fingerprint(string value) { unchecked { uint hash = 2166136261; foreach (char c in value) hash = (hash ^ c) * 16777619; return hash.ToString("x8"); } }
        static string SourceHash()
        {
            try
            {
                foreach (string path in new[] { Path.Combine(dataPath, "build-source.json"), Path.Combine(dataPath, "Resources/build-source.json"), Path.Combine(dataPath, "../build-source.json") })
                    if (File.Exists(path) && new FileInfo(path).Length <= 8 * 1024 * 1024)
                        using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            }
            catch { }
            return "unknown";
        }
        static string Serialize(Record record, bool upload)
        {
            var text = new StringBuilder(2048); text.Append('{');
            JsonField(text, "schemaVersion", "1", false); JsonField(text, "eventCode", record.code);
            JsonField(text, "recordType", record.code.StartsWith("health.", StringComparison.Ordinal) ? "health" : record.operation != null ? "operation" : record.code == "app.identity" ? "metadata" : "event");
            JsonField(text, "severity", record.outcome == "failed" || record.outcome == "timedOut" || record.code.StartsWith("runtime.error", StringComparison.Ordinal) ? "error" : record.code.StartsWith("runtime.warning", StringComparison.Ordinal) ? "warning" : "info");
            JsonField(text, "component", "game"); int dot = record.code.IndexOf('.'); JsonField(text, "subsystem", dot > 0 ? record.code.Substring(0, dot) : "app");
            JsonField(text, "utc", record.utc); JsonField(text, "monotonicMs", record.mono.ToString("F3", CultureInfo.InvariantCulture), false);
            JsonField(text, "eventSequence", record.seq.ToString(CultureInfo.InvariantCulture), false);
            JsonField(text, "buildIdRef", build ?? "unknown"); JsonField(text, "appSessionId", session ?? "unknown");
            JsonField(text, "operationId", record.operation); JsonField(text, "outcome", record.outcome); JsonField(text, "reasonCode", record.reason);
            text.Append(",\"boundedAttributes\":{"); bool first = true; int attributes = 0, omitted = 0;
            foreach (var pair in record.context) { if (upload && attributes >= 32) { omitted++; continue; } if (!first) text.Append(','); first = false; attributes++; JsonQuote(text, pair.Key); text.Append(':'); JsonQuote(text, Sanitize(pair.Value)); }
            if (record.values != null) foreach (var pair in record.values) { if (upload && attributes >= 32) { omitted++; continue; } if (!first) text.Append(','); first = false; attributes++; JsonQuote(text, pair.Key); text.Append(':').Append(pair.Value.ToString("R", CultureInfo.InvariantCulture)); }
            if (record.signature != null) { if (!upload || attributes < 32) { if (!first) text.Append(','); JsonQuote(text, "signature"); text.Append(':'); JsonQuote(text, record.signature); first = false; attributes++; } else omitted++; }
            if (record.metadata != null) foreach (var pair in record.metadata)
            {
                bool duplicate = false; foreach (var existing in record.context) if (existing.Key == pair.Key) { duplicate = true; break; }
                if (duplicate) continue; if (upload && attributes >= 32) { omitted++; continue; } if (!first) text.Append(','); first = false; attributes++; JsonQuote(text, pair.Key); text.Append(':'); JsonQuote(text, Sanitize(pair.Value));
            }
            text.Append('}');
            if (!upload) { JsonField(text, "detail", Sanitize(record.detail)); JsonField(text, "stack", Sanitize(record.stack)); }
            text.Append('}'); if (omitted > 0) Count("telemetry.upload.attributesOmitted", omitted); return text.ToString();
        }
        internal static void JsonField(StringBuilder text, string key, string value, bool quote = true)
        {
            if (text.Length > 1 && text[text.Length - 1] != '{') text.Append(','); JsonQuote(text, key); text.Append(':');
            if (quote) JsonQuote(text, value); else text.Append(value);
        }
        internal static void JsonQuote(StringBuilder text, string value)
        {
            if (value == null) { text.Append("null"); return; }
            text.Append('"'); foreach (char c in value) { switch (c) { case '"': text.Append("\\\""); break; case '\\': text.Append("\\\\"); break; case '\n': text.Append("\\n"); break; case '\r': text.Append("\\r"); break; case '\t': text.Append("\\t"); break; default: if (c < 32 || char.IsSurrogate(c)) text.Append("\\u").Append(((int)c).ToString("x4")); else text.Append(c); break; } } text.Append('"');
        }
        internal static void Stop()
        {
            if (!started) return;
            try
            {
                lock (gate) FlushUploadWindow(true);
                Event("app.quit", "completed"); Application.logMessageReceivedThreaded -= ReceiveLog;
                stopping = true; wake.Set();
                if (worker != null && worker != Thread.CurrentThread) worker.Join(300);
            }
            catch { }
        }
    }
}
