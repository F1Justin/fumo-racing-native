using System;
namespace Fumo.NativePlayable
{
    public sealed class NativeRaceResultAppend
    {
        readonly int[] results;
        public int Count {get;private set;}
        public event Action<int> EndRacerRequested;
        public NativeRaceResultAppend(int racers)
        {results=new int[racers];for(int i=0;i<racers;i++)results[i]=-1;}
        public void FinishedRace(int racer,int[] ranks)
        {
            EndRacerRequested?.Invoke(racer);
            if(Count>=results.Length)return;
            results[Count++]=racer;
            if(Count==results.Length-1)
            {
                EndRacerRequested?.Invoke(ranks[results.Length-1]);
                AutoComplete(ranks);
            }
        }
        public void AutoComplete(int[] ranks)
        {
            if(ranks==null||ranks.Length!=results.Length)throw new ArgumentException("Exact current original-rank array required",nameof(ranks));
            var present=new bool[results.Length];
            for(int i=0;i<Count;i++)present[results[i]]=true;
            foreach(int racer in ranks)
            {
                if(racer<0||racer>=results.Length)throw new ArgumentException("Current ranks contain invalid racer",nameof(ranks));
                if(present[racer])continue;
                results[Count++]=racer;present[racer]=true;
            }
            if(Count!=results.Length)throw new ArgumentException("Current ranks must contain every racer",nameof(ranks));
        }
        public int[] PrefixSnapshot()
        {
            var prefix=new int[Count];Array.Copy(results,prefix,Count);return prefix;
        }
        public int[] Snapshot()
        {
            if(Count!=results.Length)throw new InvalidOperationException("Results array incomplete");
            var seen=new bool[results.Length];
            foreach(int racer in results){if(racer<0||racer>=results.Length||seen[racer])throw new InvalidOperationException("Results must be exact racer permutation");seen[racer]=true;}
            return (int[])results.Clone();
        }
    }
}
