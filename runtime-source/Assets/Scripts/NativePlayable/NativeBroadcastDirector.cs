using System;
using System.Collections.Generic;
using System.Linq;

namespace Fumo.NativePlayable
{
    public enum NativeBroadcastStoryKind { Leader, Battle, Overtake, ItemUse, Hit, Finish }

    public sealed class NativeBroadcastRacer
    {
        public int id, place, lap, checkpoint, damage, itemUses;
        public bool finished, retired;
        public float x, y, z, vx, vy, vz;
    }

    [Serializable]
    public sealed class NativeBroadcastStory
    {
        public string key, phase;
        public NativeBroadcastStoryKind kind;
        public int focus, other = -1;
        public float score;
    }

    // Decisions consume race facts and a presentation clock; rendering remains owned by the spectator adapter.
    public sealed class NativeBroadcastDirector
    {
        sealed class Moment { public NativeBroadcastStory story; public float expires; }
        readonly Dictionary<int, NativeBroadcastRacer> previous = new Dictionary<int, NativeBroadcastRacer>();
        readonly Dictionary<string, Moment> moments = new Dictionary<string, Moment>();
        int round = -1;
        float lastRaceTime = -1, selectedAt, challengerAt;
        string challenger;
        bool rejoin;
        public const float MinimumShotSeconds = 4.5f, ChallengerSeconds = .6f, CutAdvantage = 15f;
        public NativeBroadcastStory Current { get; private set; }
        public int CutCount { get; private set; }

        public void Reset()
        {
            previous.Clear(); moments.Clear(); Current = null; challenger = null;
            round = -1; lastRaceTime = -1; selectedAt = challengerAt = 0; CutCount = 0; rejoin = false;
        }

        public NativeBroadcastStory Update(NativeBroadcastRacer[] frame, int raceRound, float raceTime, float now)
        {
            if (frame == null || frame.Length == 0) return Current;
            if (round != raceRound || raceTime < lastRaceTime - .01f) Reset();
            round = raceRound;
            var racers = frame.Where(r => r != null && r.id >= 0).GroupBy(r => r.id).Select(g => g.First()).ToArray();
            if (racers.Length == 0) return Current;
            var active = racers.Where(r => !r.finished && !r.retired).OrderBy(r => r.place).ThenBy(r => r.id).ToArray();
            bool fresh = raceTime > lastRaceTime;
            foreach (var racer in racers)
            {
                NativeBroadcastRacer old;
                if (fresh && previous.TryGetValue(racer.id, out old) && !racer.retired)
                {
                    if (racer.finished && !old.finished)
                        Remember(NativeBroadcastStoryKind.Finish, racer, -1, 175f + PositionValue(racer), now);
                    else if (!racer.finished)
                    {
                        if (racer.damage > old.damage) Remember(NativeBroadcastStoryKind.Hit, racer, Related(racer.id, racers), 115f + PositionValue(racer), now);
                        if (racer.itemUses > old.itemUses) Remember(NativeBroadcastStoryKind.ItemUse, racer, Related(racer.id, racers), 95f + PositionValue(racer), now);
                        if (racer.place > 0 && old.place > racer.place)
                            Remember(NativeBroadcastStoryKind.Overtake, racer, Overtaken(racer, old, racers), 120f + PositionValue(racer), now);
                    }
                }

            }
            if (fresh) { foreach (var racer in racers) previous[racer.id] = Copy(racer); lastRaceTime = raceTime; }
            foreach (var key in previous.Keys.Where(id => racers.All(r => r.id != id)).ToArray()) previous.Remove(key);
            foreach (var key in moments.Keys.Where(key => moments[key].expires <= now || racers.All(r => r.id != moments[key].story.focus || r.retired)).ToArray()) moments.Remove(key);
            var candidates = moments.Values.Select(m => m.story).ToList();
            if (active.Length > 0)
            {
                var leader = active[0];
                candidates.Add(Story(NativeBroadcastStoryKind.Leader, leader.id, -1, 35f + PositionValue(leader)));
                for (int i = 1; i < active.Length; i++)
                {
                    var front = active[i - 1]; var behind = active[i];
                    if (front.lap != behind.lap || Math.Abs(front.checkpoint - behind.checkpoint) > 1) continue;
                    float dx = front.x - behind.x, dy = front.y - behind.y, dz = front.z - behind.z;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (distance > 24f || Math.Abs(dy) > 8f) continue;
                    float closing = distance > .01f ? ((behind.vx - front.vx) * dx + (behind.vy - front.vy) * dy + (behind.vz - front.vz) * dz) / distance : 0;
                    float score = 60f + PositionValue(front) + (24f - distance) * 1.4f + Math.Max(0, Math.Min(closing, 12f)) * 2f;
                    candidates.Add(Story(NativeBroadcastStoryKind.Battle, behind.id, front.id, score));
                }
            }
            else
            {
                var winner = racers.OrderBy(r => r.retired).ThenBy(r => r.place).ThenBy(r => r.id).First();
                candidates.Add(Story(NativeBroadcastStoryKind.Leader, winner.id, -1, 35f));
            }
            var best = candidates.OrderByDescending(s => s.score).ThenBy(s => s.focus).ThenBy(s => s.key, StringComparer.Ordinal).First();
            var current = Current == null ? null : candidates.FirstOrDefault(s => s.key == Current.key);
            if (rejoin || Current == null || racers.All(r => r.id != Current.focus || r.retired)) { rejoin = false; return Select(best, now); }
            if (best.key == Current.key) { Current = best; challenger = null; return Current; }
            if (current != null) Current = current;
            float scoreToBeat = current == null ? 0f : current.score;
            bool urgentFinish = best.kind == NativeBroadcastStoryKind.Finish && best.score > Current.score;
            if (!urgentFinish && now - selectedAt < MinimumShotSeconds) { challenger = null; return Current; }
            if (best.score < scoreToBeat + CutAdvantage) { challenger = null; return Current; }
            if (urgentFinish) return Select(best, now);
            if (challenger != best.key) { challenger = best.key; challengerAt = now; return Current; }
            return now - challengerAt >= ChallengerSeconds ? Select(best, now) : Current;
        }

