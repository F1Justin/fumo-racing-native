using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public readonly struct NativeRacerRankState
    {
        public readonly int CurrentTrigger, LastTrigger, CurrentTriggerIndex, CurrentLap;
        public NativeRacerRankState(int current,int last,int index,int lap)
        {CurrentTrigger=current;LastTrigger=last;CurrentTriggerIndex=index;CurrentLap=lap;}
    }
    public sealed class NativeRacingRanking
    {
        readonly int laps;
        readonly int[] ranks, inverseRanks;
        readonly Comparison<int> compareDistance;
        readonly int[] finishOrder;
        int finishCount;
        readonly float[] distancesToFinish, distancesBetweenTriggers, triggerPrefixes;
        readonly Func<int,NativeRacerRankState> racerState;
        readonly Func<int,Vector3> racerPosition,triggerPosition;
        readonly float lapDistance,totalDistance;
        float firstRacerTime,forgetTime;
        int lastFirstRacer=-1;
        public float FirstRacerTime=>firstRacerTime;
        public float ForgetTime=>forgetTime;
        public int LastFirstRacerIndex=>lastFirstRacer;
        public Func<float> ReadDeltaTime=()=>Time.deltaTime;
        public Func<int,bool> IsRacerAlive;
        public Action UpdateHumanTimers;
        bool HistoryPresent=>lastFirstRacer>=0&&(IsRacerAlive==null||IsRacerAlive(lastFirstRacer));
        public int GetMostFirstRacer()=>HistoryPresent?lastFirstRacer:ranks[0];
        public int RacerAtRank(int zeroBasedRank)=>zeroBasedRank<0||zeroBasedRank>=ranks.Length?-1:ranks[zeroBasedRank];
        public bool Working {get;set;}
        public bool Started {get;private set;}
        public int Count=>ranks.Length;
        public float LapDistance=>lapDistance;
        public float TotalDistance=>totalDistance;
        public NativeRacingRanking(int racerCount,int sourceLaps,int triggerCount,Func<int,NativeRacerRankState> state,Func<int,Vector3> position,Func<int,Vector3> trigger)
        {
            laps=sourceLaps;racerState=state;racerPosition=position;triggerPosition=trigger;
            ranks=new int[racerCount];inverseRanks=new int[racerCount];finishOrder=new int[racerCount];compareDistance=CompareDistance;
            for(int i=0;i<finishOrder.Length;i++)finishOrder[i]=-1;
            distancesToFinish=new float[racerCount];distancesBetweenTriggers=new float[triggerCount];triggerPrefixes=new float[triggerCount+1];
            for(int i=0;i<ranks.Length;i++){ranks[i]=i;inverseRanks[i]=i;}
            for(int i=0;i<triggerCount;i++)
            {distancesBetweenTriggers[i]=SourceMagnitude(triggerPosition((i+1)%triggerCount)-triggerPosition(i));lapDistance+=distancesBetweenTriggers[i];}
            for(int i=0;i<triggerCount;i++)triggerPrefixes[i+1]=triggerPrefixes[i]+distancesBetweenTriggers[i];
            totalDistance=lapDistance*laps;Working=true;
        }
        public void SetOriginalArrayOrder(int[] order)
        {
            if(Started||order==null||order.Length!=ranks.Length)throw new ArgumentException("Initial exact source array required",nameof(order));
            var present=new bool[ranks.Length];foreach(int actor in order){if(actor<0||actor>=ranks.Length||present[actor])throw new ArgumentException("Source array is not a permutation",nameof(order));present[actor]=true;}
            Array.Copy(order,ranks,ranks.Length);RefreshInverseRanks();
        }
        public void StartedRace(){Started=true;}
        public void Update()
        {
            if(!Working)return;
            for(int i=0;i<ranks.Length;i++)
            {
                var state=racerState(i);float prefix=state.CurrentTriggerIndex<=0?0:triggerPrefixes[state.CurrentTriggerIndex];
                int next=state.CurrentTriggerIndex+1;if(next>=distancesBetweenTriggers.Length)next=0;
                float segment=state.CurrentTriggerIndex<0?0:distancesBetweenTriggers[state.CurrentTriggerIndex];
                segment-=SourceMagnitude(triggerPosition(next)-racerPosition(i));segment+=prefix;
                float remaining=(laps-state.CurrentLap)*lapDistance;
                remaining+=lapDistance-segment;
                if(state.CurrentTrigger<0&&state.CurrentTriggerIndex>=0&&state.LastTrigger<=1)remaining=totalDistance+totalDistance;
                distancesToFinish[i]=remaining;
            }
            int previousLeader=ranks[0];
            Array.Sort(ranks,compareDistance);RefreshInverseRanks();
            int leader=ranks[0];
            if(leader!=previousLeader)firstRacerTime=0;
            else
            {
                firstRacerTime+=ReadDeltaTime();
                if(!HistoryPresent)lastFirstRacer=leader;
            }
            if(Started)UpdateHumanTimers?.Invoke();
            if(HistoryPresent&&lastFirstRacer!=leader)forgetTime+=ReadDeltaTime();
            if(forgetTime>=4f)lastFirstRacer=-1;
        }
        public void RecordFinish(int racer)
        {
            if(racer<0||racer>=ranks.Length)throw new ArgumentOutOfRangeException(nameof(racer));
            if(finishOrder[racer]<0)
            {
                finishOrder[racer]=finishCount++;
                Array.Sort(ranks,compareDistance);RefreshInverseRanks();
            }
        }
        int CompareDistance(int x,int y)
        {
            int finishedX=finishOrder[x],finishedY=finishOrder[y];
            if(finishedX>=0)return finishedY>=0?finishedX.CompareTo(finishedY):-1;
            if(finishedY>=0)return 1;
            return distancesToFinish[x].CompareTo(distancesToFinish[y]);
        }
        void RefreshInverseRanks(){for(int i=0;i<ranks.Length;i++)inverseRanks[ranks[i]]=i;}
        public int GetRank(int racerIndex)=>racerIndex<0||racerIndex>=inverseRanks.Length?-1:inverseRanks[racerIndex];
        public int[] SnapshotRanks()=>(int[])ranks.Clone();
        public float DistanceToFinish(int racerIndex)=>distancesToFinish[racerIndex];
        static float SourceMagnitude(Vector3 value)
        {float square=value.y*value.y;square+=value.x*value.x;square+=value.z*value.z;return (float)Math.Sqrt(square);}
    }
}
