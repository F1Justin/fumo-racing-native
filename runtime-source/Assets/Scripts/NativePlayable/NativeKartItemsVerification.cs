using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartItemsVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase, item;
            public int frame, giveCount, decideCount, useCount, doneCount, boxesSpawned;
            public float topSpeed, grassTopSpeed, acceleration, driftSteerGain, projectileSpeed;
            public bool hasItem, itemDecided, boxSpawned, carried, powerApplied;
        }
        [Serializable] sealed class Report
        {
            public string version;
            public bool passed;
            public Check[] checks;
            public Sample[] samples;
            public string[] captures;
        }

        static void Add(List<Check> checks, string name, bool passed, string detail)
        { checks.Add(new Check { name = name, passed = passed, detail = detail }); }

        public static bool CheckRules()
        {
            var s = new NativeKartItemState();
            if (!s.Give(NativeKartItemKind.Power) || s.Give(NativeKartItemKind.YinYang)) return false;
            if (s.Use(false) != NativeKartItemAction.None || s.Advance(1.99f) || s.decided) return false;
            s.Advance(.01f);
            if (!s.decided || s.Use(true) != NativeKartItemAction.None) return false;
            if (s.Use(false) != NativeKartItemAction.StartPower || !s.HasItem) return false;
            if (s.Advance(2.98f) || !s.HasItem) return false;
            if (!s.Advance(.03f) || s.HasItem || s.doneCount != 1) return false;
            if (!s.Give(NativeKartItemKind.YinYang)) return false;
            s.Advance(2f);
            if (s.Use(false) != NativeKartItemAction.CarryYinYang || !s.HasItem) return false;
            if (s.Use(false) != NativeKartItemAction.ThrowYinYang || s.HasItem || s.doneCount != 2)
                return false;
            if (!s.Give(NativeKartItemKind.YinYang)) return false;
            s.Advance(2.1f);
            s.Use(false);
            if (!s.CompleteCarriedLoss() || s.HasItem || s.doneCount != 3) return false;
            s.Reset();
            return !s.HasItem && s.doneCount == 0;
        }

        static Sample Snapshot(string phase, NativeKartItemInventory inventory, NativeKartItemBox box,
            NativeItemActor projectile = null)
        {
            var stats = inventory.motor.stats;
            return new Sample
            {
                phase = phase, item = inventory.CurrentKind.ToString(), frame = Time.frameCount,
                giveCount = inventory.GiveCount, decideCount = inventory.DecideCount,
                useCount = inventory.UseCount, doneCount = inventory.DoneCount,
                boxesSpawned = UnityEngine.Object.FindObjectsOfType<NativeKartItemBox>(true).Count(b => b.Spawned),
                topSpeed = stats._topSpeed, grassTopSpeed = stats._topSpeedGrass,
                acceleration = stats._acceleration, driftSteerGain = stats._driftSteerGain,
                projectileSpeed = projectile ? projectile.Velocity.magnitude : 0f,
                hasItem = inventory.HasItem, itemDecided = inventory.ItemDecided,
                boxSpawned = box && box.Spawned, carried = inventory.State.carried,
                powerApplied = inventory.Effects && inventory.Effects.IsActive("Power")
            };
        }

        static IEnumerator EnterBox(CharacterController body, NativeKartItemBox box)
        {
            body.enabled = false;
            body.transform.position = box.transform.position - box.transform.forward * 5f;
            body.enabled = true;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            body.Move(box.transform.forward * 6f);
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        static void EnableOnly(NativeKartItemBox[] boxes, NativeKartItemBox selected)
        {
            foreach (var box in boxes)
            {
                var collider = box.GetComponent<Collider>();
                if (collider) collider.enabled = box == selected;
            }
        }

        static IEnumerator Capture(string directory, string name, List<string> captures)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            captures.Add(path);
            yield return new WaitForEndOfFrame();
        }

        static IEnumerator CheckActorLifetime(NativeKartItemInventory inventory, List<Check> checks)
        {
            inventory.ResetItems();
            Add(checks, "carried-lifetime-give", inventory.TryGive(NativeKartItemKind.YinYang, false),
                "source YinYang item is available for a decided two-action use");
            bool firstUse = inventory.UseItem();
            var carried = UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                .FirstOrDefault(a => a.Owner == inventory.motor && a.ItemId == "YinYang" && a.Carried && !a.Finished);
            Add(checks, "carried-actor-owned", firstUse && carried && inventory.HasItem &&
                inventory.State.carried && carried.Mode == NativeItemActorMode.Projectile &&
                carried.transform.parent == inventory.placePoint,
                "first use attaches the live source actor to the vehicle carry point");
            if (carried) carried.DestroyNow();
            yield return null;
            Add(checks, "destroyed-carried-item-completes-slot", !inventory.HasItem &&
                inventory.DoneCount == 1 && !inventory.UseItem(),
                "carried actor destruction completes the held item once and prevents a phantom second shot");

            inventory.TryGive(NativeKartItemKind.YinYang, false);
            bool carriedAgain = inventory.UseItem();
            var secondCarry = UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                .FirstOrDefault(a => a.Owner == inventory.motor && a.ItemId == "YinYang" && a.Carried && !a.Finished);
            bool secondUse = inventory.UseItem();
            var thrown = UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                .FirstOrDefault(a => a.Owner == inventory.motor && a.ItemId == "YinYang" && !a.Carried && !a.Finished);
            Add(checks, "throw-retires-carried-actor-once", carriedAgain && secondCarry && secondUse &&
                secondCarry.Finished && thrown && !inventory.HasItem && inventory.DoneCount == 2,
                "second action replaces the carried instance with a flying actor and completes the slot once");
            float projectileDuration = inventory.catalog.Find("YinYang").projectile.duration;
            float end = Time.time + projectileDuration + 0.2f;
            while (Time.time < end) yield return null;
            Add(checks, "projectile-lifetime-ends", !thrown || thrown.Finished,
                "live flying actor retires by the serialized projectile lifetime or an earlier collision");
            inventory.ResetItems();
            yield return null;
            Add(checks, "restart-clears-item-actors", !inventory.HasItem &&
                inventory.World && !UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                    .Any(a => a.Owner == inventory.motor && !a.Finished),
                "race reset clears every owned world actor without retaining a held slot");
        }

        static IEnumerator CheckFrogTwoAction(NativeKartItemInventory inventory, List<Check> checks)
        {
            inventory.ResetItems();
            bool given = inventory.TryGive(NativeKartItemKind.Frog, false);
            bool firstUse = inventory.UseItem();
            var frog = UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                .FirstOrDefault(a => a.Owner == inventory.motor && a.ItemId == "Frog" && !a.Finished);
            Add(checks, "frog-first-use-places", given && firstUse && frog && frog.Carried &&
                frog.Mode == NativeItemActorMode.Frog && frog.transform.parent == inventory.placePoint &&
                inventory.HasItem && inventory.State.carried,
                "source FrogItem first use places the carried actor at PlayerItems.placePoint");
            bool secondUse = inventory.UseItem();
            Add(checks, "frog-second-use-launches", secondUse && frog && !frog.Finished &&
                !frog.Carried && frog.transform.parent == null && !inventory.HasItem &&
                inventory.DoneCount == 1,
                "source FrogItem second use initializes and detaches the same actor then completes the slot");
            inventory.ResetItems();
            yield return null;
            Add(checks, "frog-restart-cleans", !frog && inventory.World &&
                !UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                    .Any(a => a.Owner == inventory.motor && a.ItemId == "Frog" && !a.Finished),
                "race reset destroys the launched Frog actor");
        }

        static IEnumerator CheckAutomaticUseDuringFreeze(NativeKartItemInventory inventory, List<Check> checks)
        {
            inventory.ResetItems();
            var hit = inventory.GetComponent<NativeKartHit>();
            if (hit) hit.ResetState();
            float baseDrift = inventory.motor.stats._driftSteerGain;
            bool frozen = hit && hit.FreezeFor(.5f);
            bool given = inventory.TryGive(NativeKartItemKind.DriftPlusPowerup, false);
            Add(checks, "auto-use-during-hit-freeze", frozen && given && inventory.Frozen &&
                inventory.Effects.IsActive("DriftPlusPowerup") && inventory.UseCount == 1 &&
                Mathf.Abs(inventory.motor.stats._driftSteerGain - (baseDrift + 12f)) < .001f,
                "Item.OnStart directly invokes useAtStart while PlayerItems manual use is frozen");
            inventory.ResetItems();
            if (hit) hit.ResetState();
            yield return null;
            Add(checks, "auto-use-freeze-reset", inventory.Effects.ActiveCount == 0 &&
                Mathf.Abs(inventory.motor.stats._driftSteerGain - baseDrift) < .001f,
                "replay cleanup removes the auto-started temporary drift modifier");
        }

        public static IEnumerator Run(NativeRace race, string directory, Action<bool> completed = null)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var captures = new List<string>();
            var boxes = UnityEngine.Object.FindObjectsOfType<NativeKartItemBox>(true);
            var motor = race.racers[0];
            var inventory = motor.GetComponent<NativeKartItemInventory>();
            var body = motor.GetComponent<CharacterController>();
            float savedTimeScale = Time.timeScale;
            bool savedDriving = motor.driving, savedMotorEnabled = motor.enabled;
            var savedOtherDriving = race.racers.Skip(1).Select(x => x.driving).ToArray();
            Vector3 savedPosition = motor.transform.position;
            Quaternion savedRotation = motor.transform.rotation;
            var savedOtherEnabled = race.racers.Skip(1).Select(x => x.enabled).ToArray();
            var savedBoxColliderEnabled = boxes.Select(b => b && b.GetComponent<Collider>() &&
                b.GetComponent<Collider>().enabled).ToArray();
            bool finished = false;
            try
            {
                race.SetVisualCapture("race", true);
                Time.timeScale = 1f;
                Add(checks, "pure-item-rules", CheckRules(), "single slot, two-second decide, Power duration, two-action YinYang");
                Add(checks, "forest-25-boxes", boxes.Length == 25 && boxes.All(b => b && b.GetComponent<Collider>() &&
                    b.GetComponent<Collider>().isTrigger && Mathf.Abs(b.respawnTime - 2f) < .001f),
                    "actual scene BoxCollider triggers and respawn time");
                var yinYang = inventory && inventory.catalog ? inventory.catalog.Find("YinYang") : null;
                Add(checks, "catalog-and-world-bound", inventory && inventory.World &&
                    yinYang != null && yinYang.throwPrefab && yinYang.carryPrefab && yinYang.projectile != null &&
                    inventory.catalog.Find("Power") != null,
                    "source item catalog, actor prefabs, and world are bound to the live inventory");
                if (!inventory || !body || boxes.Length < 3 || !inventory.World || yinYang == null ||
                    yinYang.projectile == null) yield break;
                foreach (var other in race.racers.Skip(1)) { other.enabled = false; other.driving = false; }
                motor.enabled = false;
                motor.driving = true;
                inventory.ResetItems();
                var box = boxes[0];
                EnableOnly(boxes, box);
                box.ResetBox();
                box.ForcedKind = NativeKartItemKind.Power;
                float baseTop = motor.stats._topSpeed, baseGrass = motor.stats._topSpeedGrass;
                float baseAccel = motor.stats._acceleration, baseDrift = motor.stats._driftSteerGain;
                yield return EnterBox(body, box);
                samples.Add(Snapshot("picked-power", inventory, box));
                Add(checks, "box-physics-pickup", !box.Spawned && inventory.HasItem &&
                    inventory.CurrentKind == NativeKartItemKind.Power && inventory.GiveCount == 1,
                    "CharacterController entered original Forest RandomBox collider");
                yield return Capture(directory, "item-roulette", captures);
                var fullSlotBox = boxes[1];
                EnableOnly(boxes, fullSlotBox);
                fullSlotBox.ResetBox();
                fullSlotBox.ForcedKind = NativeKartItemKind.YinYang;
                yield return EnterBox(body, fullSlotBox);
                Add(checks, "single-slot-consumes-box", !fullSlotBox.Spawned &&
                    inventory.CurrentKind == NativeKartItemKind.Power && inventory.GiveCount == 1,
                    "second original collider box hides despite occupied single slot");
                float until = Time.time + 2.15f;
                while (Time.time < until) yield return null;
                samples.Add(Snapshot("decided-power", inventory, box));
                Add(checks, "two-second-decision", inventory.ItemDecided && inventory.DecideCount == 1,
                    "original random hold delay");
                Add(checks, "box-two-second-respawn", box.Spawned,
                    "box returned after two scaled seconds");
                yield return Capture(directory, "item-decided", captures);
                Add(checks, "original-item-hud", race.itemHud && race.itemHud.icon && race.itemHud.CurrentState == "decidedItem" && race.itemHud.icon.sprite == inventory.CurrentIcon && race.itemHud.clips.Length == 3 && race.itemHud.rouletteIcons.Length > 2, "Original images, three animation states and full icon roulette loaded");
                bool used = inventory.UseItem();
                samples.Add(Snapshot("power-active", inventory, box));
                Add(checks, "power-original-additive-stats", used &&
                    Mathf.Abs(motor.stats._topSpeed - (baseTop + 50f)) < .001f &&
                    Mathf.Abs(motor.stats._topSpeedGrass - (baseGrass + 50f)) < .001f &&
                    Mathf.Abs(motor.stats._acceleration - (baseAccel + 100f)) < .001f &&
                    Mathf.Abs(motor.stats._driftSteerGain - (baseDrift + 5f)) < .001f && inventory.HasItem,
                    "Power.asset +50/+50/+100/+5 while item remains in slot");
                bool activeBeforePause = inventory.Effects.IsActive("Power");
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(.15f);
                Add(checks, "pause-freezes-item-clock", activeBeforePause &&
                    inventory.Effects.IsActive("Power") &&
                    Mathf.Abs(motor.stats._topSpeed - (baseTop + 50f)) < .001f,
                    "Power remains active with unchanged stats while scaled time is paused");
                Time.timeScale = 1f;
                until = Time.time + 3.15f;
                while (Time.time < until) yield return null;
                samples.Add(Snapshot("power-done", inventory, box));
                Add(checks, "power-restores-stats", !inventory.HasItem && inventory.DoneCount == 1 &&
                    Mathf.Abs(motor.stats._topSpeed - baseTop) < .001f &&
                    Mathf.Abs(motor.stats._topSpeedGrass - baseGrass) < .001f &&
                    Mathf.Abs(motor.stats._acceleration - baseAccel) < .001f &&
                    Mathf.Abs(motor.stats._driftSteerGain - baseDrift) < .001f,
                    "all four fields restored at three seconds");
                yield return Capture(directory, "item-used", captures);
                box = boxes[2];
                EnableOnly(boxes, box);
                box.ResetBox();
                box.ForcedKind = NativeKartItemKind.YinYang;
                yield return EnterBox(body, box);
                until = Time.time + 2.15f;
                while (Time.time < until) yield return null;
                bool carried = inventory.UseItem();
                samples.Add(Snapshot("yin-yang-carried", inventory, box));
                Add(checks, "yin-yang-first-use-carries", carried && inventory.HasItem &&
                    inventory.State.carried, "two-action first use");
                bool thrown = inventory.UseItem();
                var shot = UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                    .FirstOrDefault(p => p.Owner == motor && p.ItemId == "YinYang" &&
                        !p.Carried && !p.Finished && p.Velocity.sqrMagnitude > 1f);
                samples.Add(Snapshot("yin-yang-thrown", inventory, box, shot));
                Add(checks, "yin-yang-second-use-throws", thrown && !inventory.HasItem && shot &&
                    Mathf.Abs(shot.Velocity.magnitude - 100f) < .01f,
                    "original two-action projectile at stopped speed uses force 100");
                Add(checks, "runtime-projectile-silent", shot &&
                    shot.GetComponentsInChildren<AudioSource>(true).All(a => a.mute && !a.playOnAwake) &&
                    shot.GetComponentsInChildren<AudioListener>(true).All(a => !a.enabled),
                    "live source prefab actor has muted audio components");
                inventory.ResetItems();
                yield return null;
                Add(checks, "reset-clears-projectiles-and-state", !inventory.HasItem &&
                    inventory.GiveCount == 0 && inventory.DoneCount == 0 &&
                    !UnityEngine.Object.FindObjectsOfType<NativeItemActor>()
                        .Any(p => p.Owner == motor && !p.Finished), "new race clears owner actors and item state");
                yield return CheckActorLifetime(inventory, checks);
                yield return CheckFrogTwoAction(inventory, checks);
                yield return CheckAutomaticUseDuringFreeze(inventory, checks);
                finished = true;
            }
            finally
            {
                Time.timeScale = savedTimeScale;
                if (inventory) inventory.ResetItems();
                for (int i = 1; i < race.racers.Length; i++) race.racers[i].enabled = savedOtherEnabled[i - 1];
                for (int i = 1; i < race.racers.Length; i++) race.racers[i].driving = savedOtherDriving[i - 1];
                motor.driving = savedDriving;
                motor.enabled = savedMotorEnabled;
                if (body)
                {
                    body.enabled = false;
                    motor.transform.SetPositionAndRotation(savedPosition, savedRotation);
                    body.enabled = true;
                }
                for (int i = 0; i < boxes.Length; i++)
                    if (boxes[i])
                    {
                        boxes[i].ResetBox();
                        boxes[i].ForcedKind = NativeKartItemKind.None;
                        var collider = boxes[i].GetComponent<Collider>();
                        if (collider) collider.enabled = savedBoxColliderEnabled[i];
                    }
                Add(checks, "global-silence", AudioListener.volume == 0f, "construction listener muted");
                Add(checks, "runtime-errors", race.VisualErrorCount == 0, "actual Player runtime errors=" + race.VisualErrorCount);
                var report = new Report { version = Application.version, passed = finished && checks.All(x => x.passed),
                    checks = checks.ToArray(), samples = samples.ToArray(), captures = captures.ToArray() };
                File.WriteAllText(Path.Combine(directory, "item-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("NATIVE_ITEM_TEST " + (report.passed ? "PASS" : "FAIL") +
                    " checks=" + checks.Count + " boxes=" + boxes.Length);
                if (completed != null) completed(report.passed);
                else Application.Quit(report.passed ? 0 : 2);
            }
        }
    }
}
