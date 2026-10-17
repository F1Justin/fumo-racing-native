using System;
using System.Linq;

namespace Fumo.NativePlayable
{
    public enum NativeBroadcastShotKind { Follow, Pair, Trackside, FixedWide, AerialTele, AerialWide, Fpv, Mounted }

    [Serializable]
    public sealed class NativeBroadcastShotCandidate
    {
        public string key;
        public NativeBroadcastShotKind kind;
        public int focus, other = -1;
        public float score, coverage, readableSize, usableSeconds;
        public string rejection;
        public bool visible;
    }

    // The scene adapter supplies evaluated poses. This module selects a shot;
    // it never chooses a story, writes camera transforms, or changes race facts.
    public sealed class NativeBroadcastShotPlanner
    {
        public const float MinimumSeconds = 4.5f, ChallengerSeconds = .6f, Advantage = 10f;
        float selectedAt, challengerAt, lastTime;
        string challenger;
        public NativeBroadcastShotCandidate Current { get; private set; }
        public int CutCount { get; private set; }

        public void Reset()
        {
            Current = null; challenger = null;
            selectedAt = challengerAt = lastTime = 0; CutCount = 0;
        }

        public NativeBroadcastShotCandidate Update(NativeBroadcastShotCandidate[] candidates, int focus, float now)
        {
            if (now < lastTime) Reset();
            lastTime = now;
            var valid = (candidates ?? Array.Empty<NativeBroadcastShotCandidate>())
                .Where(c => c != null && !string.IsNullOrEmpty(c.key) && c.focus == focus && c.visible
                    && !float.IsNaN(c.score) && !float.IsInfinity(c.score) && c.coverage >= 0 && c.coverage <= 1)
                .OrderByDescending(c => c.score).ThenBy(c => c.key, StringComparer.Ordinal).ToArray();
            var best = valid.FirstOrDefault();
            var current = Current == null ? null : valid.FirstOrDefault(c => c.key == Current.key && c.focus == Current.focus && c.other == Current.other);
            if (best == null) { Current = null; challenger = null; return null; }
            // Loss of coverage/visibility or a changed subject bypasses shot dwell.
            if (current == null) return Select(best, now);
            Current = current;
            if (best.key == current.key) { challenger = null; return current; }
            if (now - selectedAt < MinimumSeconds || best.score < current.score + Advantage)
            { challenger = null; return current; }
            if (challenger != best.key) { challenger = best.key; challengerAt = now; return current; }
            return now - challengerAt >= ChallengerSeconds ? Select(best, now) : current;
        }

        NativeBroadcastShotCandidate Select(NativeBroadcastShotCandidate shot, float now)
        {
            if (Current != null && (Current.key != shot.key || Current.focus != shot.focus || Current.other != shot.other)) CutCount++;
            Current = shot; selectedAt = now; challenger = null;
            return shot;
        }
    }
}
