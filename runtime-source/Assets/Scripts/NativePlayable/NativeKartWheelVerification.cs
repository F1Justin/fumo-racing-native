using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    /// <summary>
    /// Verifies the restored VehiclePhysics visuals: source constants, spin/steer/tilt formulas,
    /// live wheel rotation against vehicle speed, and forced-state close-ups.
    /// </summary>
    public static class NativeKartWheelVerification
    {
        [Serializable]
        sealed class Check
        {
            public string name, detail;
            public bool passed;
        }

        [Serializable]
        sealed class Sample
        {
            public string wheel;
            public float speedKmh, expectedRate, measuredRate, relativeError;
        }

        [Serializable]
        sealed class Report
        {
            public string version, platform, graphics, evidence;
            public bool passed;
            public string settings;
            public int wheels;
            public float spinTolerance, steerTolerance;
            public int skippedAliasedFrames;
            public Check[] checks;
            public Sample[] samples;
            public string[] captures;
        }

        const float SpinTolerance = 0.02f;
        const float SteerTolerance = 0.5f;
        const float FormulaTolerance = 1e-3f;

        public static IEnumerator Run(NativeRace race, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var captures = new List<string>();
            Vector3 playerSpawnPosition = race.racers[0].transform.position;
            Quaternion playerSpawnRotation = race.racers[0].transform.rotation;
            var report = new Report
            {
                version = Application.version,
                platform = SystemInfo.operatingSystem,
                graphics = SystemInfo.graphicsDeviceName,
                spinTolerance = SpinTolerance,
                steerTolerance = SteerTolerance,
                evidence = "Source-formula fixtures plus live native Player sampling of wheel transforms under the restored controller."
            };

            var visuals = race.racers.Select(r => r.GetComponent<NativeKartWheelVisual>()).ToArray();
            try
            {
                CheckThat(checks, "live-component",
                    visuals.All(v => v && v.settings != null && v.settings._wheels.Length == 4
                        && v.root && v.steeringWheel && v.fumo
                        && v.suspensions.All(t => t) && v.torques.All(t => t)),
                    visuals.All(v => v) ? "Four racers expose wheel, steering wheel, root and character bindings"
                        : "Wheel component missing on a racer");

                var settings = visuals[0].settings;
                report.wheels = settings._wheels.Length;
                report.settings = JsonUtility.ToJson(settings);
                CheckThat(checks, "original-settings", visuals.All(v => OriginalSettings(v.settings)),
                    "Wheel radius, steering limits, tilt and suspension values match the original kart prefabs");

                CheckThat(checks, "original-model-assembly", visuals.All(v =>
                {
                    var controller = v.motor.GetComponent<CharacterController>();
                    return v.sourceFrame && v.sourceFrame.parent == v.motor.driftPivot.Find("AnimPivot/Kart")
                        && Vector3.Distance(v.sourceFrame.localPosition, Vector3.up * 0.035f) < 0.000001f
                        && v.fumo.name == "FumoPivot" && v.fumo == v.motor.driftPivot.Find("AnimPivot/AccelPivot/Accel2Pivot/FumoPivot")
                        && v.fumo.childCount == 1
                        && Vector3.Distance(v.fumo.GetChild(0).localPosition, Vector3.up * 0.035f) < 0.000001f
                        && Near(controller.height, 2f, 0f) && Near(controller.radius, 0.8f, 0f)
                        && Vector3.Distance(controller.center, new Vector3(0f, 0.8f, 0f)) < 0.000001f
                        && Near(controller.slopeLimit, 65f, 0f) && Near(controller.stepOffset, 0.5f, 0f)
                        && Near(controller.skinWidth, 0.015f, 0f) && Near(controller.minMoveDistance, 0.0002f, 0f);
                }), "Four original model heights, separate character pivots, kart up bindings and controller parameters are exported");

                var full = new NativeKartWheelInput { speed = 145f, topSpeed = 145f, steering = 1f, grounded = true };
                var half = full; half.speed = 72.5f;
                var reverse = full; reverse.steering = -1f;
                var over = full; over.topSpeed = 72.5f;
                var trimmed = JsonUtility.FromJson<NativeKartWheelSettings>(JsonUtility.ToJson(settings));
                trimmed._tiltSpdMult = 2f;
                var zero = new NativeKartWheelState();
                CheckThat(checks, "tilt-target",
                    Near(step(zero, full, settings)._targetTilt, 12f, FormulaTolerance)
                    && Near(step(zero, half, settings)._targetTilt, 6f, FormulaTolerance)
                    && Near(step(zero, reverse, settings)._targetTilt, -12f, FormulaTolerance)
                    && Near(step(zero, over, trimmed)._targetTilt, 15f, FormulaTolerance),
                    "Source formula: steering * _tiltTurn * (speed * _tiltSpdMult / topSpeed), clamped to _maxTilt");

                var spin = step(zero, full, settings);
                float expectedSpin = 145f / (2f * Mathf.PI * 0.215f) * Mathf.Rad2Deg;
                CheckThat(checks, "spin-formula",
                    Near(spin.SpinEuler(0, settings, 1f).magnitude, expectedSpin, 1f)
                    && Near(spin.SpinEuler(0, settings, 1f / 60f).magnitude, expectedSpin / 60f, 0.05f)
                    && Near(settings._wheels[0]._torqueAxis.y, -1f, 0f)
                    && Near(settings._wheels[2]._torqueAxis.y, 1f, 0f),
                    "Source formula: torqueSpeed / (2 * PI * wheelRadius) with per-wheel torque axis");

                CheckThat(checks, "steer-limits",
                    Near(spin.SteerEuler(1, settings).z, 20f, FormulaTolerance)
                    && Near(spin.SteerEuler(3, settings).z, 20f, FormulaTolerance)
                    && Near(spin.SteerEuler(0, settings).z, 0f, FormulaTolerance)
                    && Near(spin.SteerEuler(2, settings).z, 0f, FormulaTolerance),
                    "Rear wheels carry no steering; front wheels use original 20 degree local Z steering");

                var airborne = step(zero, new NativeKartWheelInput { topSpeed = 145f, grounded = false }, settings);
                CheckThat(checks, "airborne-target",
                    Near(airborne.targetSuspension, -0.6f, FormulaTolerance)
                    && Near(airborne._targetTilt, 0f, FormulaTolerance),
                    "Airborne suspension target -0.6 and zero tilt without steering input");

                var landed = step(zero, new NativeKartWheelInput { topSpeed = 145f, grounded = true, landed = true }, settings, 0f);
                var settled = zero;
                for (int i = 0; i < 600; i++)
                    settled = step(settled, new NativeKartWheelInput { topSpeed = 145f, grounded = true }, settings, 1f / 60f);
                CheckThat(checks, "landing-and-settle",
                    Near(landed.currentVelocity, 8f, 0f)
                    && Near(settled.currentSuspension, settings.targetGroundSuspension, FormulaTolerance)
                    && Near(settled.currentVelocity, 0f, FormulaTolerance)
                    && Near(settled._tilt, 0f, FormulaTolerance),
                    "Landing impulse equals _landSuspension; the spring settles onto the ground target");

                var paused = step(spin, full, settings, 0f);
                CheckThat(checks, "paused-zero-dt",
                    Near(paused.currentSuspension, spin.currentSuspension, 0f)
                    && Near(paused.currentVelocity, spin.currentVelocity, 0f)
                    && Near(paused._currentTilt, spin._currentTilt, 0f),
                    "Zero game time leaves suspension and tilt state untouched");

                float[] steps = { 1f / 30f, 1f / 60f, 1f / 120f };
                var settle = new List<string>();
                var values = new List<float>();
                foreach (float dt in steps)
                {
                    var state = new NativeKartWheelState();
                    var landing = new NativeKartWheelInput { topSpeed = 145f, grounded = true, landed = true };
                    var steady = new NativeKartWheelInput { topSpeed = 145f, grounded = true };
                    state = step(state, landing, settings, dt);
                    for (int i = 1; i < Mathf.RoundToInt(2f / dt); i++) state = step(state, steady, settings, dt);
                    values.Add(Mathf.Abs(state.currentSuspension));
                    settle.Add(Mathf.RoundToInt(1f / dt) + "Hz=" + state.currentSuspension.ToString("F4"));
                }
                float spread = values.Max() - values.Min();
                CheckThat(checks, "landing-decay",
                    values.All(value => value < 0.05f) && spread < 0.05f,
                    "Landing transient decays at 30/60/120 Hz: " + string.Join(" / ", settle)
                    + "; spread " + spread.ToString("F4"));

                var visual = visuals[0];
                visual.ResetState();
                CheckThat(checks, "new-race-reset",
                    Near(visual.State.currentSuspension, 0f, 0f) && Near(visual.State._tilt, 0f, 0f)
                    && Near(visual.State.torqueSpeed, 0f, 0f),
                    "Race reset clears wheel, suspension and tilt state");
            }
            catch (Exception error)
            {
                CheckThat(checks, "verification-exception", false, error.ToString());
            }

            yield return LiveSamples(race, visuals, samples, checks, report);
            var turn = new NativeKartTurnVerification.Result();
            yield return NativeKartTurnVerification.Run(race, visuals, reportDirectory, turn);
            CheckThat(checks, "turn-road-anchor", turn.roadFound && turn.vehicleCount == 4,
                "Four cars were placed on one raycast-confirmed flat Forest road pose");
            CheckThat(checks, "turn-world-body-character", turn.bodyAndCharacterLean && turn.leftRightSymmetry,
                "Root and character have matched left/right world roll without changing horizontal heading");
            CheckThat(checks, "turn-world-front-wheels", turn.frontWheelWorldSteer,
                "Both front wheels follow signed steering in world space while rear wheels hold heading");
            CheckThat(checks, "turn-body-character-suspension", turn.suspensionTravelMatched,
                "One landing transient moves vehicle root and character by the same world height");
            CheckThat(checks, "turn-render-pose", turn.renderPoseAligned,
                "Live turning keeps the visual root aligned with the interpolated physical heading");
            CheckThat(checks, "turn-pause-reset", turn.pauseFrozen && turn.resetRestored,
                "Pause freezes world transforms and reset restores body, character and all wheel poses");
            CheckThat(checks, "turn-captures", turn.capturesPresent,
                "Player captured straight, left and right from one fixed clean-road pose");
            yield return CaptureFixtures(race, visuals[0], playerSpawnPosition, playerSpawnRotation,
                reportDirectory, captures, checks);
            CheckThat(checks, "visual-captures", captures.Count == 3,
                "Straight, steered and loaded wheel close-ups from forced Player states");
            yield return null;

            yield return CheckBikeWheels(race, checks);
            report.checks = checks.ToArray();
            report.samples = samples.ToArray();
            report.captures = captures.ToArray();
            report.passed = checks.TrueForAll(check => check.passed);
            File.WriteAllText(Path.Combine(reportDirectory, "wheel-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_WHEEL_RESULT passed=" + report.passed + " checks=" + checks.Count
                + " samples=" + samples.Count);
            yield return null;
            Application.Quit(report.passed ? 0 : 2);
        }

        static IEnumerator CheckBikeWheels(NativeRace race, List<Check> checks)
        {
            var motor = race.racers[0];
            var roster = race.vehicleRoster;
            var spawn = motor.transform.position; var rotation = motor.transform.rotation;
            for (int character = 0; character < roster.CharacterCount; character++)
            {
                roster.ApplySelection(character, 1, motor);
                yield return null;
                var wheels = motor.GetComponentsInChildren<NativeKartWheelVisual>()
                    .SingleOrDefault(w => w.enabled && w.gameObject != motor.gameObject);
                CheckThat(checks, "bike-binding-" + character, wheels && wheels.motor == motor && wheels.torques.Length == 2
                    && wheels.torques.All(t => t) && wheels.settings._wheels.All(w => Near(w._wheelRadius, .279f, .0001f)),
                    "Selected bike binds both original wheel bones, axes, radius and actual racing motor");
            }
            var bike = motor.GetComponentsInChildren<NativeKartWheelVisual>().Single(w => w.enabled && w.gameObject != motor.gameObject);
            motor.ResetAt(spawn, rotation, false); motor.enabled = true; motor.driving = true;
            motor.throttle = 1; motor.steering = 0; Time.timeScale = 1;
            yield return new WaitForSecondsRealtime(1.2f);
            int movingFrames = 0; float maxError = 0;
            var previous = bike.torques.Select(t => t.localRotation).ToArray();
            for (int frame = 0; frame < 45; frame++)
            {
                yield return null; yield return new WaitForEndOfFrame();
                for (int i = 0; i < previous.Length; i++)
                {
                    float expected = Mathf.Abs(bike.State.SpinRate(i, bike.settings) * Mathf.Rad2Deg * Time.deltaTime);
                    float measured = Quaternion.Angle(previous[i], bike.torques[i].localRotation);
                    if (motor.Speed > 8 && expected > .1f && expected < 170)
                    { movingFrames++; maxError = Mathf.Max(maxError, Mathf.Abs(measured - expected) / expected); }
                    previous[i] = bike.torques[i].localRotation;
                }
            }
            CheckThat(checks, "bike-live-wheel-spin", movingFrames >= 20 && maxError < .03f,
                "End-of-frame bike wheel rotation follows real motor speed; frames=" + movingFrames + " error=" + maxError);
            Time.timeScale = 0; previous = bike.torques.Select(t => t.localRotation).ToArray();
            yield return new WaitForSecondsRealtime(.2f);
            CheckThat(checks, "bike-pause-freezes-wheels", bike.torques.Select((t,i) => Quaternion.Angle(previous[i],t.localRotation)).All(a => a == 0), "Pause freezes both bike wheels");
            bike.ResetState(); var reset = bike.torques.Select(t => t.localRotation).ToArray();
            bike.StepForTest(new NativeKartWheelInput { speed=20, topSpeed=140, grounded=true }, .1f);
            bike.ResetState();
            CheckThat(checks, "bike-reset-restores-wheels", bike.torques.Select((t,i) => Quaternion.Angle(reset[i],t.localRotation)).All(a => a < .001f), "Reset restores both bike wheel poses");
            Time.timeScale = 1; motor.throttle = 0; motor.driving = false;
            roster.ApplySelection(roster.DefaultCharacterIndex, 0, motor); motor.ResetAt(spawn, rotation, false);
        }

        static NativeKartWheelState step(NativeKartWheelState previous, NativeKartWheelInput input,
            NativeKartWheelSettings settings, float dt = 1f / 60f) =>
            NativeKartWheelState.Step(previous, input, settings, dt);

        static IEnumerator LiveSamples(NativeRace race, NativeKartWheelVisual[] visuals,
            List<Sample> samples, List<Check> checks, Report report)
        {
            var motor = race.racers[0];
            var visual = visuals[0];
            Quaternion originalPosition = motor.transform.rotation;
            Vector3 position = motor.transform.position;
            bool driving = motor.driving;
            float throttle = motor.throttle, steering = motor.steering;
            float originalTimeScale = Time.timeScale;
            bool frozen = visual.frozen;
            Quaternion[] previous = null;
            float maxError = 0f;
            float maxSteerError = 0f;
            float stoppedDrift = 0f;
            int sampled = 0;
            try
            {
                visual.frozen = false;
                Time.timeScale = 1f;
                motor.ResetAt(position, originalPosition, false);
                motor.driving = true;
                motor.throttle = 1f;
                motor.steering = 0.6f;
                yield return new WaitForSecondsRealtime(1.2f);
                for (int frame = 0; frame < 90; frame++)
                {
                    yield return new WaitForEndOfFrame();
                    if (motor.Speed < 8f || Time.deltaTime <= 0f) continue;
                    if (previous == null)
                    {
                        previous = visual.torques.Select(t => t.localRotation).ToArray();
                        continue;
                    }
                    bool aliased = false;
                    for (int i = 0; i < visual.torques.Length; i++)
                        aliased |= visual.State.SpinRate(i, visual.settings) * Mathf.Rad2Deg * Time.deltaTime >= 175f;
                    if (aliased)
                    {
                        for (int i = 0; i < visual.torques.Length; i++) previous[i] = visual.torques[i].localRotation;
                        report.skippedAliasedFrames++;
                        continue;
                    }
                    for (int i = 0; i < visual.torques.Length; i++)
                    {
                        var node = visual.torques[i];
                        float measured = Quaternion.Angle(previous[i], node.localRotation) / Time.deltaTime;
                        float expected = visual.State.SpinRate(i, visual.settings) * Mathf.Rad2Deg;
                        previous[i] = node.localRotation;
                        if (expected <= 0f) continue;
                        float error = Mathf.Abs(measured - expected) / expected;
                        maxError = Mathf.Max(maxError, error);
                        if (frame % 20 != 0) continue;
                        samples.Add(new Sample
                        {
                            wheel = "wheel" + i,
                            speedKmh = motor.Speed,
                            expectedRate = expected,
                            measuredRate = measured,
                            relativeError = error
                        });
                    }
                    var steer = visual.State.SteerEuler(1, visual.settings);
                    var tilt = Quaternion.Euler(visual.State.WheelTiltEuler(1, visual.settings));
                    var applied = Quaternion.Inverse(visual.RestRotation(1) * tilt) * visual.suspensions[1].localRotation;
                    float measuredAngle = Quaternion.Angle(Quaternion.identity, applied);
                    maxSteerError = Mathf.Max(maxSteerError, Mathf.Abs(measuredAngle - Mathf.Abs(steer.z)));
                    sampled++;
                }
                CheckThat(checks, "live-spin-matches-speed", sampled > 20 && maxError < SpinTolerance,
                    "Measured wheel rotation rate tracks torqueSpeed / (2*PI*r); max relative error "
                    + maxError.ToString("G4") + " over " + sampled + " frames");
                CheckThat(checks, "live-steer-and-follow", sampled > 20 && maxSteerError < SteerTolerance,
                    "Front wheel steering follows the restored _steering * currentSteering angle; max error "
                    + maxSteerError.ToString("G4") + " degrees");

                motor.throttle = 0f;
                motor.braking = true;
                for (int frame = 0; frame < 90 && motor.Speed > 0.2f; frame++) yield return new WaitForFixedUpdate();
                // Original slope acceleration follows braking, so use the parked state to measure a true zero-speed hold.
                motor.driving = false;
                for (int frame = 0; frame < 90 && motor.Speed > 0f; frame++) yield return new WaitForFixedUpdate();
                yield return new WaitForSecondsRealtime(0.3f);
                var stopped = visual.torques.Select(t => t.localRotation).ToArray();
                yield return new WaitForSecondsRealtime(0.4f);
                for (int i = 0; i < visual.torques.Length; i++)
                    stoppedDrift = Mathf.Max(stoppedDrift, Quaternion.Angle(stopped[i], visual.torques[i].localRotation));
                CheckThat(checks, "stopped-wheels-hold", motor.Speed == 0f && stoppedDrift < 0.01f,
                    "Parked state reaches exactly zero speed; wheel movement "
                    + stoppedDrift.ToString("G4") + " degrees");

                Time.timeScale = 0f;
                var paused = visual.torques.Select(t => t.localRotation)
                    .Concat(visual.suspensions.Select(t => t.localRotation)).ToArray();
                yield return new WaitForSecondsRealtime(0.4f);
                float pausedDrift = 0f;
                var nodes = visual.torques.Select(t => t.localRotation)
                    .Concat(visual.suspensions.Select(t => t.localRotation)).ToArray();
                for (int i = 0; i < paused.Length; i++) pausedDrift = Mathf.Max(pausedDrift, Quaternion.Angle(paused[i], nodes[i]));
                CheckThat(checks, "pause-freezes-visuals", pausedDrift == 0f,
                    "Zero game time through the pause path freezes wheel and suspension transforms");

                Time.timeScale = 1f;
                motor.ResetAt(position, originalPosition, false);
                motor.driving = true;
                motor.throttle = motor.steering = 0f;
                motor.braking = false;
                var controller = motor.GetComponent<CharacterController>();
                for (int frame = 0; frame < 60 && !motor.Grounded; frame++) yield return new WaitForEndOfFrame();
                bool startedGrounded = motor.Grounded && controller.isGrounded;
                int beforeJumpLandings = motor.Landings;
                bool airborne = false, landedSameFrame = false;
                int groundMismatch = 0, observedSteps = 0;
                int previousFixedStep = motor.FixedStepCount;
                int previousLandings = beforeJumpLandings;
                if (startedGrounded) motor.jump = true;
                for (int frame = 0; frame < 180 && startedGrounded; frame++)
                {
                    yield return new WaitForEndOfFrame();
                    if (motor.FixedStepCount == previousFixedStep) continue;
                    previousFixedStep = motor.FixedStepCount;
                    observedSteps++;
                    if (motor.Grounded != controller.isGrounded) groundMismatch++;
                    if (!motor.Grounded) airborne = true;
                    if (airborne && motor.Grounded)
                    {
                        landedSameFrame = motor.Landings == previousLandings + 1
                            && motor.Landings == beforeJumpLandings + 1;
                        break;
                    }
                    previousLandings = motor.Landings;
                }
                CheckThat(checks, "grounded-landing-same-frame",
                    startedGrounded && airborne && landedSameFrame && groundMismatch == 0,
                    "Real road jump: Grounded matches CharacterController after " + observedSteps
                    + " fixed steps; mismatch=" + groundMismatch + ", landing event same frame=" + landedSameFrame);
            }
            finally
            {
                visual.frozen = frozen;
                motor.driving = driving;
                motor.throttle = throttle;
                motor.steering = steering;
                motor.braking = false;
                Time.timeScale = originalTimeScale;
                visual.ResetState();
            }
        }

        static IEnumerator CaptureFixtures(NativeRace race, NativeKartWheelVisual visual,
            Vector3 playerSpawnPosition, Quaternion playerSpawnRotation, string directory,
            List<string> captures, List<Check> checks)
        {
            var motor = race.racers[0];
            Vector3 previousMotorPosition = motor.transform.position;
            Quaternion previousMotorRotation = motor.transform.rotation;
            var camera = race.raceCamera;
            var previousPosition = camera.transform.position;
            var previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            bool wasFrozen = visual.frozen;
            Time.timeScale = 0f;
            race.SetVisualCapture("race", false);
            motor.ResetAt(playerSpawnPosition, playerSpawnRotation, false);
            var car = race.racers[0].transform;
            camera.transform.position = car.TransformPoint(new Vector3(3f, 2.1f, -3.5f));
            camera.transform.LookAt(car.position + Vector3.up);
            camera.fieldOfView = 40f;
            try
            {
                visual.frozen = true;
                visual.ResetState();
                yield return Capture("wheels-straight.png");
                for (int i = 0; i < 120; i++)
                    visual.StepForTest(new NativeKartWheelInput
                    {
                        speed = 145f, topSpeed = 145f, steering = 1f, grounded = true
                    }, 1f / 60f);
                yield return Capture("wheels-steered.png");
                visual.ResetState();
                for (int i = 0; i < 10; i++)
                    visual.StepForTest(new NativeKartWheelInput
                    {
                        speed = 145f, topSpeed = 145f, steering = -1f, grounded = true, landed = i == 0
                    }, 1f / 60f);
                yield return Capture("wheels-loaded.png");
            }
            finally
            {
                visual.frozen = wasFrozen;
                visual.ResetState();
                motor.ResetAt(previousMotorPosition, previousMotorRotation, false);
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }

            IEnumerator Capture(string name)
            {
                yield return new WaitForSecondsRealtime(0.4f);
                yield return new WaitForEndOfFrame();
                try
                {
                    var image = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(directory, name), image.EncodeToPNG());
                    UnityEngine.Object.Destroy(image);
                    captures.Add(name);
                    Debug.Log("NATIVE_WHEEL_CAPTURE " + name + " fixture=forced-state");
                }
                catch (Exception error)
                {
                    CheckThat(checks, "capture-" + name, false, error.ToString());
                }
            }
        }

        static bool OriginalSettings(NativeKartWheelSettings settings)
        {
            if (settings == null || settings._wheels == null || settings._wheels.Length != 4) return false;
            var front = settings._wheels.Where(w => w._steering > 0f).ToArray();
            var rear = settings._wheels.Where(w => w._steering == 0f).ToArray();
            return Near(settings._heightTilt, 0.375f, 0f) && Near(settings._tiltTurn, 8f, 0f)
                && Near(settings._tiltSpdMult, 1.5f, 0f) && Near(settings._maxTilt, 15f, 0f)
                && Near(settings.maxSuspension, 8f, 0f) && Near(settings._landSuspension, 8f, 0f)
                && Near(settings.targetGroundSuspension, 0f, 0f) && Near(settings.targetAirSuspension, -0.6f, 0f)
                && Near(settings._tiltDamping, 15f, 0f) && Near(settings._tiltStiff, 200f, 0f)
                && Near(settings.steeringWheelAngle, 110f, 0f)
                && Near(settings.stiffness, 150f, 0f) && Near(settings.damping, 5f, 0f)
                && Near(settings.valueThreshold, 0.01f, 0f) && Near(settings.velocityThreshold, 0.01f, 0f)
                && front.Length == 2 && rear.Length == 2
                && front.All(w => Near(w._wheelRadius, 0.19f, 0f) && Near(w._steering, 20f, 0f)
                    && Near(w._suspensionHeight, 0.0025f, 0f))
                && rear.All(w => Near(w._wheelRadius, 0.215f, 0f) && Near(w._steering, 0f, 0f)
                    && Near(w._suspensionHeight, 0.0025f, 0f));
        }

        static bool Near(float a, float b, float tolerance) => Mathf.Abs(a - b) <= tolerance;

        static void CheckThat(List<Check> checks, string name, bool passed, string detail)
        {
            checks.Add(new Check { name = name, passed = passed, detail = detail });
            if (!passed) Debug.LogError("NATIVE_WHEEL_CHECK_FAILED " + name + ": " + detail);
        }
    }
}
