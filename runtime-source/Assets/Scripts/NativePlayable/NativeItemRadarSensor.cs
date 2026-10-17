using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemRadarSensor : MonoBehaviour
    {
        readonly List<NativeItemActor> items = new List<NativeItemActor>();
        public int Count => items.Count;

        public void OnTarget(NativeItemActor item)
        {
            if (!item || !item.OnRadar || items.Contains(item)) return;
            items.Add(item);
            item.Destroyed += OnIgnored;
        }

        public void OnIgnored(NativeItemActor item)
        {
            if (items.Remove(item) && item) item.Destroyed -= OnIgnored;
        }

        public bool GetItem(int index, out NativeItemActor item)
        {
            item = index >= 0 && index < items.Count ? items[index] : null;
            return item && !item.Finished;
        }

        void OnDestroy()
        {
            foreach (var item in items) if (item) item.Destroyed -= OnIgnored;
            items.Clear();
        }
    }
}
