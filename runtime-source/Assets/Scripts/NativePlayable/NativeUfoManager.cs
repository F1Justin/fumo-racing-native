using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeUfoManager : MonoBehaviour
    {
        public NativeUfoCatalog catalog;
        public GameObject boxPrefab;
        public GameObject[] sourceBoxes;
        public int[] sourceColors;
        public NativeUfoHud hud;
        NativeUfoHud offlineHud, onlineHud;
        bool onlineHudActive;
        public void UseOnlineHud(bool active)
        {
            if (active == onlineHudActive) return;
            onlineHudActive = active;
            if (active)
            {
                offlineHud = hud;
                if (!onlineHud) onlineHud = Instantiate(NativeContent.Load<NativeUfoHud>("NativeOnline/OnlineUfoHud"), transform, false);
                onlineHud.player = offlineHud ? offlineHud.player : null;
                if (offlineHud) offlineHud.gameObject.SetActive(false);
                hud = onlineHud; hud.gameObject.SetActive(true);
            }
            else
            {
                if (onlineHud) onlineHud.gameObject.SetActive(false);
                hud = offlineHud; if (hud) hud.gameObject.SetActive(true);
            }
        }
        public NativeUfoBox[] Boxes { get; private set; } = new NativeUfoBox[0];
        public NativeUfoPlayer[] Players { get; private set; } = new NativeUfoPlayer[0];
        public int ItemType { get; private set; }
        public void Configure(int itemType, NativeKartItemInventory[] inventories, NativeKartItemBox[] ordinaryBoxes, bool instantRespawn = false)
        {
            ItemType = itemType;
            foreach (var box in Boxes) if (box) { box.gameObject.SetActive(false); Destroy(box.gameObject); }
            Boxes = new NativeUfoBox[0];
            Players = new NativeUfoPlayer[inventories.Length];
            for (int i = 0; i < inventories.Length; i++)
            {
                var player = inventories[i].GetComponent<NativeUfoPlayer>(); if (!player) player = inventories[i].gameObject.AddComponent<NativeUfoPlayer>();
                player.catalog = catalog; player.inventory = inventories[i]; player.active = itemType == 1; player.ResetState(); Players[i] = player;
            }
            foreach (var box in ordinaryBoxes) if (box && !box.isFixed) box.gameObject.SetActive(itemType == 0);
            if (itemType == 1)
            {
                if (!catalog || !boxPrefab || sourceBoxes.Length != sourceColors.Length) throw new InvalidOperationException("Incomplete original UFO resources");
                Boxes = new NativeUfoBox[sourceBoxes.Length];
                for (int i = 0; i < Boxes.Length; i++)
                {
                    var source = sourceBoxes[i].transform;
                    var clone = Instantiate(boxPrefab, source.position, source.rotation, transform);
                    var box = clone.GetComponent<NativeUfoBox>(); box.catalog = catalog; box.respawnTime = instantRespawn ? 0 : -1; box.working = true; box.ResetBox(sourceColors[i]); Boxes[i] = box;
                }
            }
            if (hud) { hud.player = Players.Length > 0 ? Players[0] : null; hud.SetVisible(itemType == 1); }
        }
        public void SetVisible(bool visible) { if (hud) hud.SetVisible(visible && ItemType == 1); }
        public void SetWorking(bool working) { foreach (var box in Boxes) if (box) box.working = working; }
        public bool Use(int playerIndex) => playerIndex >= 0 && playerIndex < Players.Length && Players[playerIndex] && Players[playerIndex].Use();
    }
}
