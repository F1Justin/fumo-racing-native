using System;
using System.Collections.Generic;
using System.Linq;
namespace Fumo.NativePlayable
{
    public static class NativeLocalRosterOrder
    {
        public static int[] Create(int total,int humans)
        {
            if(humans<1||humans>4||total<humans)throw new ArgumentOutOfRangeException(nameof(humans));
            return Enumerable.Range(humans,total-humans).Concat(Enumerable.Range(0,humans).Reverse()).ToArray();
        }
        public static int[] Read(NativeSessionConfig config)
        {
            int count=config.racerCount;
            var order=config.sourceStorageOrder;
            if(order==null||order.Count==0)return Enumerable.Range(0,count).ToArray();
            if(order.Count!=count||order.Distinct().Count()!=count||order.Any(index=>index<0||index>=count))throw new InvalidOperationException("Source roster order is not an exact permutation");
            return order.ToArray();
        }
        public static int SourceIndex(NativeSessionConfig config,int storage) => Array.IndexOf(Read(config),storage);
        public static int GridSlot(NativeSessionConfig config,int storage,int pointCount)
        {
            if(pointCount<1)throw new ArgumentOutOfRangeException(nameof(pointCount));
            int participant=SourceIndex(config,storage);
            if(participant<0)throw new ArgumentOutOfRangeException(nameof(storage));
            if(config.roster[storage].isHuman && config.roster.Count(seat=>seat.isHuman)==1)
                return Math.Min(config.racerCount,pointCount)-1;
            return participant % pointCount;
        }
    }
}
