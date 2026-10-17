using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeTrackContentVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Report
        {
            public string version, trackId;
            public bool passed;
            public Check[] checks;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            void Add(string name, bool passed, string detail) =>
                checks.Add(new Check { name = name, passed = passed, detail = detail });

            var boxes = UnityEngine.Object.FindObjectsOfType<NativeKartItemBox>(true);
            var randomBoxes = boxes.Where(box => !box.isFixed).ToArray();
            var fixedBoxes = boxes.Where(box => box.isFixed).ToArray();
            var falls = UnityEngine.Object.FindObjectsOfType<NativeTrackFallVolume>(true);
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            var colliders = UnityEngine.Object.FindObjectsOfType<Collider>(true);
            int routeCount = race.trackId == "forest" ? 51 : race.trackId == "purplefield" ? 46 : race.trackId == "wintermistylake" ? 84 : 97;
            int gateCount = race.trackId == "forest" ? 19 : race.trackId == "purplefield" ? 21 : race.trackId == "wintermistylake" ? 33 : 29;
            int boxCount = race.trackId == "forest" ? 25 : race.trackId == "purplefield" ? 33 : race.trackId == "wintermistylake" ? 41 : 34;
            int fallCount = race.trackId == "forest" ? 0 : race.trackId == "purplefield" ? 2 : race.trackId == "wintermistylake" ? 3 : 31;
            var portals = UnityEngine.Object.FindObjectsOfType<NativeTrackTeleportVolume>(true);
            Add("source-route", race.route.Length == routeCount, "route=" + race.route.Length);
            Add("source-gates", race.gates.Length == gateCount && race.gateColliders.Length == gateCount
                && race.gateColliders.All(c => c && c.isTrigger), "gates=" + race.gates.Length);
            Add("source-boxes", randomBoxes.Length == boxCount, "randomBoxes=" + randomBoxes.Length);
            Add("source-fixed-boxes", fixedBoxes.Length == (race.trackId == "wintermistylake" ? 3 : 0)
                && fixedBoxes.All(box => box.fixedKind.ToString() == "DriftPlusPowerup"
                    && box.mesh && box.GetComponent<Collider>() && box.GetComponent<Collider>().isTrigger),
                "fixedBoxes=" + fixedBoxes.Length + " items=" + string.Join(",", fixedBoxes.Select(box => box.fixedKind.ToString())));
            Add("source-falls", falls.Length == fallCount && falls.All(v => v.GetComponent<Collider>().isTrigger),
                "falls=" + falls.Length + " water=" + falls.Count(v => v.water) + " frozen=" + falls.Count(v => v.frozen));
            Add("source-portals", portals.Length == (race.trackId == "wintermistylake" ? 2 : 0)
                && portals.All(p => p.target && p.GetComponent<Collider>().isTrigger), "portals=" + portals.Length);
            Add("rendered-collision-scene", renderers.Length > 100 && colliders.Length > 100,
                "renderers=" + renderers.Length + " colliders=" + colliders.Length);
            Add("restored-materials", renderers.Any(r => r.sharedMaterials.Any(m => m && m.shader
                && m.shader.name.StartsWith("Fumo/Restored", StringComparison.Ordinal))), "Source renderers use restored shaders");
            Add("restored-lightmaps", race.trackId == "purplefield" ? LightmapSettings.lightmaps.Length == 0
                : LightmapSettings.lightmaps.Length > 0 && renderers.Any(r => r.lightmapIndex >= 0 && r.lightmapIndex < LightmapSettings.lightmaps.Length),
                "lightmaps=" + LightmapSettings.lightmaps.Length);

            var motor = race.racers[0];
            var recovery = motor.GetComponent<NativeKartRecovery>();
            var body = motor.GetComponent<CharacterController>();
            var savedMotor = race.racers.Select(r => r.enabled).ToArray();
            var drivers = race.racers.Select(r => r.GetComponent<NativeKartOriginalAI>()).ToArray();
            var savedDrivers = drivers.Select(d => d.enabled).ToArray();
            float savedScale = Time.timeScale;
            bool savedCamera = race.AllowCameraVerificationUpdates;
            var blocked = colliders.Where(c => c && c != body && c.enabled && !c.isTrigger).ToArray();
            var fallColliders = falls.Select(v => v.GetComponent<Collider>()).ToArray();
            var fallEnabled = fallColliders.Select(c => c.enabled).ToArray();
            try
            {
                if (!recovery || !body) throw new InvalidOperationException("Track recovery components missing");
                foreach (var racer in race.racers) racer.enabled = false;
                foreach (var driver in drivers) driver.enabled = false;
                // Clear solid scenery for the isolated entry sweep; the original fall collider stays live.
                foreach (var collider in blocked) collider.enabled = false;
                race.AllowCameraVerificationUpdates = true;
                foreach (var volume in falls.Where(v => v.gameObject.activeInHierarchy).GroupBy(v => v.frozen ? "frozen" : v.water ? "water" : "land").Select(g => g.First()))
                {
                    string kind = volume.frozen ? "frozen" : volume.water ? "water" : "land";
                    if (!volume) { Add(kind + "-source-volume", false, "No active source volume"); continue; }
                    race.ResetRace(false);
                    race.SetVisualCapture("race", true);
                    Time.timeScale = 1f;
                    var trigger = volume.GetComponent<Collider>();
                    var otherFalls = falls.Where(v => v != volume && v.GetComponent<Collider>().enabled)
                        .Select(v => v.GetComponent<Collider>()).ToArray();
                    foreach (var other in otherFalls) other.enabled = false;
                    var bounds = trigger.bounds;
                    var direction = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                    float extent = Vector3.Dot(bounds.extents, direction);
                    Vector3 outside = bounds.center + direction * (extent + body.radius + 1f);
                    Vector3 inside = bounds.center;
                    motor.ResetAt(outside, Quaternion.identity, false);
                    motor.driving = true;
                    Physics.SyncTransforms();
                    CollisionFlags move = body.Move(inside - outside);
                    for (int i = 0; i < 12 && !recovery.Pending; i++) yield return new WaitForFixedUpdate();
                    foreach (var other in otherFalls) other.enabled = true;
                    Add(kind + "-character-controller-trigger", recovery.Pending,
                        "volume=" + volume.name + " position=" + motor.transform.position + " target=" + inside
                        + " move=" + move + " ignoredLayers="
                        + Physics.GetIgnoreLayerCollision(body.gameObject.layer, trigger.gameObject.layer)
                        + " requests=" + recovery.State.Requests);
                    if (!recovery.Pending) continue;
                    if (volume.frozen) Add("frozen-before-recovery", motor.HitFrozen && recovery.FrozenFall,
                        "hitFrozen=" + motor.HitFrozen + " frozenFall=" + recovery.FrozenFall);
                    Vector3 before = motor.transform.position;
                    Add(kind + "-delayed-fall", recovery.State.Teleports == 0, "Before callback=" + before);
                    for (int i = 0; i < 240 && recovery.Pending; i++) yield return null;
                    Vector3 checkpoint = race.gates[0].position;
                    Vector3 forward = Vector3.ProjectOnPlane(race.gates[0].forward, Vector3.up).normalized;
                    Add(kind + "-checkpoint-recovery", recovery.State.Teleports == 1 && motor.Resets == 1
                        && Vector3.Distance(motor.transform.position, checkpoint) < .01f
                        && Quaternion.Angle(motor.transform.rotation, Quaternion.LookRotation(forward)) < .1f,
                        "teleports=" + recovery.State.Teleports + " resets=" + motor.Resets
                        + " position=" + motor.transform.position + " checkpoint=" + checkpoint);
                }
                var waterVolume = falls.FirstOrDefault(v => v.water && v.gameObject.activeInHierarchy);
                if (waterVolume && race.vehicleRoster)
                {
                    var savedStats = motor.stats;
                    try
                    {
                        motor.stats = race.vehicleRoster.vehicles.First(v => v.settings._isWaterProof).settings;
                        race.ResetRace(false);
                        race.SetVisualCapture("race", true);
                        Time.timeScale = 1f;
                        var bounds = waterVolume.GetComponent<Collider>().bounds;
                        Vector3 outside = bounds.center + Vector3.right * (bounds.extents.x + body.radius + 1f);
                        motor.ResetAt(outside, Quaternion.identity, false);
                        motor.driving = true;
                        Physics.SyncTransforms();
                        body.Move(bounds.center - outside);
                        for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                        Add("waterproof-source-water-entry", !recovery.Pending && recovery.State.Requests == 0,
                            "waterproof=" + motor.Waterproof + " requests=" + recovery.State.Requests);
                    }
                    finally { motor.stats = savedStats; }
                }
                if (falls.Any(v => v.frozen)) Add("unfrozen-after-recovery", !motor.HitFrozen && !recovery.FrozenFall,
                    "hitFrozen=" + motor.HitFrozen + " frozenFall=" + recovery.FrozenFall);
                foreach (var collider in fallColliders) collider.enabled = false;
                foreach (var portal in portals)
                {
                    race.ResetRace(false);
                    race.SetVisualCapture("race", true);
                    Time.timeScale = 1f;
                    var trigger = portal.GetComponent<Collider>();
                    var bounds = trigger.bounds;
                    Vector3 direction = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                    Vector3 outside = bounds.center + direction * (Vector3.Dot(bounds.extents, direction) + body.radius + 1f);
                    motor.ResetAt(outside, Quaternion.identity, false);
                    motor.driving = true;
                    int previous = portal.Teleports;
                    Physics.SyncTransforms();
                    body.Move(bounds.center - outside);
                    for (int i = 0; i < 12 && portal.Teleports == previous; i++) yield return new WaitForFixedUpdate();
                    Add("portal-" + portal.name, portal.Teleports == previous + 1
                        && Vector3.Distance(motor.transform.position, portal.target.position) < .01f
                        && Quaternion.Angle(motor.transform.rotation, portal.target.rotation) < .1f
                        && motor.Resets == 0 && !motor.HitFrozen && (!portal.portalToOpen || portal.portalToOpen.open),
                        "teleports=" + portal.Teleports + " position=" + motor.transform.position + " target=" + portal.target.position);
                }
                Add("construction-muted", AudioListener.volume == 0f, "volume=" + AudioListener.volume);
                Add("runtime-errors", race.VisualErrorCount == 0, "errors=" + race.VisualErrorCount);
            }
            finally
            {
                if (recovery) recovery.ResetState();
                foreach (var collider in blocked) if (collider) collider.enabled = true;
                for (int i = 0; i < fallColliders.Length; i++)
                    if (fallColliders[i]) fallColliders[i].enabled = fallEnabled[i];
                for (int i = 0; i < race.racers.Length; i++)
                {
                    race.racers[i].enabled = savedMotor[i];
                    drivers[i].enabled = savedDrivers[i];
                }
                Time.timeScale = savedScale;
                race.AllowCameraVerificationUpdates = savedCamera;
            }
            var report = new Report
            {
                version = Application.version, trackId = race.trackId,
                passed = checks.All(c => c.passed), checks = checks.ToArray()
            };
            File.WriteAllText(Path.Combine(directory, "track-content-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_TRACK_CONTENT_RESULT passed=" + report.passed + " checks=" + checks.Count);
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
