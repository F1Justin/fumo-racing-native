using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        internal static string SourceResultCardKey(int slot)
        {
            if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            const string key = "GlobalHUD/Canvas/Results/playersresult/ResultPlayerUI";
            return slot == 0 ? key : key + " (" + slot + ")";
        }
        internal static Color SourceHumanColor(int index)
        {
            switch (index)
            {
                case 0: return Color.red;
                case 1: return Color.blue;
                case 2: return new Color(1, 235f / 255f, 4f / 255f, 1);
                case 3: return Color.green;
                default: return Color.white;
            }
        }
        internal void SourceResultCards(IReadOnlyList<NativeResultsRow> rows, string[] names, int racerCount)
        {
            for (int slot = 0; slot < 4; slot++) SetActive(SourceResultCardKey(slot), false);
            foreach (var row in rows)
            {
                if (row.HumanIndex < 0) continue;
                string key = SourceResultCardKey(row.HumanIndex);
                SetActive(key, true); SetActive(key + "/readyfade", true);
                string character = names[row.RacerIndex].Replace(" / YOU", "");
                SetText(key + "/character", NativeLocalization.Get("CHAR_" + character.ToUpperInvariant(), character));
                SetText(key + "/rank", row.Place.ToString());
                var time = TimeSpan.FromSeconds(row.Seconds);
                SetText(key + "/Time", "<mspace=18>" + time.Hours.ToString("00") + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00") + "." + time.Milliseconds.ToString("000") + "</mspace>");
                TextColor(key + "/rank", resultRankGradient.Evaluate((row.Place - 1f) / racerCount));
                transforms[key + "/banner"].GetComponent<Image>().color = SourceHumanColor(row.HumanIndex);
                SourceReadyGroup(row.HumanIndex).alpha = 0;
            }
        }
        internal void RestoreSourceResultCardVisibility(IReadOnlyList<NativeResultsRow> rows)
        {
            for(int slot=0;slot<4;slot++)
            {
                bool present=false;foreach(var row in rows)if(row.HumanIndex==slot){present=true;break;}
                SetActive(SourceResultCardKey(slot),present);
                if(present)SetActive(SourceResultCardKey(slot)+"/readyfade",true);
            }
        }
        CanvasGroup SourceReadyGroup(int slot)
        {
            var node = transforms[SourceResultCardKey(slot) + "/readyfade"].gameObject;
            return node.GetComponent<CanvasGroup>() ?? node.AddComponent<CanvasGroup>();
        }
        internal float[] SourceReadyAlphas()
        {
            var values = new float[4];
            for (int i = 0; i < 4; i++)
            {
                var group = transforms[SourceResultCardKey(i) + "/readyfade"].GetComponent<CanvasGroup>();
                values[i] = group ? group.alpha : 0;
            }
            return values;
        }
        internal bool SourceResultCardActive(int slot) => transforms[SourceResultCardKey(slot)].gameObject.activeInHierarchy;
        internal void SourceResultReady(int slot) => SourceReadyGroup(slot).alpha = 1;
        internal void SourceScoreboardColors(IReadOnlyList<NativeResultsRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var image = resultRows[i].Find("banner").GetComponent<Image>();
                var color = SourceHumanColor(rows[i].HumanIndex); color.a = .5f; image.color = color;
            }
        }
    }
}
