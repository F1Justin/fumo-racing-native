using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        bool scaledTransformCheck;
        const string SourceMainWheel = "menu2/Canvas/MainMenu/WheelRoot/Wheel";
        const string SourceRaceWheel = "menu2/Canvas/RaceMenu/Wheel";
        const string SourceExtrasWheel = "menu2/Canvas/ExtrasMenu/WheelRoot/Wheel";
        const string SourceOptionsWheel = "menu2/Canvas/OptionsMenu/WheelRoot/Wheel";
        [Serializable] sealed class ScaledTransformFrame
        {
            public string phase, path;
            public int frame, instanceId, generation, forwardCopies;
            public float time, deltaTime, timeScale, sourceRotateSpeed;
            public bool activeSelf, activeInHierarchy, mainScaledTweenRunning;
            public Quaternion localRotation, worldRotation;
        }
        [Serializable] sealed class ScaledTransformReport
        {
            public string scope = "injected nonrendered production RectTransform graph; ordinary Update scaled clocks";
            public string writerModel;
            public bool passed, completed, visualAcceptance = false, originalCallbackOrderAccepted = false, fullRenderedUiGraphRecreationAccepted = false;
            public int runtimeCount, expectedRuntimeCount = 20, failures, engineErrors;
            public float wallSeconds;
            public string exception;
            public string[] checks;
            public ScaledTransformFrame[] frames;
        }
        readonly List<string> scaledTransformChecks = new List<string>();
        readonly List<ScaledTransformFrame> scaledTransformFrames = new List<ScaledTransformFrame>();
        void TransformCheck(bool ok, string reason) => scaledTransformChecks.Add((ok ? "PASS " : "FAIL ") + reason);
        RectTransform TransformSample(string path, string stage)
        {
            var t = hud.SourceMenu2Transform(path);
            if (!t) throw new InvalidOperationException("production transform missing " + path);
            scaledTransformFrames.Add(new ScaledTransformFrame { phase = stage, path = path, frame = Time.frameCount,
                instanceId = t.GetInstanceID(), generation = hud.Menu2SourceSceneGeneration,
                forwardCopies = hud.SourceMenu2ForwardCopyCount, time = Time.time, deltaTime = Time.deltaTime,
                mainScaledTweenRunning = hud.SourceMenu2ScaledMainTweenRunning, timeScale = Time.timeScale, sourceRotateSpeed = path == SourceMainWheel ? -90 : path == SourceRaceWheel ? -360 : -65, activeSelf = t.gameObject.activeSelf,
                activeInHierarchy = t.gameObject.activeInHierarchy, localRotation = t.localRotation, worldRotation = t.rotation });
            return t;
        }
        IEnumerator WaitSourceTransition()
        {
            while (hud.MenuTransitionActive) { TransformSample(SourceMainWheel, "transition"); TransformSample(SourceRaceWheel, "transition"); yield return null; }
            yield return null;
        }
        IEnumerator VerifyScaledTransformBody()
        {
            phase = Phase.Menu; settingsOpen = false; hud.SuppressPointerInput = true;
            hud.InitializeMenu2SourceTransformGraphForCheck();
            hud.SetMenu2SourceScenePresent(false); hud.SetMenuPage(NativeMenuPage.Main); hud.SetMenu2SourceScenePresent(true);
            InitializeMainQuitSource(); Time.timeScale = 1; yield return null; yield return null;
            var main = TransformSample(SourceMainWheel, "Main start");
            float t0 = Time.time; Quaternion q0 = main.localRotation;
            yield return new WaitForSecondsRealtime(.1f);
            main = TransformSample(SourceMainWheel, "Main90");
            TransformCheck(main.gameObject.activeInHierarchy && Quaternion.Angle(q0 * Quaternion.AngleAxis(-90 * (Time.time - t0), Vector3.forward), main.localRotation) < .25f, "Main actual transform rotates scaled90");
            Time.timeScale = 0; yield return null;
            var frozen = main.rotation; float scaled = Time.time;
            yield return new WaitForSecondsRealtime(.08f);
            TransformCheck(Quaternion.Angle(frozen, TransformSample(SourceMainWheel, "Main paused").rotation) < .001f && Time.time == scaled, "Main actual transform freezes at scale0");
            Time.timeScale = 1;
            foreach (var page in new[] { NativeMenuPage.Extras, NativeMenuPage.Options })
            {
                hud.SetMenuPage(page); yield return WaitSourceTransition();
                string path = page == NativeMenuPage.Extras ? SourceExtrasWheel : SourceOptionsWheel;
                var wheel = TransformSample(path, page + " start"); var inactiveMain = TransformSample(SourceMainWheel, page + " Main inactive");
                var inactiveQuat = inactiveMain.rotation; q0 = wheel.localRotation; t0 = Time.time;
                yield return new WaitForSecondsRealtime(.1f);
                wheel = TransformSample(path, page + "65");
                TransformCheck(wheel.gameObject.activeInHierarchy && Quaternion.Angle(q0 * Quaternion.AngleAxis(-65 * (Time.time - t0), Vector3.forward), wheel.localRotation) < .25f, page + " actual transform rotates scaled65");
                inactiveMain = TransformSample(SourceMainWheel, page + " retained Main");
                TransformCheck(!inactiveMain.gameObject.activeInHierarchy && Quaternion.Angle(inactiveQuat, inactiveMain.rotation) < .001f, page + " inactive Main retains actual rotation");
                Time.timeScale = 0; yield return null; frozen = wheel.rotation;
                yield return new WaitForSecondsRealtime(.06f);
                TransformCheck(Quaternion.Angle(frozen, TransformSample(path, page + " paused").rotation) < .001f, page + " actual transform freezes at scale0");
                Time.timeScale = 1; hud.SetMenuPage(NativeMenuPage.Main); yield return WaitSourceTransition();
            }
            hud.SetMenuPage(NativeMenuPage.Options); yield return WaitSourceTransition();
            foreach (var settings in new[] { SettingsPage.Video, SettingsPage.Audio, SettingsPage.Language })
            {
                settingsPage = settings; settingsOpen = true; yield return null;
                var options = TransformSample(SourceOptionsWheel, settings + " inactive"); frozen = options.rotation;
                yield return new WaitForSecondsRealtime(.06f);
                options = TransformSample(SourceOptionsWheel, settings + " retained");
                TransformCheck(!options.gameObject.activeInHierarchy && Quaternion.Angle(frozen, options.rotation) < .001f, settings + " inactive Options graph retains rotation");
                settingsOpen = false; yield return null;
            }
            hud.SetMenuPage(NativeMenuPage.Main); yield return WaitSourceTransition();
            int copies = hud.SourceMenu2ForwardCopyCount;
            hud.SetMenuPage(NativeMenuPage.RaceModes);
            while (!hud.SourceMenu2ScaledMainTweenRunning && hud.MenuTransitionActive) yield return null;
            Time.timeScale = 0; yield return null;
            float pausedStart = Time.time; frozen = TransformSample(SourceMainWheel, "forward tween paused").rotation;
            yield return new WaitForSecondsRealtime(.07f);
            TransformCheck(hud.SourceMenu2ScaledMainTweenRunning && Time.time == pausedStart && Quaternion.Angle(frozen, TransformSample(SourceMainWheel, "forward tween retained").rotation) < .001f && hud.SourceMenu2ForwardCopyCount == copies, "source tween scaled clock pause does not advance handoff");
            Time.timeScale = 1; yield return WaitSourceTransition();
            TransformCheck(hud.SourceMenu2ForwardCopyCount == copies + 1, "forward Main to Race actual graph copies world quaternion once");
            TransformCheck(Quaternion.Angle(hud.SourceMenu2LastCopyMainWorld, hud.SourceMenu2LastCopyRaceWorld) < .001f, "copy-time actual Main and Race world quaternions match");
            var race = TransformSample(SourceRaceWheel, "Race start"); q0 = race.localRotation; t0 = Time.time;
            frozen = TransformSample(SourceMainWheel, "Race Main inactive").rotation;
            yield return new WaitForSecondsRealtime(.1f);
            race = TransformSample(SourceRaceWheel, "Race360");
            TransformCheck(race.gameObject.activeInHierarchy && Quaternion.Angle(q0 * Quaternion.AngleAxis(-360 * (Time.time - t0), Vector3.forward), race.localRotation) < .25f, "Race actual transform rotates scaled360");
            TransformCheck(!TransformSample(SourceMainWheel, "Race Main retained").gameObject.activeInHierarchy && Quaternion.Angle(frozen, hud.SourceMenu2Transform(SourceMainWheel).rotation) < .001f, "Race inactive Main retains actual quaternion");
            Time.timeScale = 0; yield return null; frozen = race.rotation;
            yield return new WaitForSecondsRealtime(.06f);
            TransformCheck(Quaternion.Angle(frozen, TransformSample(SourceRaceWheel, "Race paused").rotation) < .001f, "Race actual transform freezes at scale0");
            Time.timeScale = 1;
            copies = hud.SourceMenu2ForwardCopyCount; hud.SetMenuPage(NativeMenuPage.Main); yield return WaitSourceTransition();
            TransformCheck(hud.SourceMenu2ForwardCopyCount == copies, "reverse Race to Main performs no world quaternion copy");
            int generation = hud.Menu2SourceSceneGeneration; int oldId = TransformSample(SourceMainWheel, "before scene departure").GetInstanceID();
            hud.SetMenu2SourceScenePresent(false); var before = hud.Menu2SourceWheelAngles();
            yield return new WaitForSecondsRealtime(.06f);
            TransformCheck(before.SequenceEqual(hud.Menu2SourceWheelAngles()), "departed menu2 source clocks retain stored rotation");
            hud.SetMenu2SourceScenePresent(true);
            main = TransformSample(SourceMainWheel, "fresh generation");
            bool allSeeds = true;
            foreach (string path in new[] { SourceMainWheel, SourceExtrasWheel, SourceOptionsWheel, SourceRaceWheel })
            {
                var seed = hud.authoredUiData.nodes.Single(n => n.scene + "/" + n.path == path).rotation;
                allSeeds &= Quaternion.Angle(TransformSample(path, "fresh authored seed").localRotation, seed) < .001f;
            }
            TransformCheck(hud.Menu2SourceSceneGeneration == generation + 1 && main.GetInstanceID() != oldId && allSeeds, "new menu2 graph generation uses new actual instance and all4 authored quaternion seeds");
        }
        IEnumerator CheckScaledTransformNonrendered()
        {
            float start = Time.realtimeSinceStartup, savedScale = Time.timeScale;
            string failure = ""; bool completed = false;
            var stack = new Stack<IEnumerator>(); stack.Push(VerifyScaledTransformBody());
            while (stack.Count > 0)
            {
                if (Time.realtimeSinceStartup - start > 40) { failure = "ordinary-frame transform verification timeout"; break; }
                bool moved = false; object value = null;
                try { moved = stack.Peek().MoveNext(); if (moved) value = stack.Peek().Current; }
                catch (Exception e) { failure = e.ToString(); break; }
                if (!moved)
                {
                    try { (stack.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception e) { failure = "cleanup: " + e; break; }
                    continue;
                }
                if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return value;
            }
            completed = stack.Count == 0 && failure.Length == 0;
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception e) { failure += "\ncleanup: " + e; }
            }
            Time.timeScale = savedScale;
            int failed = scaledTransformChecks.Count(c => c.StartsWith("FAIL ", StringComparison.Ordinal));
            var report = new ScaledTransformReport { writerModel = hud.SourceMenu2WriterModel, completed = completed, runtimeCount = scaledTransformChecks.Count,
                failures = failed, engineErrors = errorCount, checks = scaledTransformChecks.ToArray(), frames = scaledTransformFrames.ToArray(),
                exception = failure, wallSeconds = Time.realtimeSinceStartup - start };
            report.passed = completed && failure.Length == 0 && report.runtimeCount == report.expectedRuntimeCount && failed == 0 && errorCount == 0;
            Directory.CreateDirectory(reportDirectory);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(reportDirectory, "scaled-transform-nonrendered.json"), json);
            Debug.Log("SCALED_TRANSFORM_NONRENDERED_RESULT " + json);
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
