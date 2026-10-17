using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartCameraVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string scenario;
            public int step;
            public float dt, speed, turn, pitch, driftYaw, speedOffset, fieldOfView;
            public Vector3 targetPosition, rootPosition, cameraPosition;
            public Quaternion rootRotation, verticalRotation, driftRotation, cameraRotation;
        }
        [Serializable] sealed class ClipSample
        {
            public string scenario;
            public bool hit;
            public int layer;
            public float distance, desiredZ, localCameraZ;
            public Vector3 origin, direction;
        }
        [Serializable] sealed class ScheduleSample
        {
            public string phase;
            public int frame, stepCount, probeCount;
            public float timeScale, deltaTime;
            public Vector3 cameraPosition;
        }
        [Serializable] sealed class Report
        {
            public string version, boundary;
            public bool passed;
            public Check[] checks;
            public Sample[] samples;
            public ClipSample[] obstructionSamples;
            public ScheduleSample[] scheduleSamples;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var clipSamples = new List<ClipSample>();
            var scheduleSamples = new List<ScheduleSample>();
            bool oldAllowCameraUpdates = race.AllowCameraVerificationUpdates;
            bool[] racerEnabled = race.racers.Select(r => r.enabled).ToArray();
            GameObject wall = null;
            float oldTimeScale = Time.timeScale;
            var cameraObject = new GameObject("Camera verification view");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false;
            view.fieldOfView = 60f;
            var rig = NativeKartCamera.Attach(view, race.racers[0]);
            rig.enabled = false;
            rig.enableObstruction = false;
            try
            {
                var bound = race.raceCamera.GetComponentInParent<NativeKartCamera>();
                Add("race-camera-bound", bound && bound.target == race.racers[0], "Race view uses the restored player camera rig");
                Add("original-clip-planes", Mathf.Abs(view.nearClipPlane - 0.01f) < 1e-7f
                    && view.farClipPlane == 40000f && Mathf.Abs(race.raceCamera.nearClipPlane - 0.01f) < 1e-7f
                    && race.raceCamera.farClipPlane == 40000f, "Live and fixture cameras retain original near 0.01 and far 40000 planes");
                Add("original-hierarchy", view.transform.parent == rig.speedPivot
                    && rig.speedPivot.parent == rig.lookBackPivot && rig.lookBackPivot.parent == rig.verticalPivot
                    && rig.verticalPivot.parent == rig.transform, "root / vertical / look-back / speed / camera");
                Add("camera-base-pose", Near(view.transform.localPosition, NativeKartCamera.CameraOffset)
                    && Quaternion.Angle(view.transform.localRotation, Quaternion.Euler(6f, 0f, 0f)) < 0.01f,
                    "Original camera local offset and six-degree pitch");
                var input = new NativeKartCameraInput { rotation = Quaternion.identity, grounded = true };
                rig.Snap(input);
                input.position = new Vector3(10f, 2f, 30f);
                input.rotation = Quaternion.Euler(0f, 90f, 0f);
                input.speed = 145f;
                rig.Step(input, 1f / 60f);
                Add("position-without-extra-lag", Near(rig.transform.position, input.position), "Original followSpeed 9999999 reaches target in one ordinary frame");
                Add("turn-original-nlerp", Quaternion.Angle(rig.transform.rotation,
                    new Quaternion(0f, 0.048021837f, 0f, 0.99884623f)) < 0.02f,
                    "Independent 90-degree fixture, original normalized quaternion Lerp at 4/60");
                Add("speed-first-step", Mathf.Abs(rig.speedPivot.localPosition.z + 0.096666667f) < 1e-6f,
                    "145 speed gives -1.933333 target and -0.096666667 first offset at 60 Hz");
                var before = rig.transform.position;
                var rotation = rig.transform.rotation;
                int steps = rig.StepCount;
                input.position = Vector3.one * 100f;
                rig.Step(input, 0f);
                Add("pause-zero-delta", rig.StepCount == steps && Near(before, rig.transform.position)
                    && Quaternion.Angle(rotation, rig.transform.rotation) < 0.01f, "Zero delta leaves camera state intact");
                input.position = Vector3.zero;
                input.rotation = Quaternion.Euler(20f, 40f, 0f);
                rig.Snap(input); rig.Step(input, 0.25f);
                Add("slope-pitch", Mathf.Abs(rig.DesiredPitch - 20f) < 0.01f
                    && Quaternion.Angle(rig.verticalPivot.localRotation, Quaternion.Euler(20f, 0f, 0f)) < 0.02f,
                    "Signed pitch from flattened forward and original vehicle right axis");
                input.grounded = false;
                input.velocity = new Vector3(20f, -30f, 0f);
                rig.Step(input, 0.25f);
                Add("air-falling-direction", Mathf.Abs(rig.DesiredPitch + 2f) < 1e-6f
                    && Quaternion.Angle(rig.transform.rotation, Quaternion.LookRotation(new Vector3(20f, -2f, 0f))) < 0.02f,
                    "Air horizontal velocity steers root; gravity pitch clamps at -2 degrees");
                input.velocity = new Vector3(20f, 30f, 0f);
                rig.Step(input, 0.25f);
                Add("air-rising-pitch", rig.DesiredPitch == 0f, "Positive vertical velocity clamps camera pitch to zero");
                input.velocity = new Vector3(0.5f, -30f, 0f);
                rig.Step(input, 0.25f);
                Add("air-speed-threshold", rig.DesiredPitch == 0f, "Velocity replacement requires horizontal speed greater than 0.5");
                input.grounded = true; input.rotation = Quaternion.identity;
                input.drifting = true; input.turn = -30f; input.driftDirection = -1f;
                rig.Step(input, 0.5f);
                Add("drift-direction", Mathf.Abs(rig.DesiredDriftYaw + 20f) < 1e-6f
                    && Quaternion.Angle(rig.speedPivot.localRotation, Quaternion.Euler(0f, -20f, 0f)) < 0.02f,
                    "abs(turn)/15*10*driftDirection supplies independent camera yaw");
                input.ended = true; rig.Step(input, 0.5f);
                Add("finish-settle", rig.DesiredDriftYaw == 0f && rig.DesiredSpeedOffset == 0f
                    && Mathf.Abs(rig.speedPivot.localPosition.z) < 1e-6f,
                    "Ended state settles speed and drift pivots");
                foreach (int rate in new[] { 30, 60, 120 })
                {
                    input = new NativeKartCameraInput { grounded = true, rotation = Quaternion.identity };
                    rig.Snap(input);
                    for (int frame = 0; frame < rate * 4; frame++)
                    {
                        float time = (frame + 1f) / rate;
                        input.position = new Vector3(time * 30f, 0f, time * 40f);
                        input.rotation = Quaternion.Euler(frame >= rate * 3 ? -15f : 0f, time * 25f, 0f);
                        input.speed = Mathf.Min(145f, time * 60f);
                        input.turn = frame >= rate && frame < rate * 3 ? 30f : 0f;
                        input.drifting = input.turn != 0f;
                        input.driftDirection = frame < rate * 2 ? -1f : 1f;
                        rig.Step(input, 1f / rate);
                        samples.Add(new Sample { scenario = rate + "hz", step = frame, dt = 1f / rate,
                            speed = input.speed, turn = input.turn, pitch = rig.DesiredPitch,
                            driftYaw = rig.DesiredDriftYaw, speedOffset = rig.speedPivot.localPosition.z,
                            fieldOfView = view.fieldOfView, targetPosition = input.position,
                            rootPosition = rig.transform.position, cameraPosition = view.transform.position,
                            rootRotation = rig.transform.rotation, verticalRotation = rig.verticalPivot.localRotation,
                            driftRotation = rig.speedPivot.localRotation, cameraRotation = view.transform.rotation });
                    }
                    Add("render-sequence-" + rate, samples.Where(s => s.scenario == rate + "hz").All(s =>
                        Near(s.targetPosition, s.rootPosition) && Finite(s.cameraPosition)
                        && s.fieldOfView == 60f && s.speedOffset <= 0f && s.speedOffset >= -1.933334f),
                        "Four seconds of acceleration, turning, counter-steer, drift release and slope at " + rate + " Hz");
                    yield return null;
                }
                rig.enableObstruction = true;
                Time.timeScale = 1f;
                input = new NativeKartCameraInput { position = new Vector3(0f, 5000f, 0f), rotation = Quaternion.identity, grounded = true };
                rig.Snap(input);
                Physics.SyncTransforms();
                rig.ProbeObstruction(); RecordClip("clear");
                Add("clip-clear", !rig.Obstructed && Mathf.Abs(rig.DesiredClipZ + 6.4999976f) < 1e-5f, "Unobstructed ray returns original camera depth");
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Camera obstruction layer 9 fixture";
                wall.layer = 9;
                wall.transform.position = new Vector3(0f, 5002.4f, -3f);
                wall.transform.localScale = new Vector3(10f, 10f, 0.2f);
                Physics.SyncTransforms();
                rig.ProbeObstruction(); RecordClip("wall");
                Add("clip-wall-layer-distance", rig.Obstructed && rig.ObstructionLayer == 9
                    && Mathf.Abs(rig.ObstructionDistance - 2.6f) < 0.005f
                    && Mathf.Abs(rig.DesiredClipZ + rig.ObstructionDistance) < 1e-6f,
                    "Real Physics sphere radius 0.3 hits the front face at center travel 2.6");
                rig.Step(input, 1f / 60f); RecordClip("wall-smoothed");
                Add("clip-original-smoothing", Mathf.Abs(view.transform.localPosition.z
                    - (-6.4999976f + (rig.DesiredClipZ + 6.4999976f) * 0.4f)) < 1e-5f,
                    "24/60 interpolation moves the camera toward the measured hit distance");
                wall.transform.position = new Vector3(0f, 5002.4f, -5f);
                Physics.SyncTransforms(); rig.ProbeObstruction(); RecordClip("wall-moved");
                Add("clip-moving-wall", rig.Obstructed && Mathf.Abs(rig.ObstructionDistance - 4.6f) < 0.005f,
                    "Moving collider changes actual hit distance after SyncTransforms");
                wall.layer = 0;
                Physics.SyncTransforms(); rig.ProbeObstruction(); RecordClip("wrong-layer");
                Add("clip-mask", !rig.Obstructed && rig.ObstructionLayer == -1, "Same collider on layer 0 is excluded by mask 512");
                float clippedZ = view.transform.localPosition.z;
                rig.Step(input, 1f / 60f); RecordClip("clear-recovery");
                Add("clip-release-smoothing", Mathf.Abs(view.transform.localPosition.z
                    - (clippedZ + (NativeKartCamera.CameraOffset.z - clippedZ) * 0.4f)) < 1e-5f,
                    "Clear ray restores default depth with the original interpolation factor");
                wall.layer = 9;
                input.rotation = Quaternion.Euler(0f, 90f, 0f);
                rig.Snap(input);
                wall.transform.position = new Vector3(-3f, 5002.4f, 0f);
                wall.transform.localScale = new Vector3(0.2f, 10f, 10f);
                Physics.SyncTransforms(); rig.ProbeObstruction(); RecordClip("turned-wall");
                Add("clip-turn-direction", rig.Obstructed && Near(rig.ObstructionDirection, Vector3.left)
                    && Mathf.Abs(rig.ObstructionDistance - 2.6f) < 0.005f, "Probe follows the root heading after a 90-degree turn");
                input.speed = 150f;
                rig.Step(input, 1f / 3f);
                Physics.SyncTransforms(); rig.ProbeObstruction(); RecordClip("speed-pivot");
                Add("clip-speed-pivot-origin", rig.Obstructed && Mathf.Abs(rig.ObstructionDistance - 0.6f) < 0.005f
                    && Near(rig.ObstructionOrigin, rig.speedPivot.position + Vector3.up * 2.4f),
                    "Speed pivot rearward travel moves the sphere origin by two world units");
                rig.transform.rotation = Quaternion.Euler(15f, 40f, 0f);
                rig.verticalPivot.localRotation = Quaternion.Euler(20f, 0f, 0f);
                rig.speedPivot.localRotation = Quaternion.Euler(0f, -25f, 0f);
                Vector3 tiltedOrigin = rig.speedPivot.position + Vector3.up * 2.4f;
                Vector3 tiltedDirection = -rig.transform.forward;
                wall.transform.SetPositionAndRotation(tiltedOrigin + tiltedDirection * 3f, Quaternion.LookRotation(tiltedDirection));
                wall.transform.localScale = new Vector3(10f, 10f, 0.2f);
                Physics.SyncTransforms(); rig.ProbeObstruction(); RecordClip("tilted-drift-wall");
                Add("clip-tilted-drift-direction", rig.Obstructed && rig.ObstructionLayer == 9
                    && Mathf.Abs(rig.ObstructionDistance - 2.6f) < 0.005f
                    && Near(rig.ObstructionOrigin, tiltedOrigin) && Near(rig.ObstructionDirection, tiltedDirection)
                    && Vector3.Angle(rig.ObstructionDirection, -rig.speedPivot.forward) > 20f,
                    "Tilted root and nonzero drift pivot keep the cast on root forward with a world-up origin offset");
                int probes = rig.ObstructionProbeCount;
                float depth = view.transform.localPosition.z;
                Time.timeScale = 0f;
                wall.transform.position += Vector3.left * 2f;
                Physics.SyncTransforms(); rig.ProbeObstruction(); rig.Step(input, 0f); RecordClip("paused");
                Add("clip-pause", rig.ObstructionProbeCount == probes && view.transform.localPosition.z == depth,
                    "Paused fixed probe and zero-delta render step retain state");
                Time.timeScale = 1f;
                rig.Snap(input); RecordClip("reset");
                Add("clip-reset", !rig.Obstructed && rig.ObstructionLayer == -1 && rig.ObstructionProbeCount == 0
                    && Near(view.transform.localPosition, NativeKartCamera.CameraOffset),
                    "Reset clears obstruction history and restores base camera position");
                if (bound)
                {
                    foreach (var racer in race.racers) racer.enabled = false;
                    race.SetVisualCapture("race", true);
                    race.AllowCameraVerificationUpdates = true;
                    Time.timeScale = 1f;
                    int initialSteps = bound.StepCount, initialProbes = bound.ObstructionProbeCount;
                    for (int frame = 0; frame < 10; frame++)
                    {
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForEndOfFrame();
                        RecordSchedule("running", bound);
                    }
                    Add("live-fixed-late-scheduling", bound.StepCount > initialSteps && bound.ObstructionProbeCount > initialProbes
                        && scheduleSamples.Where(s => s.phase == "running").Select(s => s.frame).Distinct().Count() == 10,
                        "Real main camera FixedUpdate and NativeRace LateUpdate both advance across ten rendered frames");
                    Time.timeScale = 0f;
                    int pausedSteps = bound.StepCount, pausedProbes = bound.ObstructionProbeCount;
                    Vector3 pausedPosition = race.raceCamera.transform.position;
                    for (int frame = 0; frame < 5; frame++)
                    {
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        RecordSchedule("paused", bound);
                    }
                    Add("live-pause-freezes-camera", bound.StepCount == pausedSteps && bound.ObstructionProbeCount == pausedProbes
                        && Near(pausedPosition, race.raceCamera.transform.position)
                        && scheduleSamples.Where(s => s.phase == "paused").All(s => s.deltaTime == 0f && s.timeScale == 0f),
                        "Five real paused frames freeze main camera probes, render steps and world position");
                    Time.timeScale = 1f;
                    for (int frame = 0; frame < 5; frame++)
                    {
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForEndOfFrame();
                        RecordSchedule("resumed", bound);
                    }
                    Add("live-resume-camera", bound.StepCount > pausedSteps && bound.ObstructionProbeCount > pausedProbes,
                        "Both scheduled camera phases resume after pause");
                    race.AllowCameraVerificationUpdates = oldAllowCameraUpdates;
                }
                else
                {
                    Add("live-fixed-late-scheduling", false, "Main camera rig is missing");
                    Add("live-pause-freezes-camera", false, "Main camera rig is missing");
                    Add("live-resume-camera", false, "Main camera rig is missing");
                }
                Add("fov-constant", view.fieldOfView == 60f, "Speed response moves the pivot while retaining configured FOV");
                Add("global-silence", AudioListener.volume == 0f, "Construction audio volume=" + AudioListener.volume);
            }
            finally
            {
                Time.timeScale = oldTimeScale;
                race.AllowCameraVerificationUpdates = oldAllowCameraUpdates;
                for (int i = 0; i < race.racers.Length; i++) race.racers[i].enabled = racerEnabled[i];
                if (wall) UnityEngine.Object.Destroy(wall);
                UnityEngine.Object.Destroy(rig.gameObject);
            }
            var report = new Report { version = Application.version, passed = checks.All(c => c.passed),
                boundary = "Real Unity camera hierarchy and synthetic input states; original formulas are grounded in archived machine code and prefab. Includes real Physics SphereCast fixtures and main-camera FixedUpdate/LateUpdate/pause frame observations. Full original-game camera trace remains a separate check.",
                checks = checks.ToArray(), samples = samples.ToArray(), obstructionSamples = clipSamples.ToArray(), scheduleSamples = scheduleSamples.ToArray() };
            File.WriteAllText(Path.Combine(directory, "camera-verification.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("Camera verification " + (report.passed ? "PASS" : "FAIL") + ": " + checks.Count + " checks");
            Application.Quit(report.passed ? 0 : 1);

            void RecordSchedule(string phase, NativeKartCamera main) => scheduleSamples.Add(new ScheduleSample {
                phase = phase, frame = Time.frameCount, stepCount = main.StepCount, probeCount = main.ObstructionProbeCount,
                timeScale = Time.timeScale, deltaTime = Time.deltaTime, cameraPosition = main.view.transform.position });
            void RecordClip(string scenario) => clipSamples.Add(new ClipSample { scenario = scenario,
                hit = rig.Obstructed, layer = rig.ObstructionLayer, distance = rig.ObstructionDistance,
                desiredZ = rig.DesiredClipZ, localCameraZ = view.transform.localPosition.z,
                origin = rig.ObstructionOrigin, direction = rig.ObstructionDirection });
            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
        }
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-5f;
        static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
            || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }
}
