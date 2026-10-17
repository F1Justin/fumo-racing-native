using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeRaceHudRestorationVerification
    {
        [Serializable] public sealed class Check { public string id, detail; public bool passed; }
        [Serializable] public sealed class Report { public bool passed; public string evidenceScope; public Check[] checks; public bool visualAccepted; public VisualState[] visualStates; }
        [Serializable] public sealed class VisualState { public string id, screenshot; public int frame; public float sampledScaledTime; public string renderBinding; public int[] injectedRanks; public Vector3[] racerPositions; public Vector3[] itemPositions; public NativeAuthoredUi.RaceHudNodeSnapshot[] nodes; }
        static readonly FieldInfo places = typeof(NativeRace).GetField("finishPlaces", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo physicalTarget = typeof(NativeItemActor).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo stepHoming = typeof(NativeItemActor).GetMethod("StepHoming", BindingFlags.Instance | BindingFlags.NonPublic);

        public static IEnumerator Run(NativeRace race, string directory)
        {
            QualitySettings.vSyncCount = 0;
            yield return null;
            var checks = new List<Check>();
            var spawned = new List<NativeItemActor>();
            var visuals = new List<VisualState>();
            Directory.CreateDirectory(directory);
            var originalPlaces = places.GetValue(race);
            var racers = race.racers;
            var world = race.GetComponent<NativeItemWorld>();
            try
            {
                if (racers.Length < 4) throw new InvalidOperationException("Radar target verification needs four racers");
                int[] ranks = new int[racers.Length];
                for (int i = 0; i < ranks.Length; i++) ranks[i] = i + 1;
                places.SetValue(race, ranks);
                var first = Spawn(world, racers[0], true, false, spawned);
                Add(checks, "first-owner-no-homing-target", first.RadarTarget == null && physicalTarget.GetValue(first) == null,
                    "Original rank-1 lookup returns no target for first owner");
                var actor = Spawn(world, racers[2], true, false, spawned);
                var oldSensor = racers[1].GetComponent<NativeItemRadarSensor>();
                Add(checks, "initial-rank-predecessor", actor.RadarTarget == racers[1] && Contains(oldSensor, actor), "Third owner targets second");
                actor.transform.position = racers[1].transform.position + Vector3.back * 10;
                racers[1].GetComponent<NativeKartHit>().MakeInvulnerable(5);
                stepHoming.Invoke(actor, new object[] { 0f });
                Add(checks, "invulnerable-next-predecessor", actor.RadarTarget == racers[0], "Invulnerable second target transfers to first");
                Add(checks, "old-sensor-removed-new-added", !Contains(oldSensor, actor) && Contains(racers[0].GetComponent<NativeItemRadarSensor>(), actor), "Target transfer moves the same instance");
                actor.DestroyNow();
                Add(checks, "destroy-clears-sensor", !Contains(racers[0].GetComponent<NativeItemRadarSensor>(), actor), "Destroyed instance removed immediately");
                racers[1].GetComponent<NativeKartHit>().ResetState();

                var skip = Spawn(world, racers[3], true, false, spawned);
                ranks[0] = 1; ranks[1] = 4; ranks[2] = 3; ranks[3] = 2;
                skip.transform.position = racers[2].transform.position + Vector3.back * 10;
                racers[2].GetComponent<NativeKartHit>().MakeInvulnerable(5);
                stepHoming.Invoke(skip, new object[] { 0f });
                Add(checks, "retarget-skips-owner", skip.RadarTarget == racers[0], "Current target rank3 skips owner rank2 to rank1");
                skip.DestroyNow();
                racers[2].GetComponent<NativeKartHit>().ResetState();
                for (int i = 0; i < ranks.Length; i++) ranks[i] = i + 1;
                var empty = Spawn(world, racers[1], true, false, spawned);
                empty.transform.position = racers[0].transform.position + Vector3.back * 10;
                racers[0].GetComponent<NativeKartHit>().MakeInvulnerable(5);
                stepHoming.Invoke(empty, new object[] { 0f });
                Add(checks, "no-predecessor-clears-target", empty.RadarTarget == null && physicalTarget.GetValue(empty) == null,
                    "Invulnerable first target has no preceding racer");
                empty.DestroyNow();
                racers[0].GetComponent<NativeKartHit>().ResetState();
                var quiet = Spawn(world, racers[2], false, false, spawned);
                racers[1].GetComponent<NativeItemRadarSensor>().OnTarget(quiet);
                Add(checks, "onRadar-false-excluded", quiet.RadarTarget == null && !Contains(racers[1].GetComponent<NativeItemRadarSensor>(), quiet), "Physical target remains second; sensor excludes actor");
                var ofuda = Spawn(world, racers[0], false, true, spawned);
                Add(checks, "ofuda-self-first-falls-to-next", physicalTarget.GetValue(ofuda) == racers[1] && ofuda.RadarTarget == null,
                    "Ofuda first-owner override chooses following racer");
                var firstSlot = Spawn(world, racers[2], true, false, spawned);
                var secondSlot = Spawn(world, racers[2], true, false, spawned);
                var sensor = racers[1].GetComponent<NativeItemRadarSensor>();
                Add(checks, "sensor-insertion-order", Slot(sensor, 0) == firstSlot && Slot(sensor, 1) == secondSlot, "Slots follow notification order");
                firstSlot.DestroyNow();
                Add(checks, "sensor-removal-shifts-slot", Slot(sensor, 0) == secondSlot, "Remaining instance shifts into slot0");
                Add(checks, "source-rank-curve-keys", Vector3.Distance(NativeAuthoredUi.OriginalRankScaleAt(0), Vector3.one * 1.3812f) < .00001f &&
                    Vector3.Distance(NativeAuthoredUi.OriginalRankScaleAt(.083333336f), new Vector3(1.1906f, 1.3547838f, 1.1906f)) < .00001f &&
                    Vector3.Distance(NativeAuthoredUi.OriginalRankScaleAt(.16666667f), Vector3.one) < .00001f &&
                    Mathf.Abs(NativeAuthoredUi.OriginalRankRotationAt(0) + 10.559f) < .00001f &&
                    Mathf.Abs(NativeAuthoredUi.OriginalRankRotationAt(.16666667f)) < .00001f, "Original clip keys and endpoints");
                Add(checks, "source-rank-hermite-between-keys", Vector3.Distance(NativeAuthoredUi.OriginalRankScaleAt(1f / 60),
                    new Vector3(1.36747680f, 1.38014335f, 1.36747680f)) < .00001f &&
                    Mathf.Abs(NativeAuthoredUi.OriginalRankRotationAt(1f / 60) + 10.26334799f) < .00001f,
                    "Independent sample from serialized source clip tangents");
                var catalog = race.GetComponent<NativeItemCatalog>();
                var sourceOfuda = catalog.Find("Ofuda");
                var sourceSeal = catalog.Find("FantasySeal");
                var sourceWind = catalog.Find("WindBoost");
                Add(checks, "exported-nonprojectile-radar-cleared", sourceWind != null && sourceWind.dropFrog != null &&
                    (sourceWind.dropProjectile == null || (!sourceWind.dropProjectile.onRadar && !sourceWind.dropProjectile.ofudaBehaviour && !sourceWind.dropProjectile.radarIcon)),
                    "WindBoost AyaBullet is FrogItemBehaviour; inline empty ProjectileSpec has no radar metadata");
                Add(checks, "exported-radar-flags", sourceOfuda != null && sourceOfuda.projectile != null && !sourceOfuda.projectile.onRadar &&
                    sourceOfuda.projectile.ofudaBehaviour && sourceSeal != null && sourceSeal.projectile != null &&
                    sourceSeal.projectile.onRadar && sourceSeal.projectile.radarIcon && sourceSeal.projectile.ofudaBehaviour,
                    "Original Ofuda/FantasySeal source fields exported");
                foreach (var old in spawned) if (old) old.DestroyNow();
                race.SetVisualCapture("race", true);
                for (int i = 0; i < ranks.Length; i++) ranks[i] = i + 1;
                racers[1].transform.position = racers[0].transform.TransformPoint(new Vector3(-4, 0, -20));
                racers[2].transform.position = racers[0].transform.TransformPoint(new Vector3(5, 0, -30));
                var visibleA = Spawn(world, racers[1], true, false, spawned, sourceSeal.projectile.radarIcon);
                var visibleB = Spawn(world, racers[1], true, false, spawned, sourceSeal.projectile.radarIcon);
                visibleA.transform.position = racers[0].transform.TransformPoint(new Vector3(-3, 0, -15));
                visibleB.transform.position = racers[0].transform.TransformPoint(new Vector3(3, 0, -25));
                typeof(NativeRaceHud).GetMethod("ResetRestoredRaceHud", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(race.hud, null);
                yield return SettleRenderedState();
                var visible = Capture(race, directory, "injected-visible", ranks, visibleA, visibleB);
                visuals.Add(visible);
                Add(checks, "production-visible-rear-item-rank", visible.nodes[0].active && visible.nodes[0].text == "1" &&
                    visible.nodes[1].active && visible.nodes[1].alpha == 1 && visible.nodes[2].alpha == 1 &&
                    visible.nodes[3].alpha == 1 && visible.nodes[4].alpha == 1 && visible.nodes[3].sprite.Length > 0 &&
                    visible.nodes[3].size == new Vector2(64, 64), "Production authored nodes read after rendered frame; screenshot bound to injected state");
                bool arrowsVisible = true;
                for (int i = 5; i < 9; i++)
                    arrowsVisible &= visible.nodes[i].active && visible.nodes[i].effectiveAlpha == 1 &&
                        visible.nodes[i].sprite.Length > 0 && visible.nodes[i].sourceSpriteGuid.Length > 0 &&
                        Vector2.Distance(visible.nodes[i].size, new Vector2(28.909973f, 9.280888f)) < .01f;
                Add(checks, "production-four-arrow-children", arrowsVisible &&
                    visible.nodes[5].color == Color.white && visible.nodes[6].color == Color.white &&
                    Mathf.Abs(visible.nodes[7].color.r - .9811321f) < .00001f &&
                    Mathf.Abs(visible.nodes[7].color.g - .22677112f) < .00001f &&
                    visible.nodes[7].color == visible.nodes[8].color,
                    "All four actual arrow child Images are active with source sprite, inherited alpha, source size and rear-white/item-red color");
                Add(checks, "production-marker-horizontal-positions", Mathf.Abs(visible.nodes[1].localPosition.x + 120) < .01f &&
                    Mathf.Abs(visible.nodes[2].localPosition.x - 150) < .01f &&
                    Mathf.Abs(visible.nodes[3].localPosition.x + 90) < .01f &&
                    Mathf.Abs(visible.nodes[4].localPosition.x - 90) < .01f &&
                    Mathf.Abs(visible.nodes[1].localPosition.y) < .01f && Mathf.Abs(visible.nodes[2].localPosition.y) < .01f &&
                    Mathf.Abs(visible.nodes[3].localPosition.y) < .01f && Mathf.Abs(visible.nodes[4].localPosition.y) < .01f,
                    "Rendered production transforms retain source x=local lateral distance*30 through Layout/Repaint geometry resets");
                racers[1].transform.position = racers[0].transform.TransformPoint(new Vector3(-12, 0, -20));
                racers[2].transform.position = racers[0].transform.TransformPoint(new Vector3(15, 0, -30));
                typeof(NativeRaceHud).GetMethod("ResetRestoredRaceHud", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(race.hud, null);
                yield return SettleRenderedState();
                var separated = Capture(race, directory, "injected-visible-separated", ranks, visibleA, visibleB);
                visuals.Add(separated);
                Add(checks, "production-markers-separated-state", Mathf.Abs(separated.nodes[1].localPosition.x + 360) < .01f &&
                    Mathf.Abs(separated.nodes[2].localPosition.x - 450) < .01f && separated.nodes[3].alpha == 1 && separated.nodes[4].alpha == 1,
                    "Additional injected frame separates rear captions laterally from the two central item icons for rendered inspection");
                racers[1].transform.position = racers[0].transform.position;
                racers[2].transform.position = racers[0].transform.position + Vector3.right * 100;
                visibleA.transform.position = racers[0].transform.position;
                visibleB.transform.position = racers[0].transform.position + Vector3.right * 100;
                typeof(NativeRaceHud).GetMethod("ResetRestoredRaceHud", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(race.hud, null);
                yield return SettleRenderedState();
                var hidden = Capture(race, directory, "injected-distance-hidden", ranks, visibleA, visibleB);
                visuals.Add(hidden);
                Add(checks, "production-distance-hidden", hidden.nodes[1].alpha == 0 && hidden.nodes[2].alpha == 0 &&
                    hidden.nodes[3].alpha == 0 && hidden.nodes[4].alpha == 0, "Production CanvasGroup alpha at distance zero and 100");
                var authored = (NativeAuthoredUi)typeof(NativeRaceHud).GetField("authoredUi", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(race.hud);
                ranks[0] = 3; ranks[2] = 1;
                authored.ResetOriginalRaceRank();
                Time.timeScale = 1;
                yield return new WaitForSeconds(.04f); yield return new WaitForEndOfFrame();
                authored.OriginalRaceRank(4, racers.Length, true);
                Time.timeScale = 0;
                yield return SettleRenderedState();
                var mid = Capture(race, directory, "injected-finish-mid-animation", ranks, visibleA, visibleB);
                visuals.Add(mid);
                Time.timeScale = 0;
                yield return new WaitForSecondsRealtime(.06f); yield return new WaitForEndOfFrame();
                var paused = race.hud.ReadRestoredRaceHud()[0];
                Add(checks, "finished-animation-pause", Vector3.Distance(mid.nodes[0].localScale, paused.localScale) < .00001f,
                    "Independent animation uses scaled time and pauses with Time.timeScale zero");
                Time.timeScale = 1;
                yield return new WaitForSeconds(.2f);
                Time.timeScale = 0;
                yield return SettleRenderedState();
                var completed = Capture(race, directory, "injected-finish-animation-complete", ranks, visibleA, visibleB);
                visuals.Add(completed);
                Add(checks, "finish-freezes-rank-text-animation-completes", mid.nodes[0].text == "3" && completed.nodes[0].text == "3" &&
                    Vector3.Distance(mid.nodes[0].localScale, Vector3.one) > .01f &&
                    Vector3.Distance(completed.nodes[0].localScale, Vector3.one) < .00001f &&
                    Mathf.Abs(Mathf.DeltaAngle(completed.nodes[0].localEulerAngles.z, 0)) < .0001f,
                    "RankUI freeze preserves text while independent Animator reaches its authored endpoint");
                Time.timeScale = 0;
            }
            finally
            {
                foreach (var actor in spawned) if (actor) actor.DestroyNow();
                places.SetValue(race, originalPlaces);
            }
            bool passed = checks.TrueForAll(c => c.passed);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "race-hud-restoration-behavior.json"), JsonUtility.ToJson(new Report {
                passed = passed, evidenceScope = "Injected target lifecycle and source clip fields; paired rendered acceptance recorded separately", checks = checks.ToArray(), visualAccepted = false, visualStates = visuals.ToArray()
            }, true));
            Debug.Log("RACE_HUD_RESTORATION_BEHAVIOR " + passed);
            Application.Quit(passed ? 0 : 2);
        }

        static IEnumerator SettleRenderedState()
        {
            for (int i = 0; i < 2; i++)
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return new WaitForEndOfFrame();
            }
        }

        static VisualState Capture(NativeRace race, string directory, string id, int[] ranks, params NativeItemActor[] items)
        {
            string file = id + ".png";
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            var racerPositions = new Vector3[race.racers.Length];
            for (int i = 0; i < racerPositions.Length; i++) racerPositions[i] = race.racers[i].transform.position;
            var itemPositions = new Vector3[items.Length];
            for (int i = 0; i < items.Length; i++) itemPositions[i] = items[i].transform.position;
            return new VisualState { id = id, screenshot = file, frame = Time.frameCount, sampledScaledTime = Time.time,
                renderBinding = "Injected state held through at least two complete render frames; animation captures pause scaled time before settling. No claim of an exact moving Animator frame match",
                injectedRanks = (int[])ranks.Clone(), racerPositions = racerPositions, itemPositions = itemPositions,
                nodes = race.hud.ReadRestoredRaceHud() };
        }

        static NativeItemActor Spawn(NativeItemWorld world, NativeKartMotor owner, bool radar, bool ofuda, List<NativeItemActor> spawned, Sprite icon = null)
        {
            var spec = new NativeItemCatalog.ProjectileSpec { onRadar = radar, ofudaBehaviour = ofuda,
                radarIcon = icon, seeksFirst = ofuda, homingDistance = 75, homingIntensity = 10, duration = 10 };
            var entry = new NativeItemCatalog.Entry { id = ofuda ? "Ofuda" : "YinYangHoming", projectile = spec };
            var go = new GameObject("Radar lifecycle verification");
            var actor = go.AddComponent<NativeItemActor>(); actor.enabled = false;
            actor.Init(world, entry, owner, NativeItemActorMode.Projectile, spec, Vector3.forward, false);
            spawned.Add(actor);
            return actor;
        }
        static NativeItemActor Slot(NativeItemRadarSensor sensor, int at) { NativeItemActor item; return sensor && sensor.GetItem(at, out item) ? item : null; }
        static bool Contains(NativeItemRadarSensor sensor, NativeItemActor actor)
        {
            if (!sensor) return false;
            for (int i = 0; i < sensor.Count; i++) if (Slot(sensor, i) == actor) return true;
            return false;
        }
        static void Add(List<Check> checks, string id, bool passed, string detail) => checks.Add(new Check { id = id, passed = passed, detail = detail });
    }
}
