#if UNITY_6000_0_OR_NEWER
using PhysicMaterial = UnityEngine.PhysicsMaterial;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartSurfaceParticlesVerification : MonoBehaviour
    {
        public sealed class Result { public bool passed; }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase, surfaceKey, dirtKey;
            public int fixedStepCount, fixedStepDelta, speedParticles, waterParticles, dirtParticles;
            public int speedStarts, speedStops, waterStarts, waterStops, cachedDirtCount;
            public bool grounded, onGrass, inWater, jumped, speedActive, waterActive;
            public float speedRatio, horizontalSpeed, verticalSpeed;
            public Vector3 position;
        }
        [Serializable] sealed class Image
        {
            public string name, file, surfaceKey, dirtKey;
            public int fixedStepCount, speedParticles, waterParticles, dirtParticles;
            public bool grounded;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public int width, height;
            public float fixedDeltaTime, speedThreshold;
            public Check[] checks;
            public Sample[] samples;
            public Image[] images;
        }

        public static IEnumerator Run(NativeRace race, string directory, Result result)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var images = new List<Image>();
            var fixtures = new List<GameObject>();
            var materials = new List<Material>();
            bool[] enabledBefore = race.racers.Select(r => r.enabled).ToArray();
            bool[] controllerBefore = race.racers.Select(r => r.GetComponent<CharacterController>().enabled).ToArray();
            float timeScaleBefore = Time.timeScale;
            var camera = race.raceCamera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            GameObject probe = null;
            PhysicMaterial unknown = null;
            var tick = new WaitForFixedUpdate();
            try
            {
                race.SetVisualCapture("race", false);
                foreach (var racer in race.racers)
                {
                    racer.enabled = false;
                    racer.GetComponent<CharacterController>().enabled = false;
                }
                var expectedKeys = new[] { "Grass", "OrangeGrass", "RetroGrass", "Snow" };
                Add("four-racer-surface-bindings", race.racers.Length == 4 && race.racers.All(r =>
                {
                    var p = r.GetComponent<NativeKartSurfaceParticles>();
                    return p && p.speed && p.water && p.dirtPoint && p.surfaces != null
                        && p.surfaces.Length == 4 && expectedKeys.All(key => p.surfaces.Any(s =>
                            s != null && s.key == key && s.material && s.prefab));
                }), "Four racers bind speed, water, dirt point and four original named surfaces");
                var source = race.racers[0].GetComponent<NativeKartSurfaceParticles>();
                var sourceSurfaces = source && source.surfaces != null
                    ? source.surfaces : new NativeSurfaceParticleBinding[0];
                Add("surface-physic-material-identity", race.racers.All(r =>
                {
                    var p = r.GetComponent<NativeKartSurfaceParticles>();
                    return p && p.surfaces != null && p.surfaces.Length == 4
                        && p.surfaces.All(s => s != null && s.material && s.material.name == s.key
                            && r.feedbackSurfaces.Any(f => f.material == s.material));
                }), "Dirt binding keys match four original PhysicMaterial names and each racer's collision map");
                Add("dirt-prefab-structure", sourceSurfaces != null && sourceSurfaces.Length == 4
                    && sourceSurfaces.All(s => s != null && s.prefab
                        && s.prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 5
                        && s.prefab.GetComponentsInChildren<ParticleSystem>(true).All(system =>
                            system.main.loop && !system.main.playOnAwake && !system.main.useUnscaledTime)),
                    "Each original grass effect has one root plus four looping child particle systems");
                var roots = race.racers.SelectMany(r =>
                {
                    var p = r.GetComponent<NativeKartSurfaceParticles>();
                    return p ? new[] { p.speed, p.water } : new ParticleSystem[0];
                }).Where(p => p).Select(p => p.gameObject)
                    .Concat(sourceSurfaces.Where(s => s != null && s.prefab).Select(s => s.prefab.gameObject)).ToArray();
                var renderers = roots.SelectMany(root => root.GetComponentsInChildren<ParticleSystemRenderer>(true)).ToArray();
                Add("surface-particle-shaders-supported", renderers.Length > 0 && renderers.All(r =>
                    r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)),
                    "Every speed, water and dirt renderer has a supported assigned shader; renderers=" + renderers.Length);
                Add("surface-effects-silent", roots.All(root => root.GetComponentsInChildren<AudioSource>(true).Length == 0)
                    && AudioListener.volume == 0f && race.menuPresentation.soundtrack
                    && !race.menuPresentation.soundtrack.enabled && !race.menuPresentation.soundtrack.isPlaying,
                    "All effect roots and descendants have no AudioSource; global audio is muted");

                probe = Instantiate(race.racers[0].gameObject);
                probe.name = "Surface particles verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                var effects = probe.GetComponent<NativeKartSurfaceParticles>();
                var wheels = probe.GetComponent<NativeKartWheelVisual>();
                motor.enabled = true;
                var grass = sourceSurfaces.First(s => s.key == "Grass").material;
                var orange = sourceSurfaces.First(s => s.key == "OrangeGrass").material;
                var retro = sourceSurfaces.First(s => s.key == "RetroGrass").material;
                var snow = sourceSurfaces.First(s => s.key == "Snow").material;
                unknown = new PhysicMaterial("UnknownGrass");
                Vector3 road = new Vector3(0f, 500f, 0f);
                Plane("Surface road", road, new Vector3(300f, 1f, 1600f), "Untagged", null, new Color(.22f, .25f, .29f));
                Plane("Grass", new Vector3(1000f, 500f, -20f), new Vector3(120f, 1f, 100f), "Grass", grass, new Color(.24f, .40f, .18f));
                Plane("Grass exit road", new Vector3(1000f, 500f, 100f), new Vector3(120f, 1f, 140f), "Untagged", null, new Color(.25f, .25f, .25f));
                Plane("Water", new Vector3(2000f, 500f, -20f), new Vector3(120f, 1f, 100f), "Water", grass, new Color(.10f, .35f, .53f));
                Plane("Water exit road", new Vector3(2000f, 500f, 100f), new Vector3(120f, 1f, 140f), "Untagged", null, new Color(.25f, .25f, .25f));
                Plane("Unknown grass", new Vector3(3000f, 500f, -20f), new Vector3(120f, 1f, 100f), "Grass", unknown, new Color(.35f, .35f, .15f));
                Plane("OrangeGrass", new Vector3(4000f, 500f, 0f), new Vector3(120f, 1f, 140f), "Grass", orange, new Color(.58f, .32f, .11f));
                Plane("RetroGrass", new Vector3(5000f, 500f, 0f), new Vector3(120f, 1f, 140f), "Grass", retro, new Color(.21f, .49f, .23f));
                Plane("Snow", new Vector3(6000f, 500f, 0f), new Vector3(120f, 1f, 140f), "Grass", snow, new Color(.81f, .85f, .90f));
                Plane("Grass switch source", new Vector3(7000f, 500f, -30f), new Vector3(120f, 1f, 100f), "Grass", grass, new Color(.24f, .40f, .18f));
                Plane("Orange switch target", new Vector3(7000f, 500f, 70f), new Vector3(120f, 1f, 100f), "Grass", orange, new Color(.58f, .32f, .11f));
                Plane("Unknown switch target", new Vector3(7000f, 500f, 170f), new Vector3(120f, 1f, 100f), "Grass", unknown, new Color(.35f, .35f, .15f));
                Plane("Switch exit road", new Vector3(7000f, 500f, 270f), new Vector3(120f, 1f, 100f), "Untagged", null, new Color(.25f, .25f, .25f));
                Plane("Water switch source", new Vector3(8000f, 500f, -30f), new Vector3(120f, 1f, 100f), "Water", grass, new Color(.10f, .35f, .53f));
                Plane("Grass switch middle", new Vector3(8000f, 500f, 70f), new Vector3(120f, 1f, 100f), "Grass", grass, new Color(.24f, .40f, .18f));
                Plane("Water switch target", new Vector3(8000f, 500f, 170f), new Vector3(120f, 1f, 100f), "Water", grass, new Color(.10f, .35f, .53f));
                Plane("Water switch exit road", new Vector3(8000f, 500f, 270f), new Vector3(120f, 1f, 100f), "Untagged", null, new Color(.25f, .25f, .25f));
                Physics.SyncTransforms();
                Time.timeScale = 1f;

                Reset(road + new Vector3(0f, .4f, -120f), 0f);
                yield return Drive("road-settle", 35);
                Add("road-grounded-without-dirt", motor.Grounded && !motor.OnGrass && !motor.InWater
                    && !effects.WaterActive && effects.ActiveDirtKey == null && effects.CachedDirtCount == 0,
                    "Road collision starts with no water or dirt effect");
                motor.throttle = 1f;
                var below = samples.Last();
                for (int i = 0; i < 350 && effects.SpeedRatio < .675f; i++) yield return Drive("road-accelerate-below", 1);
                below = samples.Last();
                Add("speed-below-threshold-off", motor.Grounded && below.speedRatio < .688f
                    && !below.speedActive && below.speedParticles == 0,
                    "fixedStepCount=" + below.fixedStepCount + "; horizontal ratio=" + below.speedRatio);
                for (int i = 0; i < 120 && !effects.SpeedActive; i++) yield return Drive("road-threshold-cross", 1);
                var above = samples.Last();
                Add("speed-above-threshold-on", above.speedRatio >= .688f && above.speedActive
                    && above.speedStarts == 1 && above.fixedStepDelta == 1,
                    "fixedStepCount=" + above.fixedStepCount + "; horizontal ratio=" + above.speedRatio);
                yield return Drive("road-speed-visible", 20);
                yield return Capture("speed-high", () => effects.SpeedActive && ParticleCount(effects.speed) > 0);
                Add("speed-horizontal-ratio", samples.Where(s => s.phase.StartsWith("road-")).All(s =>
                    Mathf.Abs(s.speedRatio - s.horizontalSpeed / motor.stats._topSpeed) < .0001f),
                    "Recorded speed ratio equals horizontal DriveVelocity km/h divided by original top speed");
                motor.throttle = 0f;
                motor.braking = true;
                for (int i = 0; i < 250 && effects.SpeedActive; i++) yield return Drive("road-brake-below", 1);
                var brake = samples.Last();
                Add("speed-falls-below-threshold", brake.speedRatio < .688f && !brake.speedActive
                    && brake.speedStops >= 1 && brake.fixedStepDelta == 1,
                    "fixedStepCount=" + brake.fixedStepCount + "; horizontal ratio=" + brake.speedRatio);

                Reset(new Vector3(1000f, 500.4f, -40f), 1f);
                yield return Drive("grass-entry", 45);
                yield return new WaitForEndOfFrame();
                Add("grass-ground-contact", motor.Grounded && motor.OnGrass && !motor.InWater
                    && motor.CurrentSurfaceKey == "Grass", "CharacterController contacted Grass PhysicMaterial");
                Add("grass-dirt-live", effects.ActiveDirtKey == "Grass" && effects.ActiveDirt
                    && ParticleCount(effects.ActiveDirt) > 0 && effects.CachedDirtCount == 1,
                    "Grass effect created by collision, particle count=" + ParticleCount(effects.ActiveDirt));
                yield return Capture("grass-dirt", () => effects.ActiveDirtKey == "Grass"
                    && ParticleCount(effects.ActiveDirt) > 0);
                int pausedStep = motor.FixedStepCount;
                float pausedTime = effects.ActiveDirt ? effects.ActiveDirt.time : -1f;
                int pausedCount = ParticleCount(effects.ActiveDirt);
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(.6f);
                Add("pause-freezes-surface-particles", motor.FixedStepCount == pausedStep
                    && effects.ActiveDirt && Mathf.Approximately(effects.ActiveDirt.time, pausedTime)
                    && ParticleCount(effects.ActiveDirt) == pausedCount,
                    "Pause preserves fixed step, dirt simulation time and live particle count");
                Time.timeScale = 1f;
                motor.throttle = 0f;
                motor.driving = false;
                yield return Drive("grass-stopped", 180);
                Add("grass-idle-keeps-emitter", motor.Grounded && motor.OnGrass
                    && effects.ActiveDirtKey == "Grass" && !effects.WaterActive,
                    "Offroad dirt has no speed or driving gate; horizontal speed=" + HorizontalSpeed(motor));
                motor.driving = true;
                motor.throttle = 1f;
                motor.jump = true;
                yield return Drive("grass-jump", 2);
                Add("jump-stops-dirt", !motor.Grounded && effects.ActiveDirtKey == null
                    && !effects.WaterActive && motor.JumpedThisStep == false,
                    "CharacterController airborne state stops dirt emission");
                for (int i = 0; i < 100 && !motor.Grounded; i++) yield return Drive("grass-descent", 1);
                Add("landing-restores-grass-dirt", motor.Grounded && motor.OnGrass
                    && effects.ActiveDirtKey == "Grass" && effects.CachedDirtCount == 1,
                    "Landing reuses cached Grass effect");
                for (int i = 0; i < 600 && motor.OnGrass; i++) yield return Drive("grass-to-road", 1);
                Add("grass-road-transition", motor.Grounded && !motor.OnGrass && !motor.InWater
                    && effects.ActiveDirtKey == null && !effects.WaterActive,
                    "The kart drove across the Grass to road boundary");

                Reset(new Vector3(2000f, 500.4f, -40f), 1f);
                yield return Drive("water-entry", 45);
                yield return new WaitForEndOfFrame();
                Add("water-ground-contact", motor.Grounded && motor.InWater && motor.OnGrass,
                    "CharacterController Water contact also sets the original offroad flag");
                Add("water-splash-live", effects.WaterActive && ParticleCount(effects.water) > 0
                    && effects.ActiveDirtKey == null,
                    "Water effect emits from real contact; particle count=" + ParticleCount(effects.water));
                yield return Capture("water-splash", () => effects.WaterActive && ParticleCount(effects.water) > 0);
                motor.driving = false;
                motor.throttle = 0f;
                yield return Drive("water-stopped", 180);
                Add("water-idle-keeps-emitter", motor.Grounded && motor.InWater && effects.WaterActive,
                    "Water playback stays active without a speed or driving gate; distance emission needs movement");
                motor.driving = true;
                motor.throttle = 1f;
                motor.jump = true;
                yield return Drive("water-jump", 2);
                Add("jump-stops-water", !motor.Grounded && !effects.WaterActive,
                    "CharacterController airborne state stops water emission");
                for (int i = 0; i < 100 && !motor.Grounded; i++) yield return Drive("water-descent", 1);
                Add("landing-restores-water", motor.Grounded && motor.InWater && effects.WaterActive,
                    "Landing restarts water effect");
                for (int i = 0; i < 600 && motor.InWater; i++) yield return Drive("water-to-road", 1);
                Add("water-road-transition", motor.Grounded && !motor.OnGrass && !motor.InWater
                    && !effects.WaterActive && effects.ActiveDirtKey == null,
                    "The kart drove across the Water to road boundary");

                Reset(new Vector3(3000f, 500.4f, -40f), 0f);
                yield return Drive("unknown-grass", 40);
                Add("unknown-grass-no-fallback", motor.Grounded && motor.OnGrass
                    && motor.CurrentSurfaceKey == null && effects.ActiveDirtKey == null
                    && effects.CachedDirtCount == 0,
                    "Unmapped Grass PhysicMaterial does not select a dirt prefab");
                foreach (var key in new[] { "OrangeGrass", "RetroGrass", "Snow" })
                {
                    float x = key == "OrangeGrass" ? 4000f : key == "RetroGrass" ? 5000f : 6000f;
                    Reset(new Vector3(x, 500.4f, -35f), 1f);
                    yield return Drive(key + "-entry", 40);
                    Add(key + "-own-prefab", motor.Grounded && motor.OnGrass
                        && motor.CurrentSurfaceKey == key && effects.ActiveDirtKey == key
                        && effects.ActiveDirt && ParticleCount(effects.ActiveDirt) > 0
                        && effects.CachedDirtCount == 1,
                        "Matched original surface starts its named dirt effect");
                }
                Reset(new Vector3(7000f, 500.4f, -55f), 1f);
                yield return Drive("material-switch-grass", 35);
                Add("material-switch-starts-grass", motor.Grounded && motor.CurrentSurfaceKey == "Grass"
                    && effects.ActiveDirtKey == "Grass" && effects.CachedDirtCount == 1,
                    "First mapped offroad contact starts Grass effect");
                for (int i = 0; i < 600 && motor.CurrentSurfaceKey != "OrangeGrass"; i++)
                    yield return Drive("material-switch-orange", 1);
                Add("continuous-grass-switch-caches-without-restarting", motor.Grounded
                    && motor.OnGrass && motor.CurrentSurfaceKey == "OrangeGrass"
                    && effects.CachedDirtCount == 2 && effects.ActiveDirtKey == "Grass",
                    "Continuous offroad material change caches OrangeGrass while Grass remains active");
                for (int i = 0; i < 600 && motor.CurrentSurfaceKey != null; i++)
                    yield return Drive("material-switch-unknown", 1);
                Add("continuous-unknown-grass-keeps-current", motor.Grounded && motor.OnGrass
                    && motor.CurrentSurfaceKey == null && effects.ActiveDirtKey == "Grass"
                    && effects.CachedDirtCount == 2,
                    "Unknown Grass contact preserves the active dirt effect until an offroad exit");
                for (int i = 0; i < 600 && motor.OnGrass; i++) yield return Drive("material-switch-road", 1);
                Add("material-switch-road-stops-dirt", motor.Grounded && !motor.OnGrass
                    && effects.ActiveDirtKey == null && effects.CachedDirtCount == 2,
                    "Road exit stops the held Grass effect while cache survives");
                Reset(new Vector3(8000f, 500.4f, -55f), 1f);
                yield return Drive("water-grass-start", 35);
                Add("water-grass-starts-water", motor.Grounded && motor.InWater
                    && effects.WaterActive && effects.ActiveDirtKey == null,
                    "Water lane begins with water emission");
                for (int i = 0; i < 600 && (!motor.OnGrass || motor.InWater); i++)
                    yield return Drive("water-grass-transition", 1);
                Add("water-to-grass-switch", motor.Grounded && motor.OnGrass && !motor.InWater
                    && !effects.WaterActive && effects.ActiveDirtKey == "Grass"
                    && effects.CachedDirtCount == 1,
                    "Continuous offroad water-to-grass contact stops splash and starts dirt");
                for (int i = 0; i < 600 && !motor.InWater; i++)
                    yield return Drive("grass-water-transition", 1);
                Add("grass-to-water-switch", motor.Grounded && motor.InWater && motor.OnGrass
                    && effects.WaterActive && effects.ActiveDirtKey == null
                    && effects.CachedDirtCount == 1,
                    "Continuous offroad grass-to-water contact stops dirt and restarts splash");
                Reset(new Vector3(1000f, 500.4f, -40f), 0f);
                yield return Drive("reset-grass-rebuild", 35);
                Add("reset-rebuilds-cache", effects.ActiveDirtKey == "Grass" && effects.CachedDirtCount == 1,
                    "One mapped contact reconstructs one cache entry after reset");
                motor.ResetAt(road + new Vector3(0f, .4f, -120f), Quaternion.identity, false);
                Add("reset-clears-effects-and-cache", !effects.SpeedActive && !effects.WaterActive
                    && effects.ActiveDirtKey == null && effects.ActiveDirt == null
                    && effects.CachedDirtCount == 0 && effects.SpeedStarts == 0
                    && effects.SpeedStops == 0 && effects.WaterStarts == 0 && effects.WaterStops == 0
                    && ParticleCount(effects.speed) == 0 && ParticleCount(effects.water) == 0,
                    "Motor reset immediately clears active effects, cache, counters and living particles");
                effects.UpdateSpeed(145f * .6879f, 145f);
                bool belowBoundary = !effects.SpeedActive && effects.SpeedStarts == 0;
                effects.UpdateSpeed(.688f, 1f);
                bool atBoundary = effects.SpeedActive && effects.SpeedStarts == 1;
                effects.ResetState();
                effects.UpdateSpeed(145f * .688f + .0001f, 145f);
                bool originalTopSpeed = effects.SpeedActive;
                effects.ResetState();
                effects.UpdateSpeed(200f * .688f + .0001f, 200f);
                bool otherTopSpeed = effects.SpeedActive && effects.SpeedRatio >= .688f;
                effects.ResetState();
                Add("component-speed-threshold-boundary", belowBoundary && atBoundary
                    && originalTopSpeed && otherTopSpeed,
                    "Component boundary: .6879 below, exact .688 at unit base, above for 145 and 200 km/h base top speeds");
                Add("fixed-step-event-record", samples.Count > 200
                    && samples.All(s => s.fixedStepDelta == 1)
                    && samples.Any(s => s.speedActive && s.speedRatio >= .688f)
                    && samples.Any(s => s.surfaceKey == "Grass" && s.dirtKey == "Grass")
                    && samples.Any(s => s.inWater && s.waterActive)
                    && samples.Any(s => s.onGrass && s.surfaceKey == null && s.dirtKey == null),
                    "All speed and surface changes were observed after one real motor fixed step");
                Add("visible-live-captures", images.Count == 3 && images.All(i =>
                    File.Exists(Path.Combine(directory, i.file)) && i.fixedStepCount > 0)
                    && images[0].speedParticles > 0 && images[1].dirtParticles > 0
                    && images[2].waterParticles > 0,
                    "Three 1280x720 captures contain naturally simulated speed, dirt and water particles");
                Add("silence-persists", AudioListener.volume == 0f
                    && !race.menuPresentation.soundtrack.enabled
                    && !race.menuPresentation.soundtrack.isPlaying,
                    "Global audio stayed muted through all live events");
                Add("runtime-errors", race.VisualErrorCount == 0,
                    "Player error count=" + race.VisualErrorCount);

                void Plane(string name, Vector3 center, Vector3 size, string tag, PhysicMaterial physics,
                    Color color)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = name;
                    ground.tag = tag;
                    ground.transform.position = center - Vector3.up * .5f;
                    ground.transform.localScale = size;
                    ground.GetComponent<Collider>().sharedMaterial = physics;
                    var material = ground.GetComponent<Renderer>().material;
                    material.color = color;
                    fixtures.Add(ground);
                    materials.Add(material);
                }
                void Reset(Vector3 position, float throttle)
                {
                    motor.ResetAt(position, Quaternion.identity, false);
                    wheels.ResetState();
                    motor.driving = true;
                    motor.throttle = throttle;
                    motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                    Time.timeScale = 1f;
                }
                IEnumerator Drive(string label, int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int before = motor.FixedStepCount;
                        yield return tick;
                        motor.PrepareRenderPose(false);
                        samples.Add(new Sample
                        {
                            phase = label, fixedStepCount = motor.FixedStepCount,
                            fixedStepDelta = motor.FixedStepCount - before,
                            position = motor.transform.position, grounded = motor.Grounded,
                            onGrass = motor.OnGrass, inWater = motor.InWater,
                            jumped = motor.JumpedThisStep, surfaceKey = motor.CurrentSurfaceKey,
                            dirtKey = effects.ActiveDirtKey,
                            speedActive = effects.SpeedActive, waterActive = effects.WaterActive,
                            speedRatio = effects.SpeedRatio,
                            horizontalSpeed = HorizontalSpeed(motor), verticalSpeed = motor.VerticalSpeed,
                            speedParticles = ParticleCount(effects.speed),
                            waterParticles = ParticleCount(effects.water),
                            dirtParticles = ParticleCount(effects.ActiveDirt),
                            speedStarts = effects.SpeedStarts, speedStops = effects.SpeedStops,
                            waterStarts = effects.WaterStarts, waterStops = effects.WaterStops,
                            cachedDirtCount = effects.CachedDirtCount
                        });
                    }
                }
                IEnumerator Capture(string name, Func<bool> visible)
                {
                    yield return new WaitForEndOfFrame();
                    bool live = visible();
                    int speedCount = ParticleCount(effects.speed);
                    int waterCount = ParticleCount(effects.water);
                    int dirtCount = ParticleCount(effects.ActiveDirt);
                    int step = motor.FixedStepCount;
                    string surfaceKey = motor.CurrentSurfaceKey;
                    string dirtKey = effects.ActiveDirtKey;
                    bool grounded = motor.Grounded;
                    Time.timeScale = 0f;
                    bool frozenBefore = wheels.frozen;
                    wheels.frozen = true;
                    motor.PrepareRenderPose(false);
                    camera.transform.position = motor.transform.position + new Vector3(5f, 2.6f, -6f);
                    camera.transform.LookAt(motor.transform.position + Vector3.up * .5f);
                    camera.fieldOfView = 43f;
                    yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    string file = name + ".png";
                    File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
                    Destroy(texture);
                    images.Add(new Image { name = name, file = file, fixedStepCount = step,
                        speedParticles = speedCount, waterParticles = waterCount,
                        dirtParticles = dirtCount, surfaceKey = surfaceKey,
                        dirtKey = dirtKey, grounded = grounded });
                    Add(name + "-captured-live-particles", live,
                        "fixedStepCount=" + step + "; speed=" + speedCount
                        + "; water=" + waterCount + "; dirt=" + dirtCount);
                    wheels.frozen = frozenBefore;
                    Time.timeScale = 1f;
                }
            }
            finally
            {
                Time.timeScale = timeScaleBefore;
                for (int i = 0; i < race.racers.Length; i++)
                {
                    race.racers[i].enabled = enabledBefore[i];
                    race.racers[i].GetComponent<CharacterController>().enabled = controllerBefore[i];
                }
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = cameraFov;
                if (probe) { probe.SetActive(false); Destroy(probe); }
                foreach (var fixture in fixtures) { fixture.SetActive(false); Destroy(fixture); }
                foreach (var material in materials) Destroy(material);
                if (unknown) Destroy(unknown);
            }
            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                boundary = "An exported racer clone runs real CharacterController fixed steps across tagged, PhysicMaterial-bound road, grass, water and unknown grass. Input creates speed, jump, landing, stopping and reset events. Particle counts and 1280x720 screenshots are captured only after natural simulation.",
                passed = checks.Count > 0 && checks.All(c => c.passed),
                width = Screen.width, height = Screen.height, fixedDeltaTime = Time.fixedDeltaTime,
                speedThreshold = .688f, checks = checks.ToArray(), samples = samples.ToArray(),
                images = images.ToArray()
            };
            result.passed = report.passed;
            File.WriteAllText(Path.Combine(directory, "surface-particles-report.json"), JsonUtility.ToJson(report, true));
            void Add(string name, bool passed, string detail) => checks.Add(new Check
            {
                name = name, passed = passed, detail = detail
            });
        }

        static float HorizontalSpeed(NativeKartMotor motor)
        {
            Vector3 velocity = motor.DriveVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        static int ParticleCount(ParticleSystem root)
        {
            if (!root) return 0;
            return root.GetComponentsInChildren<ParticleSystem>(true).Sum(system => system.particleCount);
        }
    }
}
