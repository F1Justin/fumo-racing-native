using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Fumo.NativePlayable
{
    // Independent HTTPS channel. Each configuration owns its queue and thread lifetime.
    internal static class NativeTelemetryUpload
    {
        const int QueueLimit = 256, BytesLimit = 512 * 1024, BatchBytesLimit = 240 * 1024;
        const long SpoolBytesLimit = 8 * 1024 * 1024;
        static readonly object gate = new object();
        static UploadState current;

        sealed class UploadState
        {
            internal readonly object gate = new object();
            internal readonly Queue<string> pending = new Queue<string>();
            internal readonly AutoResetEvent wake = new AutoResetEvent(false);
            internal string endpoint, spool;
            internal Thread worker, previousWorker;
            internal HttpWebRequest request;
            internal bool stopping;
            internal int bytes;
            internal long dropped, evictedBatches, evictedTemporary, writeFailures;
        }

        internal static void Configure(string directory, string configured)
        {
            if (string.IsNullOrEmpty(configured)) return;
            Uri uri;
            if (!Uri.TryCreate(configured, UriKind.Absolute, out uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            { NativeTelemetry.Event("telemetry.upload.disabled", "ignored", "invalidEndpoint"); return; }
            try
            {
                UploadState previous, next;
                string spool = Path.Combine(directory, "spool");
                lock (gate)
                {
                    previous = current;
                    if (previous != null && !previous.stopping && previous.worker != null && previous.worker.IsAlive && previous.endpoint == uri.AbsoluteUri && previous.spool == spool) return;
                    next = new UploadState { endpoint = uri.AbsoluteUri, spool = spool, previousWorker = previous == null ? null : previous.worker };
                    next.worker = new Thread(() => Run(next)) { IsBackground = true, Name = "Fumo diagnostic upload" };
                    current = next;
                    next.worker.Start();
                }
                SignalStop(previous);
            }
            catch { NativeTelemetry.Count("telemetry.upload.configure-failed"); }
        }

        internal static void Add(string json)
        {
            if (json == null) return;
            try
            {
                int size = json.Length > BytesLimit / 2 ? BytesLimit + 1 : json.Length * 2;
                bool tooLarge = size > BytesLimit || Encoding.UTF8.GetByteCount(json) > BatchBytesLimit - 256;
                UploadState state;
                // Select and admit atomically with configuration replacement. No IO under either lock.
                lock (gate)
                {
                    state = current;
                    if (state == null) return;
                    lock (state.gate)
                    {
                        if (state.stopping) { state.dropped++; NativeTelemetry.Count("telemetry.upload.closed-dropped"); return; }
                        // One over-sized item must not permanently block the head of the queue.
                        if (state.pending.Count >= QueueLimit || tooLarge || state.bytes + size > BytesLimit) { state.dropped++; return; }
                        state.pending.Enqueue(json); state.bytes += size;
                    }
                }
                state.wake.Set();
            }
            catch { NativeTelemetry.Count("telemetry.upload.enqueue-failed"); }
        }

        static bool Stopping(UploadState state) { lock (state.gate) return state.stopping; }
        static bool HasPending(UploadState state) { lock (state.gate) return state.pending.Count != 0; }

        static void Run(UploadState state)
        {
            try
            {
                // Never make the new main thread wait for an old HTTP request or disk flush.
                if (state.previousWorker != null && state.previousWorker != Thread.CurrentThread && state.previousWorker.IsAlive) state.previousWorker.Join();
                state.previousWorker = null;
                Directory.CreateDirectory(state.spool);
                EnsureCapacity(state, 0);
                int retrySeconds = 1;
                DateTime retryAt = DateTime.MinValue;
                while (true)
                {
                    if (!Stopping(state)) state.wake.WaitOne(250);
                    bool persisted = MakeBatch(state);
                    if (Stopping(state))
                    {
                        // Producers are closed and this queue is finite. Flush all remaining batches.
                        // A disk failure remains a reported failure; never spin forever during exit.
                        if (!persisted || !HasPending(state)) break;
                        continue;
                    }
                    if (!persisted) { state.wake.WaitOne(1000); continue; }
                    if (DateTime.UtcNow < retryAt) { state.wake.WaitOne(500); continue; }
                    FileInfo[] files;
                    try { files = new DirectoryInfo(state.spool).GetFiles("batch-*.json"); }
                    catch { NativeTelemetry.Count("telemetry.upload.spool-failed"); state.wake.WaitOne(1000); continue; }
                    Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                    if (files.Length == 0) { state.wake.WaitOne(1000); continue; }
                    var file = files[0]; bool permanent; int events;
                    bool acknowledged = Send(state, file, out permanent, out events);
                    if ((acknowledged || permanent) && DeleteBatch(state, file))
                    {
                        if (permanent) { lock (state.gate) state.dropped += Math.Max(0, events); NativeTelemetry.Count("telemetry.upload.rejected-batches"); }
                        retrySeconds = 1;
                    }
                    else { retryAt = DateTime.UtcNow.AddSeconds(retrySeconds); retrySeconds = Math.Min(60, retrySeconds * 2); }
                    Report(state);
                }
            }
            catch { NativeTelemetry.Count("telemetry.upload.worker-failed"); }
            finally { lock (state.gate) state.stopping = true; Report(state); }
        }

        static bool DeleteBatch(UploadState state, FileInfo file)
        {
            try { file.Delete(); return true; }
            catch { lock (state.gate) state.writeFailures++; NativeTelemetry.Count("telemetry.upload.delete-failed"); return false; }
        }

        static bool MakeBatch(UploadState state)
        {
            var candidates = new List<string>(32);
            lock (state.gate) foreach (string value in state.pending) { if (candidates.Count == 32) break; candidates.Add(value); }
            var entries = new List<string>(32); int length = 256;
            foreach (string value in candidates)
            {
                int encoded = Encoding.UTF8.GetByteCount(value);
                if (length + encoded > BatchBytesLimit) break;
                length += encoded + 1; entries.Add(value);
            }
            if (entries.Count == 0) return true;
            string id = Guid.NewGuid().ToString("N");
            var body = new StringBuilder(length); body.Append("{\"schemaVersion\":1,\"batchId\":"); NativeTelemetry.JsonQuote(body, id); body.Append(",\"events\":[");
            for (int i = 0; i < entries.Count; i++) { if (i > 0) body.Append(','); body.Append(entries[i]); } body.Append("]}");
            string target = Path.Combine(state.spool, "batch-" + id + ".json"), temporary = target + ".tmp";
            try
            {
                Directory.CreateDirectory(state.spool);
                if (!EnsureCapacity(state, length)) return false;
                File.WriteAllText(temporary, body.ToString(), new UTF8Encoding(false));
                File.Move(temporary, target);
                // Only this worker removes entries. Producers may append while IO runs.
                lock (state.gate) for (int i = 0; i < entries.Count; i++) { string value = state.pending.Dequeue(); state.bytes -= value.Length * 2; }
                return true;
            }
            catch
            {
                lock (state.gate) state.writeFailures++;
                NativeTelemetry.Count("telemetry.upload.write-failed");
                // Retain pending until a durable batch exists. Partial temporary files are expendable.
                try { File.Delete(temporary); } catch { }
                return false;
            }
        }

        static bool EnsureCapacity(UploadState state, long incoming)
        {
            try
            {
                // Include incomplete files left by crashes, not only completed batches.
                var files = new DirectoryInfo(state.spool).GetFiles("batch-*.json*");
                Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                long total = 0; foreach (var file in files) total += file.Length;
                foreach (var file in files)
                {
                    if (total + incoming <= SpoolBytesLimit) break;
                    long size = file.Length; int count = 0;
                    bool temporary = file.Name.EndsWith(".tmp", StringComparison.Ordinal);
                    if (!temporary && size <= 256 * 1024) try { string ignored; ReadBatch(File.ReadAllText(file.FullName), out ignored, out count); } catch { }
                    try
                    {
                        file.Delete(); total -= size;
                        lock (state.gate)
                        {
                            if (temporary) state.evictedTemporary++;
                            else { state.evictedBatches++; state.dropped += Math.Max(0, count); }
                        }
                        NativeTelemetry.Count(temporary ? "telemetry.upload.evicted-temporary" : "telemetry.upload.evicted-batches");
                    }
                    catch { }
                }
                Report(state);
                return total + incoming <= SpoolBytesLimit;
            }
            catch { NativeTelemetry.Count("telemetry.upload.spool-failed"); return false; }
        }

        static void Report(UploadState state)
        {
            try
            {
                lock (gate) if (current != state) return;
                lock (state.gate)
                {
                    NativeTelemetry.Metric("telemetry.upload.dropped", state.dropped);
                    NativeTelemetry.Metric("telemetry.upload.evictedBatches", state.evictedBatches);
                    NativeTelemetry.Metric("telemetry.upload.evictedTemporary", state.evictedTemporary);
                    NativeTelemetry.Metric("telemetry.upload.writeFailures", state.writeFailures);
                    NativeTelemetry.Metric("telemetry.upload.pending", state.pending.Count);
                }
            }
            catch { }
        }

        static bool Send(UploadState state, FileInfo file, out bool permanent, out int events)
        {
            permanent = false; events = 0;
            HttpWebRequest request = null;
            try
            {
                if (file.Length > 256 * 1024) { permanent = true; return false; }
                byte[] data = File.ReadAllBytes(file.FullName); string batchId;
                if (!ReadBatch(Encoding.UTF8.GetString(data), out batchId, out events) || file.Name != "batch-" + batchId + ".json") { permanent = true; return false; }
                request = (HttpWebRequest)WebRequest.Create(state.endpoint);
                request.Method = "POST"; request.ContentType = "application/json"; request.ContentLength = data.Length;
                request.Timeout = 3000; request.ReadWriteTimeout = 3000; request.AllowAutoRedirect = false;
                lock (state.gate) { if (state.stopping) return false; state.request = request; }
                using (var output = request.GetRequestStream()) output.Write(data, 0, data.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var input = response.GetResponseStream())
                {
                    if (response.StatusCode != HttpStatusCode.OK || input == null) return false;
                    byte[] buffer = new byte[4097]; int used = 0, read;
                    while (used < buffer.Length && (read = input.Read(buffer, used, buffer.Length - used)) > 0) used += read;
                    if (used >= buffer.Length) return false;
                    int accepted;
                    return TryAck(Encoding.UTF8.GetString(buffer, 0, used), batchId, out accepted) && accepted == events;
                }
            }
            catch (WebException error)
            {
                using (var response = error.Response as HttpWebResponse)
                {
                    if (response != null) { int status = (int)response.StatusCode; permanent = status >= 400 && status < 500 && status != 408 && status != 429; }
                }
                return false;
            }
            catch { return false; }
            finally { lock (state.gate) if (state.request == request) state.request = null; }
        }

        // Strict flat ACK. Kept as a two-argument method for the functional harness.
        static bool Ack(string json, string id) { int accepted; return TryAck(json, id, out accepted); }
        static bool TryAck(string json, string id, out int accepted)
        {
            accepted = 0;
            if (json == null || json.Length > 4096) return false;
            var fields = new Dictionary<string, string>(); var quoted = new Dictionary<string, bool>(); int index = 0;
            Skip(json, ref index); if (index >= json.Length || json[index++] != '{') return false;
            while (true)
            {
                Skip(json, ref index);
                if (index >= json.Length || json[index] != '"') return false;
                string key = Token(json, ref index); Skip(json, ref index);
                if (key == null || index >= json.Length || json[index++] != ':') return false;
                Skip(json, ref index); bool isString = index < json.Length && json[index] == '"';
                string value = Token(json, ref index);
                if (value == null || fields.ContainsKey(key)) return false;
                fields[key] = value; quoted[key] = isString;
                Skip(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == '}') { index++; break; }
                return false;
            }
            Skip(json, ref index); if (index != json.Length || (fields.Count != 4 && fields.Count != 5)) return false;
            string duplicate;
            if (fields.Count == 5 && (!fields.TryGetValue("duplicate", out duplicate) || quoted["duplicate"] || (duplicate != "true" && duplicate != "false"))) return false;
            string batch, persisted, schema, count;
            return fields.TryGetValue("batchId", out batch) && batch == id && quoted["batchId"]
                && fields.TryGetValue("persisted", out persisted) && persisted == "true" && !quoted["persisted"]
                && fields.TryGetValue("schemaVersion", out schema) && schema == "1" && !quoted["schemaVersion"]
                && fields.TryGetValue("accepted", out count) && !quoted["accepted"] && UnsignedInteger(count, out accepted);
        }
        static bool UnsignedInteger(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) || text.Length > 10 || (text.Length > 1 && text[0] == '0')) return false;
            foreach (char c in text) if (c < '0' || c > '9') return false;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
        static void Skip(string text, ref int index)
        { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++; }
        static string Token(string text, ref int index)
        {
            if (index >= text.Length) return null; int start = index;
            if (text[index] == '"')
            {
                start = ++index;
                while (index < text.Length && text[index] != '"') { if (text[index] == '\\' || text[index] < 32) return null; index++; }
                if (index >= text.Length) return null; string value = text.Substring(start, index - start); index++; return value;
            }
            while (index < text.Length && text[index] != ',' && text[index] != '}' && text[index] != ' ' && text[index] != '\t' && text[index] != '\r' && text[index] != '\n') index++;
            return index > start ? text.Substring(start, index - start) : null;
        }

        // Our persisted batches have this canonical header; count top-level event objects,
        // respecting strings and nested brackets instead of counting textual separators.
        static bool ReadBatch(string json, out string batchId, out int count)
        {
            batchId = null; count = 0;
            const string prefix = "{\"schemaVersion\":1,\"batchId\":\"";
            if (json == null || !json.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int index = prefix.Length, start = index;
            while (index < json.Length && json[index] != '"') { if (!Uri.IsHexDigit(json[index])) return false; index++; }
            if (index - start != 32 || index >= json.Length) return false;
            batchId = json.Substring(start, 32); index++;
            const string middle = ",\"events\":[";
            if (json.Length - index < middle.Length || string.CompareOrdinal(json, index, middle, 0, middle.Length) != 0) return false;
            index += middle.Length;
            while (true)
            {
                Skip(json, ref index);
                if (index >= json.Length || json[index] != '{' || count >= 32) return false;
                var nesting = new Stack<char>(); bool quoted = false;
                do
                {
                    if (index >= json.Length) return false;
                    char c = json[index++];
                    if (quoted)
                    {
                        if (c == '\\') { if (index >= json.Length) return false; index++; }
                        else if (c == '"') quoted = false;
                        else if (c < 32) return false;
                    }
                    else if (c == '"') quoted = true;
                    else if (c == '{' || c == '[') { if (nesting.Count >= 32) return false; nesting.Push(c); }
                    else if (c == '}' || c == ']') { if (nesting.Count == 0 || nesting.Pop() != (c == '}' ? '{' : '[')) return false; }
                }
                while (quoted || nesting.Count > 0);
                count++; Skip(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index >= json.Length || json[index++] != ']') return false;
                Skip(json, ref index); if (index >= json.Length || json[index++] != '}') return false;
                Skip(json, ref index); return index == json.Length;
            }
        }

        static void SignalStop(UploadState state)
        {
            if (state == null) return;
            try
            {
                HttpWebRequest request;
                lock (state.gate) { state.stopping = true; request = state.request; }
                state.wake.Set();
                if (request != null) ThreadPool.QueueUserWorkItem(_ => { try { request.Abort(); } catch { } });
            }
            catch { }
        }
        internal static void Stop()
        {
            try { UploadState state; lock (gate) state = current; SignalStop(state); }
            catch { }
        }
        // Called only by the core writer during its existing bounded background shutdown.
        internal static bool WaitForFlush(int milliseconds)
        {
            try
            {
                UploadState state; lock (gate) state = current;
                if (state == null || state.worker == null || state.worker == Thread.CurrentThread) return true;
                return state.worker.Join(Math.Max(0, Math.Min(1000, milliseconds)));
            }
            catch { return false; }
        }
    }
}
