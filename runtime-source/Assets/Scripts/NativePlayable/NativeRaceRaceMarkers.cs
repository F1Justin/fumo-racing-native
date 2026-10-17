namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        internal NativeKartMotor[] SourceMinimapRacers() => raceConfig == null ? racers
            : System.Array.ConvertAll(NativeLocalRosterOrder.Read(raceConfig), index => racers[index]);
        internal NativeVehicleRoster SourceRacerVehicleRoster(NativeKartMotor target)
            => racers != null && racers.Length > 0 && target == racers[0] ? vehicleRoster
                : target ? target.GetComponent<NativeVehicleRoster>() : null;
        readonly int[] sourceIndicatorPlayerIndices={0,1,2,3};
        public void BindSourceIndicatorPlayerIndex(int humanSlot,int inputPlayerIndex)
        {
            if(humanSlot<0||humanSlot>=4||inputPlayerIndex<0)throw new System.ArgumentOutOfRangeException();
            sourceIndicatorPlayerIndices[humanSlot]=inputPlayerIndex;
        }
        public bool TrySourceIndicatorIdentity(NativeKartMotor target, out string caption, out UnityEngine.Color color)
        {
            caption=NativeLocalization.Get("SYSTEM_CPU");color=UnityEngine.Color.white;
            if(!target||racers==null)return false;
            int racer=System.Array.IndexOf(racers,target);
            if(racer<0)return false;
            if(Net && Net.InRace && raceConfig!=null && racer<raceConfig.roster.Count)
            {
                if(raceConfig.roster[racer].isHuman)
                {
                    caption=Net.RaceIndicatorPlayerName(racer);
                    if(racer==Net.LocalRacerIndex)color=NativeAuthoredUi.SourceHumanColor(0);
                }
                return true;
            }
            if(sourceRaceState!=null)
                for(int slot=0;slot<sourceRaceState.Humans.Length;slot++)
                {
                    var human=sourceRaceState.Humans[slot];
                    if(!human.Joined||!human.RacerValid||human.RacerIndex!=racer)continue;
                    // Native race action assets use player slots; InputUser.index is an independent pause-owner identity.
                    caption=NativeLocalization.Get("SYSTEM_P"+(sourceIndicatorPlayerIndices[slot]+1).ToString());
                    color=NativeAuthoredUi.SourceHumanColor(slot);return true;
                }
            if(raceConfig!=null&&racer<raceConfig.roster.Count&&raceConfig.roster[racer].isHuman)return false;
            return true;
        }
        public NativeKartMotor RaceRacerAtRelativeRank(int offset) => RaceRacerAtRelativeRank(offset, 0);
        public NativeKartMotor RaceRacerAtRelativeRank(int offset, int ownerRacer)
        {
            if (racers == null || racers.Length == 0 || ownerRacer < 0 || ownerRacer >= racers.Length) return null;
            int desired = RankOf(racers[ownerRacer]) + offset;
            if (desired < 1 || desired > racers.Length) return null;
            foreach (var racer in racers) if (RankOf(racer) == desired) return racer;
            return null;
        }
    }
}
