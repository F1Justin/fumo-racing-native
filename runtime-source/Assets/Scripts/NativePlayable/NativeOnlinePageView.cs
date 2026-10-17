using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeOnlinePageView : MonoBehaviour
    {
        public int authoringVersion;
        readonly Dictionary<string, NativeOnlineElement> elements = new Dictionary<string, NativeOnlineElement>();
        readonly HashSet<NativeOnlineElement> used = new HashSet<NativeOnlineElement>();
        readonly Dictionary<string, NativeOnlineRowGroup> groups = new Dictionary<string, NativeOnlineRowGroup>();
        bool initialized;
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            foreach (var element in GetComponentsInChildren<NativeOnlineElement>(true))
            {
                if (element.GetComponentInParent<NativeOnlinePageView>(true) != this) continue;
                elements.Add(element.key, element); element.Initialize();
            }
            foreach (var group in GetComponentsInChildren<NativeOnlineRowGroup>(true)) groups.Add(group.key, group);
        }
        public void Begin()
        { Initialize(); used.Clear(); gameObject.SetActive(true); foreach (var group in groups.Values) group.Begin(); }
        public NativeOnlineElement Use(string key)
        {
            Initialize();
            if (!elements.TryGetValue(key, out var element)) return null; // Deleting a control in Prefab Mode is respected.
            used.Add(element); element.Visible(true); return element;
        }
        public NativeOnlinePageView Row(string key, int index)
        { Initialize(); return groups.TryGetValue(key, out var group) ? group.Row(index) : null; }
        public void End()
        {
            foreach (var element in elements.Values) if (!used.Contains(element)) element.Visible(false);
            foreach (var group in groups.Values) group.End();
        }
    }
}
