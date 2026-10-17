using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartParticlesVerification : MonoBehaviour
    {
        public sealed class Result { public bool passed; }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int fixedStepDelta, flips, landingFlips, driftStarts, driftStops, flipSpawns,
                landingSpawns, activeEffects, driftParticles, flipParticles, landingParticles;
            public bool grounded, drifting, landed, jumped, spinRequested;
            public float particleTime;
            public Vector3 position;
        }
        [Serializable] sealed class Image
        {
            public string name, file;
            public int driftParticles, flipParticles, landingParticles, flips, landingFlips;
            public bool grounded;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public int width, height;
            public float fixedDeltaTime;
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
            var enabledBefore = race.racers.Select(r => r.enabled).ToArray();
            var controllerBefore = race.racers.Select(r => r.GetComponent<CharacterController>().enabled).ToArray();
            float timeScaleBefore = Time.timeScale;
            var camera = race.raceCamera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            GameObject probe = null;
            var tick = new WaitForFixedUpdate();
            try
            {
                race.SetVisualCapture("race", false);
                foreach (var racer in race.racers)
                {
                    racer.enabled = false;
                    racer.GetComponent<CharacterController>().enabled = false;
                }
                Add("four-racer-bindings", race.racers.Length == 4 && race.racers.All(r =>
                {
                    var p = r.GetComponent<NativeKartParticles>();
                    return p && p.drift && p.flipPrefab && p.landingPrefab
                        && Mathf.Approximately(p.flipLifetime, 3f) && Mathf.Approximately(p.landingLifetime, 3f);
                }), "All four motors bind original drift, 360Flip and 360FlipBoost particle roots with three-second lifetimes");
                Add("original-particle-module-structure", race.racers.All(r =>
                {
                    var p = r.GetComponent<NativeKartParticles>();
                    if (!p || !p.drift || !p.flipPrefab || !p.landingPrefab) return false;
                    var driftSystems = p.drift.GetComponentsInChildren<ParticleSystem>(true);
                    var flipSystems = p.flipPrefab.GetComponentsInChildren<ParticleSystem>(true);
                    var landingSystems = p.landingPrefab.GetComponentsInChildren<ParticleSystem>(true);
                    return p.drift.transform.IsChildOf(r.driftPivot) && driftSystems.Length == 13
                        && !driftSystems[0].emission.enabled
                        && driftSystems.Skip(1).All(s => s.emission.enabled)
                        && driftSystems.All(s => s.main.loop && !s.main.playOnAwake
                            && !s.main.useUnscaledTime)
                        && flipSystems.Length == 2 && landingSystems.Length == 2
                        && flipSystems.Concat(landingSystems).All(s => !s.main.loop
                            && s.main.playOnAwake && !s.main.useUnscaledTime && s.emission.enabled)
                        && flipSystems[0].trails.enabled && landingSystems[0].trails.enabled;
                }), "Drift mounts under the vehicle pivot with its silent root and 12 looped scaled emitters; both original two-system flip roots retain emission and trails");
                var renderers = race.racers.SelectMany(r => r.GetComponent<NativeKartParticles>().drift
                    ? r.GetComponent<NativeKartParticles>().drift.GetComponentsInChildren<ParticleSystemRenderer>(true)
                    : new ParticleSystemRenderer[0])
                    .Concat(race.racers.SelectMany(r => r.GetComponent<NativeKartParticles>().flipPrefab
                        ? r.GetComponent<NativeKartParticles>().flipPrefab.GetComponentsInChildren<ParticleSystemRenderer>(true)
                        : new ParticleSystemRenderer[0]))
                    .Concat(race.racers.SelectMany(r => r.GetComponent<NativeKartParticles>().landingPrefab
                        ? r.GetComponent<NativeKartParticles>().landingPrefab.GetComponentsInChildren<ParticleSystemRenderer>(true)
                        : new ParticleSystemRenderer[0])).ToArray();
                Add("particle-materials-supported", renderers.Length >= 20 && renderers.All(r =>
                    r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)),
                    "Particle renderers=" + renderers.Length + "; every assigned shader is supported");
                Add("effect-prefabs-silent", race.racers.All(r =>
                {
                    var p = r.GetComponent<NativeKartParticles>();
                    return p && p.drift && p.flipPrefab && p.landingPrefab
                        && p.drift.GetComponentsInChildren<AudioSource>(true).All(source => !source.enabled && !source.isPlaying)
                        && p.flipPrefab.GetComponentsInChildren<AudioSource>(true).All(source => !source.enabled && !source.isPlaying)
                        && p.landingPrefab.GetComponentsInChildren<AudioSource>(true).All(source => !source.enabled && !source.isPlaying);
                }), "Every effect AudioSource is stopped and disabled");
                Add("global-audio-muted", NativeAudioRuntime.VerificationSilent(),
                    "Global volume=" + AudioListener.volume);

                probe = Instantiate(race.racers[0].gameObject);
                probe.name = "Particles verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                var particles = probe.GetComponent<NativeKartParticles>();
                var wheels = probe.GetComponent<NativeKartWheelVisual>();
                motor.enabled = true;
                Vector3 road = new Vector3(0f, 500f, 0f);
                Vector3 upper = new Vector3(3000f, 500f, 0f);
                Vector3 lower = new Vector3(3000f, 400f, 0f);
                Plane("Particles road", road, new Vector3(1600f, 1f, 1600f));
                Plane("Particles launch", upper, new Vector3(100f, 1f, 120f));
                Plane("Particles landing", lower, new Vector3(1200f, 1f, 1200f));
                Physics.SyncTransforms();
                Time.timeScale = 1f;

                Reset(road + new Vector3(0f, 0.4f, -35f));
                yield return Drive("drift-settle", 50);
                motor.throttle = 1f;
                yield return Drive("drift-approach", 100);
                motor.braking = motor.drifting = true;
                motor.steering = 0.7f;
                yield return Drive("drift-active", 30);
                yield return new WaitForEndOfFrame();
                Add("drift-emits", motor.IsDrifting && particles.Drifting && particles.DriftStarts == 1
                    && particles.drift.GetComponentsInChildren<ParticleSystem>(true).Any(s => s.isEmitting)
                    && ParticleCount(particles.drift.gameObject) > 0,
                    "A driven brake-and-steer drift emits real smoke and sparks; count=" + ParticleCount(particles.drift.gameObject));
                yield return Capture("drift-active", () => ParticleCount(particles.drift.gameObject) > 0);
                int beforeStop = ParticleCount(particles.drift.gameObject);
                motor.braking = false;
                yield return Drive("drift-stop", 1);
                Add("drift-stop-emitting-retains-tail", !motor.IsDrifting && !particles.Drifting
                    && particles.DriftStops == 1
                    && particles.drift.GetComponentsInChildren<ParticleSystem>(true).All(s => !s.isEmitting)
                    && beforeStop > 0 && ParticleCount(particles.drift.gameObject) > 0,
                    "StopEmitting preserves existing particles: before=" + beforeStop
                    + "; after=" + ParticleCount(particles.drift.gameObject));
                motor.braking = true;
                motor.steering = -0.7f;
                yield return Drive("drift-before-jump", 3);
                int stopsBeforeJump = particles.DriftStops;
                motor.jump = true;
                yield return Drive("drift-jump", 2);
                Add("jump-stops-drift-emission", !motor.Grounded && !motor.IsDrifting
                    && !particles.Drifting && particles.DriftStops == stopsBeforeJump + 1
                    && particles.drift.GetComponentsInChildren<ParticleSystem>(true).All(s => !s.isEmitting),
                    "A real CharacterController jump clears drift state and stops new emission");

                yield return Launch("ordinary");
                motor.jumpHeld = motor.braking = false;
                motor.steering = 0f;
                yield return UntilLanding("ordinary-descent", 320);
                Add("ordinary-landing-has-no-effect", motor.Grounded && particles.FlipSpawns == 0
                    && particles.LandingSpawns == 0 && samples.Any(s => s.phase == "ordinary-descent"
                        && s.landed && s.landingFlips == 0),
                    "A driven jump with no completed rotation lands without a particle one-shot");

                yield return Launch("single");
                motor.jumpHeld = true;
                motor.steering = 1f;
                for (int i = 0; i < 180 && !motor.Grounded && motor.SpinState.flips < 1; i++)
                    yield return Drive("single-spin", 1);
                yield return new WaitForEndOfFrame();
                Add("completed-flip-spawns", !motor.Grounded && motor.SpinState.flips == 1
                    && particles.FlipSpawns == 1 && particles.LastFlip
                    && particles.LastFlip.transform.parent == motor.transform
                    && ParticleCount(particles.LastFlip) > 0,
                    "A real completed air rotation creates a visible 360Flip; count="
                    + ParticleCount(particles.LastFlip));
                yield return Capture("flip", () => particles.LastFlip && ParticleCount(particles.LastFlip) > 0);
                motor.jumpHeld = false;
                motor.steering = 0f;
                yield return UntilLanding("single-descent", 320);
                yield return new WaitForEndOfFrame();
                Add("single-flip-landing-effect", motor.Grounded && particles.FlipSpawns == 1
                    && particles.LandingSpawns == 1 && particles.LastLanding
                    && ParticleCount(particles.LastLanding) > 0
                    && samples.Any(s => s.phase == "single-descent" && s.landed && s.landingFlips == 1),
                    "One real flip produces exactly one 360FlipBoost on landing; count="
                    + ParticleCount(particles.LastLanding));
                yield return Capture("flip-landing", () => particles.LastLanding && ParticleCount(particles.LastLanding) > 0);

                Time.timeScale = 0f;
                yield return new WaitForEndOfFrame();
                var pausedEffect = particles.LastLanding;
                var pausedSystem = pausedEffect ? pausedEffect.GetComponentInChildren<ParticleSystem>(true) : null;
                float pausedParticleTime = pausedSystem ? pausedSystem.time : -1f;
                int pausedStep = motor.FixedStepCount;
                int pausedCount = ParticleCount(pausedEffect);
                yield return new WaitForSecondsRealtime(3.2f);
                Add("pause-freezes-particles-and-lifetime", pausedEffect && pausedSystem
                    && motor.FixedStepCount == pausedStep
                    && Mathf.Approximately(pausedSystem.time, pausedParticleTime)
                    && ParticleCount(pausedEffect) == pausedCount,
                    "Pause preserves fixed step, particle simulation time, count and living one-shot");
                Time.timeScale = 1f;
                yield return new WaitForSeconds(3.2f);
                Add("one-shots-expire-after-scaled-three-seconds", !pausedEffect
                    && particles.ActiveEffects == 0 && particles.LastLanding == null,
                    "The landing effect is destroyed after its scaled three-second lifetime");

                yield return Launch("double");
                motor.braking = true;
                motor.steering = -1f;
                for (int i = 0; i < 250 && !motor.Grounded && motor.SpinState.flips < 2; i++)
                    yield return Drive("double-spin", 1);
                Add("two-completed-flips-spawn-twice", !motor.Grounded && motor.SpinState.flips == 2
                    && particles.FlipSpawns == 2 && particles.ActiveEffects >= 1,
                    "Each completed rotation spawns its own 360Flip; flips=" + motor.SpinState.flips
                    + "; spawns=" + particles.FlipSpawns);
                motor.braking = false;
                motor.steering = 0f;
                yield return UntilLanding("double-descent", 320);
                yield return new WaitForEndOfFrame();
                Add("two-flip-landing-spawns-once", motor.Grounded && particles.LandingSpawns == 1
                    && samples.Any(s => s.phase == "double-descent" && s.landed && s.landingFlips == 2)
                    && particles.LastLanding && ParticleCount(particles.LastLanding) > 0,
                    "Two completed rotations yield one landing effect; count="
                    + ParticleCount(particles.LastLanding));
                yield return Capture("multi-flip-landing", () => particles.LastLanding
                    && ParticleCount(particles.LastLanding) > 0);
                motor.ResetAt(upper + new Vector3(0f, 0.4f, -35f), Quaternion.identity, false);
                Add("reset-clears-all-effects", !particles.Drifting && particles.ActiveEffects == 0
                    && particles.LastFlip == null && particles.LastLanding == null
                    && particles.DriftStarts == 0 && particles.DriftStops == 0
                    && particles.FlipSpawns == 0 && particles.LandingSpawns == 0
                    && ParticleCount(particles.drift.gameObject) == 0,
                    "Reset clears emitter particles, active instances, handles and counters immediately");
                Add("fixed-step-event-record", samples.Count > 100
                    && samples.All(s => s.fixedStepDelta == 1)
                    && samples.Any(s => s.drifting && s.driftParticles > 0)
                    && samples.Any(s => s.flips == 1 && s.flipSpawns == 1)
                    && samples.Any(s => s.landed && s.landingFlips == 2 && s.landingSpawns == 1),
                    "Every recorded event belongs to one real motor fixed step");
                Add("visible-captures", images.Count == 4 && images.All(i =>
                    File.Exists(Path.Combine(directory, i.file)))
                    && images.Any(i => i.driftParticles > 0)
                    && images.Any(i => i.flipParticles > 0)
                    && images.Count(i => i.landingParticles > 0) == 2,
                    "Four 1280x720 frames contain driven drift, flip and landing particles");
                Add("silence-persists", AudioListener.volume == 0f
                    && !race.menuPresentation.soundtrack.enabled
                    && !race.menuPresentation.soundtrack.isPlaying,
                    "The full live scenario stays globally muted");
                Add("runtime-errors", race.VisualErrorCount == 0,
                    "Player error count=" + race.VisualErrorCount);

                void Plane(string name, Vector3 top, Vector3 scale)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = name;
                    ground.transform.position = top - Vector3.up * 0.5f;
                    ground.transform.localScale = scale;
                    var material = ground.GetComponent<Renderer>().material;
                    material.color = new Color(0.21f, 0.28f, 0.34f);
                    fixtures.Add(ground);
                    materials.Add(material);
                }
                void Reset(Vector3 position)
                {
                    motor.ResetAt(position, Quaternion.identity, false);
                    wheels.ResetState();
                    motor.driving = true;
                    motor.throttle = 1f;
                    motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                    Time.timeScale = 1f;
                }
                IEnumerator Launch(string label)
                {
                    Reset(upper + new Vector3(0f, 0.4f, -35f));
                    yield return Drive(label + "-settle", 40);
                    for (int i = 0; i < 300 && motor.transform.position.z < 54f && motor.Grounded; i++)
                        yield return Drive(label + "-approach", 1);
                    Add(label + "-approach-grounded", motor.Grounded && motor.transform.position.z >= 54f
                        && motor.transform.position.z < 60f,
                        "Real upper-platform approach z=" + motor.transform.position.z);
                    motor.jump = true;
                    yield return Drive(label + "-jump", 1);
                    Add(label + "-cc-jump", motor.JumpedThisStep && !motor.Grounded
                        && motor.VerticalSpeed > 7f,
                        "CharacterController jump vertical speed=" + motor.VerticalSpeed);
                }
                IEnumerator UntilLanding(string label, int maximum)
                {
                    for (int i = 0; i < maximum && !motor.Grounded; i++) yield return Drive(label, 1);
                }
                IEnumerator Drive(string label, int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int before = motor.FixedStepCount;
                        yield return tick;
                        samples.Add(new Sample
                        {
                            phase = label, fixedStepDelta = motor.FixedStepCount - before,
                            position = motor.transform.position, grounded = motor.Grounded,
                            drifting = motor.IsDrifting, landed = motor.LandedThisStep,
                            jumped = motor.JumpedThisStep, spinRequested = motor.SpinRequested,
                            flips = motor.SpinState.flips, landingFlips = motor.LastLandingFlips,
                            driftStarts = particles.DriftStarts, driftStops = particles.DriftStops,
                            flipSpawns = particles.FlipSpawns, landingSpawns = particles.LandingSpawns,
                            activeEffects = particles.ActiveEffects,
                            driftParticles = ParticleCount(particles.drift.gameObject),
                            flipParticles = ParticleCount(particles.LastFlip),
                            landingParticles = ParticleCount(particles.LastLanding),
                            particleTime = particles.drift.time
                        });
                    }
                }
                IEnumerator Capture(string name, Func<bool> visible)
                {
                    yield return new WaitForEndOfFrame();
                    bool containsParticles = visible();
                    int driftCount = ParticleCount(particles.drift.gameObject);
                    int flipCount = ParticleCount(particles.LastFlip);
                    int landingCount = ParticleCount(particles.LastLanding);
                    Time.timeScale = 0f;
                    bool wasFrozen = wheels.frozen;
                    wheels.frozen = true;
                    motor.PrepareRenderPose(false);
                    camera.transform.position = motor.transform.position + new Vector3(5f, 2.6f, -6f);
                    camera.transform.LookAt(motor.transform.position + Vector3.up * 0.5f);
                    camera.fieldOfView = 43f;
                    yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    string file = name + ".png";
                    File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
                    Destroy(texture);
                    images.Add(new Image
                    {
                        name = name, file = file, grounded = motor.Grounded,
                        flips = motor.SpinState.flips, landingFlips = motor.LastLandingFlips,
                        driftParticles = driftCount, flipParticles = flipCount,
                        landingParticles = landingCount
                    });
                    Add(name + "-captured-live-particles", containsParticles,
                        "Particle count at capture: drift=" + driftCount + ", flip=" + flipCount
                        + ", landing=" + landingCount);
                    wheels.frozen = wasFrozen;
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
            }
            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                boundary = "An exported kart is cloned into isolated real CharacterController road and 100-unit-drop scenes. "
                    + "Motor input drives drift, completed rotations and landing. Particle counts follow those events by fixed step. "
                    + "Four screenshots capture emitted particles after render simulation; pause, lifetime, reset and global mute are checked.",
                fixedDeltaTime = Time.fixedDeltaTime, width = Screen.width, height = Screen.height,
                passed = checks.Count > 0 && checks.All(c => c.passed),
                checks = checks.ToArray(), samples = samples.ToArray(), images = images.ToArray()
            };
            result.passed = report.passed;
            File.WriteAllText(Path.Combine(directory, "particles-report.json"), JsonUtility.ToJson(report, true));
            void Add(string name, bool passed, string detail) => checks.Add(new Check
            {
                name = name, passed = passed, detail = detail
            });
        }

        static int ParticleCount(GameObject root)
        {
            if (!root) return 0;
            return root.GetComponentsInChildren<ParticleSystem>(true).Sum(system => system.particleCount);
        }
    }
}
