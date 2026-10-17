using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Fumo.NativePlayable
{
    // Interface binding is per socket: the player's routing table and proxy remain untouched.
    public static class NativeNetConnector
    {
        public sealed class Connection : IDisposable
        {
            public TcpClient Client;
            public Stream Stream;
            public string RouteDiagnostic;
            public void Dispose() { if (Stream != null) Stream.Dispose(); if (Client != null) Client.Close(); }
        }
        sealed class Route { public string Name; public int Index, PrefixLength; public IPAddress Source; }
        [DllImport("libc", SetLastError = true)] static extern int setsockopt(int fd, int level, int option, byte[] value, uint length);
        [DllImport("libc")] static extern uint if_nametoindex(string name);
        [DllImport("libc")] static extern int getifaddrs(out IntPtr addresses);
        [DllImport("libc")] static extern void freeifaddrs(IntPtr addresses);
        [StructLayout(LayoutKind.Sequential)] struct InterfaceAddress
        {
            public IntPtr Next, Name; public uint Flags; public IntPtr Address, Mask, Destination, Data;
        }
        static readonly bool Windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        static readonly bool Mac = !Windows && Directory.Exists("/System/Library/CoreServices");
        static Dictionary<string, int> InterfacePrefixes()
        {
            var result = new Dictionary<string, int>(); if (Windows) return result;
            IntPtr head = IntPtr.Zero;
            try {
                if (getifaddrs(out head) != 0) return result;
                for (IntPtr next = head; next != IntPtr.Zero;) {
                    var item = (InterfaceAddress)Marshal.PtrToStructure(next, typeof(InterfaceAddress)); next = item.Next;
                    if (item.Address == IntPtr.Zero || item.Mask == IntPtr.Zero) continue;
                    int family = Mac ? Marshal.ReadByte(item.Address, 1) : Marshal.ReadInt16(item.Address);
                    int bytes = family == 2 ? 4 : (family == (Mac ? 30 : 10) ? 16 : 0); if (bytes == 0) continue;
                    int offset = bytes == 4 ? 4 : 8; var address = new byte[bytes]; var mask = new byte[bytes];
                    Marshal.Copy(IntPtr.Add(item.Address, offset), address, 0, bytes); Marshal.Copy(IntPtr.Add(item.Mask, offset), mask, 0, bytes);
                    int prefix = 0; bool zero = false, valid = true;
                    foreach (byte value in mask) for (int bit = 7; bit >= 0; bit--) { bool set = (value & (1 << bit)) != 0; if (set && zero) valid = false; if (set) prefix++; else zero = true; }
                    if (valid && prefix > 0) result[Marshal.PtrToStringAnsi(item.Name) + "|" + Convert.ToBase64String(address)] = prefix;
                }
            } catch { }
            finally { if (head != IntPtr.Zero) freeifaddrs(head); }
            return result;
        }
        static List<Route> Routes()
        {
            var result = new List<Route>(); var nativePrefixes = InterfacePrefixes();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus != OperationalStatus.Up || (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)) continue;
                string name = nic.Name.ToLowerInvariant();
                if (name.StartsWith("utun") || name.StartsWith("tun") || name.StartsWith("tap") || name.StartsWith("veth") || name.StartsWith("docker") || name.StartsWith("bridge")) continue;
                var properties = nic.GetIPProperties();
                foreach (var unicast in properties.UnicastAddresses) {
                    var ip = unicast.Address;
                    if (IPAddress.IsLoopback(ip) || ip.IsIPv6Multicast) continue;
                    try {
                        int index = Windows ? (ip.AddressFamily == AddressFamily.InterNetwork ? properties.GetIPv4Properties().Index : properties.GetIPv6Properties().Index) : (int)if_nametoindex(nic.Name);
                        int prefix = 0;
                        try { prefix = unicast.PrefixLength; } catch { }
                        if (prefix == 0) nativePrefixes.TryGetValue(nic.Name + "|" + Convert.ToBase64String(ip.GetAddressBytes()), out prefix);
                        if (ip.AddressFamily == AddressFamily.InterNetwork && prefix == 0) {
                            try { foreach (byte octet in unicast.IPv4Mask.GetAddressBytes()) for (int bit = 7; bit >= 0; bit--) if ((octet & (1 << bit)) != 0) prefix++; } catch { }
                        }
                        if (index > 0) result.Add(new Route { Name = nic.Name, Index = index, Source = ip, PrefixLength = prefix });
                    } catch { }
                }
            }
            return result;
        }
        static bool Local(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            byte[] b = ip.GetAddressBytes();
            if (IPAddress.IsLoopback(ip)) return true;
            if (b.Length == 4) return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            return ip.IsIPv6LinkLocal || (b[0] & 254) == 252;
        }
        static bool OnLink(IPAddress destination, Route route)
        {
            if (destination.IsIPv4MappedToIPv6) destination = destination.MapToIPv4();
            if (destination.AddressFamily != route.Source.AddressFamily || IPAddress.IsLoopback(destination) || Fake(destination)) return false;
            if (destination.IsIPv6LinkLocal && (destination.ScopeId == 0 || destination.ScopeId != route.Index || !route.Source.IsIPv6LinkLocal)) return false;
            if (!destination.IsIPv6LinkLocal && route.Source.IsIPv6LinkLocal) return false;
            byte[] target = destination.GetAddressBytes(), source = route.Source.GetAddressBytes(); int prefix = route.PrefixLength;
            if (prefix < 1 || prefix > target.Length * 8) return false;
            for (int i = 0; i < target.Length; i++) { int bits = Math.Min(8, prefix); if (bits == 0) break; int mask = 255 << (8 - bits); if ((target[i] & mask) != (source[i] & mask)) return false; prefix -= bits; }
            return true;
        }
        static List<Route> PhysicalRoutes(IPAddress destination, List<Route> routes)
        {
            if (destination.IsIPv4MappedToIPv6) destination = destination.MapToIPv4();
            var result = new List<Route>();
            if (IPAddress.IsLoopback(destination) || Fake(destination)) return result;
            foreach (var route in routes) {
                if (route.Source.AddressFamily != destination.AddressFamily) continue;
                if (Local(destination) ? !OnLink(destination, route) : route.Source.IsIPv6LinkLocal) continue;
                result.Add(route);
            }
            result.Sort((a, b) => b.PrefixLength.CompareTo(a.PrefixLength)); return result;
        }
        static bool Fake(IPAddress ip) { var b = ip.GetAddressBytes(); return (b.Length == 4 && b[0] == 198 && (b[1] == 18 || b[1] == 19)) || (b.Length == 16 && b[0] == 32 && b[1] == 1 && b[2] == 0 && b[3] == 2 && b[4] == 0 && b[5] == 0) || (b.Length == 16 && b[0] == 253 && b[1] == 254 && b[2] == 220 && b[3] == 186 && b[4] == 152 && b[5] == 118 && b[6] == 0 && b[7] == 0); }
        static void Bind(Socket socket, Route route)
        {
            if (route == null) return;
            bool v4 = socket.AddressFamily == AddressFamily.InterNetwork;
            if (Windows) socket.SetSocketOption(v4 ? SocketOptionLevel.IP : SocketOptionLevel.IPv6, (SocketOptionName)31, v4 ? IPAddress.HostToNetworkOrder(route.Index) : route.Index);
            else {
                byte[] value = Mac ? BitConverter.GetBytes(route.Index) : Encoding.ASCII.GetBytes(route.Name + "\0");
                int result = setsockopt(socket.Handle.ToInt32(), Mac ? (v4 ? 0 : 41) : 1, Mac ? (v4 ? 25 : 125) : 25, value, (uint)value.Length);
                if (result != 0) throw new IOException("Interface binding failed: " + Marshal.GetLastWin32Error());
            }
            socket.Bind(new IPEndPoint(route.Source, 0));
        }
        // Mono completes cancelled operations later; release their event only after it signals.
        static int WaitAsync(IAsyncResult result, WaitHandle cancellation, int milliseconds)
        {
            var handle = result.AsyncWaitHandle;
            int outcome = WaitHandle.WaitAny(new[] { handle, cancellation }, milliseconds);
            if (outcome == 0) handle.Close();
            else ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) => handle.Close(), null, Timeout.Infinite, true);
            return outcome;
        }
        static int Remaining(DateTime deadline) { return Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds); }
        static TcpClient Dial(IPAddress ip, int port, Route route, WaitHandle stop, DateTime deadline)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var client = new TcpClient(ip.AddressFamily);
            try {
                Bind(client.Client, route);
                var ar = client.BeginConnect(ip, port, null, null);
                {
                    if (WaitAsync(ar, stop, Math.Min(1500, Remaining(deadline))) != 0) throw new IOException("Connection cancelled or timed out");
                    client.EndConnect(ar);
                }
                if (stop.WaitOne(0)) throw new IOException("Connection cancelled");
                return client;
            } catch { client.Close(); throw; }
        }
        static byte[] Query(string host, int type, ushort id)
        {
            var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (string label in new System.Globalization.IdnMapping().GetAscii(host.TrimEnd('.')).Split('.')) {
                byte[] encoded = Encoding.ASCII.GetBytes(label); if (encoded.Length == 0 || encoded.Length > 63) throw new IOException("Invalid DNS name");
                bytes.Add((byte)encoded.Length); bytes.AddRange(encoded);
            }
            bytes.AddRange(new byte[] { 0, 0, (byte)type, 0, 1 }); return bytes.ToArray();
        }
        static int U16(byte[] b, int i) { if (i + 2 > b.Length) throw new IOException("Truncated DNS response"); return (b[i] << 8) | b[i + 1]; }
        static void SkipName(byte[] b, ref int i)
        {
            for (int n = 0; n < 128; n++) {
                if (i >= b.Length) throw new IOException("Truncated DNS name"); int length = b[i++];
                if (length == 0) return;
                if ((length & 192) == 192) { if (i >= b.Length) throw new IOException("Truncated DNS pointer"); i++; return; }
                if (length > 63 || i + length > b.Length) throw new IOException("Invalid DNS name"); i += length;
            }
            throw new IOException("DNS name limit");
        }
        static string DnsName(byte[] bytes, ref int position)
        {
            int cursor = position, jumps = 0; bool pointer = false; var labels = new List<string>();
            while (++jumps <= 128) {
                if (cursor >= bytes.Length) throw new IOException("Truncated DNS name"); int length = bytes[cursor++];
                if (length == 0) { if (!pointer) position = cursor; return string.Join(".", labels).ToLowerInvariant(); }
                if ((length & 192) == 192) {
                    if (cursor >= bytes.Length) throw new IOException("Truncated DNS pointer"); int target = ((length & 63) << 8) | bytes[cursor++];
                    if (!pointer) position = cursor; pointer = true; cursor = target; continue;
                }
                if (length > 63 || cursor + length > bytes.Length) throw new IOException("Invalid DNS label");
                labels.Add(Encoding.ASCII.GetString(bytes, cursor, length)); cursor += length;
                if (labels.Count > 127) throw new IOException("DNS label limit");
            }
            throw new IOException("DNS compression loop");
        }
        sealed class DnsRecord { public string Owner, Alias; public IPAddress Address; }
        static List<IPAddress> Answers(byte[] b, ushort id, string host, int requestedType)
        {
            if (b.Length < 12 || U16(b, 0) != id || (b[2] & 128) == 0 || (b[2] & 122) != 0 || (b[3] & 15) != 0) throw new IOException("Invalid DNS response");
            int i = 12, questions = U16(b, 4), answers = U16(b, 6);
            if (questions != 1 || answers > 128) throw new IOException("DNS response limit");
            string expected = new System.Globalization.IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
            if (DnsName(b, ref i) != expected || U16(b, i) != requestedType || U16(b, i + 2) != 1) throw new IOException("DNS question mismatch"); i += 4;
            var records = new List<DnsRecord>();
            for (int a = 0; a < answers; a++) {
                string owner = DnsName(b, ref i); int type = U16(b, i), cls = U16(b, i + 2), length = U16(b, i + 8); i += 10;
                if (i + length > b.Length) throw new IOException("Truncated DNS record");
                if (cls == 1 && type == requestedType && ((type == 1 && length == 4) || (type == 28 && length == 16))) { var address = new byte[length]; Buffer.BlockCopy(b, i, address, 0, length); records.Add(new DnsRecord { Owner = owner, Address = new IPAddress(address) }); }
                if (cls == 1 && type == 5) { int cursor = i; string alias = DnsName(b, ref cursor); if (cursor != i + length) throw new IOException("Invalid CNAME length"); records.Add(new DnsRecord { Owner = owner, Alias = alias }); }
                i += length;
            }
            var names = new HashSet<string> { expected };
            for (int chain = 0; chain < 16; chain++) { bool changed = false; foreach (var record in records) if (record.Alias != null && names.Contains(record.Owner) && names.Add(record.Alias)) changed = true; if (!changed) break; }
            var result = new List<IPAddress>(); foreach (var record in records) if (record.Address != null && names.Contains(record.Owner)) result.Add(record.Address); return result;
        }
        sealed class DnsResolver
        {
            public readonly string Host, Ipv4, Ipv6, HttpsIpv4, HttpsIpv6;
            public DnsResolver(string host, string ipv4, string ipv6, string httpsIpv4, string httpsIpv6)
            { Host = host; Ipv4 = ipv4; Ipv6 = ipv6; HttpsIpv4 = httpsIpv4; HttpsIpv6 = httpsIpv6; }
        }
        static readonly DnsResolver[] DnsResolvers = {
            new DnsResolver("dns.alidns.com", "223.5.5.5", "2400:3200::1", "223.5.5.5", "2400:3200::1"),
            new DnsResolver("doh.pub", "119.29.29.29", "2402:4e00::", "1.12.12.12", null),
            new DnsResolver("cloudflare-dns.com", "1.1.1.1", "2606:4700:4700::1111", "1.1.1.1", "2606:4700:4700::1111"),
            new DnsResolver("dns.google", "8.8.8.8", "2001:4860:4860::8888", "8.8.8.8", "2001:4860:4860::8888")
        };
        sealed class DnsBatch : IDisposable
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false), Stop = new ManualResetEvent(false);
            public List<IPAddress> Result = new List<IPAddress>();
            public int Workers;
            public bool Released;
            public void Dispose() { Done.Close(); Stop.Close(); }
        }
        static List<IPAddress> RaceDns(WaitHandle stop, DateTime deadline, int budget,
            Func<DnsResolver, WaitHandle, DateTime, List<IPAddress>> query)
        {
            var batch = new DnsBatch { Workers = DnsResolvers.Length };
            DateTime bounded = DateTime.UtcNow.AddMilliseconds(Math.Min(budget, Remaining(deadline)));
            if (stop.WaitOne(0) || Remaining(bounded) == 0) { batch.Dispose(); return batch.Result; }
            foreach (var provider in DnsResolvers) {
                var resolver = provider;
                new Thread(() => {
                    try {
                        var addresses = query(resolver, batch.Stop, bounded);
                        addresses.RemoveAll(Fake);
                        lock (batch) if (!batch.Released && batch.Result.Count == 0 && addresses.Count > 0) {
                            batch.Result = addresses; batch.Done.Set();
                        }
                    } catch { }
                    finally { lock (batch) { if (--batch.Workers == 0) { if (batch.Released) batch.Dispose(); else batch.Done.Set(); } } }
                }) { IsBackground = true, Name = "Fumo DNS " + resolver.Host }.Start();
            }
            try {
                WaitHandle.WaitAny(new WaitHandle[] { batch.Done, stop }, Remaining(bounded));
                lock (batch) return stop.WaitOne(0) ? new List<IPAddress>() : batch.Result;
            }
            finally {
                // Losing queries own their sockets until cancellation completes.
                lock (batch) { batch.Released = true; batch.Stop.Set(); if (batch.Workers == 0) batch.Dispose(); }
            }
        }
        static List<IPAddress> DirectDns(string host, Route route, WaitHandle stop, DateTime deadline)
        { return RaceDns(stop, deadline, 900, (provider, cancel, bounded) => DirectDnsProvider(host, route, provider, cancel, bounded)); }
        static List<IPAddress> DirectDnsProvider(string host, Route route, DnsResolver provider, WaitHandle stop, DateTime deadline)
        {
            var results = new List<IPAddress>();
            string resolver = route.Source.AddressFamily == AddressFamily.InterNetwork ? provider.Ipv4 : provider.Ipv6;
            foreach (int type in route.Source.AddressFamily == AddressFamily.InterNetwork ? new[] { 1, 28 } : new[] { 28, 1 }) {
                if (stop.WaitOne(0) || Remaining(deadline) == 0) break;
                ushort id = (ushort)new Random(Guid.NewGuid().GetHashCode()).Next(1, 65535); byte[] query = Query(host, type, id);
                using (var socket = new Socket(route.Source.AddressFamily, SocketType.Dgram, ProtocolType.Udp)) {
                    try {
                        Bind(socket, route); socket.Connect(IPAddress.Parse(resolver), 53);
                        socket.Send(query); var bytes = new byte[4096]; var ar = socket.BeginReceive(bytes, 0, bytes.Length, SocketFlags.None, null, null);
                        {
                            if (WaitAsync(ar, stop, Math.Min(450, Remaining(deadline))) != 0) continue;
                            int count = socket.EndReceive(ar); Array.Resize(ref bytes, count); results.AddRange(Answers(bytes, id, host, type));
                            results.RemoveAll(Fake); if (results.Count > 0) return results;
                        }
                    } catch { }
                }
            }
            return results;
        }
        static List<IPAddress> Doh(string host, Route route, WaitHandle stop, DateTime deadline)
        { return RaceDns(stop, deadline, 2200, (provider, cancel, bounded) => DohProvider(host, route, provider, cancel, bounded)); }
        static List<IPAddress> DohProvider(string host, Route route, DnsResolver provider, WaitHandle stop, DateTime deadline)
        {
            bool v4 = route.Source.AddressFamily == AddressFamily.InterNetwork;
            var endpoints = new List<IPAddress>();
            if (provider.Host == "doh.pub") {
                // DNSPod rotates its encrypted endpoints independently of its UDP service.
                foreach (var address in DirectDns(provider.Host, route, stop, deadline))
                    if (address.AddressFamily == route.Source.AddressFamily && !Local(address) && !Fake(address)) endpoints.Add(address);
            }
            string bootstrap = v4 ? provider.HttpsIpv4 : provider.HttpsIpv6;
            if (bootstrap != null && !endpoints.Contains(IPAddress.Parse(bootstrap))) endpoints.Add(IPAddress.Parse(bootstrap));
            ushort id = (ushort)new Random(Guid.NewGuid().GetHashCode()).Next(1, 65535);
            var results = new List<IPAddress>();
            foreach (var resolver in endpoints)
            foreach (int type in route.Source.AddressFamily == AddressFamily.InterNetwork ? new[] { 1, 28 } : new[] { 28, 1 }) {
                if (stop.WaitOne(0) || Remaining(deadline) < 100) break;
                TcpClient client = null; RegisteredWaitHandle cancel = null;
                try {
                    client = Dial(resolver, 443, route, stop, deadline);
                    var owned = client; cancel = ThreadPool.RegisterWaitForSingleObject(stop, (state, timedOut) => owned.Close(), null, Timeout.Infinite, true);
                    client.ReceiveTimeout = Math.Min(1000, Math.Max(1, Remaining(deadline))); client.SendTimeout = client.ReceiveTimeout;
                    using (var tls = new SslStream(client.GetStream(), false)) {
                        var auth = tls.BeginAuthenticateAsClient(provider.Host, null, SslProtocols.Tls12, true, null, null);
                        if (WaitAsync(auth, stop, Math.Min(1500, Remaining(deadline))) != 0) throw new IOException("DNS TLS timeout");
                        tls.EndAuthenticateAsClient(auth);
                        string query = Convert.ToBase64String(Query(host, type, id)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                        byte[] request = Encoding.ASCII.GetBytes("GET /dns-query?dns=" + query + " HTTP/1.1\r\nHost: " + provider.Host + "\r\nAccept: application/dns-message\r\nConnection: close\r\n\r\n");
                        tls.Write(request, 0, request.Length);
                        string headers = WebSocketStream.ReadHeaders(tls);
                        if (!headers.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal)) throw new IOException("DNS HTTPS rejected");
                        var response = new MemoryStream(); var buffer = new byte[4096];
                        while (Remaining(deadline) > 0 && !stop.WaitOne(0)) { int n = tls.Read(buffer, 0, buffer.Length); if (n == 0) break; if (response.Length + n > 8192) throw new IOException("DNS HTTPS limit"); response.Write(buffer, 0, n); }
                        if (Remaining(deadline) == 0 || stop.WaitOne(0)) throw new IOException("DNS HTTPS cancelled or timed out");
                        byte[] body = response.ToArray();
                        if (headers.IndexOf("transfer-encoding: chunked", StringComparison.OrdinalIgnoreCase) >= 0) body = Unchunk(body);
                        results.AddRange(Answers(body, id, host, type));
                        results.RemoveAll(Fake); if (results.Count > 0) return results;
                    }
                } catch { }
                finally { if (cancel != null) cancel.Unregister(null); if (client != null) client.Close(); }
            }
            return results;
        }
        static byte[] Unchunk(byte[] bytes)
        {
            var result = new MemoryStream(); int i = 0;
            while (i < bytes.Length) {
                int start = i; while (i + 1 < bytes.Length && !(bytes[i] == 13 && bytes[i + 1] == 10)) i++;
                if (i + 1 >= bytes.Length) throw new IOException("Invalid HTTPS chunk");
                string line = Encoding.ASCII.GetString(bytes, start, i - start).Split(';')[0]; int count;
                if (!int.TryParse(line, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out count) || count < 0 || count > 4096) throw new IOException("HTTPS chunk limit");
                i += 2; if (count == 0) return result.ToArray();
                if (i + count + 2 > bytes.Length || bytes[i + count] != 13 || bytes[i + count + 1] != 10) throw new IOException("Truncated HTTPS chunk");
                result.Write(bytes, i, count); i += count + 2;
            }
            throw new IOException("Truncated HTTPS body");
        }
        static IPAddress[] Resolve(string host, List<Route> routes, WaitHandle stop, DateTime deadline)
        {
            IPAddress literal; if (IPAddress.TryParse(host, out literal)) return new[] { literal };
            var results = new List<IPAddress>(); var ar = Dns.BeginGetHostAddresses(host, null, null);
            {
                if (WaitAsync(ar, stop, Math.Min(1000, Remaining(deadline))) == 0) {
                    try { foreach (var ip in Dns.EndGetHostAddresses(ar)) results.Add(ip); } catch { }
                }
            }
            if (stop.WaitOne(0)) throw new IOException("Connection cancelled");
            bool local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || (results.Count > 0 && results.TrueForAll(ip => Local(ip) && !Fake(ip)));
            if (!local) {
                int tried = 0;
                foreach (var route in routes) {
                    if (route.Source.IsIPv6LinkLocal) continue;
                    var direct = DirectDns(host, route, stop, deadline);
                    if (direct.Count == 0) direct = Doh(host, route, stop, deadline);
                    foreach (var ip in direct) if (!Fake(ip) && !results.Contains(ip)) results.Insert(0, ip);
                    if (++tried >= 2 || results.Exists(ip => !Fake(ip) && !Local(ip)) || stop.WaitOne(0)) break;
                }
            }
            if (results.Count == 0) throw new IOException("DNS resolution failed");
            return results.ToArray();
        }
        public static Connection Connect(string host, int port, WaitHandle cancellation, Uri secureFallback = null, bool preferSecure = false, bool verifyProtocol = false)
        {
            var boundedStop = new ManualResetEvent(cancellation.WaitOne(0));
            var registration = ThreadPool.RegisterWaitForSingleObject(cancellation, (state, timedOut) => boundedStop.Set(), null, Timeout.Infinite, true);
            using (var timer = new Timer(state => boundedStop.Set(), null, 12000, Timeout.Infinite)) {
                try { return ConnectCore(host, port, boundedStop, secureFallback, preferSecure, verifyProtocol); }
                finally { registration.Unregister(null); }
            }
        }
        static Connection ConnectCore(string host, int port, WaitHandle cancellation, Uri secureFallback, bool preferSecure, bool verifyProtocol)
        {
            var deadline = DateTime.UtcNow.AddSeconds(12); List<Route> routes;
            try { routes = Routes(); } catch { routes = new List<Route>(); }
            try { if (preferSecure && secureFallback != null) throw new IOException("Prefer secure route"); return ConnectTcp(host, port, routes, cancellation, secureFallback == null ? deadline : DateTime.UtcNow.AddSeconds(5), verifyProtocol || secureFallback != null); }
            catch {
                if (cancellation.WaitOne(0) || secureFallback == null) throw;
                if (secureFallback.Scheme != "wss" || !string.IsNullOrEmpty(secureFallback.UserInfo)) throw new IOException("Invalid secure room endpoint");
                var connection = ConnectTcp(secureFallback.DnsSafeHost, secureFallback.Port, routes, cancellation, deadline);
                RegisteredWaitHandle cancel = ThreadPool.RegisterWaitForSingleObject(cancellation, (state, timedOut) => connection.Client.Close(), null, Timeout.Infinite, true);
                try {
                    connection.Client.ReceiveTimeout = Math.Min(3000, Math.Max(1, Remaining(deadline))); connection.Client.SendTimeout = 3000;
                    var tls = new SslStream(connection.Stream, false);
                    var auth = tls.BeginAuthenticateAsClient(secureFallback.DnsSafeHost, null, SslProtocols.Tls12, true, null, null);
                    {
                        if (WaitAsync(auth, cancellation, Math.Min(3000, Remaining(deadline))) != 0) throw new IOException("TLS cancelled or timed out");
                        tls.EndAuthenticateAsClient(auth);
                    }
                    if (cancellation.WaitOne(0)) throw new IOException("Connection cancelled");
                    connection.Client.ReceiveTimeout = Math.Min(3000, Math.Max(1, Remaining(deadline)));
                    connection.Stream = new WebSocketStream(tls, secureFallback);
                    connection.RouteDiagnostic += "; WSS TLS" + secureFallback.Port;
                    return Verify(connection, cancellation, deadline);
                } catch { connection.Dispose(); throw; }
                finally { cancel.Unregister(null); }
            }
        }
        static Connection ConnectTcp(string host, int port, List<Route> routes, WaitHandle stop, DateTime deadline, bool preflight = false)
        {
            return ConnectResolved(Resolve(host, routes, stop, deadline), port, routes, stop, deadline, preflight);
        }
        static Connection ConnectResolved(IPAddress[] addresses, int port, List<Route> routes, WaitHandle stop, DateTime deadline, bool preflight)
        {
            Exception failure = null;
            // VPN-only private networks and unscoped link-local addresses retain system routing.
            var systemTried = new HashSet<IPAddress>();
            foreach (var ip in addresses) if (Local(ip) && !Fake(ip) && PhysicalRoutes(ip, routes).Count == 0) {
                systemTried.Add(ip);
                try { var client = Dial(ip, port, null, stop, deadline); return Promote(client, "system local " + ip.AddressFamily, stop, deadline, preflight); } catch (Exception e) { failure = e; }
            }
            int attempts = 0;
            var physicalDeadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, Remaining(deadline) - 1800));
            foreach (var ip in addresses) {
                foreach (var route in PhysicalRoutes(ip, routes)) {
                    try { var client = Dial(ip, port, route, stop, physicalDeadline); return Promote(client, "physical " + route.Name + "#" + route.Index + " " + ip.AddressFamily, stop, physicalDeadline, preflight); } catch (Exception e) { failure = e; }
                    if (++attempts >= 6 || Remaining(physicalDeadline) == 0 || stop.WaitOne(0)) break;
                }
                if (attempts >= 6 || Remaining(physicalDeadline) == 0 || stop.WaitOne(0)) break;
            }
            var systemAddresses = new List<IPAddress>(addresses);
            systemAddresses.Sort((a, b) => Fake(a) == Fake(b) ? 0 : (Fake(a) ? -1 : 1));
            foreach (var ip in systemAddresses) {
                if (systemTried.Contains(ip)) continue;
                if (Remaining(deadline) == 0 || stop.WaitOne(0)) break;
                try { var client = Dial(ip, port, null, stop, deadline); return Promote(client, "system " + ip.AddressFamily, stop, deadline, preflight); } catch (Exception e) { failure = e; }
            }
            throw new IOException(stop.WaitOne(0) ? "Connection cancelled" : "Connection failed: " + (failure == null ? "no route" : failure.Message));
        }
        static Connection Promote(TcpClient client, string diagnostic, WaitHandle stop, DateTime deadline, bool preflight)
        {
            var connection = new Connection { Client = client, Stream = client.GetStream(), RouteDiagnostic = diagnostic };
            return preflight ? Verify(connection, stop, deadline) : connection;
        }
        static Connection Verify(Connection connection, WaitHandle stop, DateTime deadline)
        {
            var client = connection.Client;
            var cancel = ThreadPool.RegisterWaitForSingleObject(stop, (state, timedOut) => client.Close(), null, Timeout.Infinite, true);
            try {
                client.ReceiveTimeout = Math.Min(750, Math.Max(1, Remaining(deadline))); client.SendTimeout = client.ReceiveTimeout;
                byte[] request = Encoding.UTF8.GetBytes("{\"kind\":\"list\",\"version\":" + NativeNetProtocol.Version + ",\"content\":\"" + NativeNetProtocol.Content + "\"}\n");
                connection.Stream.Write(request, 0, request.Length);
                var until = DateTime.UtcNow.AddMilliseconds(Math.Min(1200, Remaining(deadline)));
                for (int frames = 0; frames < 4; frames++) {
                    var bytes = new MemoryStream();
                    while (Remaining(until) > 0 && !stop.WaitOne(0)) {
                        int b = connection.Stream.ReadByte(); if (b < 0) throw new IOException("Preflight closed");
                        if (b == 10) break; if (bytes.Length >= NativeNetProtocol.MaxFrameBytes) throw new IOException("Preflight frame limit"); bytes.WriteByte((byte)b);
                    }
                    if (Remaining(until) == 0 || stop.WaitOne(0)) throw new IOException("Preflight timed out");
                    var fields = HeaderFields(new UTF8Encoding(false, true).GetString(bytes.ToArray())); string kind, version, content;
                    if (!fields.TryGetValue("kind", out kind)) throw new IOException("Invalid preflight response");
                    if (kind == "pong") continue;
                    if (kind == "ping") { byte[] pong = Encoding.ASCII.GetBytes("{\"kind\":\"pong\"}\n"); connection.Stream.Write(pong, 0, pong.Length); continue; }
                    if (!fields.TryGetValue("version", out version) || version != NativeNetProtocol.Version.ToString() || !fields.TryGetValue("content", out content) || content != NativeNetProtocol.Content || (kind != "rooms" && kind != "error")) throw new IOException("Room protocol mismatch");
                    connection.RouteDiagnostic += "; protocol verified"; return connection;
                }
                throw new IOException("Preflight response limit");
            } catch (Exception e) {
                connection.Dispose();
                if (stop.WaitOne(0)) throw new IOException("Connection cancelled", e);
                for (Exception cause = e; cause != null; cause = cause.InnerException) {
                    var socket = cause as SocketException;
                    if (socket != null && (socket.SocketErrorCode == SocketError.TimedOut || socket.SocketErrorCode == SocketError.WouldBlock || socket.SocketErrorCode == SocketError.IOPending)) throw new IOException("Preflight timed out", e);
                }
                throw;
            }
            finally { cancel.Unregister(null); }
        }
        static string JsonString(string json, ref int i)
        {
            if (i >= json.Length || json[i++] != '"') throw new IOException("Invalid JSON string"); var text = new StringBuilder();
            while (i < json.Length) {
                char c = json[i++]; if (c == '"') return text.ToString();
                if (c == '\\') { if (i >= json.Length) break; c = json[i++]; if (c == 'u') { if (i + 4 > json.Length) break; c = (char)Convert.ToInt32(json.Substring(i, 4), 16); i += 4; } else if (c == 'n') c = '\n'; else if (c == 'r') c = '\r'; else if (c == 't') c = '\t'; }
                text.Append(c);
            }
            throw new IOException("Truncated JSON string");
        }
        static Dictionary<string, string> HeaderFields(string json)
        {
            var fields = new Dictionary<string, string>(); int i = 0;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i++] != '{') throw new IOException("Invalid JSON object");
            while (i < json.Length) {
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
                if (i < json.Length && json[i] == '}') return fields;
                string key = JsonString(json, ref i); while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length || json[i++] != ':') throw new IOException("Invalid JSON field");
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                string value;
                if (i < json.Length && json[i] == '"') value = JsonString(json, ref i);
                else { int start = i, depth = 0; while (i < json.Length) { char c = json[i]; if (c == '"') { JsonString(json, ref i); continue; } if (depth == 0 && (c == ',' || c == '}')) break; if (c == '{' || c == '[') depth++; if (c == '}' || c == ']') depth--; i++; } value = json.Substring(start, i - start).Trim(); }
                if (fields.ContainsKey(key)) throw new IOException("Duplicate JSON field"); fields.Add(key, value);
            }
            throw new IOException("Truncated JSON object");
        }
        sealed class WebSocketStream : Stream
        {
            readonly Stream inner;
            readonly object writeGate = new object();
            byte[] pending = new byte[0]; int offset; bool fragmented;
            readonly MemoryStream message = new MemoryStream();
            readonly UTF8Encoding utf8 = new UTF8Encoding(false, true);
            public WebSocketStream(Stream stream, Uri endpoint)
            {
                inner = stream; byte[] nonce = new byte[16]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
                string key = Convert.ToBase64String(nonce);
                string request = "GET " + endpoint.PathAndQuery + " HTTP/1.1\r\nHost: " + endpoint.Authority + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Protocol: fumo-native-v1\r\n\r\n";
                byte[] bytes = Encoding.ASCII.GetBytes(request); inner.Write(bytes, 0, bytes.Length);
                string response = ReadHeaders(inner); string[] lines = response.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (!lines[0].StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal)) throw new IOException("WebSocket upgrade rejected");
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++) { int colon = lines[i].IndexOf(':'); if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim(); }
                string expected; using (var sha = SHA1.Create()) expected = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                string accept, upgrade, connection, protocol;
                if (!headers.TryGetValue("Sec-WebSocket-Accept", out accept) || accept != expected || !headers.TryGetValue("Upgrade", out upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase) || !headers.TryGetValue("Connection", out connection) || connection.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) < 0 || !headers.TryGetValue("Sec-WebSocket-Protocol", out protocol) || protocol != "fumo-native-v1") throw new IOException("Invalid WebSocket upgrade");
            }
            internal static string ReadHeaders(Stream stream)
            {
                var bytes = new List<byte>();
                while (bytes.Count < 16384) { int b = stream.ReadByte(); if (b < 0) throw new IOException("Upgrade connection closed"); bytes.Add((byte)b); int n = bytes.Count; if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) return Encoding.ASCII.GetString(bytes.ToArray()); }
                throw new IOException("Upgrade header limit");
            }
            int Byte() { int b = inner.ReadByte(); if (b < 0) throw new IOException("WebSocket closed"); return b; }
            public override int Read(byte[] buffer, int index, int count)
            {
                if (count == 0) return 0;
                while (offset == pending.Length) {
                    int first = Byte(), second = Byte(), op = first & 15; bool final = (first & 128) != 0;
                    if ((first & 112) != 0 || (second & 128) != 0) throw new IOException("Invalid WebSocket frame");
                    long length = second & 127;
                    if (length == 126) { length = (Byte() << 8) | Byte(); if (length < 126) throw new IOException("Noncanonical WebSocket length"); }
                    else if (length == 127) { length = 0; for (int i = 0; i < 8; i++) { if (length > NativeNetProtocol.MaxFrameBytes) throw new IOException("WebSocket frame limit"); length = (length << 8) | (uint)Byte(); } if (length <= 65535) throw new IOException("Noncanonical WebSocket length"); }
                    if (length > NativeNetProtocol.MaxFrameBytes + 1 || (op >= 8 && (!final || length > 125))) throw new IOException("WebSocket frame limit");
                    var payload = new byte[(int)length]; int read = 0;
                    while (read < payload.Length) { int n = inner.Read(payload, read, payload.Length - read); if (n == 0) throw new IOException("WebSocket closed"); read += n; }
                    if (op == 8) {
                        if (payload.Length == 1) throw new IOException("Invalid WebSocket close payload");
                        if (payload.Length >= 2) { int code = (payload[0] << 8) | payload[1]; if (!((code >= 1000 && code <= 1014 && code != 1004 && code != 1005 && code != 1006) || (code >= 3000 && code <= 4999))) throw new IOException("Invalid WebSocket close code"); utf8.GetCharCount(payload, 2, payload.Length - 2); }
                        return 0;
                    }
                    if (op == 9) { WriteFrame(10, payload, 0, payload.Length); continue; }
                    if (op == 10) continue;
                    if ((op == 0 && !fragmented) || (op == 1 && fragmented) || (op != 0 && op != 1)) throw new IOException("Invalid WebSocket continuation");
                    if (op == 1) message.SetLength(0);
                    if (message.Length + payload.Length > NativeNetProtocol.MaxFrameBytes + 1) throw new IOException("WebSocket message limit");
                    message.Write(payload, 0, payload.Length); fragmented = !final;
                    if (!final) continue;
                    pending = message.ToArray(); utf8.GetCharCount(pending); message.SetLength(0); offset = 0;
                }
                int copied = Math.Min(count, pending.Length - offset); Buffer.BlockCopy(pending, offset, buffer, index, copied); offset += copied; return copied;
            }
            void WriteFrame(int opcode, byte[] buffer, int index, int count)
            {
                if (count > NativeNetProtocol.MaxFrameBytes + 1) throw new IOException("WebSocket frame limit");
                lock (writeGate) {
                    byte[] mask = new byte[4]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
                    var frame = new List<byte> { (byte)(128 | opcode) };
                    if (count < 126) frame.Add((byte)(128 | count));
                    else if (count <= 65535) { frame.Add(254); frame.Add((byte)(count >> 8)); frame.Add((byte)count); }
                    else { frame.Add(255); for (int shift = 56; shift >= 0; shift -= 8) frame.Add((byte)((long)count >> shift)); }
                    frame.AddRange(mask); for (int i = 0; i < count; i++) frame.Add((byte)(buffer[index + i] ^ mask[i % 4]));
                    byte[] bytes = frame.ToArray(); inner.Write(bytes, 0, bytes.Length);
                }
            }
            public override void Write(byte[] buffer, int index, int count) { WriteFrame(1, buffer, index, count); }
            public override bool CanRead { get { return true; } } public override bool CanWrite { get { return true; } } public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { inner.Flush(); } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); } public override void SetLength(long length) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
