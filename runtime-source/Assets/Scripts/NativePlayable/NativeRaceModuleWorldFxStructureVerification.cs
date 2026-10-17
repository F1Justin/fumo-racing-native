using System;using System.Collections;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace Fumo.NativePlayable { public sealed partial class NativeRace {
[Serializable] sealed class ModuleWorldFxStructureReport {public bool passed,structurePassed,accepted,renderPending=true,hardwarePending=true,naturalDrivingPending=true;public string vehicle,track,scope;public string[] checks,screenshots;public NativeVehicleModuleEffects.SpawnRecord[] events,cpuEvents;}
NativeVehicleModuleEffects.SpawnRecord[] structureCpuRecords;
internal IEnumerator CheckModuleWorldFxStructureLive(){var arg=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--module-world-fx-vehicle="));var vehicle=arg==null?"broom":arg.Substring("--module-world-fx-vehicle=".Length);yield return GuardStructureDiagnostic(CheckModuleWorldFxStructureBody(),"module-world-fx-structure-"+vehicle+"-report.json");}
        internal IEnumerator CheckModuleWorldFxStructureBody()
        {
            QualitySettings.vSyncCount=0;
            var args = Environment.GetCommandLineArgs();
            string prefix = "--module-world-fx-vehicle=";
            string vehicle = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal));
            vehicle = vehicle == null ? "broom" : vehicle.Substring(prefix.Length);
            if (vehicle != "broom" && vehicle != "genji" && vehicle != "shoppingcart")
            {
                Directory.CreateDirectory(reportDirectory);
                FinishModuleWorldFxStructure(vehicle, false, new[]{"FAIL unknown source module verification vehicle"}, new NativeVehicleModuleEffects.SpawnRecord[0], new string[0]);
                yield break;
            }
            Directory.CreateDirectory(reportDirectory);
            var checks = new List<string>(); var screenshots = new List<string>(); bool passed = true;
            Action<bool, string> check = (ok, name) => { passed &= ok; checks.Add((ok ? "PASS " : "FAIL ") + name); };
            yield return null;
            check(PrepareHudFixtureQuickRace(vehicle), "real QuickRace roster selection");
            foreach(var seat in raceConfig.roster.Where(seat=>!seat.isHuman))seat.vehicleId=vehicle;
            PrepareModeRoster(raceConfig); ResetRace(true,false);
            SetVisualCapture("race", true); Time.timeScale = 1f; AllowCameraVerificationUpdates = true;
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble);
            enabled=false;
            foreach(var racer in racers){racer.enabled=false;var ai=racer.GetComponent<NativeKartOriginalAI>();if(ai){ai.enabled=false;ai.active=false;}}
            int fixtureCpuIndex=Array.FindIndex(raceConfig.roster.ToArray(),seat=>!seat.isHuman);
            if(fixtureCpuIndex<0)throw new InvalidOperationException("Structure fixture CPU missing");
            var fixture=new GameObject("NativeInjectedModulePhysicsFixture");
            var control=fixture.AddComponent<NativeModuleStructurePhysicsDriver>();
            control.Configure(new[]{racers[0],racers[fixtureCpuIndex]},vehicle);
            var motor = racers[0]; var fx = motor.GetComponent<NativeVehicleModuleEffects>();
            check(fx && fx.binding != null && fx.binding.family == motor.stats._moduleKind, "selected source module FX binding");
            if (!fx || fx.binding == null)
            {
                FinishModuleWorldFxStructure(vehicle, false, checks.ToArray(), new NativeVehicleModuleEffects.SpawnRecord[0], screenshots.ToArray());
                yield break;
            }
            var cpuFx=racers.Where((r,i)=>!raceConfig.roster[i].isHuman).Select(r=>r.GetComponent<NativeVehicleModuleEffects>()).ToArray();
            var cpuBaseline=cpuFx.Select(c=>c.records.Count).ToArray();
            var cpuObserved=(int[])cpuBaseline.Clone();var cpuCollected=new List<NativeVehicleModuleEffects.SpawnRecord>();bool cpuComplete=false;
            int baseline = fx.records.Count, observed = baseline, collectionEnd=-1;
            string[] required = vehicle == "broom" ? new[] { "Ready", "Use" }
                : vehicle == "genji" ? new[] { "Ready", "Use", "UseMax" } : new[] { "Overheat" };
            double deadline = Time.realtimeSinceStartupAsDouble + 180;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                if(!cpuComplete)
                {
                    if(cpuFx.Where((cpu,index)=>cpu.records.Count>cpuObserved[index]).Any())yield return null;
                    for(int cpuIndex=0;cpuIndex<cpuFx.Length;cpuIndex++)
                    {
                        var cpu=cpuFx[cpuIndex];var cpuMotor=cpu.GetComponent<NativeKartMotor>();
                        foreach(var row in cpu.records.Skip(cpuObserved[cpuIndex]))
                        {
                            cpuCollected.Add(row);
                            check(row.instance && row.instance.transform.parent==(row.followsVehicle?cpuMotor.transform:null),"actual CPU source parent "+row.effect);
                            if(row.followsVehicle)check(row.instance && row.instance.transform.localPosition.sqrMagnitude<.000001f && Quaternion.Angle(row.instance.transform.localRotation,Quaternion.identity)<.001f,"actual CPU source zero local pose "+row.effect);
                            var life=row.instance?row.instance.GetComponent<NativeItemPrefabLifetime>():null;
                            check(life && life.StartScheduled && life.duration==(row.effect=="Overheat"?10f:3f),"actual CPU source lifetime "+row.effect);
                        }
                        cpuObserved[cpuIndex]=cpu.records.Count;
                    }
                    cpuComplete=required.All(kind=>cpuCollected.Any(row=>row.effect==kind));
                }
                if (collectionEnd<0 && fx.records.Count > observed)
                {
                    int first = observed; observed = fx.records.Count;
                    yield return null; yield return null;
                    for (int i = first; i < observed; i++)
                    {
                        var row = fx.records[i];
                        check(row.timeScale > 0 && row.motorStep > 0, "production simulation clock " + i);
                        if(row.followsVehicle)check(row.instance && row.instance.transform.localPosition.sqrMagnitude<.000001f && Quaternion.Angle(row.instance.transform.localRotation,Quaternion.identity)<.001f,"source zero local pose " + i);
                        check(row.instance && row.instance.transform.parent == (row.followsVehicle ? motor.transform : null), "source parent " + i);
                        var life = row.instance ? row.instance.GetComponent<NativeItemPrefabLifetime>() : null;
                        check(life && life.StartScheduled && life.duration == (row.effect == "Overheat" ? 10f : 3f), "source scaled lifetime scheduled " + i);
                        check(row.instance && row.instance.GetComponentsInChildren<ParticleSystem>(true).Length > 0, "actual authored particle systems " + i);
                        if (row.effect == "Overheat") check(row.instance && row.instance.GetComponent<NativeItemPrefabQualityScope>()
                            && row.instance.GetComponentsInChildren<NativeItemPrefabQuality>(true).Length == 1, "source quality producer " + i);
                        string image = "module-world-fx-" + vehicle + "-" + i + ".png";
                        screenshots.Add("render-pending:"+image);
                    }
                }
                if(collectionEnd<0 && required.All(kind=>fx.records.Skip(baseline).Take(observed-baseline).Any(row=>row.effect==kind))) collectionEnd=observed;
                var rows = fx.records.Skip(baseline).Take(collectionEnd<0?fx.records.Count-baseline:collectionEnd-baseline).ToArray();
                if (required.All(kind => rows.Any(row => row.effect == kind)) && cpuComplete && cpuCollected.All(row=>Time.time>row.scaledTime+row.lifetime+.2f) && rows.All(row => Time.time > row.scaledTime + row.lifetime + .2f)) break;
            }
            var events = fx.records.Skip(baseline).Take(collectionEnd<0?fx.records.Count-baseline:collectionEnd-baseline).ToArray();
            var cpuEvents=cpuCollected.ToArray(); structureCpuRecords=cpuEvents;
            check(cpuEvents.Length>0 && cpuEvents.All(row=>Time.time>row.scaledTime+row.lifetime+.2f && !row.instance),"CPU collected instances source expiry");
            foreach(string kind in required)check(cpuEvents.Any(row=>row.effect==kind),"actual CPU Motor source event " + kind);
            foreach(var cpu in cpuFx.Where(c=>c))
                foreach(var row in cpu.records.Skip(cpuBaseline[Array.IndexOf(cpuFx,cpu)]))
                    check(row.motorStep>0 && row.timeScale>0 && !string.IsNullOrEmpty(row.sourcePrefabId),"actual CPU source prefab and simulation event " + row.effect);
            foreach (string kind in required) check(events.Any(row => row.effect == kind), "injected-input production Motor event observed " + kind);
            check(events.Length>0 && events.All(row=>Time.time>row.scaledTime+row.lifetime+.2f && !row.instance), "all collected actual source instances reach lifetime expiry within observation budget");
            foreach (var row in events) if (Time.time > row.scaledTime + row.lifetime + .2f)
                check(!row.instance, "source lifetime removes actual instance " + row.instanceId);
            check(events.Length > 0 && motor.FixedStepCount > 0 && phase != Phase.Menu, "real scene simulation");
            motor.ResetVehicleModule();
            check(motor.ModuleCharge == 0 && motor.ModuleStage == 0 && !motor.ModuleReady, "module reset state");
            FinishModuleWorldFxStructure(vehicle, passed, checks.ToArray(), events, screenshots.ToArray());
        }
        void FinishModuleWorldFxStructure(string vehicle, bool passed, string[] checks, NativeVehicleModuleEffects.SpawnRecord[] events, string[] screenshots)
        {
            File.WriteAllText(Path.Combine(reportDirectory, "module-world-fx-structure-" + vehicle + "-report.json"), JsonUtility.ToJson(new ModuleWorldFxStructureReport {
                passed=passed, structurePassed=passed, accepted=false, vehicle=vehicle, track=trackId,
                scope="Injected human/CPU labels, diagnostic road/grass collider placement and public throttle/brake/steer/drift/jump inputs; real fixed-step Motor events; collect until all required event kinds first appear, then observe collected instances to expiry. Timeout or missing binding/events/lifetime fails. Source denominator: three module families, six world-FX events, four original prefabs. Structure only; original pixels, hardware and complete natural-driving acceptance pending.",
                checks=checks, events=events, cpuEvents=structureCpuRecords, screenshots=screenshots },true));
            Debug.Log("NATIVE_MODULE_WORLD_FX "+(passed?"PASS":"FAIL")+" vehicle="+vehicle);
            Application.Quit(passed?0:2);
        }
} }
