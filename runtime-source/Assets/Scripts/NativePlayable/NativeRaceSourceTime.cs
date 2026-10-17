using System;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        NativeSourceLapTimer[] sourceLapTimers;
        bool[] sourceHumanDriving;
        float[] sourceHumanVertical;
        double sourceGameTime;
        public double SourceGameTime=>sourceGameTime;
        public event Action<int> SourceRacerEndedObserved;
        public event Action<int> SourceHumanAiTakeoverObserved;
        void StartSourceTimers()
        {
            sourceLapTimers = Enumerable.Range(0, racers.Length).Select(i => new NativeSourceLapTimer(RaceLaps)).ToArray();
            sourceHumanDriving = Enumerable.Range(0, racers.Length).Select(i => raceConfig.roster[i].isHuman).ToArray();
            sourceGameTime = 0;
            sourceHumanVertical = new float[4];
        }
        public event Action SourceStartedBoundaryObserved;
        void SourceTimersStartedRace()
        {
            sourceGameTime = 0;
            foreach (var h in sourceRaceState.Humans.Where(h => h.Joined && h.RacerValid)) sourceLapTimers[h.RacerIndex].StartedRace(sourceGameTime);
            SourceStartedBoundaryObserved?.Invoke();
        }
        void TickSourceTimers()
        {
            if (sourceLapTimers == null || !sourceRaceState.InRace || !raceClock.Running) return;
            sourceGameTime = raceClock.Elapsed;
            foreach (var h in sourceRaceState.Humans.Where(h => h.Joined && h.RacerValid)) sourceLapTimers[h.RacerIndex].UpdateTimer(sourceGameTime);
        }
        void SourceCountLap(int racer, int newLap) => sourceLapTimers[racer].SetCurrentLap(newLap);
        void SourceRefreshLapTime(int racer) => sourceLapTimers[racer].RefreshLapTime(sourceGameTime);
        public double SourceTotalSeconds(int racer) => sourceLapTimers[racer].GetTotalTime().TotalSeconds;
        public bool SourceHumanDrivingEnabled(int racer) => sourceHumanDriving != null && sourceHumanDriving[racer];
        bool SourceAiShouldDrive(int racer) => sourceRaceState.InRace && (!raceConfig.roster[racer].isHuman || !SourceHumanDrivingEnabled(racer) || autopilot);
        void SourceEndRacer(int racer)
        {
            if (!sourceLapTimers[racer].EndRace()) return;
            sourceRanking?.RecordFinish(racer);
            SourceRacerEndedObserved?.Invoke(racer);
            sourceRaceState.SetRacerFinished(racer);
            // HumanGamePlayer returns from the base all-human callback before disabling human drive and enabling AI.
            if (raceConfig.roster[racer].isHuman)
            {
                sourceHumanDriving[racer] = false;
                var ai = racers[racer].GetComponent<NativeKartOriginalAI>();
                ai.enabled = true; ai.active = true; racers[racer].driving = true;
                SourceHumanAiTakeoverObserved?.Invoke(racer);
            }
        }
        void ApplySourceAdditionalHumanInput()
        {
            if (phase != Phase.Racing || autopilot || sourceRaceState == null) return;
            for (int slot = 0; slot < sourceRaceState.Humans.Length; slot++)
            {
                var h = sourceRaceState.Humans[slot];
                if (!h.Joined || !h.RacerValid || h.RacerIndex == 0 || !SourceHumanDrivingEnabled(h.RacerIndex)) continue;
                var motor = racers[h.RacerIndex];
                motor.throttle = NativeInputBindings.GetAxis(NativeInputBindings.Action.Accelerate, slot) * .5f;
                motor.steering = NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal, slot);
                motor.braking = NativeInputBindings.GetAxis(NativeInputBindings.Action.Brake, slot) > .5f;
                motor.drifting = motor.throttle >= .4f && motor.braking;
                motor.jump |= NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Jump, slot);
                motor.jumpHeld = NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Jump, slot);
                float vertical = NativeInputBindings.GetAxis(NativeInputBindings.Action.Vertical, slot);
                if (NativeUserSettings.GetInt("alternativeJumping") == 1 && vertical >= .75f && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Brake, slot)) motor.jump = true;
                if (vertical != sourceHumanVertical[slot])
                {
                    motor.WheelieInputVertical(vertical);
                    sourceHumanVertical[slot] = vertical;
                }
                if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Use, slot)) motor.GetComponent<NativeKartItemInventory>().RequestUse();
            }
        }
        void CaptureSourcePresentationTimes()
        {
            foreach (var row in resultsFlow.Rows) row.Seconds = SourceTotalSeconds(row.RacerIndex);
        }
        void CommitSourceModeTimes()
        {
            var session = NativeGameSession.Current;
            if (session == null || sourceLapTimers == null) return;
            var station = session.stations.LastOrDefault(s => s.index == session.stationIndex);
            for (int racer = 0; racer < racers.Length; racer++)
            {
                if (raceConfig.mode == NativeSessionMode.GrandPrix && !SourceRacerObjectValid(racer)) continue;
                double seconds = SourceTotalSeconds(racer);
                string seat = session.config.roster[racer].seatId;
                if (raceConfig.mode == NativeSessionMode.GrandPrix && station != null)
                {
                    var row = station.results.Single(r => r.seatId == seat);
                    var total = session.totals.Single(r => r.seatId == seat);
                    total.time += seconds - row.time; row.time = seconds;
                    var pending = session.pending.FirstOrDefault(r => r.seatId == seat);
                    if (pending != null) pending.time = seconds;
                }
                if (raceConfig.roster[racer].isHuman)
                {
                    finishSeconds[racer] = seconds;
                    lapSeconds[racer].Clear(); lapSeconds[racer].AddRange(sourceLapTimers[racer].LapSeconds());
                }
            }
            CompleteModeProgress();
        }
    }
}
