using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
#if ENABLE_INPUT_SYSTEM
        [Serializable] sealed class DirectorInputReceipt
        {
            public bool passed, physicalInputAccepted;
            public string scope = "Rendered Player, authored spectator InputActions, virtual Keyboard/Gamepad and authority-state fixtures; production focus adapter. Story decisions tested separately.", error;
            public List<string> checks = new List<string>();
        }
        public IEnumerator CheckSpectatorDirector(string directory)
        {
            Directory.CreateDirectory(directory);
            var receipt = new DirectorInputReceipt(); var stack = new Stack<IEnumerator>();
            stack.Push(CheckSpectatorDirectorCore(receipt, directory));
            while (stack.Count > 0)
            {
                bool next; object value = null;
                try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                catch (Exception error) { receipt.error = error.ToString(); break; }
                if (!next) { stack.Pop(); if (stack.Count == 0) receipt.passed = true; continue; }
                if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return value;
            }
            File.WriteAllText(Path.Combine(directory, "spectator-input.json"), JsonUtility.ToJson(receipt, true));
            Application.Quit(receipt.passed ? 0 : 1);
        }
        void DirectorRequire(DirectorInputReceipt report, bool condition, string name)
        {
            report.checks.Add((condition ? "PASS " : "FAIL ") + name);
            if (!condition) throw new InvalidOperationException(name);
        }
        IEnumerator DirectorKey(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        IEnumerator DirectorButton(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button)); yield return null; yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState()); yield return null; yield return null;
        }
        IEnumerator CheckSpectatorDirectorCore(DirectorInputReceipt report, string directory)
        {
            Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            onlineUiFixture = true; Open = true; playerId = "director-fixture"; IsHost = false;
            requestedSpectator = true; LocalSeat = 8; Round = 1; Config.racers = 4; Config.laps = 1;
            Config.humanSeats = new[] { 0, 1 };
            Peers = new[] { new NativeNetPeer { id = "one", seat = 0 }, new NativeNetPeer { id = "two", seat = 1 },
                new NativeNetPeer { id = playerId, seat = 8, spectator = true } };
            Race.NetworkOpenMenu(keyboard); Race.NetworkPrepare(Config);
            InRace = true; Loading = false; paused = false; automaticSpectator = true;
            onlinePage = null; SetOnlineChoice(OnlineChoice.None); ResetPersonalResults();
            Latest = Race.NetworkSnapshot(); Latest.elapsed = 1;
            for (int i = 0; i < Latest.karts.Length; i++)
            {
                Latest.karts[i].place = i + 1; Latest.karts[i].finished = Latest.karts[i].retired = false;
                Latest.karts[i].position = new Vector3((3 - i) * 60, 0, 0);
            }
            yield return null; yield return null;
            DirectorRequire(report, SpectatorControlsActive && observedRacer == 0 && Race.NetworkCameraSeat == 0, "automatic decision reaches actual follow camera");
            yield return DirectorKey(keyboard, Key.E);
            DirectorRequire(report, automaticSpectator && observedRacer == 1 && Race.NetworkCameraSeat == 1 && ManualFocusRemaining > 7,
                "keyboard Next keeps automatic enabled and holds actual next racer for eight seconds");
            yield return DirectorKey(keyboard, Key.E);
            DirectorRequire(report, observedRacer == 2 && ManualFocusRemaining > 7, "repeated Next advances and renews focus hold");
            yield return DirectorKey(keyboard, Key.V);
            var rig = Race.raceCamera.GetComponentInParent<NativeKartCamera>();
            DirectorRequire(report, spectatorCameraMode == 1 && rig.LookingBehind && !rig.FirstPerson && observedRacer == 2, "View changes actual camera orientation while focus is held");
            yield return CaptureOnline(Path.Combine(directory, "manual-en.png"));
            yield return DirectorKey(keyboard, Key.R);
            DirectorRequire(report, automaticSpectator && ManualFocusRemaining == 0 && observedRacer == 0, "Automatic during hold immediately resumes director without disabling automatic");
            yield return DirectorKey(keyboard, Key.R);
            yield return DirectorKey(keyboard, Key.Q);
            DirectorRequire(report, !automaticSpectator && observedRacer == 3 && ManualFocusRemaining == 0, "automatic Off uses persistent manual selection and Previous wraps");
            var caption = OnlineHud.OnlinePageView.GetComponentsInChildren<NativeOnlineElement>(true).Single(e => e.key == "SpectatorControls.Label4");
            DirectorRequire(report, caption.GetComponentsInChildren<TMPro.TMP_Text>(true).All(t => string.IsNullOrEmpty(t.text)), "automatic Off clears the previous hold caption");
            yield return new WaitForSecondsRealtime(.3f);
            DirectorRequire(report, observedRacer == 3 && !PersonalResultsVisible, "manual mode does not get overwritten or open results");
            yield return DirectorButton(gamepad, GamepadButton.West);
            DirectorRequire(report, automaticSpectator && observedRacer == 0, "gamepad X enables director");
            yield return DirectorButton(gamepad, GamepadButton.South);
            DirectorRequire(report, automaticSpectator && observedRacer == 3 && ManualFocusRemaining > 7, "gamepad A selects previous while automatic remains enabled");
            yield return DirectorButton(gamepad, GamepadButton.East);
            DirectorRequire(report, observedRacer == 0 && !paused && playerId == "director-fixture", "gamepad B selects next without triggering cancel or leaving spectator");
            yield return DirectorButton(gamepad, GamepadButton.North);
            DirectorRequire(report, spectatorCameraMode == 2 && rig.FirstPerson && !rig.LookingBehind, "gamepad Y changes actual first-person camera");
            yield return DirectorButton(gamepad, GamepadButton.South);
            yield return new WaitForSecondsRealtime(8.15f);
            DirectorRequire(report, automaticSpectator && ManualFocusRemaining == 0 && observedRacer == 0 && Race.NetworkCameraSeat == 0,
                "eight-second manual hold expires and actual camera returns to automatic subject");
            NativeUserSettings.TrySetInt("lang_game", 3);
            yield return DirectorKey(keyboard, Key.E);
            yield return CaptureOnline(Path.Combine(directory, "manual-cn.png"));
            DirectorRequire(report, NativeLocalization.TranslateText("Resume Auto") == "恢复自动", "authored Chinese catalog includes manual resume caption");
            Round++; yield return null; yield return null;
            DirectorRequire(report, ManualFocusRemaining == 0 && automaticSpectator, "new round clears manual hold and keeps automatic preference");
            Latest.karts[1].retired = true;
            yield return DirectorKey(keyboard, Key.E);
            DirectorRequire(report, observedRacer == 2 && automaticSpectator, "manual Next skips retired racers while automatic remains enabled");
            if (Environment.GetCommandLineArgs().Contains("--check-broadcast-camera"))
                yield return CheckBroadcastCameraCore(report, keyboard, directory);
            yield return DirectorKey(keyboard, Key.Escape);
            DirectorRequire(report, paused && Time.timeScale == 1, "spectator Pause opens local controls without pausing authority world");
            yield return new WaitForSecondsRealtime(.3f);
        }

        IEnumerator CheckBroadcastCameraCore(DirectorInputReceipt report, Keyboard keyboard, string directory)
        {
            report.scope += " Camera planning also uses controlled scene poses and layer-9 occluders; real moving-race coverage is verified separately.";
            DirectorRequire(report, new[] { "forest", "mountains", "purplefield", "wintermistylake" }.All(id =>
                NativeContent.Load<NativeBroadcastCameraProfile>("NativeOnline/Broadcast/" + id)?.track == id), "four authored camera profiles load with correct track identity");
            float originalFov = Race.raceCamera.fieldOfView, originalNear = Race.raceCamera.nearClipPlane;
            Vector3 origin = Race.racers[0].transform.position + Vector3.up * 200;
            for (int i = 0; i < Race.racers.Length; i++)
                Race.racers[i].ApplyNetworkPose(origin + Vector3.right * (i * 6), Quaternion.identity, Vector3.zero, 0, 0, true);
            yield return DirectorKey(keyboard, Key.V); yield return DirectorKey(keyboard, Key.V);
            DirectorRequire(report, spectatorCameraMode == 4 && Race.BroadcastCameraActive && Race.BroadcastShot?.focus == observedRacer,
                "fifth View mode reaches the actual camera through the production planner");
            DirectorRequire(report, Race.BroadcastShot.other == -1 && ManualFocusRemaining > 0,
                "manual focus uses a single-subject composition while automatic stays enabled");
            for (int i = 0; i < Latest.karts.Length; i++)
            {
                Vector3 position = origin + (i == 1 ? Vector3.forward * 6 : Vector3.right * (i * 40));
                Race.racers[i].ApplyNetworkPose(position, Quaternion.identity, Vector3.zero, 0, 0, true);
                Latest.karts[i].position = position; Latest.karts[i].retired = false;
                Latest.karts[i].place = i == 0 ? 2 : i == 1 ? 1 : i + 1;
            }
            Latest.elapsed = 2; spectatorDirector.Reset(); nextSpectatorDecision = 0;
            yield return DirectorKey(keyboard, Key.R); yield return new WaitForSecondsRealtime(5.5f);
            DirectorRequire(report, Race.BroadcastShot != null && Race.BroadcastShot.focus == 0 && Race.BroadcastShot.other == 1,
                "battle story selects a two-subject shot after resuming automatic");
            DirectorRequire(report, new[] { 0, 1 }.All(id =>
            {
                var p = Race.raceCamera.WorldToViewportPoint(Race.racers[id].transform.position + Vector3.up * 1.3f);
                return p.z > 0 && p.x > .1f && p.x < .9f && p.y > .1f && p.y < .9f;
            }), "both battle participants are inside the actual camera viewport");
            yield return CaptureOnline(Path.Combine(directory, "director-pair.png"));
            yield return DirectorKey(keyboard, Key.E);
            DirectorRequire(report, Race.BroadcastShot != null && Race.BroadcastShot.other == -1 && Race.BroadcastShot.focus == observedRacer
                && automaticSpectator && ManualFocusRemaining > 7, "Next keeps manual subject in Director View and suppresses the unrelated battle partner");
            yield return DirectorKey(keyboard, Key.R); yield return new WaitForSecondsRealtime(5.5f);
            string before = Race.BroadcastShot.key;
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Broadcast verification occluder";
            blocker.layer = 9; blocker.transform.position = Race.raceCamera.transform.position; blocker.transform.localScale = Vector3.one * 2;
            Physics.SyncTransforms(); yield return null; yield return null;
            DirectorRequire(report, Race.BroadcastShot != null && Race.BroadcastShot.key != before && !Race.BroadcastCameraFallback,
                "new camera obstruction immediately selects an alternative before minimum dwell");
            blocker.transform.position = origin; blocker.transform.localScale = Vector3.one * 1000;
            Physics.SyncTransforms(); yield return null; yield return null;
            DirectorRequire(report, Race.BroadcastCameraFallback && Race.BroadcastShot == null && Mathf.Approximately(Race.raceCamera.fieldOfView, originalFov),
                "all invalid candidates fall back to the original follow camera and lens");
            for (int i = 0; i < Race.racers.Length; i++)
                Race.racers[i].ApplyNetworkPose(Race.racers[i].transform.position, Quaternion.identity, Vector3.forward * 30, 30, 0, true);
            yield return DirectorKey(keyboard, Key.E);
            var fallbackRig = Race.raceCamera.GetComponentInParent<NativeKartCamera>();
            DirectorRequire(report, Race.BroadcastCameraFallback && Mathf.Abs(fallbackRig.DesiredSpeedOffset) > .1f
                && Mathf.Abs(fallbackRig.speedPivot.localPosition.z - fallbackRig.DesiredSpeedOffset) < .001f,
                "subject change during fallback hard-cuts with fully settled legacy speed pivot");
            yield return DirectorKey(keyboard, Key.R);
            blocker.SetActive(false); Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(.2f);
            DirectorRequire(report, !Race.BroadcastCameraFallback && Race.BroadcastShot != null, "camera planner recovers when occlusion clears");
            UnityEngine.Object.Destroy(blocker);
            var standalone = new NativeBroadcastCameraRig(); standalone.Begin(Race.raceCamera, Race.trackId); standalone.ManualKind = NativeBroadcastShotKind.Trackside;
            var route = new[] { origin, origin + Vector3.forward * 80, origin + Vector3.right * 80 };
            var finish = new NativeBroadcastStory { kind = NativeBroadcastStoryKind.Finish, focus = 0 };
            standalone.Step(Race.racers, route, finish, 0, Time.unscaledTime, .016f);
            DirectorRequire(report, standalone.Current?.kind == NativeBroadcastShotKind.Trackside, "manual trackside mode selects an authored station");
            Vector3 station = Race.raceCamera.transform.position;
            Race.racers[0].ApplyNetworkPose(origin + Vector3.forward * 8, Quaternion.identity, Vector3.zero, 0, 0, true);
            standalone.Step(Race.racers, route, finish, 0, Time.unscaledTime + .2f, .016f);
            DirectorRequire(report, Vector3.Distance(station, Race.raceCamera.transform.position) < .01f, "trackside station stays fixed while its pan follows the subject");
            Race.raceCamera.transform.position += Vector3.right * 4;
            standalone.Step(Race.racers, route, finish, 0, Time.unscaledTime + .3f, .016f);
            DirectorRequire(report, Vector3.Distance(station, Race.raceCamera.transform.position) < .001f,
                "same-shot pose is applied directly without camera interpolation or catch-up motion");
            int histories = standalone.HistoryResetCount; standalone.ManualKind = NativeBroadcastShotKind.Follow;
            standalone.Step(Race.racers, route, null, 1, Time.unscaledTime + .4f, .016f);
            var profile = NativeContent.Load<NativeBroadcastCameraProfile>("NativeOnline/Broadcast/" + Race.trackId);
            Func<Vector3, Vector3, Vector3> follow = (center, heading) => center - heading * profile.followDistance
                + Vector3.Cross(Vector3.up, heading) * (standalone.Current.key == "follow--1" ? -3 : 3) + Vector3.up * profile.followHeight;
            Vector3 center1 = Race.racers[1].transform.position + Vector3.up * 1.3f;
            DirectorRequire(report, standalone.LastStepCut && standalone.Current.focus == 1
                && Vector3.Distance(Race.raceCamera.transform.position, follow(center1, Vector3.forward)) < .001f,
                "new subject reaches the exact incoming camera pose on the first cut frame");
            var post = Race.raceCamera.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
            DirectorRequire(report, !post || !post.enabled || !post.haveBundlesBeenInited || standalone.HistoryResetCount > histories,
                "hard cut resets the active post-processing temporal history");
            Race.racers[1].ApplyNetworkPose(Race.racers[1].transform.position + Vector3.right,
                Quaternion.Euler(0, 90, 0), Vector3.right * 30, 30, 0, true);
            standalone.Step(Race.racers, route, null, 1, Time.unscaledTime + .416f, .016f);
            center1 = Race.racers[1].transform.position + Vector3.up * 1.3f;
            Vector3 expected = follow(center1, Vector3.right);
            DirectorRequire(report, Vector3.Distance(Race.raceCamera.transform.position, expected) < .001f
                && Quaternion.Angle(Race.raceCamera.transform.rotation, Quaternion.LookRotation(center1 + Vector3.right * 2 - expected)) < .01f,
                "rigid follow frame after a subject cut has no residual transition");
            var motion = new NativeBroadcastCameraMotion();
            motion.Step(Vector3.zero, Vector3.forward * 20, 30, .016f, true, true, 80, 40, 60);
            Vector3 priorVelocity = motion.velocity; Quaternion priorRotation = motion.rotation;
            motion.Step(Vector3.right * 20, Vector3.right * 40, 60, .02f, true, true, 80, 40, 60);
            DirectorRequire(report, motion.position.x > 0 && motion.position.x < 1
                && (motion.velocity - priorVelocity).magnitude <= 40 * .02f + .001f,
                "airborne camera keeps independent velocity and bounded acceleration");
            DirectorRequire(report, Quaternion.Angle(priorRotation, motion.rotation) <= 60 * .02f + .001f
                && motion.fieldOfView > 30 && motion.fieldOfView <= 30 + 22 * .02f + .001f,
                "gimbal and optical zoom obey their own rate limits");
            Vector3 locked = new Vector3(10, 4, 10);
            motion.Step(locked, Vector3.forward * 30, 50, .02f, false, true);
            DirectorRequire(report, motion.position == locked, "fixed pan camera never moves its platform while tracking");
            standalone.End();
            for (int mode = 5; mode < NativeOnlineSpectatorView.ModeCount; mode++)
            {
                yield return DirectorKey(keyboard, Key.V);
                DirectorRequire(report, spectatorCameraMode == mode && Race.BroadcastCameraActive
                    && NativeOnlineSpectatorView.ManualKind(mode).HasValue,
                    "View input reaches manual camera " + NativeOnlineSpectatorView.ViewCaption(mode));
            }
            yield return DirectorKey(keyboard, Key.V);
            var rig = Race.raceCamera.GetComponentInParent<NativeKartCamera>();
            DirectorRequire(report, spectatorCameraMode == 0 && !Race.BroadcastCameraActive && !rig.FirstPerson && !rig.LookingBehind
                && Mathf.Approximately(Race.raceCamera.fieldOfView, originalFov) && Mathf.Approximately(Race.raceCamera.nearClipPlane, originalNear),
                "leaving Director View restores original third-person camera and lens");
        }
#else
        public IEnumerator CheckSpectatorDirector(string directory) { yield break; }
#endif
    }
}
