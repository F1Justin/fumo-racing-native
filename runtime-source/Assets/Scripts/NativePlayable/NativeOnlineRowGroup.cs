using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeOnlineRowGroup : MonoBehaviour
    {
        public string key;
        public NativeOnlinePageView template;
        readonly List<NativeOnlinePageView> rows = new List<NativeOnlinePageView>();
        int used;
        public void Begin() { used = 0; }
        public NativeOnlinePageView Row(int index)
        {
            if (!template) return null;
            while (rows.Count <= index)
            {
                var row = Instantiate(template, transform, false);
                row.name = key + " " + (rows.Count + 1);
                row.Initialize(); rows.Add(row);
            }
            used = Mathf.Max(used, index + 1);
            var result = rows[index]; result.Begin(); return result;
        }
        public void End()
        { for (int i = 0; i < rows.Count; i++) { if (i < used) rows[i].End(); else rows[i].gameObject.SetActive(false); } }
    }
}
