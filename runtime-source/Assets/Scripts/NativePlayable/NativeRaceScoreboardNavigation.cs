using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        public event Action<int, Vector2> SourceScoreboardNavigateObserved;
        internal void SourceScoreboardNavigate(int humanSlot, Vector2 direction)
        {
            if (sourceRaceState.Humans[humanSlot].Selected != NativeRaceStateFlow.Selection.Scoreboard) return;
            SourceScoreboardNavigateObserved?.Invoke(humanSlot, direction);
            GetComponent<NativeResultsNaturalProbe>()?.Record("scoreboard-navigate-return-null");
            // Source ISelectable.Move returns null; automatic scrollbar time remains unchanged.
        }
    }
}
