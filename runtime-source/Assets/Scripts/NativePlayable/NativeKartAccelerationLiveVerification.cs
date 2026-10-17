using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartAccelerationLiveVerification : MonoBehaviour
    {
        public sealed class Result { public bool passed; }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Contact
        {
            public string phase, collider;
            public int step;
            public Vector3 normal, right, point;
            public float signedAngle;
        }
        [Serializable] sealed class Sample
        {
            public int step, fixedStepDelta, contacts;
            public bool groundedBefore, groundedAfter, grassBefore, grassAfter, waterBefore, waterAfter, restored, expectedRestored;
            public float throttle, speedBefore, speedAfter, groundAngleBefore, groundAngleAfter;
            public float expectedSpeed, integrationError, contactAngleError;
            public float verticalSpeed, expectedVerticalSpeed, groundStickError;
            public Vector3 position, physicsVelocity, expectedGroundForward;
            public NativeKartAccelerationState state;
        }
        [Serializable] sealed class DriveCase
        {
            public string name;
            public int steps, groundedSteps, restoredSteps, fallbackSteps, branchMismatchSteps, contacts, grassSteps, roadSteps;
            public float startSpeed, endSpeed, maxIntegrationError, maxContactAngleError, maxGroundStickError;
            public Vector3 displacement;
            public Sample[] samples;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public float fixedDeltaTime, worldSpeedDivisor;
            public Check[] checks;
            public DriveCase[] cases;
            public Contact[] contacts;
        }

        readonly HashSet<Collider> watched = new HashSet<Collider>();
        readonly List<Contact> contacts = new List<Contact>();
        string phase;
        int step;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!watched.Contains(hit.collider)) return;
            Vector3 right = transform.right;
            float angle = Vector3.Angle(Vector3.up, hit.normal);
            if (angle > GetComponent<CharacterController>().slopeLimit) return;
            contacts.Add(new Contact
            {
                phase = phase, step = step, collider = hit.collider.name,
                normal = hit.normal, right = right, point = hit.point,
                signedAngle = angle * Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, hit.normal), right))
            });
        }

        public static IEnumerator Run(NativeRace race, string directory, Result result)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var cases = new List<DriveCase>();
            var fixtures = new List<GameObject>();
            bool[] enabledBefore = race.racers.Select(r => r.enabled).ToArray();
            float timeScaleBefore = Time.timeScale;
            GameObject probe = null;
            NativeKartAccelerationLiveVerification observer = null;
            var tick = new WaitForFixedUpdate();
            try
            {
                foreach (var racer in race.racers) racer.enabled = false;
                probe = Instantiate(race.racers[0].gameObject);
                probe.name = "Acceleration verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                var body = probe.GetComponent<CharacterController>();
                observer = probe.AddComponent<NativeKartAccelerationLiveVerification>();
                motor.enabled = true;
                Add("serialized-original-settings", race.racers.All(r =>
                    r.stats._acceleration == 45f && r.stats._topSpeed == 145f && r.stats._topSpeedGrass == 90f
                    && r.stats._friction == 2f && r.stats._frictionGrass == 40f && r.stats._brakeForce == 50f
                    && r.stats._slopeSpeedGain == 25f && r.stats._slopeAccelGain == 25f
                    && r.stats._slopeSpeedLoss == 0f && r.stats._slopeAccelLoss == 0f
                    && r.stats._topSpeedCap == 200f && r.stats._topSpeedAdd == 0f && r.stats._driftSpeedMaxGain == 0f
                    && r.stats._stickForce == 30f),
                    "All exported motors carry original KartStats driving values and PlayerKart stickForce=30");
                Add("original-controller-contact-settings", body.slopeLimit == 65f
                    && Mathf.Abs(body.skinWidth - 0.015f) < 0.000001f
                    && Mathf.Abs(body.minMoveDistance - 0.0002f) < 0.000001f,
                    "slopeLimit=" + body.slopeLimit + "; skinWidth=" + body.skinWidth + "; minMoveDistance=" + body.minMoveDistance);

                var roadOrigin = new Vector3(0f, 500f, 0f);
                var grassOrigin = new Vector3(1000f, 500f, 0f);
                var transitionOrigin = new Vector3(2000f, 500f, 0f);
                var downhillOrigin = new Vector3(3000f, 500f, 0f);
                var uphillOrigin = new Vector3(4000f, 500f, 0f);
                var waterOrigin = new Vector3(5000f, 500f, 0f);
                Plane("Acceleration verification road", roadOrigin, Quaternion.identity, new Vector3(600f, 1f, 2400f), false);
                Plane("Acceleration verification grass", grassOrigin, Quaternion.identity, new Vector3(600f, 1f, 2400f), true);
                Plane("Acceleration transition road entry", transitionOrigin + Vector3.back * 50f,
                    Quaternion.identity, new Vector3(200f, 1f, 100f), false);
                Plane("Acceleration transition grass", transitionOrigin + Vector3.forward * 10f,
                    Quaternion.identity, new Vector3(200f, 1f, 20f), true);
                Plane("Acceleration transition road exit", transitionOrigin + Vector3.forward * 620f,
                    Quaternion.identity, new Vector3(200f, 1f, 1200f), false);
                Quaternion downhillRotation = Quaternion.Euler(20f, 0f, 0f);
                Quaternion uphillRotation = Quaternion.Euler(-20f, 0f, 0f);
                Plane("Acceleration verification downhill", downhillOrigin, downhillRotation, new Vector3(300f, 1f, 300f), false);
                Plane("Acceleration verification uphill", uphillOrigin, uphillRotation, new Vector3(300f, 1f, 300f), false);
                Plane("Acceleration verification water", waterOrigin, Quaternion.identity, new Vector3(600f, 1f, 2400f), false, true);
                Physics.SyncTransforms();
                Time.timeScale = 1f;

                Reset(roadOrigin, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                NativeRace.ApplyPlayerDrivingInput(motor, true, false, false, false);
                yield return Drive("player-z-held", 4f);
                DriveCase keyboard = cases[cases.Count - 1];
                Add("player-z-original-target", keyboard.samples.All(s => s.throttle == 0.5f)
                    && Mathf.Abs(keyboard.endSpeed - 72.5f) < 0.01f && keyboard.displacement.z > 100f,
                    "Original OnAccelerate input scale=0.5; ordinary flat-road speed=" + keyboard.endSpeed);
                AddIntegration(keyboard);
                NativeRace.ApplyPlayerDrivingInput(motor, false, false, false, false);
                yield return Drive("player-z-released", 1f);
                DriveCase released = cases[cases.Count - 1];
                Add("player-z-release", motor.throttle == 0f && !motor.drifting
                    && Mathf.Abs(released.endSpeed - 70.5f) < 0.01f,
                    "Z release clears throttle and uses original road friction; speed=" + released.endSpeed);
                AddIntegration(released);
                NativeRace.ApplyPlayerDrivingInput(motor, true, false, true, true);
                for (int i = 0; i < Mathf.CeilToInt(0.3f / Time.fixedDeltaTime); i++) yield return tick;
                Add("player-z-brake-drift", motor.throttle == 0.5f && motor.steering == 1f
                    && motor.braking && motor.drifting && motor.IsDrifting,
                    "Z+X+right enters the actual drift state at the original 0.5 player throttle");
                NativeRace.ApplyPlayerDrivingInput(motor, true, false, false, false);
                yield return tick;
                yield return tick;
                Add("player-z-drift-release", motor.throttle == 0.5f && !motor.braking && !motor.drifting && !motor.IsDrifting,
                    "Releasing X and steering exits drift while Z keeps the original throttle");

                Reset(roadOrigin, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                motor.throttle = 0.35f;
                yield return Drive("road-partial-throttle", 3f);
                DriveCase partial = cases[cases.Count - 1];
                Add("partial-throttle-target", Mathf.Abs(partial.endSpeed - 50.75f) < 0.01f
                    && partial.displacement.z > 10f,
                    "0.35 throttle reaches original-domain speed=" + partial.endSpeed + "; travelled=" + partial.displacement.z);
                AddIntegration(partial);

                Reset(roadOrigin, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                motor.throttle = 1f;
                yield return Drive("road-full-throttle", 4f);
                DriveCase full = cases[cases.Count - 1];
                Add("full-throttle-target", Mathf.Abs(full.endSpeed - 145f) < 0.01f
                    && full.displacement.z > partial.displacement.z,
                    "Full throttle reaches original-domain speed=" + full.endSpeed + "; travelled=" + full.displacement.z);
                int halfSecond = Mathf.Max(0, Mathf.RoundToInt(0.5f / Time.fixedDeltaTime) - 1);
                Add("throttle-controls-target", Mathf.Abs(partial.samples[halfSecond].speedAfter - full.samples[halfSecond].speedAfter) < 0.001f
                    && Mathf.Abs(full.samples[halfSecond].speedAfter - 22.5f) < 0.02f,
                    "Partial and full throttle share the original acceleration rate before reaching their different targets");
                AddIntegration(full);
                motor.throttle = 0f;
                yield return Drive("road-coasting", 1f);
                DriveCase coast = cases[cases.Count - 1];
                Add("road-coasting-friction", Mathf.Abs((coast.startSpeed - coast.endSpeed) - 2f) < 0.01f,
                    "One second of straight coasting loses original-domain speed=" + (coast.startSpeed - coast.endSpeed));
                AddIntegration(coast);
                motor.braking = true;
                yield return Drive("road-braking", 1f);
                DriveCase brake = cases[cases.Count - 1];
                Add("brake-follows-friction", Mathf.Abs((brake.startSpeed - brake.endSpeed) - 52f) < 0.02f,
                    "One second of no-throttle braking combines friction 2 and brake 50; loss=" + (brake.startSpeed - brake.endSpeed));
                AddIntegration(brake);
                motor.throttle = 1f;
                yield return Drive("throttle-and-brake", 1f);
                DriveCase simultaneous = cases[cases.Count - 1];
                Add("brake-follows-throttle", Mathf.Abs((simultaneous.startSpeed - simultaneous.endSpeed) - 5f) < 0.02f,
                    "Simultaneous throttle and brake applies acceleration 45 followed by brake 50; loss="
                    + (simultaneous.startSpeed - simultaneous.endSpeed));
                AddIntegration(simultaneous);
                motor.braking = false;
                motor.steering = 0.6f;
                yield return Drive("turn-then-acceleration", 1f);
                DriveCase turning = cases[cases.Count - 1];
                Add("turn-loss-before-acceleration", turning.restoredSteps == turning.steps
                    && turning.branchMismatchSteps == 0 && turning.maxIntegrationError < 0.0001f && Mathf.Abs(motor.CurrentTurn) > 5f,
                    "Each actual step matches steering loss followed by acceleration; max speed/state error=" + turning.maxIntegrationError);

                Reset(grassOrigin, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                motor.throttle = 1f;
                yield return Drive("grass-full-throttle", 3f);
                DriveCase grass = cases[cases.Count - 1];
                Add("grass-contact-target", grass.grassSteps == grass.steps && grass.contacts > 0
                    && Mathf.Abs(grass.endSpeed - 90f) < 0.01f,
                    "Real Grass-tagged contact reaches original-domain target=" + grass.endSpeed);
                AddIntegration(grass);
                motor.throttle = 0f;
                yield return Drive("grass-coasting", 1f);
                DriveCase grassCoast = cases[cases.Count - 1];
                Add("grass-coasting-friction", Mathf.Abs((grassCoast.startSpeed - grassCoast.endSpeed) - 40f) < 0.02f,
                    "One second of grass coasting loses original-domain speed=" + (grassCoast.startSpeed - grassCoast.endSpeed));
                AddIntegration(grassCoast);

                Reset(waterOrigin, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                motor.throttle = 1f;
                yield return Drive("water-full-throttle", 3f);
                DriveCase water = cases[cases.Count - 1];
                Add("water-contact-target", water.samples.All(s => s.waterBefore && s.waterAfter)
                    && water.contacts > 0 && Mathf.Abs(water.endSpeed - 90f) < 0.01f,
                    "Real Water-tagged contact uses the original Grass-or-Water speed target=" + water.endSpeed);
                AddIntegration(water);
                motor.throttle = 0f;
                yield return Drive("water-coasting", 1f);
                DriveCase waterCoast = cases[cases.Count - 1];
                Add("water-coasting-friction", waterCoast.samples.All(s => s.waterBefore && s.waterAfter)
                    && Mathf.Abs((waterCoast.startSpeed - waterCoast.endSpeed) - 40f) < 0.02f,
                    "One second of Water-tagged contact coasting loses original-domain speed="
                    + (waterCoast.startSpeed - waterCoast.endSpeed));
                AddIntegration(waterCoast);

                Reset(transitionOrigin + Vector3.back * 10f, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                motor.throttle = 1f;
                yield return Drive("road-grass-road", 5f);
                DriveCase transition = cases[cases.Count - 1];
                int transitions = transition.samples.Count(s => s.grassBefore != s.grassAfter);
                Add("surface-sampled-before-move", transitions >= 2 && transition.grassSteps > 5
                    && transition.roadSteps > 5 && transition.maxIntegrationError < 0.0001f
                    && !transition.samples[transition.samples.Length - 1].grassAfter,
                    "Actual adjacent road/grass/road contacts: transitions=" + transitions
                    + "; the current speed step uses grassBefore and the next step consumes grassAfter");
                AddIntegration(transition);

                foreach (bool downhill in new[] { false, true })
                {
                    Vector3 origin = downhill ? downhillOrigin : uphillOrigin;
                    Quaternion slopeRotation = downhill ? downhillRotation : uphillRotation;
                    Reset(origin, slopeRotation);
                    yield return Settle();
                    motor.driving = true;
                    motor.throttle = 1f;
                    yield return Drive(downhill ? "downhill" : "uphill", 1f);
                    DriveCase slope = cases[cases.Count - 1];
                    float targetAngle = downhill ? 20f : -20f;
                    bool signed = slope.samples.All(s => Mathf.Abs(s.groundAngleBefore - targetAngle) < 0.2f
                        && Mathf.Abs(s.groundAngleAfter - targetAngle) < 0.2f);
                    float expectedDomainSpeed = downhill ? 45f + 20f / 65f * 25f : 45f;
                    Add(downhill ? "downhill-contact-and-gain" : "uphill-contact-and-sign",
                        signed && slope.contacts > 0 && slope.maxContactAngleError < 0.02f
                        && Mathf.Abs(slope.endSpeed - expectedDomainSpeed) < 0.08f,
                        "Actual 20-degree slope, stored angle=" + motor.GroundAngle
                        + "; one-second original-domain speed=" + slope.endSpeed
                        + "; independent collision-angle error=" + slope.maxContactAngleError);
                    AddIntegration(slope);
                }
                DriveCase uphill = cases.First(c => c.name == "uphill");
                DriveCase downhillCase = cases.First(c => c.name == "downhill");
                Add("ground-stick-vertical-speed", cases.All(c => c.maxGroundStickError < 0.0001f)
                    && full.samples.All(s => Mathf.Abs(s.verticalSpeed + 30f) < 0.0001f)
                    && downhillCase.samples.All(s => Mathf.Abs(s.verticalSpeed + 30f) < 0.0001f)
                    && uphill.samples.All(s => s.verticalSpeed >= -30.0001f && s.verticalSpeed <= -14.9999f),
                    "Actual CurrentVerticalSpeed uses original PlayerKart stickForce: flat/downhill=-30, uphill in [-30,-15]; max integration error="
                    + cases.Max(c => c.maxGroundStickError));

                float lastSlopeAngle = motor.GroundAngle;
                observer.phase = "downhill-jump";
                motor.jump = true;
                yield return tick;
                yield return tick;
                yield return tick;
                Add("air-retains-last-ground-angle", !motor.Grounded && !motor.UsesRestoredAcceleration
                    && Mathf.Abs(lastSlopeAngle - 20f) < 0.2f && motor.GroundAngle == lastSlopeAngle,
                    "A real jump leaves the slope while retaining its last accepted ground angle=" + motor.GroundAngle);

                Reset(roadOrigin, Quaternion.identity);
                yield return Settle();
                yield return tick;
                Add("parked-adapter-boundary", !motor.driving && motor.Grounded && !motor.UsesRestoredAcceleration,
                    "Parked race state retains its existing stopping branch");
                motor.driving = true;
                motor.throttle = 1f;
                motor.steering = 0.7f;
                motor.braking = true;
                motor.drifting = true;
                for (int i = 0; i < Mathf.CeilToInt(0.9f / Time.fixedDeltaTime); i++) yield return tick;
                Add("drift-restored-ground-branch", motor.Grounded && motor.IsDrifting && motor.UsesRestoredAcceleration,
                    "Held brake, throttle and steering use the restored drift acceleration branch");
                motor.drifting = false;
                motor.braking = false;
                yield return tick;
                yield return tick;
                Add("drift-release-normal-acceleration", motor.Grounded && !motor.IsDrifting && motor.UsesRestoredAcceleration,
                    "Drift release immediately retains restored ordinary grounded acceleration");

                Reset(roadOrigin, Quaternion.identity);
                yield return Settle();
                motor.driving = true;
                motor.throttle = 1f;
                motor.jump = true;
                yield return tick;
                yield return tick;
                yield return tick;
                Add("air-adapter-boundary", !motor.Grounded && !motor.UsesRestoredAcceleration,
                    "A real CharacterController jump selects the existing airborne acceleration branch");
                Time.timeScale = 0f;
                Vector3 pausedPosition = motor.transform.position;
                float pausedSpeed = motor.Speed;
                int pausedStep = motor.FixedStepCount;
                yield return new WaitForSecondsRealtime(0.2f);
                Add("pause-freezes-motor", motor.FixedStepCount == pausedStep
                    && motor.transform.position == pausedPosition && motor.Speed == pausedSpeed,
                    "Pause retains the actual fixed-step counter, position and speed");
                motor.ResetAt(roadOrigin + Vector3.up * 0.4f, Quaternion.identity, false);
                Add("reset-clears-acceleration", motor.Speed == 0f && motor.GroundAngle == 0f
                    && !motor.UsesRestoredAcceleration && motor.AccelerationState.currentSpeed == 0f,
                    "Reset clears the speed, sampled ground angle and acceleration branch state");
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);
                Add("construction-silence", AudioListener.volume == 0f
                    && UnityEngine.Object.FindObjectsOfType<AudioSource>(true).All(source => !source.isPlaying),
                    "Construction build keeps global audio volume at zero and every audio source stopped");

                void Plane(string name, Vector3 origin, Quaternion rotation, Vector3 size, bool grassTag, bool waterTag = false)
                {
                    var ground = new GameObject(name);
                    fixtures.Add(ground);
                    ground.transform.SetPositionAndRotation(origin - rotation * Vector3.up * 0.5f, rotation);
                    if (grassTag) ground.tag = "Grass";
                    if (waterTag) ground.tag = "Water";
                    var collider = ground.AddComponent<BoxCollider>();
                    collider.size = size;
                    observer.watched.Add(collider);
                }
                void Reset(Vector3 origin, Quaternion rotation)
                {
                    motor.ResetAt(origin + rotation * Vector3.up * 0.4f, rotation, false);
                    motor.driving = false;
                    motor.throttle = motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = false;
                }
                IEnumerator Settle()
                {
                    observer.phase = "settle";
                    for (int i = 0; i < 40; i++) { observer.step = i; yield return tick; }
                }
                IEnumerator Drive(string name, float seconds)
                {
                    var drive = new DriveCase { name = name, startSpeed = motor.Speed,
                        steps = Mathf.RoundToInt(seconds / Time.fixedDeltaTime) };
                    Vector3 start = motor.transform.position;
                    var samples = new List<Sample>();
                    observer.phase = name;
                    for (int i = 0; i < drive.steps; i++)
                    {
                        observer.step = i;
                        float speedBefore = motor.Speed;
                        bool groundedBefore = motor.Grounded, grassBefore = motor.OnGrass || motor.InWater;
                        bool waterBefore = motor.InWater;
                        float angleBefore = motor.GroundAngle;
                        int fixedStep = motor.FixedStepCount, contactStart = observer.contacts.Count;
                        var turn = NativeKartSteeringState.Step(motor.CurrentTurn, new NativeKartSteeringInput
                        { speed = speedBefore, wishTurn = motor.steering }, motor.stats, Time.fixedDeltaTime);
                        var expected = NativeKartAccelerationState.Step(turn.currentSpeed, new NativeKartAccelerationInput
                        {
                            wishAccel = motor.throttle, braking = motor.braking, onGrass = grassBefore,
                            groundAngle = angleBefore, slopeLimit = body.slopeLimit, topSpeedModifier = 1f, extraSpeed = 0f
                        }, motor.stats, Time.fixedDeltaTime);
                        Vector3 expectedForward = turn.GroundRotation(motor.transform.rotation,
                            motor.GroundNormal, motor.stats, Time.fixedDeltaTime) * Vector3.forward;
                        float expectedVerticalSpeed = NativeKartAccelerationState.GroundStickSpeed(
                            expected.speedBeforeCaps, expectedForward, motor.stats);
                        float expectedSpeed = expected.currentSpeed;
                        if (!groundedBefore)
                        {
                            // These scalar driving phases keep drifting disabled.
                            float fallbackTarget = (motor.OnGrass ? motor.stats._topSpeedGrass : motor.stats._topSpeed);
                            if (motor.throttle > 0f && !motor.braking)
                                expectedSpeed = Mathf.MoveTowards(speedBefore, fallbackTarget,
                                    motor.stats._acceleration * motor.throttle * Time.fixedDeltaTime);
                            else if (motor.braking)
                                expectedSpeed = Mathf.MoveTowards(speedBefore, 0f, motor.stats._brakeForce * Time.fixedDeltaTime);
                            else
                                expectedSpeed = Mathf.MoveTowards(speedBefore, 0f, 3.5f * Time.fixedDeltaTime);
                            if (motor.OnGrass && expectedSpeed > fallbackTarget)
                                expectedSpeed = Mathf.MoveTowards(expectedSpeed, fallbackTarget, 15f * Time.fixedDeltaTime);
                        }
                        yield return tick;
                        var state = motor.AccelerationState;
                        float error = Mathf.Abs(motor.Speed - expectedSpeed);
                        if (motor.UsesRestoredAcceleration)
                            error = Mathf.Max(error, Mathf.Abs(state.currentSpeed - expected.currentSpeed),
                                Mathf.Abs(state.targetTopSpeed - expected.targetTopSpeed),
                                Mathf.Abs(state.approachRate - expected.approachRate),
                                Mathf.Abs(state.speedAfterApproach - expected.speedAfterApproach),
                                Mathf.Abs(state.speedAfterBrake - expected.speedAfterBrake),
                                Mathf.Abs(state.speedBeforeCaps - expected.speedBeforeCaps));
                        float groundStickError = motor.UsesRestoredAcceleration
                            ? Mathf.Abs(motor.CurrentVerticalSpeed - expectedVerticalSpeed) : 0f;
                        int contactCount = observer.contacts.Count - contactStart;
                        float contactAngleError = contactCount > 0
                            ? Mathf.Abs(observer.contacts[observer.contacts.Count - 1].signedAngle - motor.GroundAngle) : 0f;
                        samples.Add(new Sample
                        {
                            step = i, fixedStepDelta = motor.FixedStepCount - fixedStep, contacts = contactCount,
                            groundedBefore = groundedBefore, groundedAfter = motor.Grounded,
                            grassBefore = grassBefore, grassAfter = motor.OnGrass || motor.InWater,
                            waterBefore = waterBefore, waterAfter = motor.InWater, restored = motor.UsesRestoredAcceleration,
                            expectedRestored = groundedBefore,
                            throttle = motor.throttle, speedBefore = speedBefore, speedAfter = motor.Speed,
                            groundAngleBefore = angleBefore, groundAngleAfter = motor.GroundAngle,
                            expectedSpeed = expectedSpeed, integrationError = error,
                            verticalSpeed = motor.CurrentVerticalSpeed, expectedVerticalSpeed = expectedVerticalSpeed,
                            groundStickError = groundStickError, expectedGroundForward = expectedForward,
                            contactAngleError = contactAngleError, position = motor.transform.position,
                            physicsVelocity = motor.PhysicsVelocity, state = state
                        });
                        if (motor.Grounded) drive.groundedSteps++;
                        if (motor.UsesRestoredAcceleration) drive.restoredSteps++; else drive.fallbackSteps++;
                        if (motor.UsesRestoredAcceleration != groundedBefore) drive.branchMismatchSteps++;
                        if (grassBefore) drive.grassSteps++; else drive.roadSteps++;
                        drive.contacts += contactCount;
                        drive.maxIntegrationError = Mathf.Max(drive.maxIntegrationError, error);
                        drive.maxContactAngleError = Mathf.Max(drive.maxContactAngleError, contactAngleError);
                        drive.maxGroundStickError = Mathf.Max(drive.maxGroundStickError, groundStickError);
                    }
                    drive.endSpeed = motor.Speed;
                    drive.displacement = motor.transform.position - start;
                    drive.samples = samples.ToArray();
                    cases.Add(drive);
                }
                void AddIntegration(DriveCase drive) => Add("live-integration-" + drive.name,
                    drive.groundedSteps == drive.steps && drive.restoredSteps == drive.steps
                    && drive.branchMismatchSteps == 0
                    && drive.samples.All(s => s.fixedStepDelta == 1) && drive.contacts > 0
                    && drive.maxIntegrationError < 0.0001f && drive.maxContactAngleError < 0.02f
                    && drive.maxGroundStickError < 0.0001f,
                    drive.steps + " actual CharacterController steps; restored=" + drive.restoredSteps
                    + "; fallback=" + drive.fallbackSteps + "; branch mismatches=" + drive.branchMismatchSteps
                    + "; prior contact inputs and steering-loss order; speed error=" + drive.maxIntegrationError
                    + "; ground stick error=" + drive.maxGroundStickError);
            }
            finally
            {
                Time.timeScale = timeScaleBefore;
                for (int i = 0; i < race.racers.Length; i++) race.racers[i].enabled = enabledBefore[i];
                if (probe) { probe.SetActive(false); Destroy(probe); }
                foreach (var fixture in fixtures) { fixture.SetActive(false); Destroy(fixture); }
            }
            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                boundary = "A cloned exported kart on isolated real CharacterController road, grass, Water-tagged ground, road/grass seams and +/-20 degree slopes. "
                    + "Analytic target/rate observations and independent collision records accompany per-step integration checks. "
                    + "The separate x64 reference establishes scalar-formula parity; this report verifies contact sampling and runtime wiring. "
                    + "Ground stick and zeroed forward vertical velocity are supported by the original instruction audit and these live checks. "
                    + "Water is checked for the original Grass-or-Water scalar input; waterproof and other water behaviors remain outside this slice. "
                    + "Drift shares the restored grounded acceleration branch and has its own reference and live suite. "
                    + "Motor speed and acceleration state share original world units; airborne and parked behavior are checked separately.",
                fixedDeltaTime = Time.fixedDeltaTime, worldSpeedDivisor = 1f,
                passed = checks.Count > 0 && checks.All(c => c.passed), checks = checks.ToArray(),
                cases = cases.ToArray(), contacts = ReferenceEquals(observer, null) ? new Contact[0] : observer.contacts.ToArray()
            };
            result.passed = report.passed;
            File.WriteAllText(Path.Combine(directory, "acceleration-live-report.json"), JsonUtility.ToJson(report, true));
            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
        }
    }
}
