using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartAirborneLiveVerification : MonoBehaviour
    {
        public sealed class Result { public bool passed; }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Contact
        {
            public string phase, collider;
            public int step;
            public Vector3 normal, point;
        }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int step, fixedStepDelta, contacts, landings;
            public bool groundedBefore, grounded, restoredAirborne, jumped, leftGround, landed, jumpRequested;
            public bool braking, drifting;
            public float throttle, steering, speedBefore, speed, airTime, airTurn, verticalSpeed, moveVerticalSpeed;
            public float gravityError, turnError, velocityError, rotationError, airTimeError, speedError;
            public Vector3 position, driveVelocityBefore, driveVelocity, physicsVelocity;
            public Quaternion rotation, driftVisualRotation;
        }
        [Serializable] sealed class Pose
        {
            public string name, image;
            public bool grounded;
            public float airTime, speed, verticalSpeed, appliedRotationError;
            public Vector3 position, velocity;
            public Quaternion rotation;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public int width, height;
            public float fixedDeltaTime, worldSpeedDivisor;
            public Check[] checks;
            public Sample[] samples;
            public Contact[] contacts;
            public Pose[] poses;
        }

        readonly HashSet<Collider> watched = new HashSet<Collider>();
        readonly List<Contact> contacts = new List<Contact>();
        string phase;
        int step;
        bool recordForest;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (watched.Contains(hit.collider) || (recordForest && !hit.collider.GetComponentInParent<NativeKartMotor>()))
                contacts.Add(new Contact { phase = phase, step = step, collider = hit.collider.name,
                    normal = hit.normal, point = hit.point });
        }

        public static IEnumerator Run(NativeRace race, string directory, Result result)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var poses = new List<Pose>();
            var fixtures = new List<GameObject>();
            var materials = new List<Material>();
            bool[] enabledBefore = race.racers.Select(r => r.enabled).ToArray();
            bool[] controllerBefore = race.racers.Select(r => r.GetComponent<CharacterController>().enabled).ToArray();
            Vector3 forestPosition = race.racers[0].transform.position;
            Quaternion forestRotation = race.racers[0].transform.rotation;
            float timeScaleBefore = Time.timeScale;
            var camera = race.raceCamera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            GameObject probe = null;
            NativeKartAirborneLiveVerification observer = null;
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
                probe.name = "Airborne verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                var wheels = probe.GetComponent<NativeKartWheelVisual>();
                var body = probe.GetComponent<CharacterController>();
                observer = probe.AddComponent<NativeKartAirborneLiveVerification>();
                motor.enabled = true;
                Vector3 road = new Vector3(0f, 500f, 0f);
                Vector3 slope = new Vector3(4000f, 500f, 0f);
                Vector3 ledge = new Vector3(8000f, 520f, 0f);
                Quaternion uphill = Quaternion.Euler(-20f, 0f, 0f);
                Plane("Airborne verification road", road, Quaternion.identity, new Vector3(1600f, 1f, 1600f));
                Plane("Airborne verification uphill", slope, uphill, new Vector3(1200f, 1f, 1200f));
                Plane("Airborne verification ledge", ledge, Quaternion.identity, new Vector3(100f, 1f, 40f));
                Plane("Airborne verification lower landing", ledge + Vector3.down * 20f,
                    Quaternion.identity, new Vector3(1600f, 1f, 1600f));
                Physics.SyncTransforms();
                Time.timeScale = 1f;

                Add("serialized-original-airborne-settings", race.racers.All(r => r.stats._jumpForce == 7.5f
                    && r.stats._turnForce == 125f && r.stats._turnAirMult == 3.75f
                    && r.stats._turnVelAirMult == 0.25f && r.stats._gravity == 25f
                    && r.stats._flipSpeedBoost == 20f && !r.stats._convertsAirVelocity),
                    "All four motors carry original Kart and PlayerKart ordinary-air parameters");
                Add("construction-audio-silenced", AudioListener.volume == 0f && race.menuPresentation.soundtrack
                    && !race.menuPresentation.soundtrack.enabled && !race.menuPresentation.soundtrack.isPlaying,
                    "Global listener volume=" + AudioListener.volume + "; soundtrack enabled="
                    + race.menuPresentation.soundtrack.enabled + "; soundtrack playing=" + race.menuPresentation.soundtrack.isPlaying);

                Reset(road, Quaternion.identity);
                yield return Settle();
                Inputs(1f, 0f, false);
                yield return Drive("flat-approach", 100);
                float flatHeight = motor.transform.position.y;
                int flatLandings = motor.Landings;
                motor.jump = true;
                yield return Drive("flat-jump", 1);
                Add("flat-jump-force-and-order", motor.JumpedThisStep && !motor.Grounded
                    && Mathf.Abs(motor.CurrentVerticalSpeed - 7.5f) < 0.0001f
                    && Mathf.Abs(motor.VerticalSpeed - 7.5f) < 0.0001f,
                    "The takeoff move uses full jump force; moveY=" + motor.CurrentVerticalSpeed
                    + "; carryY=" + motor.VerticalSpeed + "; jumped=" + motor.JumpedThisStep);
                yield return Drive("flat-ascent", 9);
                float repeatGravity = motor.VerticalSpeed;
                motor.jump = true;
                yield return Drive("air-jump-request", 1);
                Add("air-jump-request-rejected", !motor.JumpedThisStep && !motor.jump && !motor.Grounded
                    && Mathf.Abs(motor.VerticalSpeed - (repeatGravity - 25f * Time.fixedDeltaTime)) < 0.0001f,
                    "A second C edge is consumed during flight; carryY=" + motor.VerticalSpeed);
                yield return Drive("flat-apex", 8);
                Add("flat-ballistic-apex", !motor.Grounded && motor.transform.position.y > flatHeight + 1f
                    && Mathf.Abs(motor.VerticalSpeed) < 0.5f,
                    "Real controller apex height=" + (motor.transform.position.y - flatHeight)
                    + "; vertical speed=" + motor.VerticalSpeed);
                yield return Capture("jump-apex");
                yield return UntilLanding("flat-descent", 100);
                Add("flat-landing-contact", motor.Grounded && motor.Landings == flatLandings + 1
                    && motor.LandedThisStep && observer.contacts.Any(c => c.phase == "flat-descent"
                        && Vector3.Angle(c.normal, Vector3.up) <= body.slopeLimit),
                    "A single observed valid-ground landing increments the counter; delta=" + (motor.Landings - flatLandings));
                yield return Capture("landing");

                foreach (float direction in new[] { -1f, 1f })
                {
                    string side = direction < 0f ? "left" : "right";
                    Reset(road, Quaternion.identity);
                    yield return Settle();
                    Inputs(0f, 0f, false);
                    motor.jump = true;
                    yield return Drive(side + "-stationary-jump", 1);
                    Quaternion initial = motor.transform.rotation;
                    motor.steering = direction;
                    yield return Drive(side + "-stationary-air-turn", 20);
                    float yaw = Vector3.SignedAngle(initial * Vector3.forward, motor.transform.forward, Vector3.up);
                    Add(side + "-zero-speed-air-turn", !motor.Grounded && motor.UsesRestoredAirborne
                        && Mathf.Abs(motor.Speed) < 0.0001f && direction * yaw > 9f
                        && Mathf.Abs(yaw - direction * 125f * 0.25f * Time.fixedDeltaTime * 20f) < 0.05f,
                        "Zero-speed body yaw=" + yaw + "; expected=" + direction * 125f * 0.25f * Time.fixedDeltaTime * 20f);
                }

                Reset(road, Quaternion.identity);
                yield return Settle();
                Inputs(1f, 0f, false);
                yield return Drive("moving-approach", 100);
                motor.jump = true;
                yield return Drive("moving-jump", 1);
                Inputs(0f, 0.8f, true);
                int inputStart = samples.Count;
                yield return Drive("air-brake-no-throttle", 10);
                Inputs(1f, -0.8f, false);
                yield return Drive("air-full-throttle", 10);
                var inputSamples = samples.Skip(inputStart).ToArray();
                Add("air-inputs-preserve-speed", inputSamples.All(s => s.restoredAirborne
                    && !s.grounded && s.speedError < 0.0001f) && motor.DriveVelocity.magnitude > 5f,
                    "Both brake without throttle and full throttle retain the in-flight scalar speed; max error="
                    + inputSamples.Max(s => s.speedError));
                yield return Capture("air-turn");
                motor.transform.Rotate(0f, 25f, 0f);
                motor.steering = 0.05f;
                float mismatchBefore = Vector3.Angle(motor.transform.forward, motor.DriveVelocity);
                yield return Drive("low-input-alignment", 5);
                float mismatchAfter = Vector3.Angle(motor.transform.forward, motor.DriveVelocity);
                Add("low-input-aligns-to-velocity", !motor.Grounded && Mathf.Abs(motor.CurrentAirTurn) <= 0.1f
                    && mismatchAfter < mismatchBefore - 10f,
                    "Injected body-to-velocity angle contracts from " + mismatchBefore + " to " + mismatchAfter);
                motor.steering = 1f;
                yield return UntilLanding("turning-descent", 100);
                Add("landing-aligns-moving-body", motor.Grounded && motor.LandedThisStep
                    && Vector3.Angle(motor.transform.forward, motor.DriveVelocity) < 0.1f,
                    "Landing forward-to-carried-velocity angle=" + Vector3.Angle(motor.transform.forward, motor.DriveVelocity));

                Reset(slope, uphill);
                yield return Settle();
                Inputs(1f, 0f, false);
                yield return Drive("slope-approach", 100);
                float slopeAngle = motor.GroundAngle;
                motor.jump = true;
                yield return Drive("slope-jump", 1);
                Vector3 slopeDirection = motor.transform.forward.normalized;
                Vector3 slopeMotion = slopeDirection * motor.Speed;
                slopeMotion.y = 0f;
                float expectedSlopeJump = Vector3.Project(slopeMotion, slopeDirection).y + 7.5f;
                Add("slope-jump-carries-climb", motor.JumpedThisStep && !motor.Grounded && slopeAngle < -15f
                    && motor.VerticalSpeed > 7.5f && Mathf.Abs(motor.VerticalSpeed - expectedSlopeJump) < 0.0001f
                    && observer.contacts.Any(c => c.phase == "slope-approach"
                        && Vector3.Angle(c.normal, Vector3.up) > 15f),
                    "Real uphill contact angle=" + slopeAngle + "; takeoff vertical speed=" + motor.VerticalSpeed
                    + "; projected world-speed expectation=" + expectedSlopeJump);

                Reset(ledge + Vector3.back * 15f, Quaternion.identity);
                yield return Settle();
                Inputs(1f, 0f, false);
                int ledgeStart = samples.Count;
                for (int i = 0; i < 240 && !motor.LeftGroundThisStep; i++) yield return Drive("ledge-approach", 1);
                Add("natural-ledge-departure", motor.LeftGroundThisStep && !motor.Grounded && !motor.JumpedThisStep
                    && !motor.jump && samples.Skip(ledgeStart).Any(s => s.groundedBefore && !s.grounded && s.leftGround),
                    "Moving off a real finite platform sets natural departure without a jump request; carryY=" + motor.VerticalSpeed);
                Inputs(0f, -0.6f, true);
                yield return Drive("ledge-air-turn", 20);
                yield return UntilLanding("ledge-descent", 240);
                Add("ledge-lands-on-lower-ground", motor.Grounded && motor.LandedThisStep
                    && motor.transform.position.y < ledge.y - 15f && observer.contacts.Any(c => c.phase == "ledge-descent"
                        && c.collider == "Airborne verification lower landing"),
                    "Natural flight lands on the lower collision fixture; finalY=" + motor.transform.position.y);

                Reset(road, Quaternion.identity);
                yield return Settle();
                Inputs(1f, 0.7f, true);
                motor.drifting = true;
                yield return Drive("drift-approach", 45);
                motor.jump = true;
                yield return Drive("drift-jump", 1);
                Quaternion takeoffDrift = motor.DriftVisualRotation;
                yield return Drive("drift-air", 8);
                Add("air-clears-drift-and-holds-visual", !motor.Grounded && !motor.IsDrifting
                    && motor.DriftTime == 0f && Quaternion.Angle(takeoffDrift, motor.DriftVisualRotation) < 0.01f,
                    "Air clears logical drift and retains takeoff model yaw="
                    + Quaternion.Angle(motor.DriftVisualRotation, Quaternion.identity));
                yield return UntilLanding("drift-descent", 100);
                yield return Drive("drift-ground-resume", 2);
                Add("landing-resumes-held-drift", motor.Grounded && motor.IsDrifting && motor.DriftDirection == 1f,
                    "Held grounded inputs re-enter drift after the landing transition");

                Reset(road + Vector3.up * 150f, Quaternion.identity);
                Inputs(0f, 0f, false);
                yield return Drive("long-fall", 150);
                Add("gravity-has-no-adapter-terminal-cap", !motor.Grounded && motor.VerticalSpeed < -60f,
                    "After 150 free-fall fixed steps, carryY=" + motor.VerticalSpeed + "; original gravity continues past -55");
                Time.timeScale = 0f;
                yield return new WaitForEndOfFrame();
                Vector3 pausedPosition = motor.transform.position, pausedDriveVelocity = motor.DriveVelocity;
                Quaternion pausedRotation = motor.transform.rotation;
                float pausedTime = motor.AirTime, pausedGravity = motor.VerticalSpeed;
                int pausedSteps = motor.FixedStepCount;
                yield return new WaitForSecondsRealtime(0.25f);
                Add("pause-freezes-airborne-state", motor.FixedStepCount == pausedSteps && motor.transform.position == pausedPosition
                    && motor.transform.rotation == pausedRotation && motor.DriveVelocity == pausedDriveVelocity
                    && motor.AirTime == pausedTime && motor.VerticalSpeed == pausedGravity,
                    "Pause preserves actual fixed-step count, pose, carried velocity, air time and gravity");
                motor.ResetAt(road + Vector3.up * 0.4f, Quaternion.identity, false);
                Add("reset-clears-airborne-state", !motor.Grounded && !motor.UsesRestoredAirborne
                    && !motor.JumpedThisStep && !motor.LeftGroundThisStep && !motor.LandedThisStep
                    && motor.AirTime == 0f && motor.VerticalSpeed == 0f && motor.CurrentAirTurn == 0f
                    && motor.DriveVelocity == Vector3.zero && motor.Speed == 0f,
                    "Reset clears all exported air state and transition flags");
                Time.timeScale = 1f;

                Reset(forestPosition, forestRotation);
                observer.recordForest = true;
                yield return Settle();
                Inputs(1f, 0f, false);
                yield return Drive("forest-approach", 55);
                motor.jump = true;
                yield return Drive("forest-jump", 1);
                yield return Drive("forest-air", 10);
                yield return UntilLanding("forest-descent", 180);
                var forestSamples = samples.Where(s => s.phase.StartsWith("forest-", StringComparison.Ordinal)).ToArray();
                Add("forest-jump-and-landing", forestSamples.Any(s => s.jumped)
                    && forestSamples.Any(s => s.restoredAirborne && !s.grounded)
                    && forestSamples.Any(s => s.landed) && observer.contacts.Any(c => c.phase == "forest-descent"),
                    "Forest short drive records actual takeoff, airborne steps and landing; contacts="
                    + observer.contacts.Count(c => c.phase.StartsWith("forest-", StringComparison.Ordinal)));
                observer.recordForest = false;

                var airSamples = samples.Where(s => !s.groundedBefore && s.restoredAirborne).ToArray();
                Add("fixed-step-observations", samples.Count > 0 && samples.All(s => s.fixedStepDelta == 1),
                    "Each of " + samples.Count + " retained samples covers exactly one actual fixed update");
                Add("airborne-wiring-matches-formulas", airSamples.Length > 100
                    && airSamples.All(s => s.gravityError < 0.0001f && s.turnError < 0.0001f
                        && s.velocityError < 0.0001f && s.rotationError < 0.08f
                        && s.airTimeError < 0.0001f && s.speedError < 0.0001f),
                    "Air steps=" + airSamples.Length + "; max gravity error=" + airSamples.Max(s => s.gravityError)
                    + "; max turn error=" + airSamples.Max(s => s.turnError)
                    + "; max carried-velocity error=" + airSamples.Max(s => s.velocityError)
                    + "; max rotation error=" + airSamples.Max(s => s.rotationError)
                    + "; max time error=" + airSamples.Max(s => s.airTimeError)
                    + "; max speed error=" + airSamples.Max(s => s.speedError));
                Add("applied-live-airborne-poses", poses.Count == 3 && poses.All(p => p.appliedRotationError < 0.08f
                    && File.Exists(Path.Combine(directory, p.image))) && poses.Count(p => p.grounded) == 1,
                    "Apex, turning and landing screenshots retain the actual driven pose and applied model rotation");
                Add("construction-silence-persists", AudioListener.volume == 0f
                    && !race.menuPresentation.soundtrack.enabled && !race.menuPresentation.soundtrack.isPlaying,
                    "The full live run retains zero global volume and disabled soundtrack playback");
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);

                void Plane(string name, Vector3 origin, Quaternion rotation, Vector3 scale)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = name;
                    ground.transform.SetPositionAndRotation(origin - rotation * Vector3.up * 0.5f, rotation);
                    ground.transform.localScale = scale;
                    var material = ground.GetComponent<Renderer>().material;
                    material.color = new Color(0.24f, 0.27f, 0.3f);
                    materials.Add(material);
                    observer.watched.Add(ground.GetComponent<Collider>());
                    fixtures.Add(ground);
                    for (int line = -8; line <= 8; line++)
                    {
                        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        stripe.name = "Airborne fixture distance grid";
                        var collider = stripe.GetComponent<Collider>();
                        collider.enabled = false;
                        Destroy(collider);
                        stripe.transform.SetPositionAndRotation(origin + rotation * new Vector3(0f, 0.003f, line * 2f), rotation);
                        stripe.transform.localScale = new Vector3(Mathf.Min(100f, scale.x), 0.002f, 0.025f);
                        fixtures.Add(stripe);
                    }
                }
                void Reset(Vector3 origin, Quaternion rotation)
                {
                    motor.ResetAt(origin + Vector3.up * 0.4f, rotation, false);
                    wheels.ResetState();
                    motor.driving = false;
                    motor.throttle = motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                    Time.timeScale = 1f;
                }
                void Inputs(float throttle, float steering, bool braking)
                {
                    motor.driving = true;
                    motor.throttle = throttle;
                    motor.steering = steering;
                    motor.braking = braking;
                    motor.drifting = false;
                }
                IEnumerator Settle()
                {
                    observer.phase = "settle";
                    for (int i = 0; i < 40; i++) { observer.step = i; yield return tick; }
                }
                IEnumerator UntilLanding(string name, int maximum)
                {
                    for (int i = 0; i < maximum && !motor.Grounded; i++) yield return Drive(name, 1);
                }
                IEnumerator Drive(string name, int count)
                {
                    observer.phase = name;
                    for (int i = 0; i < count; i++)
                    {
                        observer.step = samples.Count;
                        int fixedStep = motor.FixedStepCount, contactStart = observer.contacts.Count;
                        bool grounded = motor.Grounded, requestedJump = motor.jump;
                        float speed = motor.Speed, dt = Time.fixedDeltaTime;
                        float expectedGravity = motor.VerticalSpeed - motor.stats._gravity * dt;
                        var turn = NativeKartSpin.Turn(motor.driving ? motor.steering : 0f,
                            motor.SpinRequested, motor.AirTime, motor.stats, dt);
                        float expectedTurn = turn.bodyYaw;
                        float expectedAirTime = motor.AirTime + dt;
                        Vector3 previousVelocity = motor.DriveVelocity;
                        Vector3 expectedVelocity = Quaternion.Euler(0f, turn.velocityYaw, 0f) * previousVelocity;
                        expectedVelocity.y = 0f;
                        Quaternion expectedRotation = motor.transform.rotation * Quaternion.Euler(0f, expectedTurn, 0f);
                        if (Mathf.Abs(expectedTurn) <= 0.1f && (expectedVelocity).sqrMagnitude > 0.1f)
                            expectedRotation = Quaternion.Lerp(expectedRotation,
                                Quaternion.LookRotation(expectedVelocity, expectedRotation * Vector3.up),
                                2f * motor.stats._turnForce * dt * dt * motor.stats._turnAirMult);
                        yield return tick;
                        if (motor.LandedThisStep && (expectedVelocity).magnitude > 1f)
                            expectedRotation = Quaternion.LookRotation(expectedVelocity, expectedRotation * Vector3.up);
                        bool compare = !grounded && motor.UsesRestoredAirborne;
                        samples.Add(new Sample
                        {
                            phase = name, step = observer.step, fixedStepDelta = motor.FixedStepCount - fixedStep,
                            contacts = observer.contacts.Count - contactStart, landings = motor.Landings,
                            groundedBefore = grounded, grounded = motor.Grounded, restoredAirborne = motor.UsesRestoredAirborne,
                            jumped = motor.JumpedThisStep, leftGround = motor.LeftGroundThisStep, landed = motor.LandedThisStep,
                            jumpRequested = requestedJump, throttle = motor.throttle, steering = motor.steering,
                            braking = motor.braking, drifting = motor.IsDrifting,
                            speedBefore = speed, speed = motor.Speed, airTime = motor.AirTime, airTurn = motor.CurrentAirTurn,
                            verticalSpeed = motor.VerticalSpeed, moveVerticalSpeed = motor.CurrentVerticalSpeed,
                            gravityError = compare ? Mathf.Abs(motor.CurrentVerticalSpeed - expectedGravity) : 0f,
                            turnError = compare ? Mathf.Abs(motor.CurrentAirTurn - expectedTurn) : 0f,
                            velocityError = compare ? Vector3.Distance(motor.DriveVelocity, expectedVelocity) : 0f,
                            rotationError = compare ? Quaternion.Angle(motor.transform.rotation, expectedRotation) : 0f,
                            airTimeError = compare && !motor.LandedThisStep ? Mathf.Abs(motor.AirTime - expectedAirTime) : 0f,
                            speedError = compare ? Mathf.Abs(motor.Speed -
                                (NativeKartAirborne.ClampSpeed(speed, motor.stats)
                                    + motor.LastLandingBonus)) : 0f,
                            position = motor.transform.position, driveVelocityBefore = previousVelocity,
                            driveVelocity = motor.DriveVelocity, physicsVelocity = motor.PhysicsVelocity,
                            rotation = motor.transform.rotation, driftVisualRotation = motor.DriftVisualRotation
                        });
                    }
                }
                IEnumerator Capture(string name)
                {
                    Time.timeScale = 0f;
                    bool frozen = wheels.frozen;
                    wheels.frozen = true;
                    motor.PrepareRenderPose(false);
                    camera.transform.position = motor.transform.TransformPoint(new Vector3(5.2f, 2.5f, -6f));
                    camera.transform.LookAt(motor.transform.position + Vector3.up * 0.2f);
                    camera.fieldOfView = 40f;
                    yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    string filename = name + ".png";
                    File.WriteAllBytes(Path.Combine(directory, filename), texture.EncodeToPNG());
                    Destroy(texture);
                    poses.Add(new Pose
                    {
                        name = name, image = filename, grounded = motor.Grounded, airTime = motor.AirTime,
                        speed = motor.Speed, verticalSpeed = motor.VerticalSpeed, position = motor.transform.position,
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
                boundary = "A cloned exported kart performs ordinary and brake-requested spin air movement using real CharacterController collisions: "
                    + "flat and uphill takeoff, repeated jump input, zero-speed steering, moving air input, velocity alignment, "
                    + "natural ledge departure, lower-platform landing, long fall, pause/reset, and a short Forest jump. "
                    + "Per-step independent equations check runtime wiring. The separate original x64 reference establishes formula parity. "
                    + "Ground movement directly uses original world speed; "
                    + "the separate spin slice covers flip rewards and the full original-player trajectory remains outside these slices.",
                fixedDeltaTime = Time.fixedDeltaTime, worldSpeedDivisor = 1f,
                width = Screen.width, height = Screen.height,
                passed = checks.Count > 0 && checks.All(c => c.passed), checks = checks.ToArray(),
                samples = samples.ToArray(), poses = poses.ToArray(),
                contacts = ReferenceEquals(observer, null) ? new Contact[0] : observer.contacts.ToArray()
            };
            result.passed = report.passed;
            File.WriteAllText(Path.Combine(directory, "air-live-report.json"), JsonUtility.ToJson(report, true));
            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
        }
    }
}
