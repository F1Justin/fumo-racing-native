using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeUfoVerification
    {
        [Serializable] sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report { public bool passed; public string error; public int recipeCount, boxCount; public Check[] checks; }
        public static bool Passed { get; private set; }
        public static IEnumerator StartVerification(string directory, NativeUfoManager manager)
        {
            var checks = new List<Check>(); var report = new Report();
            NativeUfoBox liveBox = null; NativeUfoPlayer livePlayer = null; bool wasDriving = false;
            try
            {
                if (!manager || !manager.catalog) throw new InvalidOperationException("Native UFO manager/catalog missing");
                var catalog = manager.catalog; report.recipeCount = catalog.recipes.Length; report.boxCount = manager.Boxes.Length;
                Add(checks, "original-four-slots-three-cycling-colors-gray-index", catalog.recipeLength == 4 && catalog.colors.Length == 3 && catalog.randomColor == 3 && catalog.respawnTime == 3 && catalog.switchTime == 3.25f && catalog.warnTime == 2.25f && catalog.warnRate == .1f);
                Add(checks, "original-26-unique-recipes", catalog.recipes.Length == 26);
                foreach (var recipe in catalog.recipes)
                {
                    var reversed = (int[])recipe.colors.Clone(); Array.Reverse(reversed);
                    string item = NativeUfoRules.Resolve(catalog.recipes, reversed, "YinYang", catalog.defaultItemId, out bool matched);
                    Add(checks, "recipe-" + recipe.id + "-unordered-and-original-icon", matched && item == (recipe.exclusive ? "YinYang" : recipe.itemId) && catalog.itemCatalog.Find(item)?.icon != null);
                }
                Add(checks, "multiplicity-and-nonnull-count", NativeUfoRules.Matches(new[] { 0, -1, 1, 1 }, new[] { 1, 0, 1 }) && !NativeUfoRules.Matches(new[] { 0, 0, 1 }, new[] { 0, 1, 1 }) && !NativeUfoRules.Matches(new[] { 0, 1 }, new[] { 0 }));
                var synthetic = new[] { new NativeUfoRules.Recipe { id = "red", colors = new[] { 0 }, itemId = "YinYang" }, new NativeUfoRules.Recipe { id = "pair", colors = new[] { 0, 0 }, itemId = "Power2" } };
                var state = new NativeUfoRules.State(4);
                NativeUfoRules.Collect(state, 0, 3, synthetic, null, "Frog"); NativeUfoRules.Collect(state, 2, 3, synthetic, null, "Frog");
                Add(checks, "unmatched-extra-color-retains-last-successful-preview", state.previewItemId == "YinYang");
                NativeUfoRules.Collect(state, 1, 3, synthetic, null, "Frog"); NativeUfoRules.Collect(state, 1, 3, synthetic, null, "Frog");
                Add(checks, "four-slot-capacity-no-fifth", state.count == 4 && !NativeUfoRules.Collect(state, 2, 3, synthetic, null, "Frog"));
                Add(checks, "occupied-and-failed-give-retain-colors", !NativeUfoRules.Use(state, true, id => true) && !NativeUfoRules.Use(state, false, id => false) && state.count == 4);
                string received = null;
                Add(checks, "use-gives-retained-recipe-once-clears-four-slots", NativeUfoRules.Use(state, false, id => { received = id; return true; }) && received == "YinYang" && state.count == 0 && state.colors[0] == -1 && state.previewItemId == null && !NativeUfoRules.Use(state, false, id => true));
                var fallback = new NativeUfoRules.State(4); NativeUfoRules.Collect(fallback, 2, 3, synthetic, null, "Frog");
                Add(checks, "first-unmatched-uses-original-default-frog", fallback.previewItemId == "Frog" && catalog.defaultItemId == "Frog");
                Add(checks, "exclusive-recipe-uses-character-item", NativeUfoRules.Resolve(new[] { new NativeUfoRules.Recipe { colors = new[] { 0 }, exclusive = true } }, new[] { 0 }, "WindBoost", "Frog", out bool exclusive) == "WindBoost" && exclusive);
                Add(checks, "color-wrap-and-gray-joins-cycle", NativeUfoRules.NextColor(0, 3) == 1 && NativeUfoRules.NextColor(2, 3) == 0 && NativeUfoRules.NextColor(3, 3) == 0);
                Add(checks, "type1-created-original-model-trigger-boxes", manager.ItemType == 1 && manager.Boxes.Length == manager.sourceBoxes.Length && manager.Boxes.Length > 0 && manager.Boxes[0].mesh && manager.Boxes[0].GetComponent<Collider>().isTrigger);
                bool originalsInactive = true; foreach (var original in manager.sourceBoxes) if (original.activeSelf) originalsInactive = false;
                Add(checks, "type1-replaces-all-original-random-boxes", originalsInactive);
                if (manager.Players.Length > 0 && manager.Boxes.Length > 0)
                {
                    livePlayer = manager.Players[0]; liveBox = manager.Boxes[0]; wasDriving = livePlayer.inventory.motor.driving; livePlayer.inventory.motor.driving = true;
                    livePlayer.inventory.ResetItems(); livePlayer.ResetState(); liveBox.ResetBox(0); liveBox.working = true;
                    Add(checks, "actual-color-pickup-collects-not-random-inventory", liveBox.Pickup(livePlayer) && livePlayer.State.count == 1 && !livePlayer.inventory.HasItem && !liveBox.Spawned && !liveBox.Pickup(livePlayer));
                    liveBox.Advance(2.99f); bool waiting = !liveBox.Spawned; liveBox.Advance(.02f);
                    Add(checks, "actual-box-three-second-respawn", waiting && liveBox.Spawned);
                    Add(checks, "actual-recipe-use-immediate-give-original-icon", livePlayer.Use() && livePlayer.inventory.HasItem && livePlayer.inventory.ItemDecided && livePlayer.inventory.CurrentId == "YinYang" && livePlayer.inventory.CurrentIcon && livePlayer.State.count == 0);
                    liveBox.ResetBox(1); liveBox.Pickup(livePlayer); int collected = livePlayer.State.count;
                    Add(checks, "ordinary-item-occupancy-preserves-collected-ufo", !livePlayer.Use() && livePlayer.State.count == collected);
                    livePlayer.inventory.ResetItems(); livePlayer.ResetState(); liveBox.ResetBox(3);
                    Add(checks, "actual-gray-pickup-uses-random-inventory-no-color", liveBox.Pickup(livePlayer) && livePlayer.State.count == 0 && livePlayer.inventory.HasItem);
                }
                else Add(checks, "actual-live-player-box-present", false);
                report.passed = checks.TrueForAll(check => check.passed);
            }
            catch (Exception error) { report.error = error.Message; }
            finally
            {
                if (livePlayer) { livePlayer.inventory.ResetItems(); livePlayer.ResetState(); livePlayer.inventory.motor.driving = wasDriving; }
                if (liveBox) { int index = Array.IndexOf(manager.Boxes, liveBox); liveBox.ResetBox(manager.sourceColors[index]); }
            }
            report.checks = checks.ToArray(); Passed = report.passed; Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ufo-verification.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
        static void Add(List<Check> checks, string name, bool passed) => checks.Add(new Check { name = name, passed = passed });
    }
}
