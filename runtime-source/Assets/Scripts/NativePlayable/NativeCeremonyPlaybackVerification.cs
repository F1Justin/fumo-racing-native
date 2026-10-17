using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeCeremonyPlaybackVerification
    {
        [Serializable] public sealed class Sample
        {
            public float time, fadeAlpha, completeTextY;
            public Vector3 cameraPosition, cameraEuler, championLocalPosition;
            public string[] activeTargets;
        }
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public string source = "Original ceremony.playable, Recorded_3.anim, Recorded (1)_2.anim, Recorded (2)_0.anim; native Player direct curve sampling.";
            public List<string> checks = new List<string>();
            public List<Sample> samples = new List<Sample>();
        }
        static void Check(Report report, bool value, string description)
        {
            report.passed &= value;
            report.checks.Add((value ? "PASS " : "FAIL ") + description);
        }
        public static bool Run(NativeCeremonyView view, Camera camera, string directory)
        {
            var report = new Report();
            float previousTime = view.PlaybackTime;
            try
            {
                var cameraTrack = view.animatedTransforms.Single(track => track.target.name == "PlayerCamera");
                Check(report, cameraTrack.sourceTrackId == "114985305603660343" && cameraTrack.sourceClip.EndsWith("Recorded_3.anim", StringComparison.Ordinal), "Original camera track binding");
                Check(report, Vector3.Distance(cameraTrack.offsetPosition, new Vector3(48.767536f, 7.8431625f, 37.66919f)) < .0001f
                    && Quaternion.Angle(cameraTrack.offsetRotation, Quaternion.Euler(0, 60.000004f, 0)) < .01f, "Original infinite-clip offsets");
                Check(report, view.animatedTransforms.Length == 2 && view.activationWindows.Length == 6 && Mathf.Abs(view.finishSignalTime - 30.033333f) < .0001f, "All transform, activation and finish signal tracks recovered");
                Check(report, view.ceremonyMusic && view.originalCamera.fieldOfView == 50 && view.originalCamera.farClipPlane == 40000, "Original music and camera lens");
                float[] keyTimes = { 0, 1.5f, 3, 8.5f, 16.466667f };
                Vector3[] originalPositions = {
                    new Vector3(-58.21245f, 3.4568377f, 79.83448f),
                    new Vector3(-25.59593f, -2.8531628f, 42.528122f),
                    new Vector3(-9.240063f, -1.3731627f, 16.457367f),
                    new Vector3(5.3294125f, -.34316254f, 3.6924362f),
                    new Vector3(.089282036f, 2.2768373f, 11.856262f)
                };
                for (int i = 0; i < keyTimes.Length; i++)
                {
                    view.SamplePlayback(camera, keyTimes[i]);
                    Vector3 expected = cameraTrack.target.parent.TransformPoint(cameraTrack.offsetPosition + cameraTrack.offsetRotation * originalPositions[i]);
                    Check(report, Vector3.Distance(camera.transform.position, expected) < .001f, "Camera original position key " + keyTimes[i]);
                }
                view.SamplePlayback(camera, 0); Vector3 start = camera.transform.position;
                Quaternion firstRotation = camera.transform.rotation;
                view.SamplePlayback(camera, 8.5f);
                Check(report, Vector3.Distance(start, camera.transform.position) > 50 && Quaternion.Angle(firstRotation, camera.transform.rotation) > 20, "Camera translates and rotates through original sequence");
                view.SamplePlayback(camera, 13.366667f);
                Check(report, Mathf.Abs(view.championStatueAnchor.localPosition.y - 3.88f) < .001f && view.championStatueAnchor.gameObject.activeInHierarchy, "Champion reveal begins at original height");
                view.SamplePlayback(camera, 14);
                Check(report, view.championStatueAnchor.localPosition.sqrMagnitude < .000001f, "Champion descent lands at original 14 second key");
                foreach (var window in view.activationWindows)
                {
                    view.SamplePlayback(camera, Mathf.Max(0, window.start - .001f));
                    Check(report, !window.target.activeSelf, "Activation leading edge " + window.target.name);
                    view.SamplePlayback(camera, window.start + .001f);
                    Check(report, window.target.activeSelf, "Activation active window " + window.target.name);
                    view.SamplePlayback(camera, window.start + window.duration + .001f);
                    Check(report, !window.target.activeSelf, "Activation trailing edge " + window.target.name);
                }
                view.SamplePlayback(camera, 19);
                Check(report, view.FadeAlpha < .001f, "Original fade starts clear");
                view.SamplePlayback(camera, 25.016666f);
                Check(report, view.FadeAlpha > .999f && Mathf.Abs(view.CompleteTextY) < .001f, "Original fade and title exit curve complete");
                foreach (float time in new[] { 0, 4f, 8.5f, 13.7f, 14f, 17.5f, 19f, 23f, 30.033333f })
                {
                    view.SamplePlayback(camera, time);
                    report.samples.Add(new Sample { time = time, cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                        championLocalPosition = view.championStatueAnchor.localPosition, fadeAlpha = view.FadeAlpha, completeTextY = view.CompleteTextY,
                        activeTargets = view.activationWindows.Where(window => window.target.activeSelf).Select(window => window.target.name).ToArray() });
                }
                Check(report, view.Finished, "Original finish signal boundary");
                view.SamplePlayback(camera, 16.466667f); var finalPosition = camera.transform.position;
                view.SamplePlayback(camera, 30);
                Check(report, Vector3.Distance(finalPosition, camera.transform.position) < .0001f, "Camera holds last authored key after clip end");
            }
            catch (Exception error) { Check(report, false, error.ToString()); }
            finally { view.SeekPlayback(camera, previousTime); }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ceremony-dynamics-report.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("NATIVE_CEREMONY_DYNAMICS " + (report.passed ? "PASS" : "FAIL"));
            return report.passed;
        }
        public static IEnumerator CaptureReplay(NativeCeremonyView view, Camera camera, string directory)
        {
            Directory.CreateDirectory(directory);
            float previousTime = view.PlaybackTime;
            foreach (float time in new[] { 0, 4f, 8.5f, 14f, 17.5f, 19f, 23f })
            {
                view.SeekPlayback(camera, time);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "ceremony-" + time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
                yield return null;
            }
            view.SeekPlayback(camera, previousTime);
        }
    }
}
