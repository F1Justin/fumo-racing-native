using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartClockVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string scenario;
            public int index, frameBefore, frameAfter, lastStepFrame, stepBefore, stepAfter;
            public float requestedDeltaTime, observedDeltaTime, deltaTimeError, throttle, steering;
            public float speedBefore, speed, turnBefore, turn, speedError, turnError, rotationError, movementError;
            public double realtimeBefore, realtimeAfter;
            public bool groundedBefore, groundedAfter, fixedVerificationClock;
            public Vector3 positionBefore, position, driveVelocity, physicsVelocity;
        }
        [Serializable] sealed class Scenario
        {
            public string name;
            public int frameCount, schedulingFailures, groundedSteps;
            public float simulatedSeconds, maxDeltaTimeError, maxSpeedError, maxTurnError, maxRotationError, maxMovementError;
            public double wallSeconds;
            public Vector3 start, end;
            public Sample[] samples;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public int targetFrameRate, vSyncCount;
            public float initialFixedDeltaTime;
            public Check[] checks;
            public Scenario[] scenarios;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var scenarios = new List<Scenario>();
            var enabledBefore = race.racers.Select(r => r.enabled).ToArray();
            var controllerBefore = race.racers.Select(r => r.GetComponent<CharacterController>().enabled).ToArray();
            float timeScaleBefore = Time.timeScale, captureBefore = Time.captureDeltaTime;
            float initialFixedDeltaTime = Time.fixedDeltaTime;
            GameObject probe = null, ground = null;
            try
            {
                race.SetVisualCapture("race", false);
                foreach (var racer in race.racers)
                {
                    racer.enabled = false;
                    racer.GetComponent<CharacterController>().enabled = false;
                }
                probe = UnityEngine.Object.Instantiate(race.racers[0].gameObject);
                probe.name = "Update clock isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                motor.enabled = false;
                motor.UseFixedVerificationClock = false;
                ground = new GameObject("Update clock flat ground");
                ground.transform.position = new Vector3(0f, 499.5f, 0f);
                ground.AddComponent<BoxCollider>().size = new Vector3(2400f, 1f, 2400f);
                Physics.SyncTransforms();
                Time.timeScale = 1f;
                Time.captureDeltaTime = 1f / 60f;
                Add("real-character-controller", probe.GetComponent<CharacterController>() != null,
                    "A real isolated CharacterController moves on a flat BoxCollider");
                Add("default-update-clock", !motor.UseFixedVerificationClock,
                    "All cases explicitly exercise Update and retain the configured fixedDeltaTime");
                Add("construction-silence-before", Silent(), "Global audio listener is muted and menu soundtrack is disabled");

                foreach (string name in new[] { "30fps", "60fps", "120fps", "variable-delta" })
                {
                    motor.enabled = false;
                    motor.ResetAt(new Vector3(0f, 500.3f, 0f), Quaternion.identity, false);
                    motor.UseFixedVerificationClock = false;
                    motor.driving = true;
                    motor.throttle = motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                    Time.captureDeltaTime = 1f / 60f;
                    motor.enabled = true;
                    for (int settle = 0; settle < 30; settle++) yield return null;
                    Add("grounded-before-" + name, motor.Grounded, "Settled on the test plane before recording inputs");
                    var samples = new List<Sample>();
                    var scenario = new Scenario { name = name, start = motor.transform.position };
                    for (int index = 0; index < 120; index++)
                    {
                        float dt = Delta(name, index);
                        Time.captureDeltaTime = dt;
                        motor.throttle = index % 24 < 16 ? 1f : 0f;
                        motor.steering = index % 36 < 12 ? 0f : index % 36 < 24 ? .65f : -.65f;
                        motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                        var sample = new Sample
                        {
                            scenario = name, index = index, requestedDeltaTime = dt,
                            frameBefore = Time.frameCount, stepBefore = motor.FixedStepCount,
                            realtimeBefore = Time.realtimeSinceStartupAsDouble,
                            throttle = motor.throttle, steering = motor.steering,
                            speedBefore = motor.Speed, turnBefore = motor.CurrentTurn,
                            positionBefore = motor.transform.position, groundedBefore = motor.Grounded
                        };
                        var expectedTurn = NativeKartSteeringState.Step(motor.CurrentTurn,
                            new NativeKartSteeringInput { speed = motor.Speed, wishTurn = motor.steering }, motor.stats, dt);
                        var expectedSpeed = NativeKartAccelerationState.Step(expectedTurn.currentSpeed,
                            new NativeKartAccelerationInput { wishAccel = motor.throttle,
                                slopeLimit = probe.GetComponent<CharacterController>().slopeLimit,
                                groundAngle = motor.GroundAngle, onGrass = motor.OnGrass || motor.InWater,
                                topSpeedModifier = 1f, currentTurn = expectedTurn.currentTurn,
                                turnLimit = expectedTurn.turnLimit }, motor.stats, dt);
                        Quaternion expectedRotation = expectedTurn.GroundRotation(motor.transform.rotation, motor.GroundNormal, motor.stats, dt);
                        Vector3 expectedMovement = expectedRotation * Vector3.forward * expectedSpeed.currentSpeed * dt;
                        expectedMovement.y = 0f;
                        yield return null;
                        sample.frameAfter = Time.frameCount;
                        sample.stepAfter = motor.FixedStepCount;
                        sample.lastStepFrame = motor.LastStepFrame;
                        sample.realtimeAfter = Time.realtimeSinceStartupAsDouble;
                        sample.observedDeltaTime = motor.LastStepDeltaTime;
                        sample.deltaTimeError = Mathf.Abs(sample.observedDeltaTime - dt);
                        sample.speed = motor.Speed;
                        sample.turn = motor.CurrentTurn;
                        sample.position = motor.transform.position;
                        sample.driveVelocity = motor.DriveVelocity;
                        sample.physicsVelocity = motor.PhysicsVelocity;
                        sample.groundedAfter = motor.Grounded;
                        sample.fixedVerificationClock = motor.UseFixedVerificationClock;
                        sample.speedError = Mathf.Abs(motor.Speed - expectedSpeed.currentSpeed);
                        sample.turnError = Mathf.Abs(motor.CurrentTurn - expectedTurn.currentTurn);
                        sample.rotationError = Quaternion.Angle(motor.transform.rotation, expectedRotation);
                        Vector3 displacement = sample.position - sample.positionBefore;
                        displacement.y = 0f;
                        sample.movementError = Vector3.Distance(displacement, expectedMovement);
                        samples.Add(sample);
                    }
                    scenario.samples = samples.ToArray();
                    scenario.frameCount = samples.Count;
                    scenario.schedulingFailures = samples.Count(s => s.stepAfter != s.stepBefore + 1
                        || s.frameAfter != s.frameBefore + 1 || s.lastStepFrame != s.frameAfter || s.fixedVerificationClock);
                    scenario.groundedSteps = samples.Count(s => s.groundedBefore && s.groundedAfter);
                    scenario.simulatedSeconds = samples.Sum(s => s.observedDeltaTime);
                    scenario.wallSeconds = samples.Last().realtimeAfter - samples.First().realtimeBefore;
                    scenario.maxDeltaTimeError = samples.Max(s => s.deltaTimeError);
                    scenario.maxSpeedError = samples.Max(s => s.speedError);
                    scenario.maxTurnError = samples.Max(s => s.turnError);
                    scenario.maxRotationError = samples.Max(s => s.rotationError);
                    scenario.maxMovementError = samples.Max(s => s.movementError);
                    scenario.end = motor.transform.position;
                    scenarios.Add(scenario);
                    Add("one-update-per-frame-" + name, scenario.schedulingFailures == 0,
                        "Recorded frames=" + samples.Count + "; scheduling failures=" + scenario.schedulingFailures);
                    Add("frame-delta-consumed-" + name, scenario.maxDeltaTimeError < .000001f,
                        "Largest requested-to-consumed delta error=" + scenario.maxDeltaTimeError);
                    Add("same-frame-input-" + name, scenario.maxSpeedError < .0001f && scenario.maxTurnError < .0001f
                        && scenario.maxRotationError < .08f,
                        "Current frame inputs feed steering then acceleration; max speed/turn/rotation errors="
                        + scenario.maxSpeedError + "/" + scenario.maxTurnError + "/" + scenario.maxRotationError);
                    Add("continuous-real-motion-" + name, scenario.groundedSteps == samples.Count
                        && scenario.maxMovementError < .001f && Vector3.Distance(scenario.start, scenario.end) > 1f,
                        "Grounded frames=" + scenario.groundedSteps + "; largest flat controller displacement error=" + scenario.maxMovementError);
                }

                Time.timeScale = 0f;
                int pausedStep = motor.FixedStepCount, pausedLastFrame = motor.LastStepFrame;
                Vector3 pausedPosition = motor.transform.position, pausedVelocity = motor.DriveVelocity;
                Quaternion pausedRotation = motor.transform.rotation;
                float pausedSpeed = motor.Speed, pausedTurn = motor.CurrentTurn;
                for (int pauseFrame = 0; pauseFrame < 12; pauseFrame++) yield return null;
                Add("pause-freezes-update", motor.FixedStepCount == pausedStep && motor.LastStepFrame == pausedLastFrame
                    && motor.transform.position == pausedPosition && motor.transform.rotation == pausedRotation
                    && motor.DriveVelocity == pausedVelocity && motor.Speed == pausedSpeed && motor.CurrentTurn == pausedTurn,
                    "Twelve actual rendered frames elapsed while simulation steps remained=" + (motor.FixedStepCount - pausedStep));
                Time.captureDeltaTime = 1f / 60f;
                Time.timeScale = 1f;
                motor.throttle = 1f;
                int resumeFrame = Time.frameCount, resumeStep = motor.FixedStepCount;
                yield return null;
                Add("resume-on-next-update", motor.FixedStepCount == resumeStep + 1
                    && motor.LastStepFrame == resumeFrame + 1 && motor.LastStepFrame == Time.frameCount
                    && Mathf.Abs(motor.LastStepDeltaTime - 1f / 60f) < .000001f,
                    "Resume produces exactly one step on the next rendered frame");
                motor.enabled = false;
                motor.ResetAt(new Vector3(0f, 500.3f, 0f), Quaternion.identity, false);
                Add("reset-clears-state", motor.Speed == 0f && motor.CurrentTurn == 0f
                    && motor.DriveVelocity == Vector3.zero && motor.PhysicsVelocity == Vector3.zero
                    && !motor.Grounded && motor.AirTime == 0f && !motor.IsDrifting,
                    "Reset clears simulated motion, contact and drift state");
                motor.throttle = motor.steering = 0f;
                motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                motor.enabled = true;
                int resetStep = motor.FixedStepCount, resetFrame = Time.frameCount;
                yield return null;
                Add("reset-preserves-update-mode", !motor.UseFixedVerificationClock
                    && motor.FixedStepCount == resetStep + 1 && motor.LastStepFrame == resetFrame + 1,
                    "Reset retains the Update clock and subsequent frame scheduling");
                Add("fixed-clock-unchanged", Time.fixedDeltaTime == initialFixedDeltaTime,
                    "Configured fixedDeltaTime=" + Time.fixedDeltaTime);
                Add("construction-silence-after", Silent(), "Global audio silence remains active after every case");
                Add("runtime-errors", race.VisualErrorCount == 0, "Runtime errors=" + race.VisualErrorCount);
            }
            finally
            {
                if (probe) { probe.GetComponent<NativeKartMotor>().enabled = false; UnityEngine.Object.Destroy(probe); }
                if (ground) UnityEngine.Object.Destroy(ground);
                Time.captureDeltaTime = captureBefore;
                Time.timeScale = timeScaleBefore;
                for (int i = 0; i < race.racers.Length; i++)
                {
                    race.racers[i].GetComponent<CharacterController>().enabled = controllerBefore[i];
                    race.racers[i].enabled = enabledBefore[i];
                }
            }
            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                passed = checks.All(c => c.passed), checks = checks.ToArray(), scenarios = scenarios.ToArray(),
                targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount,
                initialFixedDeltaTime = initialFixedDeltaTime,
                boundary = "Real Update scheduling and CharacterController movement at four controlled frame-delta sequences. Time.captureDeltaTime sets simulated time per rendered frame; recorded realtime intervals describe actual throughput without claiming wall-clock 120 FPS. Formula checks establish input timing, while independent original machine-code parity belongs to the separate steering and acceleration suites."
            };
            File.WriteAllText(Path.Combine(directory, "clock-live-report.json"), JsonUtility.ToJson(report, true));
            WriteCsv(Path.Combine(directory, "clock-frames.csv"), scenarios);
            Debug.Log("NATIVE_CLOCK_RESULT passed=" + report.passed + " checks=" + checks.Count);
            yield return null;
            Application.Quit(report.passed ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            bool Silent() => NativeAudioRuntime.VerificationSilent();
        }

        static float Delta(string scenario, int index)
        {
            if (scenario == "30fps") return 1f / 30f;
            if (scenario == "60fps") return 1f / 60f;
            if (scenario == "120fps") return 1f / 120f;
            float[] sequence = { 1f / 30f, 1f / 120f, 1f / 60f, 1f / 45f, 1f / 90f, 1f / 75f };
            return sequence[index % sequence.Length];
        }

        static void WriteCsv(string path, IEnumerable<Scenario> scenarios)
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("scenario,index,frameBefore,frameAfter,lastStepFrame,stepBefore,stepAfter,requestedDt,observedDt,realtimeBefore,realtimeAfter,throttle,steering,speedBefore,speed,turnBefore,turn,groundedBefore,groundedAfter,fixedClock,positionX,positionY,positionZ,speedError,turnError,rotationError,movementError");
                foreach (var scenario in scenarios)
                    foreach (var s in scenario.samples)
                        writer.WriteLine(string.Join(",", new[] { s.scenario, s.index.ToString(), s.frameBefore.ToString(),
                            s.frameAfter.ToString(), s.lastStepFrame.ToString(), s.stepBefore.ToString(), s.stepAfter.ToString(),
                            F(s.requestedDeltaTime), F(s.observedDeltaTime), s.realtimeBefore.ToString("R", CultureInfo.InvariantCulture),
                            s.realtimeAfter.ToString("R", CultureInfo.InvariantCulture), F(s.throttle), F(s.steering),
                            F(s.speedBefore), F(s.speed), F(s.turnBefore), F(s.turn), s.groundedBefore.ToString(),
                            s.groundedAfter.ToString(), s.fixedVerificationClock.ToString(), F(s.position.x), F(s.position.y),
                            F(s.position.z), F(s.speedError), F(s.turnError), F(s.rotationError), F(s.movementError) }));
            }
        }
        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
