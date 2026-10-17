using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ModuleWorldFxReport
        {
            public bool passed, accepted;
            public string vehicle, track, scope;
            public string[] checks;
            public NativeVehicleModuleEffects.SpawnRecord[] events;
            public string[] screenshots;
        }
        internal IEnumerator CheckModuleWorldFxLive()
        {
            QualitySettings.vSyncCount=0;
            var args = Environment.GetCommandLineArgs();
            string prefix = "--module-world-fx-vehicle=";
            string vehicle = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal));
            vehicle = vehicle == null ? "broom" : vehicle.Substring(prefix.Length);
            if (vehicle != "broom" && vehicle != "genji" && vehicle != "shoppingcart")
            {
                Directory.CreateDirectory(reportDirectory);
                FinishModuleWorldFx(vehicle, false, new[]{"FAIL unknown source module verification vehicle"}, new NativeVehicleModuleEffects.SpawnRecord[0], new string[0]);
                yield break;
            }
            Directory.CreateDirectory(reportDirectory);
            var checks = new List<string>(); var screenshots = new List<string>(); bool passed = true;
            Action<bool, string> check = (ok, name) => { passed &= ok; checks.Add((ok ? "PASS " : "FAIL ") + name); };
            yield return null;
            check(PrepareHudFixtureQuickRace(vehicle), "real QuickRace roster selection");
            SetVisualCapture("race", true); Time.timeScale = 1f; AllowCameraVerificationUpdates = true;
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble);
            foreach (var racer in racers)
            {
                racer.enabled = true; racer.driving = true;
                var ai = racer.GetComponent<NativeKartOriginalAI>();
                if (ai) { ai.enabled = true; ai.active = true; }
            }
            var motor = racers[0]; var fx = motor.GetComponent<NativeVehicleModuleEffects>();
            check(fx && fx.binding != null && fx.binding.family == motor.stats._moduleKind, "selected source module FX binding");
            if (!fx || fx.binding == null)
            {
                FinishModuleWorldFx(vehicle, false, checks.ToArray(), new NativeVehicleModuleEffects.SpawnRecord[0], screenshots.ToArray());
                yield break;
            }
            int baseline = fx.records.Count, observed = baseline, collectionEnd=-1;
            string[] required = vehicle == "broom" ? new[] { "Ready", "Use" }
                : vehicle == "genji" ? new[] { "Ready", "Use", "UseMax" } : new[] { "Overheat" };
            double deadline = Time.realtimeSinceStartupAsDouble + 180;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                if (collectionEnd<0 && fx.records.Count > observed)
                {
                    int first = observed; observed = fx.records.Count;
                    yield return null; yield return new WaitForEndOfFrame();
                    for (int i = first; i < observed; i++)
                    {
                        var row = fx.records[i];
                        check(row.timeScale > 0 && row.motorStep > 0, "production simulation clock " + i);
                        check(row.instance && row.instance.transform.parent == (row.followsVehicle ? motor.transform : null), "source parent " + i);
                        var life = row.instance ? row.instance.GetComponent<NativeItemPrefabLifetime>() : null;
                        check(life && life.StartScheduled && life.duration == (row.effect == "Overheat" ? 10f : 3f), "source scaled lifetime scheduled " + i);
                        check(row.instance && row.instance.GetComponentsInChildren<ParticleSystem>(true).Length > 0, "actual authored particle systems " + i);
                        if (row.effect == "Overheat") check(row.instance && row.instance.GetComponent<NativeItemPrefabQualityScope>()
                            && row.instance.GetComponentsInChildren<NativeItemPrefabQuality>(true).Length == 1, "source quality producer " + i);
                        string image = "module-world-fx-" + vehicle + "-" + i + ".png";
                        ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, image)); screenshots.Add(image);
                    }
                }
                if(collectionEnd<0 && required.All(kind=>fx.records.Skip(baseline).Take(observed-baseline).Any(row=>row.effect==kind))) collectionEnd=observed;
                var rows = fx.records.Skip(baseline).Take(collectionEnd<0?fx.records.Count-baseline:collectionEnd-baseline).ToArray();
                if (required.All(kind => rows.Any(row => row.effect == kind)) && rows.All(row => Time.time > row.scaledTime + row.lifetime + .2f)) break;
            }
            var events = fx.records.Skip(baseline).Take(collectionEnd<0?fx.records.Count-baseline:collectionEnd-baseline).ToArray();
            foreach (string kind in required) check(events.Any(row => row.effect == kind), "natural production event observed " + kind);
            check(events.Length>0 && events.All(row=>Time.time>row.scaledTime+row.lifetime+.2f && !row.instance), "all collected actual source instances reach lifetime expiry within observation budget");
            foreach (var row in events) if (Time.time > row.scaledTime + row.lifetime + .2f)
                check(!row.instance, "source lifetime removes actual instance " + row.instanceId);
            check(events.Length > 0 && motor.FixedStepCount > 0 && phase != Phase.Menu, "real scene simulation");
            motor.ResetVehicleModule();
            check(motor.ModuleCharge == 0 && motor.ModuleStage == 0 && !motor.ModuleReady, "module reset state");
            FinishModuleWorldFx(vehicle, passed, checks.ToArray(), events, screenshots.ToArray());
        }
        void FinishModuleWorldFx(string vehicle, bool passed, string[] checks, NativeVehicleModuleEffects.SpawnRecord[] events, string[] screenshots)
        {
            File.WriteAllText(Path.Combine(reportDirectory, "module-world-fx-" + vehicle + "-report.json"), JsonUtility.ToJson(new ModuleWorldFxReport {
                passed=passed, accepted=false, vehicle=vehicle, track=trackId,
                scope="Natural QuickRace AI simulation; collect until all required event kinds first appear, then observe collected instances to expiry. Timeout or missing binding/events/lifetime fails. Original pixels, quality0/1/2 and pause timing remain separate.",
                checks=checks, events=events, screenshots=screenshots },true));
            Debug.Log("NATIVE_MODULE_WORLD_FX "+(passed?"PASS":"FAIL")+" vehicle="+vehicle);
            Application.Quit(passed?0:2);
        }
    }
}
