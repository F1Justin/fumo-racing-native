using UnityEngine;

namespace Fumo.NativePlayable
{
    [RequireComponent(typeof(NativeRace))]
    public sealed class NativeShaderGlobalsManager : MonoBehaviour
    {
        NativeRace race;
        public int ResetCount { get; private set; }
        public string LastResetReason { get; private set; }

        void Awake()
        {
            race = GetComponent<NativeRace>();
            race.OriginalRaceStarted += OnRaceStarted;
            race.SourceRaceFinishObserved += OnRaceFinish;
            ResetGlobal("Awake");
        }

        void OnRaceStarted() => ResetGlobal("RaceStarted");
        void OnRaceFinish()
        {
            ResetGlobal("RaceFinish");
            race.ObserveOriginalRaceFinish();
        }
        void OnApplicationQuit() => ResetGlobal("ApplicationQuit");

        void OnDestroy()
        {
            if (!race) return;
            race.OriginalRaceStarted -= OnRaceStarted;
            race.SourceRaceFinishObserved -= OnRaceFinish;
        }

        void ResetGlobal(string reason)
        {
            // Source race events reset the global and preserve the scene timeline's age and enabled state.
            Shader.SetGlobalFloat("_MeltingFactor", 0f);
            ResetCount++;
            LastResetReason = reason;
        }
    }
}
