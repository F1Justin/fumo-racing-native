using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartFinishCameraVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int frame, triggerCount;
            public float sampleTime, normalizedTime, timeScale, positionError, angleError;
            public bool ended, firstPerson, lookingBehind, stateIsFinish;
            public Vector3 localPosition, referencePosition;
            public Quaternion localRotation, referenceRotation;
        }
        [Serializable] sealed class Capture
        {
            public string file;
            public int frame, width, height;
            public bool firstPerson, lookingBehind;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }
        [Serializable] sealed class Report
        {
            public string version, boundary, controller, clip;
            public bool passed;
            public Check[] checks;
            public Sample[] samples;
            public Capture[] captures;
        }

        public static IEnumerator Run(NativeRace race, string directory)
        {
            Directory.CreateDirectory(directory);
            var checks = new List<Check>();
            var samples = new List<Sample>();
            var captures = new List<Capture>();
            var rig = race.raceCamera.GetComponentInParent<NativeKartCamera>();
            var finish = race.FinishCamera;
            var animator = finish ? finish.Animator : null;
            var motorEnabled = race.racers.Select(r => r.enabled).ToArray();
            var wheels = race.racers.Select(r => r.GetComponent<NativeKartWheelVisual>()).Where(w => w).ToArray();
            var wheelEnabled = wheels.Select(w => w.enabled).ToArray();
            float savedTimeScale = Time.timeScale;
            bool savedAllow = race.AllowCameraVerificationUpdates;
            bool savedObstruction = rig && rig.enableObstruction;
            bool savedRigEnabled = rig && rig.enabled;
            GameObject referenceRoot = null;
            AnimationClip clip = null;
            try
            {
                Add("finish-component-binding", finish && animator && rig && finish.gameObject == rig.gameObject
                    && animator.runtimeAnimatorController, "Main race camera owns the original finish Animator controller");
                if (!finish || !animator || !rig || !animator.runtimeAnimatorController)
                    throw new InvalidOperationException("Finish camera bindings missing");
                clip = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == "cameraRaceEnd");
                Add("original-controller-clip", animator.runtimeAnimatorController.name == "PlayerCamera" && clip
                    && Mathf.Abs(clip.length - 2f) < .00001f && Mathf.Abs(clip.frameRate - 60f) < .00001f
                    && !clip.isLooping && clip.events.Length == 0
                    && animator.parameters.Any(p => p.name == "end" && p.type == AnimatorControllerParameterType.Trigger),
                    "Original controller has end Trigger and the 2-second 60Hz nonlooping cameraRaceEnd clip");
                if (!clip) throw new InvalidOperationException("Original finish clip missing");
                Add("original-animation-binding-path", rig.transform.Find(NativeKartFinishCamera.CameraBindingPath) == race.raceCamera.transform
                    && !rig.transform.Find("path_0x7373F227_mjTnvVL"),
                    "Real original camera path resolves; the unmatched original hash track retains no runtime target");
                Add("animator-original-options", !animator.applyRootMotion && animator.updateMode == AnimatorUpdateMode.Normal
                    && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
                    "No root motion, normal scaled clock, and original always-animate culling mode");
                foreach (var racer in race.racers) racer.enabled = false;
                foreach (var wheel in wheels) wheel.enabled = false;
                race.AllowCameraVerificationUpdates = false;
                race.SetVisualCapture("finish", true);
                Time.timeScale = 1f;
                rig.enabled = false;
                rig.enableObstruction = false;
                finish.ResetForRace(); rig.SnapToTarget();
                Add("finish-idle-disabled", !finish.Ended && !animator.enabled && finish.EndTriggerCount == 0,
                    "Finish Animator starts disabled and without a trigger");
                referenceRoot = new GameObject("Original finish clip reference");
                Transform referenceCamera = referenceRoot.transform;
                foreach (string name in NativeKartFinishCamera.CameraBindingPath.Split('/'))
                {
                    var child = new GameObject(name).transform;
                    child.SetParent(referenceCamera, false);
                    referenceCamera = child;
                }
                referenceRoot.AddComponent<Animator>().enabled = false;
                referenceCamera.localPosition = NativeKartCamera.CameraOffset;
                referenceCamera.localRotation = NativeKartCamera.CameraAngle;
                finish.OnEndedRace(); animator.Update(0f);
                Add("end-trigger-enters-state", finish.Ended && animator.enabled && finish.EndTriggerCount == 1
                    && animator.GetCurrentAnimatorStateInfo(0).IsName("cameraRaceEnd"),
                    "Original end trigger enters cameraRaceEnd from the empty state with zero transition");
                float previousTime = 0f;
                foreach (float time in new[] { 0f, 1f / 6f, .33333334f, .5f, .8333333f, 1f, 1.5f, 1.75f, 2f, 2.5f })
                {
                    animator.Update(time - previousTime);
                    previousTime = time;
                    clip.SampleAnimation(referenceRoot, Mathf.Min(time, 2f));
                    Record("manual-curve", time, referenceCamera.localPosition, referenceCamera.localRotation);
                }
                Add("animator-matches-original-clip", samples.Where(s => s.phase == "manual-curve")
                    .All(s => s.positionError < .003f && s.angleError < .08f && s.stateIsFinish),
                    "Actual original Animator agrees with independent clip.SampleAnimation at keys and intermediate times");
                float[] keyTimes = { 0f, .33333334f, .8333333f, 1.5f, 2f };
                Vector3[] keyPositions = { new Vector3(0f,2.4f,-6.4999976f), new Vector3(-2.5f,2.0198f,-3.9514f),
                    new Vector3(-3.16f,1.4614f,-.4f), new Vector3(-.9f,.79f,2.67f), new Vector3(-.9f,.79f,2.67f) };
                float[] keyYaw = { 0f, 34.384f, 85.99f, 155f, 155f };
                bool keysMatch = true;
                for (int i = 0; i < keyTimes.Length; i++)
                {
                    Sample sample = samples.First(s => s.phase == "manual-curve" && Mathf.Abs(s.sampleTime - keyTimes[i]) < .000001f);
                    keysMatch &= Near(sample.localPosition, keyPositions[i], .003f)
                        && Quaternion.Angle(sample.localRotation, Quaternion.Euler(6f,keyYaw[i],0f)) < .08f;
                }
                Add("original-keyframe-values", keysMatch, "Five original serialized positions and Euler keys match the running Animator");
                Add("finish-holds-last-pose", Near(race.raceCamera.transform.localPosition, new Vector3(-.9f,.79f,2.67f), .003f)
                    && Quaternion.Angle(race.raceCamera.transform.localRotation, Quaternion.Euler(6f,155f,0f)) < .08f,
                    "The nonlooping clip retains its final pose beyond two seconds");
                Vector3 finishedPosition = race.raceCamera.transform.localPosition;
                Quaternion finishedRotation = race.raceCamera.transform.localRotation;
                var input = rig.ReadTarget(true); input.grounded = true; input.speed = 145f; input.turn = 30f;
                input.drifting = true; input.driftDirection = 1f;
                rig.Step(input, 1f / 60f);
                Add("third-person-finish-preserves-animation", Near(race.raceCamera.transform.localPosition, finishedPosition)
                    && Quaternion.Angle(race.raceCamera.transform.localRotation, finishedRotation) < .02f
                    && rig.DesiredSpeedOffset == 0f && rig.DesiredDriftYaw == 0f,
                    "Ended third-person LateUpdate retains animated camera pose while speed and drift targets settle");
                animator.speed = 0f;
                yield return CaptureFrame("finish-third-person.png");

                finish.ResetForRace(); rig.SnapToTarget(); animator.speed = 1f;
                rig.ToggleView(); rig.SetLookBehind(true);
                finish.OnEndedRace(); animator.Update(0f); animator.Update(.5f);
                Quaternion animatedRotation = race.raceCamera.transform.localRotation;
                Vector3 oldSmooth = rig.SmoothedFirstPersonOffset;
                Vector3 anchorLocal = rig.target.transform.InverseTransformPoint(rig.FirstPersonAnchor.position);
                Vector3 expectedSmooth = oldSmooth + (anchorLocal - oldSmooth) * .2f;
                rig.Step(input, 1f / 60f);
                Vector3 expectedFirstPerson = NativeKartCamera.FirstPersonPosition + Vector3.up * rig.FirstPersonOffset + expectedSmooth;
                Record("first-person-finish", .5f, expectedFirstPerson, animatedRotation);
                Add("first-person-finish-position-overwrite", rig.FirstPerson && Near(race.raceCamera.transform.localPosition, expectedFirstPerson)
                    && rig.speedPivot.localPosition == Vector3.zero, "Ended first-person LateUpdate preserves the original anchored-position overwrite");
                Add("first-person-finish-keeps-animation-rotation", Quaternion.Angle(race.raceCamera.transform.localRotation, animatedRotation) < .02f
                    && Quaternion.Angle(animatedRotation, NativeKartCamera.CameraAngle) > 20f,
                    "First-person retains the finish animation rotation while overriding position");
                Add("finish-preserves-look-behind", rig.LookingBehind
                    && Quaternion.Angle(rig.lookBackPivot.localRotation, Quaternion.Euler(0f,180f,0f)) < .02f,
                    "OnEndedRace preserves the existing look-behind pivot");
                animator.speed = 0f;
                yield return CaptureFrame("finish-first-person.png");

                race.ResetRace(true);
                Add("restart-resets-finish", !finish.Ended && !animator.enabled && finish.EndTriggerCount == 0
                    && !rig.FirstPerson && !rig.LookingBehind && Near(race.raceCamera.transform.localPosition, NativeKartCamera.CameraOffset)
                    && Quaternion.Angle(race.raceCamera.transform.localRotation, NativeKartCamera.CameraAngle) < .02f,
                    "Real race restart disables and clears finish animation, view modes and camera pose");
                race.SetVisualCapture("finish", true);
                Time.timeScale = 1f; animator.speed = 1f;
                race.AllowCameraVerificationUpdates = true;
                finish.OnEndedRace();
                for (int frame = 0; frame < 12; frame++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Record("live-running", -1f, race.raceCamera.transform.localPosition, race.raceCamera.transform.localRotation);
                }
                var running = samples.Where(s => s.phase == "live-running").ToArray();
                Add("live-finish-clock-advances", running.Select(s => s.frame).Distinct().Count() == 12
                    && running.All(s => s.stateIsFinish && s.ended && s.triggerCount == 1)
                    && running.Last().normalizedTime > running.First().normalizedTime,
                    "Twelve actual rendered frames advance original Animator on the live race camera with one trigger");
                float pausedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Vector3 pausedPosition = race.raceCamera.transform.localPosition;
                Quaternion pausedRotation = race.raceCamera.transform.localRotation;
                Time.timeScale = 0f;
                for (int frame = 0; frame < 5; frame++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Record("live-paused", -1f, pausedPosition, pausedRotation);
                }
                Add("live-finish-pause", samples.Where(s => s.phase == "live-paused").All(s =>
                    s.timeScale == 0f && Mathf.Abs(s.normalizedTime - pausedTime) < .000001f
                    && s.positionError < .0001f && s.angleError < .02f),
                    "Five actual paused frames freeze Animator time and local camera pose");
                Time.timeScale = 1f;
                for (int frame = 0; frame < 5; frame++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Record("live-resumed", -1f, race.raceCamera.transform.localPosition, race.raceCamera.transform.localRotation);
                }
                Add("live-finish-resume", animator.GetCurrentAnimatorStateInfo(0).normalizedTime > pausedTime,
                    "Original normal-clock Animator resumes after timeScale returns to one");
                Add("second-finish-starts-once", finish.EndTriggerCount == 1 && finish.Ended,
                    "A new race can trigger the finish animation once after reset");
                Add("global-silence", AudioListener.volume == 0f, "Construction audio remains globally muted");
                Add("runtime-errors", race.VisualErrorCount == 0, "Runtime errors=" + race.VisualErrorCount);
            }
            finally
            {
                if (finish) { finish.ResetForRace(); if (animator) animator.speed = 1f; }
                if (rig) { rig.enabled = savedRigEnabled; rig.enableObstruction = savedObstruction; rig.SnapToTarget(); }
                for (int i = 0; i < race.racers.Length; i++) race.racers[i].enabled = motorEnabled[i];
                for (int i = 0; i < wheels.Length; i++) wheels[i].enabled = wheelEnabled[i];
                race.AllowCameraVerificationUpdates = savedAllow;
                Time.timeScale = savedTimeScale;
                if (referenceRoot) UnityEngine.Object.Destroy(referenceRoot);
            }
            var report = new Report { version = Application.version, passed = checks.All(c => c.passed),
                controller = animator ? animator.runtimeAnimatorController.name : "", clip = clip ? clip.name : "",
                checks = checks.ToArray(), samples = samples.ToArray(), captures = captures.ToArray(),
                boundary = "Original controller and clip on the real race camera. Manual Animator.Update key/intermediate samples are compared to serialized keys and a separate clip.SampleAnimation hierarchy; twenty-two real rendered frames cover animation, pause and resume. Finish event is invoked through the same component method as gameplay; complete-race delivery verifies the checkpoint event binding separately." };
            File.WriteAllText(Path.Combine(directory, "finish-camera-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_FINISH_CAMERA_RESULT passed=" + report.passed + " checks=" + checks.Count);
            Application.Quit(report.passed ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name=name, passed=passed, detail=detail });
            void Record(string phase, float time, Vector3 expectedPosition, Quaternion expectedRotation)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                samples.Add(new Sample { phase=phase, frame=Time.frameCount, sampleTime=time, normalizedTime=state.normalizedTime,
                    stateIsFinish=state.IsName("cameraRaceEnd"), timeScale=Time.timeScale, triggerCount=finish.EndTriggerCount,
                    ended=finish.Ended, firstPerson=rig.FirstPerson, lookingBehind=rig.LookingBehind,
                    localPosition=race.raceCamera.transform.localPosition, localRotation=race.raceCamera.transform.localRotation,
                    referencePosition=expectedPosition, referenceRotation=expectedRotation,
                    positionError=Vector3.Distance(race.raceCamera.transform.localPosition,expectedPosition),
                    angleError=Quaternion.Angle(race.raceCamera.transform.localRotation,expectedRotation) });
            }
            IEnumerator CaptureFrame(string file)
            {
                bool beforeCaptureAllow = race.AllowCameraVerificationUpdates;
                race.AllowCameraVerificationUpdates = true;
                yield return null;
                yield return new WaitForEndOfFrame();
                race.AllowCameraVerificationUpdates = beforeCaptureAllow;
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    File.WriteAllBytes(Path.Combine(directory,file),texture.EncodeToPNG());
                    captures.Add(new Capture { file=file, frame=Time.frameCount, width=texture.width, height=texture.height,
                        firstPerson=rig.FirstPerson, lookingBehind=rig.LookingBehind,
                        localPosition=race.raceCamera.transform.localPosition, localRotation=race.raceCamera.transform.localRotation });
                }
                finally { UnityEngine.Object.Destroy(texture); }
            }
        }
        static bool Near(Vector3 a, Vector3 b, float tolerance=.0001f) => Vector3.Distance(a,b) < tolerance;
    }
}
