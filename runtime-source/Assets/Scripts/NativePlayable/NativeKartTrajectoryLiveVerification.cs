using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartTrajectoryLiveVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string scenario, mode;
            public int step, fixedStep;
            public float dt, throttle, steering, speed, turn, directionError, magnitudeError;
            public bool braking, drifting, isDrifting, groundedBefore, groundedAfter, compared;
            public Vector3 position, forward, groundNormal, commandVelocity, expectedVelocity, physicsVelocity;
            public Quaternion rotation;
        }
        [Serializable] sealed class Scenario
        {
            public string name;
            public int comparedSteps, groundedSteps;
            public float maxDirectionError, maxMagnitudeError;
            public Vector3 start, end, displacement;
            public Sample[] samples;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, mode, boundary;
            public bool passed;
            public float fixedDeltaTime, worldSpeedDivisor;
            public Check[] checks;
            public Scenario[] scenarios;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            bool baseline = Environment.GetCommandLineArgs().Contains("--trajectory-baseline");
            AudioListener.volume = 0f;
            var checks = new List<Check>();
            var scenarios = new List<Scenario>();
            var fixtures = new List<GameObject>();
            var racerEnabled = race.racers.Select(r => r.enabled).ToArray();
            float originalTimeScale = Time.timeScale;
            GameObject probe = null;
            var tick = new WaitForFixedUpdate();
            try
            {
                race.SetVisualCapture("race", false);
                foreach (var racer in race.racers) racer.enabled = false;
                probe = UnityEngine.Object.Instantiate(race.racers[0].gameObject);
                probe.name = "Trajectory verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                motor.enabled = false;
                motor.driving = true;
                Time.timeScale = 1f;

                Add("global-silence", AudioListener.volume == 0f, "AudioListener volume=" + AudioListener.volume);
                Add("fixed-step-60hz", Mathf.Abs(Time.fixedDeltaTime - 1f / 60f) < 0.000001f,
                    "Fixed delta time=" + Time.fixedDeltaTime);
                Add("character-controller", probe.GetComponent<CharacterController>() != null,
                    "Isolated kart carries a real CharacterController");

                MakeGround("trajectory flat", new Vector3(0f, 499.5f, 0f), Quaternion.identity);
                MakeGround("trajectory slope", new Vector3(2000f, 499.5f, 0f), Quaternion.Euler(-20f, 0f, 0f));
                Physics.SyncTransforms();

                string[] names = { "straight", "left", "right", "switch", "drift", "slope" };
                foreach (string name in names)
                {
                    Vector3 anchor = name == "slope" ? new Vector3(2000f, 500.3f, 0f) : new Vector3(0f, 500.3f, 0f);
                    motor.enabled = false;
                    motor.ResetAt(anchor, Quaternion.identity, false);
                    motor.driving = true;
                    motor.throttle = motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                    motor.enabled = true;
                    for (int i = 0; i < 30; i++) yield return tick;
                    var scenario = new Scenario { name = name, start = motor.transform.position };
                    var samples = new List<Sample>();
                    for (int step = 0; step < 240; step++)
                    {
                        SetInputs(name, step, motor);
                        bool groundedBefore = motor.Grounded;
                        int beforeStep = motor.FixedStepCount;
                        yield return tick;
                        bool exactlyOneStep = motor.FixedStepCount == beforeStep + 1;
                        if (!exactlyOneStep) Add("fixed-step-" + name + "-" + step, false,
                            "Observed fixed step delta=" + (motor.FixedStepCount - beforeStep));
                        Vector3 forward = motor.transform.forward.normalized;
                        Vector3 expected = forward * motor.Speed;
                        expected.y = 0f;
                        Vector3 command = motor.DriveVelocity;
                        Vector3 horizontal = new Vector3(command.x, 0f, command.z);
                        bool compared = groundedBefore && motor.Grounded && !motor.JumpedThisStep && exactlyOneStep;
                        float directionError = horizontal.sqrMagnitude > 0.000001f && expected.sqrMagnitude > 0.000001f
                            ? Vector3.Angle(horizontal, expected) : 0f;
                        float magnitudeError = Vector3.Distance(horizontal, expected);
                        if (compared)
                        {
                            scenario.comparedSteps++;
                            scenario.maxDirectionError = Mathf.Max(scenario.maxDirectionError, directionError);
                            scenario.maxMagnitudeError = Mathf.Max(scenario.maxMagnitudeError, magnitudeError);
                        }
                        if (motor.Grounded) scenario.groundedSteps++;
                        samples.Add(new Sample
                        {
                            scenario = name, mode = baseline ? "baseline" : "restored", step = step,
                            fixedStep = motor.FixedStepCount, dt = Time.fixedDeltaTime,
                            throttle = motor.throttle, steering = motor.steering,
                            braking = motor.braking, drifting = motor.drifting, isDrifting = motor.IsDrifting,
                            speed = motor.Speed, turn = motor.CurrentTurn,
                            groundedBefore = groundedBefore, groundedAfter = motor.Grounded, compared = compared,
                            position = motor.transform.position, rotation = motor.transform.rotation,
                            forward = forward, groundNormal = motor.GroundNormal,
                            commandVelocity = command, expectedVelocity = expected,
                            physicsVelocity = motor.PhysicsVelocity,
                            directionError = directionError, magnitudeError = magnitudeError
                        });
                    }
                    scenario.end = motor.transform.position;
                    scenario.displacement = scenario.end - scenario.start;
                    scenario.samples = samples.ToArray();
                    scenarios.Add(scenario);
                    Add("ground-contact-" + name, scenario.comparedSteps >= 200,
                        "Continuous grounded steps=" + scenario.comparedSteps);
                    if (name == "drift")
                        Add("drift-active", samples.Count(s => s.isDrifting && s.compared) >= 30,
                            "Grounded active drift steps=" + samples.Count(s => s.isDrifting && s.compared));
                    if (!baseline)
                        Add("direct-ground-velocity-" + name,
                            scenario.maxMagnitudeError < 0.0001f && scenario.maxDirectionError < 0.05f,
                            "Max vector error=" + scenario.maxMagnitudeError + "; direction error=" + scenario.maxDirectionError);
                }

                motor.enabled = false;
                motor.ResetAt(new Vector3(0f, 500.3f, 0f), Quaternion.identity, false);
                motor.enabled = true;
                for (int i = 0; i < 30; i++) yield return tick;
                motor.throttle = 1f;
                for (int i = 0; i < 12; i++) yield return tick;
                Time.timeScale = 0f;
                Vector3 pausePosition = motor.transform.position;
                Quaternion pauseRotation = motor.transform.rotation;
                float pauseSpeed = motor.Speed;
                int pauseStep = motor.FixedStepCount;
                yield return new WaitForSecondsRealtime(0.2f);
                Add("pause-freezes-motor", motor.FixedStepCount == pauseStep && motor.transform.position == pausePosition
                    && motor.transform.rotation == pauseRotation && motor.Speed == pauseSpeed,
                    "Fixed steps during pause=" + (motor.FixedStepCount - pauseStep));
                motor.ResetAt(new Vector3(0f, 500.3f, 0f), Quaternion.identity, false);
                Add("reset-clears-motion", motor.Speed == 0f && motor.DriveVelocity == Vector3.zero
                    && motor.CurrentTurn == 0f && !motor.Grounded,
                    "Reset clears speed, command velocity, steering and contact state");
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                if (probe) UnityEngine.Object.Destroy(probe);
                foreach (var fixture in fixtures) UnityEngine.Object.Destroy(fixture);
                for (int i = 0; i < race.racers.Length; i++) race.racers[i].enabled = racerEnabled[i];
            }

            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                mode = baseline ? "baseline" : "restored", fixedDeltaTime = Time.fixedDeltaTime,
                worldSpeedDivisor = 1f,
                boundary = "Same Player harness with real CharacterController. Baseline samples the prior ground assembly; restored mode asserts direct post-turn forward velocity on continuous grounded steps. Speed uses original world units; FixedUpdate scheduling remains a separate comparison boundary.",
                passed = checks.All(c => c.passed), checks = checks.ToArray(), scenarios = scenarios.ToArray()
            };
            File.WriteAllText(Path.Combine(directory, "trajectory-live-report.json"), JsonUtility.ToJson(report, true));
            WriteCsv(Path.Combine(directory, "trajectory-steps.csv"), scenarios);
            Debug.Log("NATIVE_TRAJECTORY_RESULT passed=" + report.passed + " mode=" + report.mode + " checks=" + checks.Count);
            yield return null;
            Application.Quit(report.passed ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            void MakeGround(string name, Vector3 position, Quaternion rotation)
            {
                var ground = new GameObject(name);
                ground.transform.SetPositionAndRotation(position, rotation);
                var box = ground.AddComponent<BoxCollider>();
                box.size = new Vector3(1200f, 1f, 1200f);
                fixtures.Add(ground);
            }
        }

        static void SetInputs(string name, int step, NativeKartMotor motor)
        {
            motor.throttle = step < 195 ? 1f : 0f;
            motor.steering = step < 60 ? 0f
                : name == "left" ? -0.65f : name == "right" ? 0.65f
                : name == "switch" ? (step < 150 ? 0.7f : -0.7f)
                : name == "drift" ? 0.65f
                : name == "slope" && step < 150 ? 0.35f : 0f;
            motor.braking = name == "drift" && step >= 75 && step < 195;
            motor.drifting = motor.braking;
            motor.jump = motor.jumpHeld = false;
        }

        static void WriteCsv(string path, IEnumerable<Scenario> scenarios)
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("scenario,mode,step,fixedStep,dt,throttle,steering,braking,drifting,isDrifting,groundedBefore,groundedAfter,compared,speed,turn,positionX,positionY,positionZ,rotationX,rotationY,rotationZ,rotationW,forwardX,forwardY,forwardZ,groundNormalX,groundNormalY,groundNormalZ,commandX,commandY,commandZ,expectedX,expectedY,expectedZ,physicsX,physicsY,physicsZ,directionError,magnitudeError");
                foreach (var scenario in scenarios)
                    foreach (var s in scenario.samples)
                        writer.WriteLine(string.Join(",", new[]
                        {
                            s.scenario, s.mode, s.step.ToString(), s.fixedStep.ToString(), F(s.dt), F(s.throttle), F(s.steering),
                            s.braking.ToString(), s.drifting.ToString(), s.isDrifting.ToString(),
                            s.groundedBefore.ToString(), s.groundedAfter.ToString(), s.compared.ToString(),
                            F(s.speed), F(s.turn), F(s.position.x), F(s.position.y), F(s.position.z),
                            F(s.rotation.x), F(s.rotation.y), F(s.rotation.z), F(s.rotation.w),
                            F(s.forward.x), F(s.forward.y), F(s.forward.z),
                            F(s.groundNormal.x), F(s.groundNormal.y), F(s.groundNormal.z),
                            F(s.commandVelocity.x), F(s.commandVelocity.y), F(s.commandVelocity.z),
                            F(s.expectedVelocity.x), F(s.expectedVelocity.y), F(s.expectedVelocity.z),
                            F(s.physicsVelocity.x), F(s.physicsVelocity.y), F(s.physicsVelocity.z),
                            F(s.directionError), F(s.magnitudeError)
                        }));
            }
        }

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
