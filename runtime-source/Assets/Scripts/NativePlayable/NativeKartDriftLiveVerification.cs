using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartDriftLiveVerification : MonoBehaviour
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
            public int step, fixedStepDelta, contacts;
            public float throttle, steering, speed, currentTurn, driftDirection, driftTime;
            public float visualYaw, velocityYaw, visualToVelocityAngle;
            public float speedError, turnError, visualRotationError, elapsedError;
            public bool braking, driftRequested, grounded, grass, water, isDrifting;
            public bool groundedBefore;
            public bool restoredDrift, restoredSteering, restoredAcceleration;
            public Vector3 position, physicsVelocity, forward;
            public Quaternion rotation, driftVisualRotation;
        }
        [Serializable] sealed class Pose
        {
            public string name, image;
            public float direction, speed, driftTime, appliedRotationError, visualToVelocityAngle;
            public bool grounded, isDrifting;
            public Vector3 position, velocity, modelForward;
            public Quaternion rotation, expectedLocalRotation, appliedLocalRotation;
        }
        [Serializable] sealed class Report
        {
            public string version, platform, boundary;
            public bool passed;
            public float fixedDeltaTime, worldSpeedDivisor;
            public int width, height;
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
            bool[] controllerEnabledBefore = race.racers.Select(r => r.GetComponent<CharacterController>().enabled).ToArray();
            Vector3 forestPosition = race.racers[0].transform.position;
            Quaternion forestRotation = race.racers[0].transform.rotation;
            float timeScaleBefore = Time.timeScale;
            var camera = race.raceCamera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            GameObject probe = null;
            NativeKartDriftLiveVerification observer = null;
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
                probe.name = "Drift verification isolated kart";
                var motor = probe.GetComponent<NativeKartMotor>();
                var wheels = probe.GetComponent<NativeKartWheelVisual>();
                var body = probe.GetComponent<CharacterController>();
                observer = probe.AddComponent<NativeKartDriftLiveVerification>();
                motor.enabled = true;
                Vector3 road = new Vector3(0f, 500f, 0f);
                Vector3 grass = new Vector3(4000f, 500f, 0f);
                Vector3 water = new Vector3(8000f, 500f, 0f);
                Plane("Drift verification road", road, "Untagged");
                Plane("Drift verification grass", grass, "Grass");
                Plane("Drift verification water", water, "Water");
                Physics.SyncTransforms();
                Time.timeScale = 1f;

                Add("serialized-original-drift-settings", race.racers.All(r => r.stats._driftAngle == 50f
                    && r.stats._driftSmooth == 3.5f && r.stats._driftAutoIntensity == 90f
                    && r.stats._driftSteerGain == 4f && r.stats._driftSpeedGain == 15f
                    && r.stats._driftSpeedMaxGain == 0f && r.stats._driftGrassTopSpeed == 45f
                    && r.stats._driftGrassFriction == 85f && r.stats._driftSpeedDrain == 75f
                    && r.stats._driftDrainAngle == 8f),
                    "All four exported motors carry the original KartStats drift parameters");
                Add("drift-pivot-binding", race.racers.All(r => r.driftPivot
                    && r.driftPivot.parent == r.visual
                    && r.GetComponent<NativeKartWheelVisual>().sourceFrame.IsChildOf(r.driftPivot)
                    && r.GetComponent<NativeKartWheelVisual>().fumo.IsChildOf(r.driftPivot)),
                    "Every exported vehicle and Fumo pivot share the serialized drift rotation node");

                Reset(road);
                yield return Settle();
                Inputs(0.39f, 0.1f, true, true);
                yield return Drive("gate-throttle-below", 2);
                Add("throttle-below-threshold", !motor.IsDrifting && motor.DriftDirection == 0f,
                    "Grounded brake and steering=0.1 with throttle=0.39 retain zero direction");
                Inputs(0.4f, 0.099f, true, true);
                yield return Drive("gate-steering-below", 2);
                Add("steering-below-threshold", !motor.IsDrifting && motor.DriftDirection == 0f,
                    "Grounded brake and throttle=0.4 with steering=0.099 retain zero direction");
                Inputs(0.4f, 0.1f, true, true);
                yield return Drive("gate-at-threshold", 2);
                Add("threshold-enters-drift", motor.Grounded && motor.IsDrifting && motor.DriftDirection == 1f,
                    "Throttle=0.4 and steering=0.1 enter positive drift at the inclusive source thresholds");
                Inputs(0.39f, 0.1f, true, true);
                yield return Drive("gate-throttle-release", 2);
                Add("throttle-below-exits-drift", !motor.IsDrifting && motor.DriftDirection == 0f && motor.DriftTime == 0f,
                    "Dropping throttle to 0.39 clears direction and elapsed drift time");

                foreach (float direction in new[] { -1f, 1f })
                {
                    string side = direction < 0f ? "left" : "right";
                    Reset(road);
                    yield return Settle();
                    Inputs(1f, 0f, false, false);
                    yield return Drive(side + "-approach", 120);
                    Inputs(1f, direction * 0.7f, true, true);
                    int start = samples.Count;
                    yield return Drive(side + "-entry", 60);
                    var entry = samples.Skip(start).ToArray();
                    Add(side + "-grounded-drift", entry.All(s => s.grounded && s.isDrifting
                        && s.driftDirection == direction && s.restoredDrift && s.restoredSteering && s.restoredAcceleration)
                        && entry.Sum(s => s.contacts) > 0 && entry.Last().driftTime > entry.First().driftTime,
                        "60 real CharacterController steps; direction=" + motor.DriftDirection
                        + "; time=" + motor.DriftTime + "; contact records=" + entry.Sum(s => s.contacts));
                    Add(side + "-visual-heading", direction * entry.Last().visualYaw > 35f
                        && entry.Last().visualToVelocityAngle > 15f && motor.PhysicsVelocity.sqrMagnitude > 1f,
                        "Live model yaw=" + entry.Last().visualYaw + "; model-to-velocity angle="
                        + entry.Last().visualToVelocityAngle + "; original velocity heading is recorded per step");
                    yield return Capture(side + "-drift");
                    Inputs(1f, -direction, true, true);
                    start = samples.Count;
                    yield return Drive(side + "-countersteer", 30);
                    Add(side + "-direction-lock", samples.Skip(start).All(s => s.isDrifting && s.driftDirection == direction),
                        "Opposite steering keeps the entry direction=" + motor.DriftDirection);
                    Inputs(1f, 0f, true, true);
                    start = samples.Count;
                    yield return Drive(side + "-neutral-steering", 30);
                    Add(side + "-neutral-holds-drift", samples.Skip(start).All(s => s.isDrifting && s.driftDirection == direction),
                        "Releasing steering keeps the entry direction while brake and throttle stay held");
                    float speedBefore = motor.Speed;
                    var expectedTurn = NativeKartSteeringState.Step(motor.CurrentTurn,
                        new NativeKartSteeringInput { speed = speedBefore }, motor.stats, Time.fixedDeltaTime);
                    var expectedAcceleration = NativeKartAccelerationState.Step(expectedTurn.currentSpeed,
                        new NativeKartAccelerationInput { wishAccel = 1f, topSpeedModifier = 1f,
                            slopeLimit = body.slopeLimit, groundAngle = motor.GroundAngle }, motor.stats, Time.fixedDeltaTime);
                    Inputs(1f, 0f, false, true);
                    yield return Drive(side + "-brake-release", 1);
                    Add(side + "-brake-release-no-boost", !motor.IsDrifting && motor.DriftDirection == 0f
                        && motor.DriftTime == 0f && motor.UsesRestoredSteering && motor.UsesRestoredAcceleration
                        && Mathf.Abs(motor.Speed - expectedAcceleration.currentSpeed) < 0.0001f,
                        "Brake release returns to the original ordinary acceleration step; actual=" + motor.Speed
                        + "; expected=" + expectedAcceleration.currentSpeed);
                    yield return Drive(side + "-visual-return", 120);
                    Add(side + "-visual-returns", Quaternion.Angle(motor.DriftVisualRotation, Quaternion.identity) < 0.1f,
                        "Released drift rotation returns to identity; remaining angle="
                        + Quaternion.Angle(motor.DriftVisualRotation, Quaternion.identity));
                }

                foreach (bool inWater in new[] { false, true })
                {
                    string surface = inWater ? "water" : "grass";
                    Reset(inWater ? water : grass);
                    yield return Settle();
                    Inputs(1f, 0f, false, false);
                    yield return Drive(surface + "-approach", 120);
                    Inputs(1f, -0.7f, true, true);
                    int start = samples.Count;
                    yield return Drive(surface + "-drift", 60);
                    var surfaceSamples = samples.Skip(start).ToArray();
                    Add(surface + "-contact-drift", surfaceSamples.All(s => s.grounded && s.isDrifting
                        && s.restoredDrift && s.restoredSteering && s.restoredAcceleration
                        && (inWater ? s.water : s.grass)) && surfaceSamples.Sum(s => s.contacts) > 0,
                        "Real " + surface + " contacts keep the drift branch active; end speed=" + motor.Speed);
                }

                Reset(road);
                yield return Settle();
                Inputs(1f, 0f, false, false);
                yield return Drive("jump-approach", 120);
                Inputs(1f, 0.7f, true, true);
                yield return Drive("jump-drift-entry", 30);
                motor.jump = true;
                yield return Drive("jump-leaving-ground", 3);
                Add("air-clears-drift", !motor.Grounded && !motor.IsDrifting
                    && motor.DriftDirection == 0f && motor.DriftTime == 0f && !motor.UsesRestoredDrift,
                    "A real CharacterController jump clears the active direction and time on the airborne branch");
                int airborneStart = samples.Count;
                yield return Drive("jump-air-and-landing", 90);
                var landing = samples.Skip(airborneStart).ToArray();
                Add("landing-reenters-held-drift", landing.Any(s => !s.grounded && !s.isDrifting)
                    && landing.Any(s => s.grounded && s.isDrifting && s.driftDirection == 1f)
                    && motor.Grounded && motor.IsDrifting,
                    "Held brake/throttle/steering re-enter positive drift after real landing; airborne steps="
                    + landing.Count(s => !s.grounded) + "; grounded drifting steps=" + landing.Count(s => s.grounded && s.isDrifting));
                Inputs(1f, 0.7f, true, false);
                yield return Drive("permission-release", 2);
                Add("request-release-exits-drift", !motor.IsDrifting && motor.DriftDirection == 0f,
                    "The native playable input permission clears the drift when released");

                motor.ResetAt(forestPosition + Vector3.up * 0.2f, forestRotation, false);
                wheels.ResetState();
                motor.driving = false;
                motor.throttle = motor.steering = 0f;
                motor.braking = motor.drifting = false;
                observer.recordForest = true;
                yield return Settle();
                Inputs(1f, 0f, false, false);
                yield return Drive("forest-approach", 90);
                Inputs(1f, 0.7f, true, true);
                int forestStart = samples.Count;
                yield return Drive("forest-drift", 30);
                var forestSamples = samples.Skip(forestStart).ToArray();
                Add("forest-contact-drift", forestSamples.Any(s => s.grounded && s.isDrifting)
                    && motor.Grounded && motor.IsDrifting
                    && observer.contacts.Any(c => c.phase == "forest-drift"),
                    "A short live Forest arc after 90 approach steps; grounded drift steps="
                    + forestSamples.Count(s => s.grounded && s.isDrifting)
                    + "; observed colliders=" + string.Join(", ", observer.contacts.Where(c => c.phase == "forest-drift")
                        .Select(c => c.collider).Distinct().ToArray()));
                yield return Capture("forest-drift");
                observer.recordForest = false;
                Reset(road);
                yield return Settle();
                Inputs(1f, -0.7f, true, true);
                yield return Drive("pause-drift-entry", 20);
                Time.timeScale = 0f;
                yield return new WaitForEndOfFrame();
                Vector3 pausePosition = motor.transform.position;
                Quaternion pauseRotation = motor.transform.rotation, pauseDrift = motor.DriftVisualRotation;
                float pauseTime = motor.DriftTime, pauseDirection = motor.DriftDirection, pauseSpeed = motor.Speed;
                int pauseSteps = motor.FixedStepCount;
                yield return new WaitForSecondsRealtime(0.25f);
                Add("pause-freezes-drift", motor.FixedStepCount == pauseSteps && motor.transform.position == pausePosition
                    && motor.transform.rotation == pauseRotation && motor.DriftVisualRotation == pauseDrift
                    && motor.DriftTime == pauseTime && motor.DriftDirection == pauseDirection && motor.Speed == pauseSpeed,
                    "Pause retains actual fixed-step count, collision pose, speed, direction, time and visual rotation");
                motor.ResetAt(road + Vector3.up * 0.4f, Quaternion.identity, false);
                Add("reset-clears-drift", !motor.IsDrifting && motor.DriftDirection == 0f && motor.DriftTime == 0f
                    && !motor.UsesRestoredDrift && Quaternion.Angle(motor.DriftVisualRotation, Quaternion.identity) < 0.001f
                    && Quaternion.Angle(motor.driftPivot.localRotation, Quaternion.identity) < 0.001f,
                    "Reset clears the drift state and immediately restores the applied drift pivot");
                Add("fixed-step-observations", samples.Count > 0 && samples.All(s => s.fixedStepDelta == 1),
                    "Each of the " + samples.Count + " retained samples covers exactly one live fixed update");
                Add("grounded-drift-integration", samples.Any(s => s.groundedBefore && s.isDrifting)
                    && samples.Where(s => s.groundedBefore).All(s => s.speedError < 0.0001f
                        && s.turnError < 0.0001f && s.visualRotationError < 0.08f && s.elapsedError < 0.0001f),
                    "Original reference-backed drift state, steering and acceleration helpers match live wiring; "
                    + "max raw-speed error=" + samples.Max(s => s.speedError)
                    + "; max turn error=" + samples.Max(s => s.turnError)
                    + "; max visual angle error=" + samples.Max(s => s.visualRotationError)
                    + "; max elapsed error=" + samples.Max(s => s.elapsedError));
                Add("applied-drift-pose", poses.Count == 3 && poses.All(p => p.grounded && p.isDrifting
                    && p.appliedRotationError < 0.01f && File.Exists(Path.Combine(directory, p.image))),
                    "Three captured live collision poses share the actual model rotation; max application error="
                    + (poses.Count == 0 ? -1f : poses.Max(p => p.appliedRotationError)));
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);

                void Plane(string name, Vector3 origin, string tag)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ground.name = name;
                    ground.tag = tag;
                    ground.transform.position = origin - Vector3.up * 0.5f;
                    ground.transform.localScale = new Vector3(2000f, 1f, 2000f);
                    var material = ground.GetComponent<Renderer>().material;
                    material.color = new Color(0.24f, 0.27f, 0.3f);
                    materials.Add(material);
                    observer.watched.Add(ground.GetComponent<Collider>());
                    fixtures.Add(ground);
                    for (int line = -10; line <= 10; line++)
                    {
                        foreach (bool alongZ in new[] { false, true })
                        {
                            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            stripe.name = "Drift fixture direction grid";
                            var collider = stripe.GetComponent<Collider>();
                            collider.enabled = false;
                            Destroy(collider);
                            stripe.transform.position = origin + Vector3.up * 0.003f
                                + (alongZ ? Vector3.right : Vector3.forward) * line * 10f;
                            stripe.transform.localScale = alongZ
                                ? new Vector3(0.025f, 0.002f, 200f) : new Vector3(200f, 0.002f, 0.025f);
                            fixtures.Add(stripe);
                        }
                    }
                }
                void Reset(Vector3 origin)
                {
                    motor.ResetAt(origin + Vector3.up * 0.4f, Quaternion.identity, false);
                    wheels.ResetState();
                    motor.driving = false;
                    motor.throttle = motor.steering = 0f;
                    motor.braking = motor.drifting = motor.jump = false;
                    Time.timeScale = 1f;
                }
                void Inputs(float throttle, float steering, bool braking, bool requested)
                {
                    motor.driving = true;
                    motor.throttle = throttle;
                    motor.steering = steering;
                    motor.braking = braking;
                    motor.drifting = requested;
                }
                IEnumerator Settle()
                {
                    observer.phase = "settle";
                    for (int i = 0; i < 40; i++) { observer.step = i; yield return tick; }
                }
                IEnumerator Drive(string name, int count)
                {
                    observer.phase = name;
                    for (int i = 0; i < count; i++)
                    {
                        observer.step = i;
                        int fixedStep = motor.FixedStepCount, contactStart = observer.contacts.Count;
                        bool groundedBefore = motor.Grounded;
                        var expectedDrift = NativeKartDriftState.Request(motor.DriftState, new NativeKartDriftInput
                        {
                            requested = motor.driving && motor.drifting, grounded = groundedBefore,
                            braking = motor.braking, wishAccel = motor.throttle, wishTurn = motor.steering
                        }, motor.stats);
                        var expectedTurn = NativeKartSteeringState.Step(motor.CurrentTurn,
                            new NativeKartSteeringInput { speed = motor.Speed,
                                wishTurn = motor.steering, driftDirection = expectedDrift.direction }, motor.stats, Time.fixedDeltaTime);
                        var expectedAcceleration = NativeKartAccelerationState.Step(expectedTurn.currentSpeed,
                            new NativeKartAccelerationInput
                            {
                                wishAccel = motor.throttle, braking = motor.braking,
                                onGrass = motor.OnGrass || motor.InWater, groundAngle = motor.GroundAngle,
                                slopeLimit = body.slopeLimit, topSpeedModifier = 1f,
                                driftDirection = expectedDrift.direction, currentTurn = expectedTurn.currentTurn,
                                turnLimit = expectedTurn.turnLimit
                            }, motor.stats, Time.fixedDeltaTime);
                        Quaternion expectedVisual = NativeKartDriftState.VisualRotation(motor.DriftVisualRotation,
                            expectedDrift.direction, motor.stats, Time.fixedDeltaTime);
                        expectedDrift = expectedDrift.AdvanceTime(Time.fixedDeltaTime);
                        yield return tick;
                        Vector3 planarVelocity = Vector3.ProjectOnPlane(motor.PhysicsVelocity, Vector3.up);
                        Vector3 modelForward = motor.transform.rotation * motor.DriftVisualRotation * Vector3.forward;
                        samples.Add(new Sample
                        {
                            phase = name, step = i, fixedStepDelta = motor.FixedStepCount - fixedStep,
                            contacts = observer.contacts.Count - contactStart,
                            groundedBefore = groundedBefore,
                            speedError = groundedBefore ? Mathf.Abs(motor.Speed - expectedAcceleration.currentSpeed) : 0f,
                            turnError = groundedBefore ? Mathf.Abs(motor.CurrentTurn - expectedTurn.currentTurn) : 0f,
                            visualRotationError = groundedBefore ? Quaternion.Angle(motor.DriftVisualRotation, expectedVisual) : 0f,
                            elapsedError = Mathf.Abs(motor.DriftTime - expectedDrift.elapsed),
                            throttle = motor.throttle, steering = motor.steering, braking = motor.braking,
                            driftRequested = motor.drifting, speed = motor.Speed, currentTurn = motor.CurrentTurn,
                            driftDirection = motor.DriftDirection, driftTime = motor.DriftTime,
                            grounded = motor.Grounded, grass = motor.OnGrass, water = motor.InWater,
                            isDrifting = motor.IsDrifting, restoredDrift = motor.UsesRestoredDrift,
                            restoredSteering = motor.UsesRestoredSteering, restoredAcceleration = motor.UsesRestoredAcceleration,
                            visualYaw = Vector3.SignedAngle(Vector3.forward, motor.DriftVisualRotation * Vector3.forward, Vector3.up),
                            velocityYaw = planarVelocity.sqrMagnitude > 0.01f
                                ? Vector3.SignedAngle(motor.transform.forward, planarVelocity, Vector3.up) : 0f,
                            visualToVelocityAngle = planarVelocity.sqrMagnitude > 0.01f ? Vector3.Angle(modelForward, planarVelocity) : 0f,
                            position = motor.transform.position, physicsVelocity = motor.PhysicsVelocity,
                            forward = motor.transform.forward, rotation = motor.transform.rotation,
                            driftVisualRotation = motor.DriftVisualRotation
                        });
                    }
                }
                IEnumerator Capture(string name)
                {
                    Time.timeScale = 0f;
                    bool wasFrozen = wheels.frozen;
                    wheels.frozen = true;
                    motor.PrepareRenderPose(false);
                    camera.transform.position = motor.transform.TransformPoint(new Vector3(4.8f, 3.2f, -5.8f));
                    camera.transform.LookAt(motor.transform.position + Vector3.up * 0.7f);
                    camera.fieldOfView = 38f;
                    yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    string filename = name + ".png";
                    File.WriteAllBytes(Path.Combine(directory, filename), texture.EncodeToPNG());
                    Destroy(texture);
                    var planarVelocity = Vector3.ProjectOnPlane(motor.PhysicsVelocity, Vector3.up);
                    poses.Add(new Pose
                    {
                        name = name, image = filename, direction = motor.DriftDirection, speed = motor.Speed,
                        driftTime = motor.DriftTime, grounded = motor.Grounded, isDrifting = motor.IsDrifting,
                        position = motor.transform.position, velocity = motor.PhysicsVelocity, rotation = motor.transform.rotation,
                        modelForward = motor.driftPivot.forward, expectedLocalRotation = motor.DriftVisualRotation,
                        appliedLocalRotation = motor.driftPivot.localRotation,
                        appliedRotationError = Quaternion.Angle(motor.DriftVisualRotation, motor.driftPivot.localRotation),
                        visualToVelocityAngle = Vector3.Angle(motor.driftPivot.forward, planarVelocity)
                    });
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
                    race.racers[i].GetComponent<CharacterController>().enabled = controllerEnabledBefore[i];
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
                boundary = "A cloned exported kart drives on isolated real CharacterController road, Grass and Water contacts. "
                    + "Live samples establish input thresholds, direction locking, state exits, airborne re-entry, applied model yaw and motion. "
                    + "A short Forest approach and drift arc records actual track contacts. Three screenshots freeze actual driven poses; "
                    + "the separate original x64 reference establishes drift formula parity. "
                    + "Motor speed and drift state share original world units; airborne integration is checked separately.",
                fixedDeltaTime = Time.fixedDeltaTime, worldSpeedDivisor = 1f, width = Screen.width, height = Screen.height,
                passed = checks.Count > 0 && checks.All(c => c.passed), checks = checks.ToArray(),
                samples = samples.ToArray(), poses = poses.ToArray(),
                contacts = ReferenceEquals(observer, null) ? new Contact[0] : observer.contacts.ToArray()
            };
            result.passed = report.passed;
            File.WriteAllText(Path.Combine(directory, "drift-live-report.json"), JsonUtility.ToJson(report, true));
            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
        }
    }
}
