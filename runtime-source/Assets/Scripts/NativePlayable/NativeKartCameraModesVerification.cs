using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartCameraModesRenderProbe : MonoBehaviour
    {
        public Renderer[] own, other;
        public bool[] expectedOther;
        public int observations;
        public bool allOwnHidden, otherUnchanged;
        void OnPreRender()
        {
            observations++;
            allOwnHidden = own != null && own.Length > 0 && own.All(r => r && !r.enabled);
            otherUnchanged = other != null && other.Select((r, i) => r && r.enabled == expectedOther[i]).All(x => x);
        }
    }

    public static class NativeKartCameraModesVerification
    {
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Sample
        {
            public string phase;
            public int frame, preCull, postRender, lastHidden, renderObservations;
            public bool firstPerson, lookingBehind, ownRenderersRestored, obstructed;
            public float deltaTime, offset, speedZ, desiredClipZ;
            public Vector3 anchorLocal, smoothOffset, cameraLocal, expectedLocal, cameraWorld, worldForward;
            public Quaternion lookRotation;
        }
        [Serializable] sealed class Capture
        {
            public string file;
            public int frame, width, height, preCull, postRender, lastHidden;
            public bool firstPerson, lookingBehind, renderersRestored;
        }
        [Serializable] sealed class Report
        {
            public string version, boundary;
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
            var hiding = race.raceCamera.GetComponent<NativeKartFirstPersonHiding>();
            var motor = race.racers[0];
            var motorEnabled = race.racers.Select(r => r.enabled).ToArray();
            var wheels = race.racers.Select(r => r.GetComponent<NativeKartWheelVisual>()).Where(w => w).ToArray();
            var wheelEnabled = wheels.Select(w => w.enabled).ToArray();
            float savedTimeScale = Time.timeScale;
            bool savedAllow = race.AllowCameraVerificationUpdates;
            bool savedObstruction = rig && rig.enableObstruction;
            bool savedRigEnabled = rig && rig.enabled;
            bool savedHidingEnabled = hiding && hiding.enabled;
            Transform anchor = rig ? rig.FirstPersonAnchor : null;
            Vector3 anchorPosition = anchor ? anchor.localPosition : Vector3.zero;
            Renderer[] own = hiding ? hiding.CharacterRenderers : new Renderer[0];
            Renderer[] all = race.racers.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
            bool[] rendererEnabled = all.Select(r => r.enabled).ToArray();
            var observer = race.raceCamera.gameObject.AddComponent<NativeKartCameraModesRenderProbe>();
            GameObject wall = null;
            try
            {
                Add("bound-camera-and-hiding", rig && hiding && rig.target == motor && anchor && own.Length > 0,
                    "The real race camera binds its target, character anchor and renderer hiding component");
                if (!rig || !hiding || !anchor || own.Length == 0) throw new InvalidOperationException("Camera mode bindings missing");
                foreach (var racer in race.racers) racer.enabled = false;
                foreach (var wheel in wheels) wheel.enabled = false;
                race.AllowCameraVerificationUpdates = false;
                race.SetVisualCapture("race", true);
                Time.timeScale = 1f;
                rig.enabled = false;
                rig.enableObstruction = false;
                hiding.enabled = true;
                rig.SnapToTarget();
                observer.own = own;
                observer.other = all.Except(own).ToArray();
                observer.expectedOther = observer.other.Select(r => r.enabled).ToArray();
                var ownBefore = own.Select(r => r.enabled).ToArray();
                Add("character-only-binding", own.All(r => r.transform.IsChildOf(anchor.parent))
                    && observer.other.Any(r => r.transform.IsChildOf(motor.transform))
                    && observer.other.Any(r => r.transform.IsChildOf(race.racers[1].transform)),
                    "Hiding is scoped to this car's character; its kart mesh and other racers remain outside the set");
                Add("snap-defaults", !rig.FirstPerson && !rig.LookingBehind
                    && Near(rig.SmoothedFirstPersonOffset, Vector3.zero)
                    && Near(race.raceCamera.transform.localPosition, NativeKartCamera.CameraOffset),
                    "Snap resets view mode, look-back and first-person smoothing");
                var input = rig.ReadTarget(false);
                input.grounded = true;
                input.speed = 0f;
                rig.Step(input, 1f / 60f);
                Record("third-person", NativeKartCamera.CameraOffset, 1f / 60f);
                yield return CaptureFrame("third-person.png");

                Vector3 forwardBefore = race.raceCamera.transform.forward;
                race.ApplyCameraInput(true, false);
                Add("look-behind-immediate", rig.LookingBehind
                    && Quaternion.Angle(rig.lookBackPivot.localRotation, Quaternion.Euler(0f, 180f, 0f)) < .02f
                    && Vector3.Angle(Vector3.ProjectOnPlane(forwardBefore, Vector3.up),
                        Vector3.ProjectOnPlane(race.raceCamera.transform.forward, Vector3.up)) > 179.9f,
                    "Shared race input applies 180-degree yaw immediately and reverses the actual world heading");
                Record("look-behind", race.raceCamera.transform.localPosition, 0f);
                yield return CaptureFrame("look-behind.png");
                race.ApplyCameraInput(false, false);
                Add("look-forward-immediate", !rig.LookingBehind
                    && Quaternion.Angle(rig.lookBackPivot.localRotation, Quaternion.identity) < .02f
                    && Vector3.Angle(forwardBefore, race.raceCamera.transform.forward) < .02f,
                    "Release restores the local and world forward orientation immediately");

                input.speed = 145f;
                rig.Step(input, 1f / 60f);
                Add("third-person-offset-before-entry", rig.speedPivot.localPosition.z < -.09f,
                    "The third-person speed pivot starts measurably rearward before first-person entry");
                race.ApplyCameraInput(false, true);
                Vector3 baseFirstPerson = new Vector3(0f, .83f, -.5f) + Vector3.up * rig.FirstPersonOffset;
                Add("first-person-entry-immediate", rig.FirstPerson && Near(race.raceCamera.transform.localPosition, baseFirstPerson),
                    "Entry immediately applies the original first-person position plus the cached Fumo pivot height");
                Add("first-person-anchor", anchor == motor.GetComponent<NativeKartWheelVisual>().Fumo.GetChild(0)
                    && Mathf.Abs(rig.FirstPersonOffset - motor.GetComponent<NativeKartWheelVisual>().Fumo.localPosition.y) < .00001f,
                    "Anchor is the actual first child of the Fumo pivot and its initial height is retained");
                Vector3 smoothBefore = rig.SmoothedFirstPersonOffset;
                Vector3 local = AnchorLocal();
                Vector3 expectedSmooth = smoothBefore + (local - smoothBefore) * .2f;
                input.speed = 145f;
                rig.Step(input, 1f / 60f);
                Add("first-person-full-vector-12dt", Near(rig.SmoothedFirstPersonOffset, expectedSmooth)
                    && Near(race.raceCamera.transform.localPosition, baseFirstPerson + expectedSmooth),
                    "Independent target-space anchor coordinates use full x/y/z and the 12/60 interpolation factor");
                Add("first-person-speed-pivot-zero", rig.speedPivot.localPosition == Vector3.zero,
                    "The first first-person step immediately clears the pre-existing nonzero speed pivot");
                Record("first-person-initial", baseFirstPerson + expectedSmooth, 1f / 60f);
                anchor.localPosition = anchorPosition + new Vector3(.15f, .25f, -.2f);
                local = AnchorLocal();
                smoothBefore = rig.SmoothedFirstPersonOffset;
                expectedSmooth = smoothBefore + (local - smoothBefore) * .2f;
                rig.Step(input, 1f / 60f);
                Add("first-person-anchor-motion", Near(rig.SmoothedFirstPersonOffset, expectedSmooth)
                    && Near(race.raceCamera.transform.localPosition, baseFirstPerson + expectedSmooth),
                    "A real anchor transform displacement in all three axes follows the original smoothing equation");
                Record("first-person-moved-anchor", baseFirstPerson + expectedSmooth, 1f / 60f);
                anchor.localPosition = anchorPosition;
                rig.Step(input, .25f);
                Record("first-person", baseFirstPerson + AnchorLocal(), .25f);
                yield return CaptureFrame("first-person.png");

                int pre = hiding.PreCullCount, post = hiding.PostRenderCount, observed = observer.observations;
                race.raceCamera.Render();
                Add("real-render-hides-character", hiding.PreCullCount > pre && hiding.PostRenderCount > post
                    && observer.observations > observed && observer.allOwnHidden && hiding.LastHiddenCount > 0,
                    "An actual Camera.Render reaches OnPreCull, observes hidden character meshes in OnPreRender, then reaches OnPostRender");
                Add("real-render-restores-character", own.Select((r, i) => r.enabled == ownBefore[i]).All(x => x),
                    "After rendering, each character renderer returns to its pre-render enabled state");
                Add("kart-and-other-racers-unchanged", observer.otherUnchanged
                    && observer.other.Select((r, i) => r.enabled == observer.expectedOther[i]).All(x => x),
                    "The local kart mesh and all other racers retain their renderer states during and after rendering");
                own[0].enabled = false;
                bool[] mixed = own.Select(r => r.enabled).ToArray();
                race.raceCamera.Render();
                Add("pre-disabled-renderer-preserved", observer.allOwnHidden
                    && own.Select((r, i) => r.enabled == mixed[i]).All(x => x),
                    "A renderer disabled before culling remains disabled after restoration");
                own[0].enabled = ownBefore[0];
                typeof(NativeKartFirstPersonHiding).GetMethod("OnPreCull",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hiding, null);
                hiding.enabled = false;
                Add("disable-restores-pending-hide", own.Select((r, i) => r.enabled == ownBefore[i]).All(x => x),
                    "A synthetic interrupted-cull lifecycle followed by real OnDisable restores cached renderer states");
                hiding.enabled = true;

                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "First-person clip bypass wall";
                wall.layer = 9;
                Vector3 castOrigin = rig.speedPivot.position + Vector3.up * 2.4f;
                Vector3 castDirection = -rig.transform.forward;
                wall.transform.SetPositionAndRotation(castOrigin + castDirection * 3f, Quaternion.LookRotation(castDirection));
                wall.transform.localScale = new Vector3(10f, 10f, .2f);
                rig.enableObstruction = true;
                Physics.SyncTransforms();
                rig.ProbeObstruction();
                Vector3 obstructedExpected = baseFirstPerson + Vector3.Lerp(rig.SmoothedFirstPersonOffset, AnchorLocal(), .2f);
                rig.Step(input, 1f / 60f);
                Add("first-person-bypasses-occlusion", rig.Obstructed && rig.ObstructionLayer == 9
                    && rig.DesiredClipZ > NativeKartCamera.CameraOffset.z
                    && Near(race.raceCamera.transform.localPosition, obstructedExpected),
                    "A real layer-9 SphereCast hit supplies a closer clip target while first-person keeps its anchor position");
                Record("first-person-obstructed", obstructedExpected, 1f / 60f);
                wall.SetActive(false);
                rig.enableObstruction = false;
                Physics.SyncTransforms();

                race.ApplyCameraInput(true, false);
                Add("first-person-look-behind", rig.FirstPerson && rig.LookingBehind
                    && Quaternion.Angle(rig.lookBackPivot.localRotation, Quaternion.Euler(0f, 180f, 0f)) < .02f,
                    "Look-back remains available in first-person mode");
                Record("first-person-look-behind", race.raceCamera.transform.localPosition, 0f);
                yield return CaptureFrame("first-person-look-behind.png");
                smoothBefore = rig.SmoothedFirstPersonOffset;
                rig.ToggleView();
                Add("third-person-return-immediate", !rig.FirstPerson && rig.LookingBehind
                    && Near(race.raceCamera.transform.localPosition, NativeKartCamera.CameraOffset)
                    && Near(rig.SmoothedFirstPersonOffset, smoothBefore),
                    "Leaving first-person immediately restores camera depth while preserving look-back and smoothing state");
                rig.Step(input, 1f / 60f);
                Add("third-person-speed-response-restored", Mathf.Abs(rig.speedPivot.localPosition.z + .096666667f) < .00001f,
                    "Returning to third-person restores the original 145-speed first-step rearward offset");
                for (int cycle = 0; cycle < 8; cycle++) rig.ToggleView();
                Add("toggle-cycle-preserves-state", !rig.FirstPerson && rig.LookingBehind
                    && Near(rig.SmoothedFirstPersonOffset, smoothBefore),
                    "Eight view toggles preserve the final mode, held look-back and first-person smoothing history");
                rig.ToggleView();
                race.SetVisualCapture("pause", true);
                Vector3 pausedPosition = race.raceCamera.transform.position;
                Vector3 pausedSmooth = rig.SmoothedFirstPersonOffset;
                race.ApplyCameraInput(false, true);
                rig.Step(input, 0f);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Add("pause-retains-mode", rig.FirstPerson && rig.LookingBehind
                    && Near(race.raceCamera.transform.position, pausedPosition)
                    && Near(rig.SmoothedFirstPersonOffset, pausedSmooth),
                    "Paused shared input ignores release/toggle and zero-delta simulation leaves camera state fixed");
                race.SetVisualCapture("race", true); Time.timeScale = 1f;
                race.ApplyCameraInput(false, false);
                Add("resume-releases-look-behind", rig.FirstPerson && !rig.LookingBehind,
                    "Resuming applies the current release without toggling view mode");
                race.ResetRace(true);
                Add("restart-resets-modes", !rig.FirstPerson && !rig.LookingBehind
                    && rig.SmoothedFirstPersonOffset == Vector3.zero
                    && Near(race.raceCamera.transform.localPosition, NativeKartCamera.CameraOffset),
                    "The real race restart resets view modes, smoothing and camera position");
                race.ApplyCameraInput(true, true);
                Add("countdown-accepts-camera-input", rig.FirstPerson && rig.LookingBehind,
                    "The shared input path remains active during countdown");
                race.ResetRace(false);
                race.ApplyCameraInput(true, true);
                Add("menu-resets-and-ignores-input", !rig.FirstPerson && !rig.LookingBehind
                    && rig.SmoothedFirstPersonOffset == Vector3.zero && !race.raceCamera.enabled,
                    "Returning to the real menu resets camera modes and ignores driving-camera input");
                Add("global-silence", AudioListener.volume == 0f, "Construction audio remains globally muted");
                Add("runtime-errors", race.VisualErrorCount == 0, "Runtime errors=" + race.VisualErrorCount);
            }
            finally
            {
                if (anchor) anchor.localPosition = anchorPosition;
                if (hiding) hiding.enabled = savedHidingEnabled;
                for (int i = 0; i < all.Length; i++) if (all[i]) all[i].enabled = rendererEnabled[i];
                if (rig) { rig.enabled = savedRigEnabled; rig.enableObstruction = savedObstruction; rig.SnapToTarget(); }
                if (wall) UnityEngine.Object.Destroy(wall);
                UnityEngine.Object.Destroy(observer);
                for (int i = 0; i < race.racers.Length; i++) race.racers[i].enabled = motorEnabled[i];
                for (int i = 0; i < wheels.Length; i++) wheels[i].enabled = wheelEnabled[i];
                race.AllowCameraVerificationUpdates = savedAllow;
                Time.timeScale = savedTimeScale;
            }
            var report = new Report { version = Application.version, passed = checks.All(c => c.passed),
                checks = checks.ToArray(), samples = samples.ToArray(), captures = captures.ToArray(),
                boundary = "Real race camera, original character anchor, actual Camera.Render callbacks and four rendered screenshots. Mode input calls the same race method as keyboard handling; physical keyboard injection is untested. Camera position cases use explicit input states and controlled anchor transforms. Interrupted-cull cleanup is a declared synthetic OnPreCull invocation followed by real component disabling." };
            File.WriteAllText(Path.Combine(directory, "camera-modes-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_CAMERA_MODES_RESULT passed=" + report.passed + " checks=" + checks.Count);
            Application.Quit(report.passed ? 0 : 2);

            void Add(string name, bool passed, string detail) => checks.Add(new Check { name = name, passed = passed, detail = detail });
            Vector3 AnchorLocal()
            {
                Vector3 delta = Quaternion.Inverse(motor.transform.rotation) * (anchor.position - motor.transform.position);
                Vector3 scale = motor.transform.lossyScale;
                return new Vector3(delta.x / scale.x, delta.y / scale.y, delta.z / scale.z);
            }
            bool OwnRestored() => own.All(r => r && r.enabled == rendererEnabled[Array.IndexOf(all, r)]);
            void Record(string phase, Vector3 expected, float dt) => samples.Add(new Sample {
                phase = phase, frame = Time.frameCount, deltaTime = dt, offset = rig.FirstPersonOffset,
                firstPerson = rig.FirstPerson, lookingBehind = rig.LookingBehind, anchorLocal = AnchorLocal(),
                smoothOffset = rig.SmoothedFirstPersonOffset, cameraLocal = race.raceCamera.transform.localPosition,
                expectedLocal = expected, cameraWorld = race.raceCamera.transform.position,
                worldForward = race.raceCamera.transform.forward, lookRotation = rig.lookBackPivot.localRotation,
                speedZ = rig.speedPivot.localPosition.z, obstructed = rig.Obstructed, desiredClipZ = rig.DesiredClipZ, preCull = hiding.PreCullCount, postRender = hiding.PostRenderCount,
                lastHidden = hiding.LastHiddenCount, renderObservations = observer.observations, ownRenderersRestored = OwnRestored() });
            IEnumerator CaptureFrame(string name)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    File.WriteAllBytes(Path.Combine(directory, name), texture.EncodeToPNG());
                    captures.Add(new Capture { file = name, frame = Time.frameCount, width = texture.width, height = texture.height,
                        preCull = hiding.PreCullCount, postRender = hiding.PostRenderCount, lastHidden = hiding.LastHiddenCount,
                        firstPerson = rig.FirstPerson, lookingBehind = rig.LookingBehind, renderersRestored = OwnRestored() });
                }
                finally { UnityEngine.Object.Destroy(texture); }
            }
        }
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .0001f;
    }
}
