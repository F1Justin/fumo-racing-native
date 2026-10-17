using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartSteeringLiveVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int step;
            public float turn, speed, yaw;
            public Vector3 position;
            public bool grounded, restored;
        }
        [Serializable] sealed class DriveCase
        {
            public string name;
            public float input, startSpeed, endSpeed, yaw, maxTurnError, maxSpeedError, maxRotationError;
            public int comparedSteps;
            public Vector3 displacement;
            public Sample[] samples;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public Check[] checks;
            public DriveCase[] cases;
        }

        public static IEnumerator Run(NativeRace race, string directory, string referencePath)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            Add("native-machine-code-reference", NativeKartSteeringVerification.RunReference(referencePath, directory),
                "Actual steering state compared with original x64 scalar execution; see steering-reference-report.json");
            var motor = race.racers[0];
            var wheels = motor.GetComponent<NativeKartWheelVisual>();
            var cases = new List<DriveCase>();
            race.SetVisualCapture("race", false);
            foreach (var racer in race.racers) racer.enabled = false;
            var ground = new GameObject("Steering verification flat contact");
            ground.transform.position = new Vector3(0, 499.5f, 0);
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(1600, 1, 1600);
            Physics.SyncTransforms();
            var anchor = new Vector3(0, 500.3f, 0);
            motor.enabled = true;
            Time.timeScale = 1f;
            var tick = new WaitForFixedUpdate();
            try
            {
                Add("serialized-original-settings", race.racers.All(r =>
                    r.stats._turnForce == 125f && r.stats._turnMax == 25f && r.stats._turnLowMult == 0.5f
                    && r.stats._turnFriction == 160f && r.stats._turnLoseSpeed == 45f && r.stats._smooth == 3f),
                    "All four exported motors carry original KartStats and PlayerKart steering parameters");
                Reset();
                for (int i = 0; i < 30; i++) yield return tick;
                Vector3 idlePosition = motor.transform.position;
                Quaternion idleRotation = motor.transform.rotation;
                motor.steering = 1f;
                for (int i = 0; i < 30; i++) yield return tick;
                float idleYaw = Vector3.SignedAngle(idleRotation * Vector3.forward, motor.transform.forward, Vector3.up);
                Add("stationary-turn", motor.Grounded && motor.UsesRestoredSteering && motor.Speed == 0f
                    && motor.CurrentTurn > 25f && idleYaw > 20f
                    && Vector3.Distance(idlePosition, motor.transform.position) < 0.02f,
                    "Standstill yaw=" + idleYaw + "; currentTurn=" + motor.CurrentTurn + "; original low-speed steering remains active");

                foreach (float input in new[] { 0f, -0.7f, 0.7f })
                {
                    Reset();
                    for (int i = 0; i < 30; i++) yield return tick;
                    motor.throttle = 1f;
                    for (int i = 0; i < 120; i++) yield return tick;
                    motor.throttle = 0f;
                    motor.steering = input;
                    var drive = new DriveCase { name = input == 0f ? "straight" : input < 0 ? "left" : "right", input = input, startSpeed = motor.Speed };
                    Vector3 start = motor.transform.position;
                    var samples = new List<Sample>();
                    for (int i = 0; i < 60; i++)
                    {
                        var expected = NativeKartSteeringState.Step(motor.CurrentTurn,
                            new NativeKartSteeringInput { speed = motor.Speed, wishTurn = input },
                            motor.stats, Time.fixedDeltaTime);
                        float expectedSpeed = NativeKartAccelerationState.Step(expected.currentSpeed,
                            new NativeKartAccelerationInput { slopeLimit = motor.GetComponent<CharacterController>().slopeLimit,
                                topSpeedModifier = 1f }, motor.stats, Time.fixedDeltaTime).currentSpeed;
                        Quaternion expectedRotation = expected.GroundRotation(motor.transform.rotation, Vector3.up, motor.stats, Time.fixedDeltaTime);
                        yield return tick;
                        if (motor.Grounded && motor.UsesRestoredSteering) drive.comparedSteps++;
                        drive.maxTurnError = Mathf.Max(drive.maxTurnError, Mathf.Abs(motor.CurrentTurn - expected.currentTurn));
                        drive.maxSpeedError = Mathf.Max(drive.maxSpeedError, Mathf.Abs(motor.Speed - expectedSpeed));
                        drive.maxRotationError = Mathf.Max(drive.maxRotationError, Quaternion.Angle(motor.transform.rotation, expectedRotation));
                        samples.Add(new Sample { phase = drive.name, step = i, turn = motor.CurrentTurn,
                            speed = motor.Speed, yaw = Yaw(), position = motor.transform.position - start,
                            grounded = motor.Grounded, restored = motor.UsesRestoredSteering });
                    }
                    drive.endSpeed = motor.Speed;
                    drive.yaw = Yaw();
                    drive.displacement = motor.transform.position - start;
                    drive.samples = samples.ToArray();
                    cases.Add(drive);
                    Add("live-integration-" + drive.name, drive.comparedSteps == 60
                        && drive.maxTurnError < 0.0001f && drive.maxSpeedError < 0.0001f && drive.maxRotationError < 0.08f,
                        "60 real CharacterController steps; original steering loss precedes original coasting; yaw error=" + drive.maxRotationError);
                }
                Add("turning-speed-loss", cases[0].endSpeed - cases[1].endSpeed > 0.5f
                    && Mathf.Abs(cases[1].endSpeed - cases[2].endSpeed) < 0.001f,
                    "Straight speed=" + cases[0].endSpeed + "; left=" + cases[1].endSpeed + "; right=" + cases[2].endSpeed);
                Add("left-right-trajectory", cases[1].yaw < -20f && cases[2].yaw > 20f
                    && Mathf.Abs(cases[1].yaw + cases[2].yaw) < 0.1f
                    && Mathf.Abs(cases[1].displacement.x + cases[2].displacement.x) < 0.02f
                    && Mathf.Abs(cases[1].displacement.z - cases[2].displacement.z) < 0.02f,
                    "Flat PhysX contact with mirrored input; left yaw=" + cases[1].yaw + "; right yaw=" + cases[2].yaw);
                yield return new WaitForEndOfFrame();
                Add("wheel-input-from-turn", Mathf.Abs(wheels.State.currentSteering - motor.CurrentTurn / motor.stats._turnMax) < 0.0001f,
                    "Live wheel and body visual component receives the restored angular state");
                motor.steering = 0f;
                for (int i = 0; i < 20; i++) yield return tick;
                Add("release-centers", motor.CurrentTurn == 0f, "Released steering returns to zero through original turn friction");
                motor.steering = 0.7f;
                motor.throttle = 1f;
                motor.braking = true;
                motor.drifting = true;
                yield return tick;
                Add("drift-restored-ground-branch", motor.Grounded && motor.IsDrifting && motor.UsesRestoredSteering,
                    "Held brake, throttle and steering enter the restored drift steering branch");
                motor.drifting = false;
                motor.braking = false;
                motor.jump = true;
                yield return tick;
                yield return tick;
                Add("air-adapter-boundary", !motor.Grounded && !motor.UsesRestoredSteering,
                    "Airborne steering remains on the existing controller branch");
                Time.timeScale = 0f;
                Vector3 pausePosition = motor.transform.position;
                Quaternion pauseRotation = motor.transform.rotation;
                float pauseTurn = motor.CurrentTurn, pauseSpeed = motor.Speed;
                yield return new WaitForSecondsRealtime(0.25f);
                Add("pause-freezes-motor", motor.transform.position == pausePosition
                    && motor.transform.rotation == pauseRotation && motor.CurrentTurn == pauseTurn && motor.Speed == pauseSpeed,
                    "Pause retains position, rotation, speed and angular state");
                Reset();
                Add("reset-clears-turn", motor.CurrentTurn == 0f && motor.CurrentSteering == 0f
                    && motor.Speed == 0f && !motor.UsesRestoredSteering,
                    "Reset clears original angular state and the active-branch indicator");
                yield return NativeKartSteeringSlopeVerification.Run(motor, directory, Add);
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);
            }
            finally
            {
                Time.timeScale = 0f;
                motor.enabled = false;
                UnityEngine.Object.Destroy(ground);
            }
            var report = new Report { version = Application.version, platform = SystemInfo.operatingSystem,
                boundary = "Real CharacterController on flat and 20-degree BoxColliders, plus a real 68-degree wall contact. Forest racing is checked separately. Original grounded acceleration follows steering loss and includes the active drift branch. Motor speed and steering state share original world units; airborne integration is checked separately.",
                passed = checks.All(c => c.passed), checks = checks.ToArray(), cases = cases.ToArray() };
            File.WriteAllText(Path.Combine(directory, "steering-live-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_STEERING_RESULT passed=" + report.passed + " checks=" + checks.Count);
            yield return null;
            Application.Quit(report.passed ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            float Yaw() => Vector3.SignedAngle(Vector3.forward, motor.transform.forward, Vector3.up);
            void Reset()
            {
                motor.ResetAt(anchor, Quaternion.identity, false);
                wheels.ResetState();
                motor.driving = true;
                motor.throttle = motor.steering = 0f;
                motor.braking = motor.drifting = false;
            }
        }
    }
}
