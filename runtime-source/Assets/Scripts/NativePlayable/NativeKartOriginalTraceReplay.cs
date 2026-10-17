using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartOriginalTraceReplay
    {
        [Serializable] sealed class TraceInput { public float accel, turn, drift, driftRequest; public bool brake, stopDrift, jump; }
        [Serializable] sealed class TraceState
        {
            public Vector3 position, velocity;
            public Quaternion rotation;
            public float speed, turn, driftDir, currentGravity;
            public bool grounded, controllerGrounded, isDrifting, wishSpin, wishJump;
        }
        [Serializable] sealed class TraceFrame
        {
            public int index;
            public float deltaTime;
            public TraceInput input;
            public TraceState before, after;
        }
        [Serializable] sealed class TraceReference
        {
            public string format, originalAssemblySha256, sourceSha256;
            public int expectedFrames;
            public int[] captureFrameIndices;
            public TraceFrame[] frames;
        }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class ReplayFrame
        {
            public int index, fixedStep, nativeUnityFrame, lastStepFrame, stepDelta;
            public float originalDeltaTime, nativeDeltaTime;
            public TraceInput input;
            public bool inputMatched, grounded, originalGrounded, groundedMatched;
            public bool drifting, originalDrifting, driftMatched, originalControllerGrounded, nativeControllerGrounded, controllerGroundedMatched, jumpRequested, jumpedThisStep;
            public float driftDirection, originalDriftDirection, verticalSpeed, originalVerticalSpeed, verticalSpeedError;
            public Vector3 originalBeforePosition, originalAfterPosition, nativePosition;
            public Quaternion originalAfterRotation, nativeRotation;
            public Vector3 originalAfterVelocity, nativeDriveVelocity, nativeMovementVelocity, nativePhysicsVelocity;
            public float originalAfterSpeed, nativeSpeed, originalAfterTurn, nativeTurn;
            public float positionError, rotationErrorDegrees, rawSpeedError, worldSpeedError, turnError, horizontalVelocityError;
        }
        [Serializable] sealed class Report
        {
            public string simulationClock = "Update";
            public string version, platform, boundary, originalAssemblySha256, originalTraceSha256, dtMode;
            public bool protocolPassed, trajectoryAligned, equivalencePassed;
            public int expectedFrames, capturedFrames, groundedFrames, groundedMismatches, inputMismatches, fixedStepMismatches, deltaTimeMismatches;
            public int driftMismatches, controllerGroundedMismatches, screenshotRequests, capturedScreenshots;
            public float maxVerticalSpeedError;
            public int[] screenshotFrameIndices;
            public float nativeFixedDeltaTime, minOriginalDeltaTime, maxOriginalDeltaTime;
            public float maxPositionError, endPositionError, maxRotationErrorDegrees;
            public float maxRawSpeedError, maxWorldSpeedError, maxTurnError, maxHorizontalVelocityError;
            public Check[] checks;
            public ReplayFrame[] frames;
        }

        public static IEnumerator Run(NativeRace race, string directory, string referencePath)
        {
            Directory.CreateDirectory(directory);
            float originalAudioVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            var checks = new List<Check>();
            var samples = new List<ReplayFrame>();
            var reference = JsonUtility.FromJson<TraceReference>(File.ReadAllText(referencePath));
            bool extended = reference != null && reference.format == "fumo-original-actions-v2";
            bool validReference = reference != null && reference.frames != null
                && (reference.format == "fumo-original-trajectory-v1" && reference.frames.Length == 690
                    || extended && reference.expectedFrames == 720 && reference.frames.Length == 720);
            Add("normalized-reference", validReference,
                "Format=" + (reference == null ? "null" : reference.format) + "; frames="
                + (reference == null || reference.frames == null ? 0 : reference.frames.Length));
            if (!validReference)
            {
                WriteFailure();
                yield break;
            }

            var motor = race.racers[0];
            var screenshotIndices = extended && reference.captureFrameIndices != null
                ? new HashSet<int>(reference.captureFrameIndices) : new HashSet<int>();
            var requestedScreenshots = new List<int>();
            bool[] racerEnabled = race.racers.Select(r => r.enabled).ToArray();
            bool[] colliderEnabled = race.racers.Select(r => r.GetComponent<CharacterController>().enabled).ToArray();
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            int fixedStepMismatches = 0;
            int deltaTimeMismatches = 0;
            float originalCaptureDeltaTime = Time.captureDeltaTime;
            try
            {
                race.SetVisualCapture("race", extended);
                race.AllowCameraVerificationUpdates = extended;
                foreach (var racer in race.racers)
                {
                    racer.enabled = false;
                    if (racer != motor) racer.GetComponent<CharacterController>().enabled = false;
                }
                yield return null;
                motor.ResetAt(reference.frames[0].before.position,
                    reference.frames[0].before.rotation, false);
                if (extended) motor.RestoreVerificationGroundState(reference.frames[0].before.grounded, reference.frames[0].before.currentGravity);
                motor.UseFixedVerificationClock = false;
                motor.driving = true;
                motor.throttle = motor.steering = 0f;
                motor.braking = motor.drifting = motor.jump = motor.jumpHeld = false;
                motor.AIDriftActive = extended;
                Time.timeScale = 1f;
                motor.enabled = true;

                Add("global-silence", AudioListener.volume == 0f, "AudioListener volume=" + AudioListener.volume);
                Add("initial-rest-state", reference.frames[0].before.speed == 0f
                    && reference.frames[0].before.turn == 0f && motor.Speed == 0f && motor.CurrentTurn == 0f,
                    "Original initial speed=" + reference.frames[0].before.speed
                    + "; turn=" + reference.frames[0].before.turn
                    + "; original grounded=" + reference.frames[0].before.grounded
                    + "; Native reset grounded=" + motor.Grounded);
                Add("initial-fixed-step-60hz", Mathf.Abs(originalFixedDeltaTime - 1f / 60f) < 0.000001f,
                    "Native initial fixed delta time=" + originalFixedDeltaTime);
                Add("real-character-controller", motor.GetComponent<CharacterController>() != null,
                    "Forest racer uses a real CharacterController");
                int inputMismatches = 0;
                float requestedDriftDirection = 0f;
                foreach (var frame in reference.frames)
                {
                    motor.throttle = frame.input.accel;
                    motor.steering = frame.input.turn;
                    motor.braking = frame.input.brake;
                    motor.drifting = motor.jumpHeld = false;
                    motor.jump = extended && frame.input.jump;
                    motor.AIDriftActive = extended;
                    if (extended)
                    {
                        if (frame.input.driftRequest != 0f) requestedDriftDirection = frame.input.driftRequest;
                        if (frame.input.stopDrift) requestedDriftDirection = 0f;
                    }
                    motor.AIDriftDirection = extended ? requestedDriftDirection : 0f;
                    motor.VerificationSpinRequested = extended ? (bool?)frame.before.wishSpin : null;
                    Time.captureDeltaTime = frame.deltaTime;
                    int priorStep = motor.FixedStepCount;
                    yield return null;
                    if (motor.FixedStepCount != priorStep + 1 || motor.LastStepFrame != Time.frameCount) fixedStepMismatches++;
                    if (Mathf.Abs(motor.LastStepDeltaTime - frame.deltaTime) > 0.000001f) deltaTimeMismatches++;
                    bool inputMatched = motor.throttle == frame.input.accel
                        && motor.steering == frame.input.turn && motor.braking == frame.input.brake
                        && (!extended || motor.AIDriftActive && motor.AIDriftDirection == frame.input.drift
                            && frame.input.jump == motor.JumpedThisStep);
                    if (!inputMatched) inputMismatches++;
                    motor.PrepareRenderPose(false);
                    Vector3 position = motor.transform.position;
                    Quaternion rotation = motor.transform.rotation;
                    var sample = new ReplayFrame
                    {
                        index = frame.index, nativeUnityFrame = Time.frameCount, lastStepFrame = motor.LastStepFrame, stepDelta = motor.FixedStepCount - priorStep, fixedStep = motor.FixedStepCount,
                        originalDeltaTime = frame.deltaTime, nativeDeltaTime = motor.LastStepDeltaTime,
                        input = frame.input, inputMatched = inputMatched, grounded = motor.Grounded,
                        originalGrounded = frame.after.grounded,
                        groundedMatched = motor.Grounded == frame.after.grounded,
                        drifting = motor.IsDrifting, originalDrifting = frame.after.isDrifting,
                        driftDirection = motor.DriftDirection, originalDriftDirection = frame.after.driftDir,
                        driftMatched = !extended || motor.IsDrifting == frame.after.isDrifting
                            && Mathf.Abs(motor.DriftDirection - frame.after.driftDir) < 0.000001f,
                        verticalSpeed = motor.VerticalSpeed, originalVerticalSpeed = frame.after.currentGravity,
                        verticalSpeedError = extended ? Mathf.Abs(motor.VerticalSpeed - frame.after.currentGravity) : 0f,
                        originalControllerGrounded = frame.after.controllerGrounded,
                        nativeControllerGrounded = motor.GetComponent<CharacterController>().isGrounded,
                        controllerGroundedMatched = !extended || motor.GetComponent<CharacterController>().isGrounded == frame.after.controllerGrounded,
                        jumpRequested = extended && frame.input.jump, jumpedThisStep = motor.JumpedThisStep,
                        originalBeforePosition = frame.before.position,
                        originalAfterPosition = frame.after.position,
                        nativePosition = position, originalAfterRotation = frame.after.rotation,
                        nativeRotation = rotation, originalAfterVelocity = frame.after.velocity,
                        nativeDriveVelocity = motor.DriveVelocity,
                        nativeMovementVelocity = motor.DriveVelocity + Vector3.up * motor.CurrentVerticalSpeed,
                        nativePhysicsVelocity = motor.PhysicsVelocity,
                        originalAfterSpeed = frame.after.speed, nativeSpeed = motor.Speed,
                        originalAfterTurn = frame.after.turn, nativeTurn = motor.CurrentTurn,
                        positionError = Vector3.Distance(position, frame.after.position),
                        rotationErrorDegrees = Quaternion.Angle(rotation, frame.after.rotation),
                        rawSpeedError = Mathf.Abs(motor.Speed - frame.after.speed),
                        worldSpeedError = Mathf.Abs(motor.Speed - frame.after.speed),
                        turnError = Mathf.Abs(motor.CurrentTurn - frame.after.turn),
                        horizontalVelocityError = Vector2.Distance(
                            new Vector2(motor.DriveVelocity.x, motor.DriveVelocity.z),
                            new Vector2(frame.after.velocity.x, frame.after.velocity.z))
                    };
                    samples.Add(sample);
                    if (extended && screenshotIndices.Contains(frame.index))
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "frame-" + frame.index.ToString("D4") + ".png"));
                        requestedScreenshots.Add(frame.index);
                    }
                }
                Add("all-original-inputs-applied", inputMismatches == 0, "Input mismatches=" + inputMismatches);
                Add("one-native-update-per-frame", fixedStepMismatches == 0, "Fixed-step mismatches=" + fixedStepMismatches);
                Add("per-frame-delta-time", deltaTimeMismatches == 0, "Original/native dt mismatches=" + deltaTimeMismatches);
                Add("complete-forest-replay", samples.Count == reference.frames.Length,
                    "Captured=" + samples.Count + "; expected=" + reference.frames.Length);
                Add("runtime-errors", race.VisualErrorCount == 0, "Player error count=" + race.VisualErrorCount);
                if (extended)
                {
                    Add("original-direct-actions", samples.Count(s => s.jumpRequested) == 1
                        && samples.All(s => s.driftMatched),
                        "Jump requests=" + samples.Count(s => s.jumpRequested)
                        + "; drift mismatches=" + samples.Count(s => !s.driftMatched));
                    Add("same-frame-screenshot-requests", requestedScreenshots.Count == screenshotIndices.Count()
                        && requestedScreenshots.All(screenshotIndices.Contains),
                        "Requested=" + requestedScreenshots.Count + "; declared=" + screenshotIndices.Count());
                }

                Time.timeScale = 0f;
                Vector3 pausedPosition = motor.transform.position;
                Quaternion pausedRotation = motor.transform.rotation;
                float pausedSpeed = motor.Speed;
                int pausedStep = motor.FixedStepCount;
                yield return new WaitForSecondsRealtime(0.2f);
                Add("pause-freezes-motor", motor.FixedStepCount == pausedStep
                    && motor.transform.position == pausedPosition && motor.transform.rotation == pausedRotation
                    && motor.Speed == pausedSpeed, "Fixed steps during pause=" + (motor.FixedStepCount - pausedStep));
            }
            finally
            {
                motor.enabled = false;
                motor.VerificationSpinRequested = null;
                motor.AIDriftActive = false;
                race.AllowCameraVerificationUpdates = false;
                for (int i = 0; i < race.racers.Length; i++) { race.racers[i].enabled = racerEnabled[i]; race.racers[i].GetComponent<CharacterController>().enabled = colliderEnabled[i]; }
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
                Time.captureDeltaTime = originalCaptureDeltaTime;
                AudioListener.volume = originalAudioVolume;
            }

            var report = new Report
            {
                version = Application.version, platform = SystemInfo.operatingSystem,
                originalAssemblySha256 = reference.originalAssemblySha256,
                originalTraceSha256 = reference.sourceSha256,
                dtMode = "recorded-per-frame", nativeFixedDeltaTime = originalFixedDeltaTime,
                minOriginalDeltaTime = reference.frames.Min(f => f.deltaTime),
                maxOriginalDeltaTime = reference.frames.Max(f => f.deltaTime),
                expectedFrames = reference.frames.Length, capturedFrames = samples.Count,
                groundedFrames = samples.Count(s => s.grounded),
                groundedMismatches = samples.Count(s => !s.groundedMatched),
                inputMismatches = samples.Count(s => !s.inputMatched),
                fixedStepMismatches = fixedStepMismatches,
                deltaTimeMismatches = deltaTimeMismatches,
                driftMismatches = extended ? samples.Count(s => !s.driftMatched) : 0,
                controllerGroundedMismatches = extended ? samples.Count(s => !s.controllerGroundedMatched) : 0,
                maxVerticalSpeedError = extended ? samples.Max(s => s.verticalSpeedError) : 0f,
                screenshotRequests = requestedScreenshots.Count,
                capturedScreenshots = requestedScreenshots.Count(i => File.Exists(Path.Combine(directory, "frame-" + i.ToString("D4") + ".png"))),
                screenshotFrameIndices = requestedScreenshots.ToArray(),
                maxPositionError = samples.Max(s => s.positionError),
                endPositionError = samples.Last().positionError,
                maxRotationErrorDegrees = samples.Max(s => s.rotationErrorDegrees),
                maxRawSpeedError = samples.Max(s => s.rawSpeedError),
                maxWorldSpeedError = samples.Max(s => s.worldSpeedError),
                maxTurnError = samples.Max(s => s.turnError),
                maxHorizontalVelocityError = samples.Max(s => s.horizontalVelocityError),
                protocolPassed = checks.All(c => c.passed),
                trajectoryAligned = samples.Max(s => s.positionError) <= 0.25f
                    && samples.Max(s => s.rotationErrorDegrees) <= 1f
                    && samples.Max(s => s.rawSpeedError) <= 0.1f
                    && samples.All(s => s.groundedMatched && s.driftMatched && s.controllerGroundedMatched)
                    && (!extended || samples.Max(s => s.verticalSpeedError) <= 0.1f),
                equivalencePassed = samples.Max(s => s.positionError) <= 0.25f
                    && samples.Max(s => s.rotationErrorDegrees) <= 1f
                    && samples.Max(s => s.rawSpeedError) <= 0.1f
                    && samples.All(s => s.groundedMatched && s.driftMatched && s.controllerGroundedMatched)
                    && (!extended || samples.Max(s => s.verticalSpeedError) <= 0.1f),
                boundary = extended
                    ? "720 original Update samples; direct Drift, StopDrift and Jump requests precede original before snapshots. Replay requests Native drift and jump for that frame, preserving original measured dt. Screenshots are requested after the simulated Update and captured at frame end. Limits: position 0.25, rotation 1 degree, raw speed and vertical speed 0.1, exact grounded and drift state."
                    : "Protocol success confirms 690 ordered inputs and one real Native CharacterController Update per recorded original Update. Each replay step uses that original frame's measured deltaTime; captureDeltaTime is restored afterward. Trajectory alignment uses predeclared limits: max position 0.25 world units, max rotation 1 degree, max raw speed 0.1, and every grounded flag matched. Native speed now uses original world units; contact responses remain separate comparison targets.",
                checks = checks.ToArray(), frames = samples.ToArray()
            };
            File.WriteAllText(Path.Combine(directory, "original-trajectory-replay-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_ORIGINAL_TRAJECTORY_RESULT protocolPassed=" + report.protocolPassed
                + " equivalencePassed=" + report.equivalencePassed + " frames=" + report.capturedFrames);
            Application.Quit(report.protocolPassed && (!extended || report.equivalencePassed) ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            void WriteFailure()
            {
                var failed = new Report { version = Application.version, platform = SystemInfo.operatingSystem,
                    expectedFrames = extended ? 720 : 690, capturedFrames = 0, protocolPassed = false, equivalencePassed = false,
                    boundary = "Normalized original trajectory reference failed validation.", checks = checks.ToArray(), frames = new ReplayFrame[0] };
                File.WriteAllText(Path.Combine(directory, "original-trajectory-replay-report.json"), JsonUtility.ToJson(failed, true));
                Debug.Log("NATIVE_ORIGINAL_TRAJECTORY_RESULT protocolPassed=False equivalencePassed=False frames=0");
                Application.Quit(2);
            }
        }
    }
}
