using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeMountainsVerification
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
            var falls = UnityEngine.Object.FindObjectsOfType<NativeTrackFallVolume>(true);
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            var colliders = UnityEngine.Object.FindObjectsOfType<Collider>(true);
            Add("original-mountains-route", race.trackId == "mountains" && race.route.Length == 97,
                "track=" + race.trackId + " route=" + race.route.Length);
            Add("original-mountains-gates", race.gates.Length == 29 && race.gateColliders.Length == 29
                && race.gateColliders.All(c => c && c.isTrigger), "gates=" + race.gates.Length);
            Add("original-mountains-boxes", boxes.Length == 34, "boxes=" + boxes.Length);
            Add("original-mountains-fall-volumes", falls.Length == 31 && falls.Count(v => v.water) == 1
                && falls.All(v => v.GetComponent<Collider>() && v.GetComponent<Collider>().isTrigger),
                "falls=" + falls.Length + " water=" + falls.Count(v => v.water));
            Add("rendered-collision-scene", renderers.Length > 100 && colliders.Length > 100,
                "renderers=" + renderers.Length + " colliders=" + colliders.Length);
            Add("restored-mountains-materials", renderers.Any(r => r && r.lightmapIndex >= 0
                && r.sharedMaterials.Any(m => m && m.shader && m.shader.name.StartsWith("Fumo/Restored", StringComparison.Ordinal))),
                "A lightmapped source renderer uses the native restored shader");
            Add("restored-mountains-lightmaps", LightmapSettings.lightmaps.Length >= 2
                && renderers.Any(r => r && r.lightmapIndex >= 0 && r.lightmapIndex < LightmapSettings.lightmaps.Length),
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
                if (!recovery || !body) throw new InvalidOperationException("Mountains recovery components missing");
                foreach (var racer in race.racers) racer.enabled = false;
                foreach (var driver in drivers) driver.enabled = false;
                // Clear solid scenery for the isolated entry sweep; the original fall collider stays live.
                foreach (var collider in blocked) collider.enabled = false;
                race.AllowCameraVerificationUpdates = true;
                foreach (bool water in new[] { false, true })
                {
                    string kind = water ? "water" : "land";
                    var volume = falls.FirstOrDefault(v => v.water == water && v.gameObject.activeInHierarchy);
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
            File.WriteAllText(Path.Combine(directory, "mountains-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_MOUNTAINS_RESULT passed=" + report.passed + " checks=" + checks.Count);
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
