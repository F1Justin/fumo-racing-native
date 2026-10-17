using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartTurnVerification
    {
        [Serializable]
        public sealed class Result
        {
            public bool roadFound, bodyAndCharacterLean, leftRightSymmetry, frontWheelWorldSteer;
            public bool suspensionTravelMatched, renderPoseAligned, pauseFrozen, resetRestored, capturesPresent;
            public bool normalRestartAnimationReset;
            public string roadCollider, roadMaterial, detail;
            public Vector3 roadPoint, settledAnchor;
            public int vehicleCount, liveFrames;
            public float maxRenderPoseError, maxPausePositionChange, maxPauseAngleChange;
            public float maxResetPositionError, maxResetAngleError;
            public TurnCase[] vehicles;
            public string[] captures;
        }

        [Serializable]
        public sealed class TurnCase
        {
            public string vehicle;
            public float targetTilt, leftRootRoll, rightRootRoll, leftFumoRoll, rightFumoRoll;
            public float leftRootHeading, rightRootHeading, leftFumoHeading, rightFumoHeading;
            public float[] leftWheelHeading, rightWheelHeading;
            public float[] straightGroundGap, leftGroundGap, rightGroundGap;
            public int wheelGroundRayHits;
            public float landedRootTravel, landedFumoTravel;
            public bool bodyAndCharacterLean, symmetric, frontWheelWorldSteer, suspensionTravelMatched;
        }

        sealed class Pose
        {
            public Vector3 rootPosition, fumoPosition, visualPosition;
            public Quaternion rootRotation, fumoRotation, visualRotation;
            public Vector3[] wheelPositions;
            public Quaternion[] wheelRotations, torqueRotations;
            public Vector3[] wheelHeadingAxes;
        }

        struct Road
        {
            public bool found;
            public Vector3 point;
            public Quaternion heading;
            public Collider collider;
        }

        public static IEnumerator Run(NativeRace race, NativeKartWheelVisual[] visuals,
            string parentDirectory, Result result)
        {
            string directory = Path.Combine(parentDirectory, "turn");
            Directory.CreateDirectory(directory);
            Road road = FindRoad(race);
            result.roadFound = road.found;
            if (!road.found)
            {
                result.detail = "No flat non-grass Forest road collider beneath the route";
                Write(directory, result);
                yield break;
            }
            result.roadPoint = road.point;
            result.roadCollider = road.collider.name;
            result.roadMaterial = road.collider.sharedMaterial ? road.collider.sharedMaterial.name : "none";

            var motors = race.racers;
            var originalPositions = new Vector3[motors.Length];
            var originalRotations = new Quaternion[motors.Length];
            var originalEnabled = new bool[motors.Length];
            var originalDriving = new bool[motors.Length];
            var originalThrottle = new float[motors.Length];
            var originalSteering = new float[motors.Length];
            var originalFrozen = new bool[visuals.Length];
            for (int i = 0; i < motors.Length; i++)
            {
                originalPositions[i] = motors[i].transform.position;
                originalRotations[i] = motors[i].transform.rotation;
                originalEnabled[i] = motors[i].enabled;
                originalDriving[i] = motors[i].driving;
                originalThrottle[i] = motors[i].throttle;
                originalSteering[i] = motors[i].steering;
                originalFrozen[i] = visuals[i].frozen;
            }
            float previousTimeScale = Time.timeScale;
            var camera = race.raceCamera;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            var cases = new List<TurnCase>();
            var captures = new List<string>();
            result.bodyAndCharacterLean = true;
            result.leftRightSymmetry = true;
            result.frontWheelWorldSteer = true;
            result.suspensionTravelMatched = true;
            Vector3 anchor = road.point + Vector3.up * 0.6f;

            try
            {
                Time.timeScale = 0f;
                race.SetVisualCapture("race", false);
                for (int i = 0; i < motors.Length; i++)
                {
                    motors[i].enabled = false;
                    visuals[i].frozen = true;
                }

                var probe = motors[0];
                probe.ResetAt(anchor, road.heading, false);
                probe.driving = false;
                probe.throttle = probe.steering = 0f;
                probe.braking = probe.drifting = false;
                probe.enabled = true;
                Time.timeScale = 1f;
                for (int step = 0; step < 90; step++) yield return new WaitForFixedUpdate();
                if (!probe.Grounded) throw new InvalidOperationException("Turn fixture failed to settle on the Forest road");
                anchor = probe.transform.position;
                result.settledAnchor = anchor;
                probe.enabled = false;
                Time.timeScale = 0f;

                for (int i = 0; i < motors.Length; i++)
                {
                    var motor = motors[i];
                    var visual = visuals[i];
                    motor.ResetAt(anchor, road.heading, false);
                    visual.ResetState();
                    ResetAnimation(motor);
                    var start = Snapshot(motor, visual);
                    for (int step = 0; step < 6; step++)
                        visual.StepForTest(new NativeKartWheelInput
                        {
                            topSpeed = motor.stats._topSpeed, grounded = true, landed = step == 0
                        }, 1f / 60f);
                    var landed = Snapshot(motor, visual);
                    float rootTravel = Vector3.Dot(landed.rootPosition - start.rootPosition, motor.transform.up);
                    float fumoTravel = Vector3.Dot(landed.fumoPosition - start.fumoPosition, motor.transform.up);
                    visual.ResetState();
                    float speed = motor.stats._topSpeed * 0.6f;
                    ApplyFixture(visual, speed, motor.stats._topSpeed, 0f);
                    var straight = Snapshot(motor, visual);
                    if (i == 0)
                    {
                        PlaceCamera(camera, motor.transform);
                        yield return Capture("turn-straight.png");
                    }
                    visual.ResetState();
                    ApplyFixture(visual, speed, motor.stats._topSpeed, -1f);
                    var left = Snapshot(motor, visual);
                    var leftState = visual.State;
                    if (i == 0) yield return Capture("turn-left.png");
                    visual.ResetState();
                    ApplyFixture(visual, speed, motor.stats._topSpeed, 1f);
                    var right = Snapshot(motor, visual);
                    if (i == 0) yield return Capture("turn-right.png");

                    var turn = EvaluateCase(motor, visual, road, start, straight, left, right, leftState._targetTilt);
                    turn.landedRootTravel = rootTravel;
                    turn.landedFumoTravel = fumoTravel;
                    turn.suspensionTravelMatched = Mathf.Abs(rootTravel) > 0.01f
                        && Mathf.Abs(rootTravel - fumoTravel) < 0.005f;
                    cases.Add(turn);
                    result.bodyAndCharacterLean &= turn.bodyAndCharacterLean;
                    result.leftRightSymmetry &= turn.symmetric;
                    result.frontWheelWorldSteer &= turn.frontWheelWorldSteer;
                    result.suspensionTravelMatched &= turn.suspensionTravelMatched;
                }
                result.vehicleCount = cases.Count;
                for (int i = 1; i < motors.Length; i++)
                {
                    motors[i].ResetAt(originalPositions[i], originalRotations[i], false);
                    visuals[i].ResetState();
                    ResetAnimation(motors[i]);
                }

                var player = motors[0];
                var playerVisual = visuals[0];
                player.ResetAt(anchor, road.heading, false);
                playerVisual.ResetState();
                ResetAnimation(player);
                var idle = Snapshot(player, playerVisual);
                playerVisual.StepForTest(new NativeKartWheelInput
                {
                    speed = 145f, topSpeed = 145f, grounded = true
                }, 1f / 6f);
                playerVisual.ResetState();
                var immediatelyReset = Snapshot(player, playerVisual);
                PoseDifference(idle, immediatelyReset, out float immediatePosition, out float immediateAngle);
                player.enabled = true;
                playerVisual.frozen = false;
                player.driving = true;
                player.throttle = 1f;
                player.steering = 0.65f;
                Time.timeScale = 1f;
                float until = Time.realtimeSinceStartup + 1.2f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return new WaitForEndOfFrame();
                    player.PrepareRenderPose(true);
                    if (player.Speed <= 2f) continue;
                    result.liveFrames++;
                    result.maxRenderPoseError = Mathf.Max(result.maxRenderPoseError,
                        Quaternion.Angle(player.visual.rotation, player.RenderRotation));
                }
                result.renderPoseAligned = result.liveFrames >= 15 && result.maxRenderPoseError < 0.5f;

                Time.timeScale = 0f;
                yield return new WaitForEndOfFrame();
                var beforePause = Snapshot(player, playerVisual);
                yield return new WaitForSecondsRealtime(0.35f);
                yield return new WaitForEndOfFrame();
                var afterPause = Snapshot(player, playerVisual);
                PoseDifference(beforePause, afterPause, out result.maxPausePositionChange, out result.maxPauseAngleChange);
                result.pauseFrozen = result.maxPausePositionChange < 0.002f && result.maxPauseAngleChange < 0.1f;

                player.enabled = false;
                playerVisual.frozen = true;
                // Exercise the shared visual reset without starting an input-owned race in this fixture.
                race.ResetRace(false);
                Time.timeScale = 0f;
                result.normalRestartAnimationReset = true;
                foreach (var motor in motors)
                {
                    var animation = motor.GetComponent<NativeKartAnimation>();
                    if (!animation || !animation.animator || !animation.accelerationPivot)
                    {
                        result.normalRestartAnimationReset = false;
                        continue;
                    }
                    result.normalRestartAnimationReset &= animation.AverageAcceleration == 0f
                        && Quaternion.Angle(animation.accelerationPivot.localRotation, Quaternion.identity) < 0.1f
                        && Mathf.Abs(animation.animator.GetFloat("vspeed")) < 0.0001f
                        && Mathf.Abs(animation.animator.GetCurrentAnimatorStateInfo(0).normalizedTime) < 0.0001f;
                }
                // Reposition the restarted car to the same road anchor for a world-pose comparison.
                player.ResetAt(anchor, road.heading, false);
                playerVisual.ResetState();
                var reset = Snapshot(player, playerVisual);
                PoseDifference(idle, reset, out result.maxResetPositionError, out result.maxResetAngleError);
                result.maxResetPositionError = Mathf.Max(result.maxResetPositionError, immediatePosition);
                result.maxResetAngleError = Mathf.Max(result.maxResetAngleError, immediateAngle);
                result.resetRestored = result.normalRestartAnimationReset
                    && result.maxResetPositionError < 0.002f && result.maxResetAngleError < 0.1f;
            }
            finally
            {
                Time.timeScale = 0f;
                for (int i = 0; i < motors.Length; i++)
                {
                    motors[i].driving = false;
                    motors[i].throttle = motors[i].steering = 0f;
                    motors[i].ResetAt(originalPositions[i], originalRotations[i], false);
                    visuals[i].ResetState();
                    ResetAnimation(motors[i]);
                    motors[i].enabled = originalEnabled[i];
                    motors[i].driving = originalDriving[i];
                    motors[i].throttle = originalThrottle[i];
                    motors[i].steering = originalSteering[i];
                    visuals[i].frozen = originalFrozen[i];
                }
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = cameraFov;
                Time.timeScale = previousTimeScale;
            }

            result.vehicles = cases.ToArray();
            result.captures = captures.ToArray();
            result.capturesPresent = captures.Count == 3;
            result.detail = "Fixed flat Forest road, deterministic 120 x 1/60 s left/right visual fixtures for four cars; separate live Player steering, pause and normal ResetRace observations with animation and wheel poses. Ground gaps are recorded without an acceptance threshold.";
            Write(directory, result);

            IEnumerator Capture(string name)
            {
                yield return new WaitForSecondsRealtime(0.4f);
                yield return new WaitForEndOfFrame();
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(directory, name), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
                captures.Add(name);
            }
        }

        static void ResetAnimation(NativeKartMotor motor)
        {
            var animation = motor.GetComponent<NativeKartAnimation>();
            if (animation) animation.ResetPlayback();
        }

        static void ApplyFixture(NativeKartWheelVisual visual, float speed, float topSpeed, float steering)
        {
            var input = new NativeKartWheelInput
            {
                speed = speed, topSpeed = topSpeed, steering = steering, grounded = true
            };
            for (int i = 0; i < 120; i++) visual.StepForTest(input, 1f / 60f);
        }

        static Road FindRoad(NativeRace race)
        {
            for (int i = 0; i < race.route.Length - 1; i++)
            {
                Vector3 forward = race.route[i + 1] - race.route[i];
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) continue;
                RaycastHit[] hits = Physics.RaycastAll(race.route[i] + Vector3.up * 20f, Vector3.down, 50f);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit hit in hits)
                {
                    if (!hit.collider || hit.collider.isTrigger || hit.collider.GetComponentInParent<NativeKartMotor>()) continue;
                    if (hit.normal.y < 0.97f || hit.collider.CompareTag("Grass") || hit.collider.CompareTag("Water")) break;
                    return new Road { found = true, point = hit.point,
                        heading = Quaternion.LookRotation(forward.normalized), collider = hit.collider };
                }
            }
            return default;
        }

        static Pose Snapshot(NativeKartMotor motor, NativeKartWheelVisual visual)
        {
            var pose = new Pose
            {
                rootPosition = visual.root.position, rootRotation = visual.root.rotation,
                fumoPosition = visual.fumo.position, fumoRotation = visual.fumo.rotation,
                visualPosition = motor.visual.position, visualRotation = motor.visual.rotation,
                wheelPositions = new Vector3[visual.suspensions.Length],
                wheelRotations = new Quaternion[visual.suspensions.Length],
                torqueRotations = new Quaternion[visual.torques.Length],
                wheelHeadingAxes = new Vector3[visual.suspensions.Length]
            };
            for (int i = 0; i < visual.suspensions.Length; i++)
            {
                var wheel = visual.suspensions[i];
                pose.wheelPositions[i] = wheel.position;
                pose.wheelRotations[i] = wheel.rotation;
                Vector3[] axes = { wheel.forward, wheel.right, wheel.up };
                Vector3 best = axes[0];
                foreach (Vector3 axis in axes)
                    if (Vector3.ProjectOnPlane(axis, Vector3.up).sqrMagnitude
                        > Vector3.ProjectOnPlane(best, Vector3.up).sqrMagnitude) best = axis;
                pose.wheelHeadingAxes[i] = Vector3.ProjectOnPlane(best, Vector3.up).normalized;
            }
            for (int i = 0; i < visual.torques.Length; i++)
                pose.torqueRotations[i] = visual.torques[i].rotation;
            return pose;
        }

        static TurnCase EvaluateCase(NativeKartMotor motor, NativeKartWheelVisual visual, Road road,
            Pose start, Pose straight, Pose left, Pose right, float leftTarget)
        {
            int straightHits, leftHits, rightHits;
            float[] straightGaps = GroundGaps(straight, visual, road, out straightHits);
            float[] leftGaps = GroundGaps(left, visual, road, out leftHits);
            float[] rightGaps = GroundGaps(right, visual, road, out rightHits);
            var result = new TurnCase
            {
                vehicle = motor.name, targetTilt = leftTarget,
                leftWheelHeading = new float[visual.suspensions.Length],
                rightWheelHeading = new float[visual.suspensions.Length],
                straightGroundGap = straightGaps,
                leftGroundGap = leftGaps,
                rightGroundGap = rightGaps,
                wheelGroundRayHits = straightHits + leftHits + rightHits
            };
            Vector3 axis = motor.transform.forward;
            result.leftRootRoll = Roll(straight.rootRotation, left.rootRotation, axis);
            result.rightRootRoll = Roll(straight.rootRotation, right.rootRotation, axis);
            result.leftFumoRoll = Roll(straight.fumoRotation, left.fumoRotation, axis);
            result.rightFumoRoll = Roll(straight.fumoRotation, right.fumoRotation, axis);
            result.leftRootHeading = Heading(straight.rootRotation, left.rootRotation);
            result.rightRootHeading = Heading(straight.rootRotation, right.rootRotation);
            result.leftFumoHeading = Heading(straight.fumoRotation, left.fumoRotation);
            result.rightFumoHeading = Heading(straight.fumoRotation, right.fumoRotation);
            float expectedRoll = Mathf.Abs(leftTarget);
            result.bodyAndCharacterLean = expectedRoll > 5f
                && Mathf.Abs(Mathf.Abs(result.leftRootRoll) - expectedRoll) < 2.5f
                && Mathf.Abs(Mathf.Abs(result.leftFumoRoll) - expectedRoll) < 2.5f
                && Mathf.Abs(result.leftRootRoll - result.leftFumoRoll) < 2f
                && Mathf.Abs(result.leftRootHeading) < 3f && Mathf.Abs(result.leftFumoHeading) < 3f;
            result.symmetric = Mathf.Abs(result.leftRootRoll + result.rightRootRoll) < 2f
                && Mathf.Abs(result.leftFumoRoll + result.rightFumoRoll) < 2f
                && Mathf.Abs(result.rightRootHeading) < 3f && Mathf.Abs(result.rightFumoHeading) < 3f;
            result.frontWheelWorldSteer = true;
            for (int i = 0; i < result.leftWheelHeading.Length; i++)
            {
                Vector3 localAxis = Quaternion.Inverse(straight.wheelRotations[i]) * straight.wheelHeadingAxes[i];
                Vector3 leftAxis = Vector3.ProjectOnPlane(left.wheelRotations[i] * localAxis, Vector3.up).normalized;
                Vector3 rightAxis = Vector3.ProjectOnPlane(right.wheelRotations[i] * localAxis, Vector3.up).normalized;
                result.leftWheelHeading[i] = Vector3.SignedAngle(straight.wheelHeadingAxes[i], leftAxis, Vector3.up);
                result.rightWheelHeading[i] = Vector3.SignedAngle(straight.wheelHeadingAxes[i], rightAxis, Vector3.up);
                float expected = visual.settings._wheels[i]._steering;
                if (expected > 0f)
                    result.frontWheelWorldSteer &= Mathf.Abs(Mathf.Abs(result.leftWheelHeading[i]) - expected) < 4f
                        && Mathf.Abs(result.leftWheelHeading[i] + result.rightWheelHeading[i]) < 4f;
                else
                    result.frontWheelWorldSteer &= Mathf.Abs(result.leftWheelHeading[i]) < 4f
                        && Mathf.Abs(result.rightWheelHeading[i]) < 4f;
            }
            return result;
        }

        static float Roll(Quaternion baseline, Quaternion turned, Vector3 axis)
            => Vector3.SignedAngle(baseline * Vector3.up, turned * Vector3.up, axis);

        static float Heading(Quaternion baseline, Quaternion turned)
        {
            Vector3 before = Vector3.ProjectOnPlane(baseline * Vector3.forward, Vector3.up);
            Vector3 after = Vector3.ProjectOnPlane(turned * Vector3.forward, Vector3.up);
            return before.sqrMagnitude > 0.01f && after.sqrMagnitude > 0.01f
                ? Vector3.SignedAngle(before, after, Vector3.up) : 180f;
        }

        static float[] GroundGaps(Pose pose, NativeKartWheelVisual visual, Road road, out int hits)
        {
            hits = 0;
            var result = new float[pose.wheelPositions.Length];
            for (int i = 0; i < result.Length; i++)
            {
                Vector3 point = pose.wheelPositions[i];
                RaycastHit hit;
                if (road.collider.Raycast(new Ray(point + Vector3.up * 2f, Vector3.down), out hit, 5f))
                {
                    result[i] = point.y - hit.point.y - visual.settings._wheels[i]._wheelRadius;
                    hits++;
                }
                else result[i] = -999f;
            }
            return result;
        }

        static void PoseDifference(Pose a, Pose b, out float position, out float angle)
        {
            position = Mathf.Max(Vector3.Distance(a.rootPosition, b.rootPosition),
                Vector3.Distance(a.fumoPosition, b.fumoPosition),
                Vector3.Distance(a.visualPosition, b.visualPosition));
            angle = Mathf.Max(Quaternion.Angle(a.rootRotation, b.rootRotation),
                Quaternion.Angle(a.fumoRotation, b.fumoRotation),
                Quaternion.Angle(a.visualRotation, b.visualRotation));
            for (int i = 0; i < a.wheelPositions.Length; i++)
            {
                position = Mathf.Max(position, Vector3.Distance(a.wheelPositions[i], b.wheelPositions[i]));
                angle = Mathf.Max(angle, Quaternion.Angle(a.wheelRotations[i], b.wheelRotations[i]));
            }
            for (int i = 0; i < a.torqueRotations.Length; i++)
                angle = Mathf.Max(angle, Quaternion.Angle(a.torqueRotations[i], b.torqueRotations[i]));
        }

        static void PlaceCamera(Camera camera, Transform car)
        {
            camera.transform.position = car.TransformPoint(new Vector3(3.3f, 1.9f, -4.4f));
            camera.transform.LookAt(car.position + Vector3.up * 0.75f);
            camera.fieldOfView = 38f;
        }

        static void Write(string directory, Result result)
            => File.WriteAllText(Path.Combine(directory, "turn-report.json"), JsonUtility.ToJson(result, true));
    }
}