        public void Rejoin(float now)
        {
            rejoin = true;
        }

        NativeBroadcastStory Select(NativeBroadcastStory story, float now)
        {
            if (Current != null && Current.key != story.key) CutCount++;
            Current = story; selectedAt = now; challenger = null;
            return Current;
        }
        void Remember(NativeBroadcastStoryKind kind, NativeBroadcastRacer racer, int other, float score, float now)
        {
            var story = Story(kind, racer.id, other, score);
            moments[story.key] = new Moment { story = story, expires = now + 6f };
        }
        int Related(int racer, NativeBroadcastRacer[] racers)
        {
            // A competitive partner is context, never an inferred attacker or item target.
            int other = Current == null ? -1 : Current.focus == racer ? Current.other : Current.other == racer ? Current.focus : -1;
            return racers.Any(r => r.id == other && !r.retired && !r.finished) ? other : -1;
        }
        int Overtaken(NativeBroadcastRacer racer, NativeBroadcastRacer old, NativeBroadcastRacer[] racers)
        {
            // Compare the whole previous snapshot before replacing any racer; iteration order must not affect identity.
            var crossed = racers.Where(r => r.id != racer.id && !r.retired && !r.finished && previous.ContainsKey(r.id)
                && previous[r.id].place < old.place && previous[r.id].place >= racer.place && r.place > racer.place)
                .OrderBy(r => Math.Abs(previous[r.id].place - old.place)).ThenBy(r => r.id).FirstOrDefault();
            return crossed != null ? crossed.id : Related(racer.id, racers);
        }
        static float PositionValue(NativeBroadcastRacer racer) => Math.Max(0, 20f - Math.Max(0, racer.place - 1) * 2f);
        static NativeBroadcastStory Story(NativeBroadcastStoryKind kind, int focus, int other, float score) => new NativeBroadcastStory
        { key = other >= 0 ? "duel:" + Math.Min(focus, other) + ":" + Math.Max(focus, other) : kind + ":" + focus,
            phase = kind == NativeBroadcastStoryKind.Overtake ? "result" : kind == NativeBroadcastStoryKind.Battle ? "contest"
                : kind == NativeBroadcastStoryKind.Hit ? "impact" : kind == NativeBroadcastStoryKind.ItemUse ? "action" : kind.ToString(),
            kind = kind, focus = focus, other = other, score = score };
        static NativeBroadcastRacer Copy(NativeBroadcastRacer r) => new NativeBroadcastRacer
        { id = r.id, place = r.place, lap = r.lap, checkpoint = r.checkpoint, damage = r.damage, itemUses = r.itemUses,
            finished = r.finished, retired = r.retired, x = r.x, y = r.y, z = r.z, vx = r.vx, vy = r.vy, vz = r.vz };
    }
}
