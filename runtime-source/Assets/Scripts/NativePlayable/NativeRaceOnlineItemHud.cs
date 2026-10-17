using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        NativeKartItemHud offlineItemHud, onlineItemHud;
        bool onlineItemHudActive;
        void UseOnlineItemHud(bool active)
        {
            if (active == onlineItemHudActive) return;
            onlineItemHudActive = active;
            var ufo = GetComponent<NativeUfoManager>(); if (ufo) ufo.UseOnlineHud(active);
            if (active)
            {
                offlineItemHud = itemHud;
                if (!onlineItemHud)
                {
                    var prefab = NativeContent.Load<NativeKartItemHud>("NativeOnline/OnlineItemHud");
                    if (!prefab) throw new InvalidOperationException("Missing authored Online Item HUD");
                    onlineItemHud = Instantiate(prefab, transform, false);
                }
                onlineItemHud.inventory = offlineItemHud ? offlineItemHud.inventory : null;
                if (offlineItemHud) offlineItemHud.gameObject.SetActive(false);
                itemHud = onlineItemHud; onlineItemHud.gameObject.SetActive(true);
            }
            else
            {
                if (onlineItemHud) onlineItemHud.gameObject.SetActive(false);
                itemHud = offlineItemHud; if (itemHud) itemHud.gameObject.SetActive(true);
            }
        }
    }
}
