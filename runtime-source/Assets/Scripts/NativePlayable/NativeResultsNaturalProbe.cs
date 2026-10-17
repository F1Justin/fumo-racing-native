using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeResultsNaturalProbe : MonoBehaviour
    {
        NativeResultsFlow flow;
        NativeRaceStateFlow state;
        string path;
        public Func<float[]> ReadReadyAlpha;
        [Serializable] sealed class Receipt
        {
            public string kind, screen;
            public int frame, raceId, currentRaceId;
            public float scaledTime, unscaledTime, timeScale;
            public bool inRace, accepted = false, injectedState = false;
            public float[] readyAlpha;
            public int[] racerOrder, score, remaining, delta, ready;
        }
        public void Bind(NativeResultsFlow next, NativeRaceStateFlow producer)
        {
            if (flow == next) return;
            if (flow != null) Unbind();
            flow = next; state = producer;
            path = Path.Combine(Application.persistentDataPath, "results-natural-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".jsonl");
            flow.CardsOpened += Cards; flow.ScoreboardOpened += Scoreboard; flow.RowsChanged += Rows;
            flow.CardsDisposed += DisposeCards; flow.PanelHidden += Hidden;
            Write("begin");
        }
        public void Record(string kind) { if (flow != null) Write(kind); }
        void Cards() => Write("cards-opened");
        void Scoreboard() => Write("scoreboard-opened");
        void Rows() => Write("score-row-update");
        void DisposeCards() => Write("cards-disposed");
        void Hidden() => Write("panel-hidden");
        void LateUpdate() { if (flow != null && flow.State != NativeResultsFlow.Screen.Closed) Write("natural-frame"); }
        void Write(string kind)
        {
            var r = new Receipt { kind = kind, frame = Time.frameCount, scaledTime = Time.time, unscaledTime = Time.unscaledTime,
                timeScale = Time.timeScale, raceId = flow.RaceId, currentRaceId = state.RaceId, inRace = state.InRace,
                screen = flow.State.ToString(), readyAlpha = ReadReadyAlpha?.Invoke(), racerOrder = flow.Rows.Select(x => x.RacerIndex).ToArray(),
                score = flow.Rows.Select(x => x.DisplayedScore).ToArray(), remaining = flow.Rows.Select(x => x.Remaining).ToArray(),
                delta = flow.Rows.Select(x => x.DisplayedDelta).ToArray(), ready = state.Humans.Select(x => x.Ready ? 1 : 0).ToArray() };
            File.AppendAllText(path, JsonUtility.ToJson(r) + "\n");
        }
        void Unbind()
        {
            flow.CardsOpened -= Cards; flow.ScoreboardOpened -= Scoreboard; flow.RowsChanged -= Rows;
            flow.CardsDisposed -= DisposeCards; flow.PanelHidden -= Hidden;
        }
        void OnDestroy() { if (flow != null) Unbind(); }
    }
}
