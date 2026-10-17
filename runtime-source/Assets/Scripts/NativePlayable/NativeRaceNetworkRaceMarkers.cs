namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        internal string RaceIndicatorPlayerName(int racerIndex)
        {
            // Round identities survive disconnects, retirement and changes to the live room list.
            if (roundNames.TryGetValue(racerIndex, out var name) && !string.IsNullOrWhiteSpace(name)) return name;
            int seat = PeerSeat(racerIndex);
            if (seat >= 0)
                foreach (var peer in Peers)
                    if (peer.seat == seat && !string.IsNullOrWhiteSpace(peer.name)) return peer.name;
            return "Player";
        }
    }
}
