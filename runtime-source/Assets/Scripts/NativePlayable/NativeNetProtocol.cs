using System;
namespace Fumo.NativePlayable
{
    public static class NativeNetProtocol
    {
        public const int Version = 3, DefaultPort = 27777, MaxFrameBytes = 262144, MaxQueue = 256;
        public const string Content = "tfr-native-0.38";
    }
    [Serializable] public sealed class NativeNetPeer { public string id, name; public int seat; public bool ready, spectator, connected = true, returned = true, host; }
    [Serializable] public sealed class NativeNetMessage
    {
        public string kind, content = NativeNetProtocol.Content, room, player, name, payload, error, resumeToken;
        public int version = NativeNetProtocol.Version, seat, round, sequence, capacity;
        public bool ready, racing, completed, returned, spectator, host, setup;
        public string candidateTrack;
        public NativeNetPeer[] peers;
    }
}
