using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativeNetTransport : IDisposable
    {
        readonly object gate = new object();
        readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        readonly Queue<string> incoming = new Queue<string>();
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        TcpClient client;
        Stream stream;
        volatile string routeDiagnostic;
        bool started;
        bool closeAfterDrain;
        Timer closeDeadline;
        volatile bool connected, disposed;
        volatile string error;
        long lastRead;
        public bool IsConnected { get { return connected && !disposed; } }
        public string Error { get { return error; } }
        public string RouteDiagnostic { get { return routeDiagnostic; } }
        public double SecondsSinceRead { get { return lastRead == 0 ? 0 : TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRead)).TotalSeconds; } }
        public void Connect(string host, int port) { Connect(host, port, null); }
        public void Connect(string host, int port, Uri secureFallback, bool preferSecure = false, bool verifyProtocol = false)
        {
            if (started || disposed) throw new InvalidOperationException("Transport already used");
            started = true;
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535) { Fail("Invalid address or port"); return; }
            new Thread(() => {
                try {
                    var connection = NativeNetConnector.Connect(host, port, stop, secureFallback, preferSecure, verifyProtocol);
                    lock (gate) {
                        if (disposed) { connection.Dispose(); return; }
                        client = connection.Client; stream = connection.Stream; routeDiagnostic = connection.RouteDiagnostic;
                        BeginIO();
                    }
                } catch (Exception e) { Fail(e.Message); }
            }) { IsBackground = true, Name = "Fumo connect" }.Start();
        }
        internal NativeNetTransport(TcpClient accepted) { client = accepted; stream = client.GetStream(); started = true; routeDiagnostic = "accepted system"; BeginIO(); }
        public NativeNetTransport() { }
        void BeginIO()
        {
            client.NoDelay = true; client.ReceiveTimeout = 0; client.SendTimeout = 5000;
            lastRead = DateTime.UtcNow.Ticks; connected = true;
            new Thread(ReadLoop) { IsBackground = true, Name = "Fumo receive" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "Fumo send" }.Start();
        }
        public void CloseGracefully(NativeNetMessage finalMessage)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(finalMessage) + "\n");
            if (bytes.Length > NativeNetProtocol.MaxFrameBytes + 1) { Fail("Message exceeds frame limit"); return; }
            lock (gate) {
                if (disposed) return;
                if (!connected) { Dispose(); return; }
                outgoing.Clear(); outgoing.Enqueue(bytes); closeAfterDrain = true;
                closeDeadline = new Timer(state => Dispose(), null, 1000, Timeout.Infinite);
            }
        }
        public bool Send(NativeNetMessage message) { return Enqueue(JsonUtility.ToJson(message)); }
        bool Enqueue(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            if (bytes.Length > NativeNetProtocol.MaxFrameBytes + 1) { Fail("Message exceeds frame limit"); return false; }
            lock (gate) {
                if (disposed || closeAfterDrain) return false;
                if (outgoing.Count >= NativeNetProtocol.MaxQueue) { Fail("Send queue overflow"); return false; }
                outgoing.Enqueue(bytes); return true;
            }
        }
        public bool TryReceive(out NativeNetMessage message)
        {
            string json; lock (gate) { if (incoming.Count == 0) { message = null; return false; } json = incoming.Dequeue(); }
            try { message = JsonUtility.FromJson<NativeNetMessage>(json); if (message == null || string.IsNullOrEmpty(message.kind)) throw new IOException("Invalid message"); return true; }
            catch (Exception e) { message = null; Fail("Invalid JSON: " + e.Message); return false; }
        }
        void ReadLoop()
        {
            try {
                var buffer = new byte[8192]; var frame = new MemoryStream();
                var utf8 = new UTF8Encoding(false, true);
                while (!disposed) {
                    int count = stream.Read(buffer, 0, buffer.Length);
                    if (count == 0) throw new IOException("Connection closed");
                    Interlocked.Exchange(ref lastRead, DateTime.UtcNow.Ticks);
                    for (int i = 0; i < count; i++) {
                        if (buffer[i] == 10) {
                            string json = utf8.GetString(frame.ToArray()); frame.SetLength(0);
                            if (json == "{\"kind\":\"ping\"}") { Enqueue("{\"kind\":\"pong\"}"); continue; }
                            if (json == "{\"kind\":\"pong\"}") continue;
                            lock (gate) { if (incoming.Count >= NativeNetProtocol.MaxQueue) throw new IOException("Receive queue overflow"); incoming.Enqueue(json); }
                        } else { if (frame.Length >= NativeNetProtocol.MaxFrameBytes) throw new IOException("Frame exceeds limit"); frame.WriteByte(buffer[i]); }
                    }
                }
            } catch (Exception e) { Fail(e.Message); }
        }
        void WriteLoop()
        {
            try {
                long nextPing = DateTime.UtcNow.AddSeconds(5).Ticks;
                while (!disposed) {
                    if (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRead) > TimeSpan.FromSeconds(20).Ticks) throw new IOException("Heartbeat timed out");
                    byte[] bytes = null; lock (gate) { if (outgoing.Count > 0) bytes = outgoing.Dequeue(); }
                    if (bytes != null) stream.Write(bytes, 0, bytes.Length);
                    lock (gate) { if (closeAfterDrain && outgoing.Count == 0) { Dispose(); return; } }
                    if (DateTime.UtcNow.Ticks >= nextPing) { Enqueue("{\"kind\":\"ping\"}"); nextPing = DateTime.UtcNow.AddSeconds(5).Ticks; }
                    if (bytes == null) stop.WaitOne(10);
                }
            } catch (Exception e) { Fail(e.Message); }
        }
        void Fail(string reason) { if (!disposed) error = reason; Dispose(); }
        public void Dispose() { lock (gate) { disposed = true; connected = false; stop.Set(); if (closeDeadline != null) closeDeadline.Dispose(); if (client != null) try { client.Close(); } catch { } } }
        public static bool ParseAddress(string address, out string host, out int port, out string error)
        {
            host = (address ?? "").Trim(); port = NativeNetProtocol.DefaultPort; error = null;
            string suffix = null;
            if (host.StartsWith("[")) { int end = host.IndexOf(']'); if (end < 1) { error = "Invalid IPv6 address"; return false; } string rest = host.Substring(end + 1); string literal = host.Substring(1, end - 1); if (rest.Length > 0) { if (!rest.StartsWith(":")) { error = "Invalid address"; return false; } suffix = rest.Substring(1); } host = literal; }
            else if (host.IndexOf(':') == host.LastIndexOf(':') && host.IndexOf(':') >= 0) { int index = host.IndexOf(':'); suffix = host.Substring(index + 1); host = host.Substring(0, index); }
            if (suffix != null && (!int.TryParse(suffix, out port) || port < 1 || port > 65535)) { error = "Invalid port"; return false; }
            if (host.Length == 0 || host.IndexOfAny(new[] {'/', '\\', ' ', '\t', '\n', '\r'}) >= 0) { error = "Invalid host"; return false; } return true;
        }
    }
}
