using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartSpinLiveVerification : MonoBehaviour
    {
        public sealed class Result { public bool passed; }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int step, fixedStepDelta, flips, landingFlips;
            public bool groundedBefore, grounded, jumped, landed, spinRequested, braking, jumpHeld;
            public float steering, airTimeBefore, airTime, bodyYaw, velocityYaw, turned,
                bodyYawError, velocityYawError, stateError, velocityError, rotationError,
                speedBefore, speed, landingBonus;
            public Vector3 position, velocityBefore, velocity;
            public Quaternion rotation;
        }
        [Serializable] sealed class Pose
        {
            public string name, image;
            public int flips, landingFlips;
            public bool grounded;
            public float turned, speed, landingBonus, appliedRotationError;
            public Vector3 position, velocity;
            public Quaternion rotation;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public int width, height;
            public float fixedDeltaTime;
            public Check[] checks;
            public Sample[] samples;
            public Pose[] poses;
        }

        public static IEnumerator Run(NativeRace race, string directory, Result result)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var poses = new List<Pose>();
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
                probe = Instantiate(race.racers[0].gameObject);
                probe.name = "Spin verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                var wheels = probe.GetComponent<NativeKartWheelVisual>();
                motor.enabled = true;
                Vector3 upper = new Vector3(0f, 500f, 0f);
                Vector3 lower = new Vector3(0f, 400f, 0f);
                Plane("Spin launch platform", upper + Vector3.back * 170f, new Vector3(100f, 1f, 460f), new Color(0.21f, 0.28f, 0.34f));
                Plane("Spin landing platform", lower, new Vector3(1200f, 1f, 1200f), new Color(0.20f, 0.27f, 0.31f));
                Physics.SyncTransforms();
                Time.timeScale = 1f;
                Add("serialized-spin-settings", race.racers.All(r => r.stats._turnForce == 125f
                    && r.stats._turnAirMult == 3.75f && r.stats._turnVelAirMult == 0.25f
                    && r.stats._flipSpeedBoost == 20f && !r.stats._convertsAirVelocity),
                    "Original Kart turn and flip boost settings are present on every racer");
                Add("global-audio-muted", NativeAudioRuntime.VerificationSilent(),
                    "Global volume=" + AudioListener.volume);

                yield return Launch("right", 1f);
                motor.jumpHeld = true;
                motor.steering = 1f;
                yield return Drive("right-c-held-spin", 16);
                Add("held-c-spins-without-brake", !motor.Grounded && motor.SpinRequested && !motor.braking
                    && motor.SpinState.turned > 0f && motor.CurrentAirTurn > motor.CurrentAirVelocityTurn,
                    "Turned=" + motor.SpinState.turned + "; body=" + motor.CurrentAirTurn
                    + "; velocity=" + motor.CurrentAirVelocityTurn);
                yield return Capture("spin-turn");
                for (int i = 0; i < 160 && !motor.Grounded && motor.SpinState.flips < 1; i++)
                    yield return Drive("right-first-flip", 1);
                int rightFlips = motor.SpinState.flips;
                motor.jumpHeld = false;
                motor.steering = 0f;
                yield return Drive("right-normal-air", 5);
                Add("ordinary-air-retains-flip", rightFlips == 1 && motor.SpinState.flips == 1
                    && !motor.SpinRequested && Mathf.Abs(motor.CurrentAirTurn) < 0.0001f,
                    "Counter retains the completed right rotation when spin input is released");
                yield return UntilLanding("right-descent", 300);
                var rightLanding = samples.LastOrDefault(s => s.phase == "right-descent" && s.landed);
                Add("right-landing-reward", rightLanding != null && rightLanding.landingFlips == 1
                    && Mathf.Abs(rightLanding.landingBonus - 20f) < 0.0001f
                    && Mathf.Abs(rightLanding.speed - rightLanding.speedBefore - 20f) < 0.01f,
                    "Right landing raw boost=" + motor.LastLandingBonus + "; final speed=" + motor.Speed);
                yield return Capture("spin-landing");
                yield return Drive("right-after-landing", 1);
                Add("landing-record-is-one-step", motor.LastLandingFlips == 0 && motor.LastLandingBonus == 0f
                    && motor.SpinState.flips == 0 && motor.SpinState.turned == 0f && motor.AirTime == 0f,
                    "Landing telemetry clears on the next fixed step; spin accumulator remains reset");

                yield return Launch("left", -1f);
                motor.braking = true;
                motor.steering = -1f;
                yield return Drive("left-x-held-spin", 16);
                Add("held-x-spins-without-c", !motor.Grounded && motor.SpinRequested && !motor.jumpHeld
                    && motor.SpinState.turned < 0f && motor.CurrentAirTurn < motor.CurrentAirVelocityTurn,
                    "Turned=" + motor.SpinState.turned + "; body=" + motor.CurrentAirTurn
                    + "; velocity=" + motor.CurrentAirVelocityTurn);
                float beforeReverse = motor.SpinState.turned;
                motor.steering = 1f;
                yield return Drive("left-reverse", 6);
                Add("direction-reversal-cancels-angle", motor.SpinState.flips == 0
                    && motor.SpinState.turned > beforeReverse && motor.SpinState.turned < 0f,
                    "The signed accumulator moves toward zero: " + beforeReverse + " to " + motor.SpinState.turned);
                motor.steering = -1f;
                for (int i = 0; i < 220 && !motor.Grounded && motor.SpinState.flips < 2; i++)
                    yield return Drive("left-two-flips", 1);
                Add("left-multiple-flips", !motor.Grounded && motor.SpinState.flips == 2
                    && Mathf.Abs(motor.SpinState.turned) < 358f,
                    "Two signed rotations each reset the partial angle; remaining=" + motor.SpinState.turned);
                yield return Capture("multi-flip");
                motor.braking = false;
                motor.steering = 0f;
                yield return UntilLanding("left-descent", 300);
                var leftLanding = samples.LastOrDefault(s => s.phase == "left-descent" && s.landed);
                Add("left-landing-reward", leftLanding != null && leftLanding.landingFlips == 2
                    && Mathf.Abs(leftLanding.landingBonus - 40f) < 0.0001f
                    && Mathf.Abs(leftLanding.speed - leftLanding.speedBefore - 40f) < 0.01f,
                    "Left landing gives two raw 20-unit boosts");
                yield return Drive("left-after-landing", 1);
                float effectiveCap = Mathf.Min(motor.stats._topSpeed + motor.stats._topSpeedAdd
                    + motor.stats._driftSpeedMaxGain, motor.stats._topSpeedCap);
                Add("post-bonus-cap", leftLanding != null && leftLanding.speed > effectiveCap
                    && motor.Speed <= effectiveCap + 0.01f,
                    "Landing raw speed=" + (leftLanding == null ? -1f : leftLanding.speed)
                    + "; next-step speed=" + motor.Speed + "; cap=" + effectiveCap);

                yield return Launch("low", 1f, 0.6f);
                motor.jumpHeld = true;
                motor.steering = 1f;
                for (int i = 0; i < 160 && !motor.Grounded && motor.SpinState.flips < 1; i++)
                    yield return Drive("low-first-flip", 1);
                motor.jumpHeld = false;
                motor.steering = 0f;
                yield return UntilLanding("low-descent", 300);
                var lowLanding = samples.LastOrDefault(s => s.phase == "low-descent" && s.landed);
                yield return Drive("low-after-landing", 1);
                Add("low-speed-bonus-persists", lowLanding != null && lowLanding.landingFlips == 1
                    && lowLanding.speedBefore < effectiveCap - 20f
                    && Mathf.Abs(lowLanding.speed - lowLanding.speedBefore - 20f) < 0.01f
                    && motor.Speed > lowLanding.speedBefore + 10f,
                    "Low-speed raw speed=" + (lowLanding == null ? -1f : lowLanding.speedBefore)
                    + "; landing=" + (lowLanding == null ? -1f : lowLanding.speed)
                    + "; next step=" + motor.Speed);

                yield return Launch("pause", 1f);
                motor.jumpHeld = true;
                motor.steering = 1f;
                yield return Drive("pause-spin", 15);
                Time.timeScale = 0f;
                yield return new WaitForEndOfFrame();
                int pausedSteps = motor.FixedStepCount;
                var pausedState = motor.SpinState;
                Vector3 pausedPosition = motor.transform.position;
                yield return new WaitForSecondsRealtime(0.25f);
                Add("pause-freezes-spin", motor.FixedStepCount == pausedSteps
                    && motor.SpinState.flips == pausedState.flips && motor.SpinState.turned == pausedState.turned
                    && motor.transform.position == pausedPosition,
                    "Paused fixed-step count and signed rotation stay fixed");
                motor.ResetAt(upper + new Vector3(0f, 0.4f, -300f), Quaternion.identity, false);
                Add("reset-clears-spin", motor.SpinState.flips == 0 && motor.SpinState.turned == 0f
                    && motor.LastLandingFlips == 0 && motor.LastLandingBonus == 0f
                    && motor.AirTime == 0f && !motor.jumpHeld,
                    "Reset clears rotation, reward telemetry, air time and held C input");
                Time.timeScale = 1f;
                Add("fixed-step-observations", samples.Count > 100 && samples.All(s => s.fixedStepDelta == 1),
                    "Each sampled row covers exactly one real fixed step");
                var air = samples.Where(s => !s.groundedBefore && !s.grounded).ToArray();
                Add("spin-wiring-matches-equations", air.Length > 80
                    && air.All(s => s.bodyYawError < 0.0001f && s.velocityYawError < 0.0001f
                        && s.stateError < 0.0001f && s.velocityError < 0.0001f
                        && s.rotationError < 0.08f),
                    "Body yaw, velocity yaw, carried velocity, actual rotation and signed accumulator match per-step equations");
                Add("actual-model-poses", poses.Count == 3 && poses.All(p => p.appliedRotationError < 0.08f
                    && File.Exists(Path.Combine(directory, p.image))) && poses.Count(p => p.grounded) == 1,
                    "Three captures show actual model rotation at spin, multiple flips and landing");
                Add("silence-persists", AudioListener.volume == 0f && !race.menuPresentation.soundtrack.enabled
                    && !race.menuPresentation.soundtrack.isPlaying, "The full live test remains globally muted");
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);

                void Plane(string name, Vector3 top, Vector3 scale, Color color)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = name;
                    ground.transform.position = top - Vector3.up * 0.5f;
                    ground.transform.localScale = scale;
                    var material = ground.GetComponent<Renderer>().material;
                    material.color = color;
                    materials.Add(material);
                    fixtures.Add(ground);
                    int spacing = scale.z > 200f ? 25 : 10;
                    int first = scale.z > 200f ? -8 : -5;
                    int last = scale.z > 200f ? 12 : 5;
                    for (int z = first; z <= last; z++)
                    {
                        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        stripe.name = "Spin distance grid";
                        stripe.GetComponent<Collider>().enabled = false;
                        stripe.transform.position = top + new Vector3(0f, 0.003f, z * spacing);
                        stripe.transform.localScale = new Vector3(scale.x, 0.002f, 0.06f);
                        fixtures.Add(stripe);
                    }
                }
                void Reset()
                {
                    motor.ResetAt(upper + new Vector3(0f, 0.4f, -300f), Quaternion.identity, false);
                    wheels.ResetState();
                    motor.driving = true;
                    motor.throttle = 1f;
                    motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                    Time.timeScale = 1f;
                }
                IEnumerator Launch(string label, float direction, float throttle = 1f)
                {
                    Reset();
                    motor.throttle = throttle;
                    yield return Drive(label + "-settle", 40);
                    for (int i = 0; i < 600 && motor.transform.position.z < 54f && motor.Grounded; i++)
                        yield return Drive(label + "-approach", 1);
                    Add(label + "-platform-approach", motor.Grounded && motor.transform.position.z >= 54f
                        && motor.transform.position.z < 60f,
                        "Real launch-platform approach at z=" + motor.transform.position.z);
                    motor.jump = true;
                    yield return Drive(label + "-jump", 1);
                    Add(label + "-cc-jump", motor.JumpedThisStep && !motor.Grounded
                        && motor.VerticalSpeed > 7f, "CharacterController takeoff vertical speed=" + motor.VerticalSpeed);
                    motor.steering = direction;
                }
                IEnumerator UntilLanding(string label, int maximum)
                {
                    for (int i = 0; i < maximum && !motor.Grounded; i++) yield return Drive(label, 1);
                }
                IEnumerator Drive(string label, int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        bool groundedBefore = motor.Grounded;
                        int fixedStep = motor.FixedStepCount;
                        float dt = Time.fixedDeltaTime, airBefore = motor.AirTime;
                        float speedBefore = motor.Speed;
                        Vector3 velocityBefore = motor.DriveVelocity;
                        float wish = Mathf.Clamp(motor.driving ? motor.steering : 0f, -1f, 1f);
                        float ordinaryYaw = motor.stats._turnForce * wish * dt * motor.stats._turnVelAirMult;
                        float expectedBodyYaw = motor.SpinRequested
                            ? motor.stats._turnForce * wish * dt * motor.stats._turnAirMult
                                * Mathf.Clamp01(airBefore * 3f) : ordinaryYaw;
                        float expectedVelocityYaw = motor.SpinRequested ? ordinaryYaw * 0.1f : ordinaryYaw;
                        Vector3 expectedVelocity = Quaternion.Euler(0f, expectedVelocityYaw, 0f) * velocityBefore;
                        expectedVelocity.y = 0f;
                        Quaternion expectedRotation = motor.transform.rotation * Quaternion.Euler(0f, expectedBodyYaw, 0f);
                        if (Mathf.Abs(expectedBodyYaw) <= 0.1f && (expectedVelocity).sqrMagnitude > 0.1f)
                            expectedRotation = Quaternion.Lerp(expectedRotation,
                                Quaternion.LookRotation(expectedVelocity, expectedRotation * Vector3.up),
                                2f * motor.stats._turnForce * dt * dt * motor.stats._turnAirMult);
                        float expectedTurned = motor.SpinState.turned + expectedBodyYaw;
                        int expectedFlips = motor.SpinState.flips;
                        if (expectedTurned >= 358f || expectedTurned <= -358f)
                        {
                            expectedFlips++;
                            expectedTurned = 0f;
                        }
                        bool requested = motor.SpinRequested;
                        float steering = motor.steering;
                        bool braking = motor.braking, jumpHeld = motor.jumpHeld;
                        yield return tick;
                        samples.Add(new Sample
                        {
                            phase = label, step = samples.Count, fixedStepDelta = motor.FixedStepCount - fixedStep,
                            groundedBefore = groundedBefore, grounded = motor.Grounded,
                            jumped = motor.JumpedThisStep, landed = motor.LandedThisStep,
                            spinRequested = requested, braking = braking, jumpHeld = jumpHeld, steering = steering,
                            airTimeBefore = airBefore, airTime = motor.AirTime,
                            bodyYaw = motor.CurrentAirTurn, velocityYaw = motor.CurrentAirVelocityTurn,
                            turned = motor.SpinState.turned, flips = motor.SpinState.flips,
                            bodyYawError = groundedBefore ? 0f : Mathf.Abs(motor.CurrentAirTurn - expectedBodyYaw),
                            velocityYawError = groundedBefore ? 0f : Mathf.Abs(motor.CurrentAirVelocityTurn - expectedVelocityYaw),
                            stateError = groundedBefore || motor.LandedThisStep ? 0f
                                : Mathf.Abs(motor.SpinState.turned - expectedTurned)
                                    + Mathf.Abs(motor.SpinState.flips - expectedFlips),
                            velocityError = groundedBefore || motor.LandedThisStep ? 0f
                                : Vector3.Distance(motor.DriveVelocity, expectedVelocity),
                            rotationError = groundedBefore || motor.LandedThisStep ? 0f
                                : Quaternion.Angle(motor.transform.rotation, expectedRotation),
                            speedBefore = speedBefore, speed = motor.Speed,
                            landingFlips = motor.LastLandingFlips, landingBonus = motor.LastLandingBonus,
                            position = motor.transform.position, velocityBefore = velocityBefore,
                            velocity = motor.DriveVelocity, rotation = motor.transform.rotation
                        });
                    }
                }
                IEnumerator Capture(string name)
                {
                    Time.timeScale = 0f;
                    bool frozen = wheels.frozen;
                    wheels.frozen = true;
                    motor.PrepareRenderPose(false);
                    camera.transform.position = motor.transform.position + new Vector3(7f, 3.2f, -9f);
                    camera.transform.LookAt(motor.transform.position + Vector3.up * 0.3f);
                    camera.fieldOfView = 40f;
                    yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    string image = name + ".png";
                    File.WriteAllBytes(Path.Combine(directory, image), texture.EncodeToPNG());
                    Destroy(texture);
                    poses.Add(new Pose
                    {
                        name = name, image = image, grounded = motor.Grounded,
                        flips = motor.SpinState.flips, turned = motor.SpinState.turned,
                        landingFlips = motor.LastLandingFlips, landingBonus = motor.LastLandingBonus,
                        speed = motor.Speed, position = motor.transform.position,
                        velocity = motor.PhysicsVelocity, rotation = motor.transform.rotation,
                        appliedRotationError = Quaternion.Angle(motor.visual.rotation, motor.transform.rotation)
                    });
                    wheels.frozen = frozen;
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
                boundary = "A cloned exported kart jumps from a real upper CharacterController platform to a lower collision plane. "
                    + "Live fixed steps cover C-held and X-held spin, signed reversal, ordinary air carry, multiple rotations, "
                    + "landing boost, following-step cap, pause/reset and global mute. Three screenshots retain actual model poses. "
                    + "The separate original x64 scalar reference establishes turn and flip formula parity.",
                fixedDeltaTime = Time.fixedDeltaTime, width = Screen.width, height = Screen.height,
                passed = checks.Count > 0 && checks.All(c => c.passed),
                checks = checks.ToArray(), samples = samples.ToArray(), poses = poses.ToArray()
            };
            result.passed = report.passed;
            File.WriteAllText(Path.Combine(directory, "spin-live-report.json"), JsonUtility.ToJson(report, true));
            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
        }
    }
}
