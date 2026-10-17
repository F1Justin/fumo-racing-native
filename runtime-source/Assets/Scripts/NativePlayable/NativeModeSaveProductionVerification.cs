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
    [Serializable] public sealed class NativeModePhysicalEvent
    {
        public string kind, track, phase, sessionJson;
        public int station, raceId, frame, modeFinishedCount, originalSceneHandle;
        public int[] publisherInstanceIds;
        public string resultsScreen, humanSelection, actionAsset, boundActionAsset;
        public bool focused, keyboardEnabled, ready, cardActive, justStarted;
        public double cachedTime, sceneAge;
        public float meltingGlobal, scaledTime, frameDelta, fixedDelta;
        public float[] publisherElapsed;
        public bool[] publisherEnabled;
        public NativeModeSaveDiskReceipt.Receipt disk;
    }
    [Serializable] public sealed class NativeModePhysicalStation
    {
        public string track;
        public int index;
        public double[] humanLaps;
        public double humanTotal;
        public int[] gateEntries, gateExits;
        public int actualOkStarted, sourceEndFrame, cardsFrame;
        public float sourceEndScaledTime, cardsScaledTime, cardsFrameDelta;
    }
    [Serializable] public sealed class NativeModePhysicalReport
    {
        public string scope = "Injected transform placement through real SceneCollider exits and keyboard state through actual InputAction.started. Real Begin/countdown/source timers/cards/GameModeFinished/commit/scene routes; natural driving and hardware input untested.";
        public string mode;
        public bool passed, accepted = false;
        public string sourceMapping = "TT/GP raw event order captured; save/ceremony source timing requires independent source mapping review";
        public List<string> checks = new List<string>();
        public List<NativeModePhysicalEvent> events = new List<NativeModePhysicalEvent>();
        public List<NativeModePhysicalStation> stations = new List<NativeModePhysicalStation>();
        public NativeModeSaveDiskReceipt.Receipt finalReload;
    }
    public sealed class NativeModeSavePhysicalRunner : MonoBehaviour
    {
        static NativeModeSavePhysicalRunner instance;
        public readonly NativeModePhysicalReport Report = new NativeModePhysicalReport();
        public string[] Args;
        public string OwnedSave, ReportDirectory;
        public int ModeFinishedCount;
        Keyboard fixtureKeyboard;
        bool fixtureKeyboardWasEnabled;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--mode-save-production-self-test") < 0 || instance) return;
            var node = new GameObject("Mode save physical audit runner");
            DontDestroyOnLoad(node);
            instance = node.AddComponent<NativeModeSavePhysicalRunner>();
            instance.Args = args;
            instance.StartCoroutine(instance.Guarded());
        }
        public static string Value(string[] args, string key)
        { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        public void Require(bool ok, string message)
        { Report.checks.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }
        public void Capture(NativeRace race, string kind)
        {
            var session = NativeGameSession.Current;
            var publishers = UnityEngine.Object.FindObjectsOfType<NativeShaderGlobalsOverTime>(true).Where(p => p.gameObject.scene == race.gameObject.scene).OrderBy(p => p.GetInstanceID()).ToArray();
            Report.events.Add(new NativeModePhysicalEvent { kind = kind, track = race.ModePhysicalTrack,
                station = session != null ? session.stationIndex : Report.events.LastOrDefault()?.station ?? 0, raceId = race.SourceRaceState.RaceId, frame = Time.frameCount,
                cachedTime = race.SourceGameTime, phase = race.SourceNativePhase, modeFinishedCount = ModeFinishedCount,
                originalSceneHandle = race.gameObject.scene.handle, publisherInstanceIds = publishers.Select(p => p.GetInstanceID()).ToArray(),
                sceneAge = Time.timeSinceLevelLoadAsDouble, meltingGlobal = Shader.GetGlobalFloat("_MeltingFactor"),
                scaledTime = Time.time, frameDelta = Time.deltaTime, fixedDelta = Time.fixedDeltaTime,
                publisherElapsed = publishers.Select(p => p.Elapsed).ToArray(),
                publisherEnabled = publishers.Select(p => p.enabled).ToArray(),
                resultsScreen = race.ModePhysicalResultsState, humanSelection = race.SourceRaceState.Humans[0].Selected.ToString(),
                focused = Application.isFocused, keyboardEnabled = Keyboard.current != null && Keyboard.current.enabled,
                ready = race.SourceRaceState.Humans[0].Ready, cardActive = race.SourceRaceState.Humans[0].CardActive,
                justStarted = race.SourceRaceState.Humans[0].JustStarted,
                actionAsset = race.ModePhysicalActionIdentity(false), boundActionAsset = race.ModePhysicalActionIdentity(true),
                sessionJson = session != null ? JsonUtility.ToJson(session) : "null", disk = NativeModeSaveDiskReceipt.Capture(OwnedSave, Args) });
        }
        IEnumerator Guarded()
        {
            var previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var stack = new Stack<IEnumerator>(); stack.Push(Run()); Exception failure = null;
            while (failure == null && stack.Count > 0)
            {
                bool moved = false; object current = null;
                try { var e = stack.Peek(); moved = e.MoveNext(); if (moved) current = e.Current; else { stack.Pop(); (e as IDisposable)?.Dispose(); } }
                catch (Exception e) { failure = e; }
                if (failure != null) break;
                if (!moved) continue;
                if (current is IEnumerator nested && !(current is CustomYieldInstruction)) stack.Push(nested); else yield return current;
            }
            while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { if (failure == null) failure = e; } }
#if ENABLE_INPUT_SYSTEM
            if (fixtureKeyboard != null && fixtureKeyboard.added && !fixtureKeyboardWasEnabled) InputSystem.DisableDevice(fixtureKeyboard);
#endif
            InputSystem.settings.backgroundBehavior = previousBackground;
            Report.passed = failure == null;
            if (failure != null) Report.checks.Add("FAIL exception " + failure);
            if (!string.IsNullOrEmpty(ReportDirectory))
            {
                Directory.CreateDirectory(ReportDirectory);
                File.WriteAllText(Path.Combine(ReportDirectory, "mode-save-production-" + Report.mode + ".json"), JsonUtility.ToJson(Report, true));
            }
            if (failure != null) Debug.LogError("NATIVE_MODE_SAVE_PRODUCTION " + failure);
            Application.Quit(Report.passed ? 0 : 2);
        }
        IEnumerator Run()
        {
            Report.mode = Value(Args, "--mode-save-production-mode");
            OwnedSave = Value(Args, "--save-directory");
            ReportDirectory = Value(Args, "--report-dir");
            Require(Report.mode == "tt" || Report.mode == "gp", "independent tt or gp group");
            Require(!string.IsNullOrEmpty(OwnedSave) && !string.IsNullOrEmpty(ReportDirectory), "explicit isolated save and report directories");
            Require(!Args.Any(a => a.EndsWith("-self-test", StringComparison.Ordinal) && a != "--mode-save-production-self-test")
                && Array.IndexOf(Args, "--autodrive") < 0, "no AI/visual/mode self-test flags or extra autopilot clock");
            NativeRace race = null;
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while ((!race || !race.ModePhysicalInitialized) && Time.realtimeSinceStartupAsDouble < deadline)
            { race = UnityEngine.Object.FindObjectOfType<NativeRace>(); yield return null; }
            Require(race && race.ModePhysicalInitialized, "initial scene Start completed");
            Require(NativeModeSaveDiskReceipt.IsolatedExactDirectory(OwnedSave, Args, out string isolation), "isolated path " + isolation);
            var initial = NativeModeSaveDiskReceipt.Capture(OwnedSave, Args);
            Require(initial.timeTrials.Length == 0 && initial.cups.Length == 0, "fresh owned group save records");
            fixtureKeyboard = Keyboard.current;
            Require(fixtureKeyboard != null, "injected keyboard fixture device exists");
            fixtureKeyboardWasEnabled = fixtureKeyboard.enabled;
            bool fixtureFocusedBeforeBegin = Application.isFocused;
#if ENABLE_INPUT_SYSTEM
            if (!fixtureKeyboardWasEnabled) InputSystem.EnableDevice(fixtureKeyboard);
#endif
            Require(fixtureKeyboard.enabled, "injected keyboard fixture enabled for background audit; enabled-before="+fixtureKeyboardWasEnabled+" focused-before="+fixtureFocusedBeforeBegin);
            race.ModePhysicalBegin(Report.mode == "gp");
            Require(NativeLocalSelectionOwners.Instance.Paired(NativeLocalSelectionOwners.Instance.AtSlot(0), fixtureKeyboard), "fixture owner explicitly paired with keyboard");
            int stations = Report.mode == "gp" ? 4 : 1;
            string sessionId = NativeGameSession.Current.sessionId;
            for (int index = 0; index < stations; index++)
            {
                Require(NativeGameSession.Current.sessionId == sessionId && NativeGameSession.Current.stationIndex == index, "session/station preserved " + index);
                yield return race.ModePhysicalRunStation(this, index);
                if (index + 1 < stations)
                {
                    int oldInstance = race.GetInstanceID(); race = null;
                    deadline = Time.realtimeSinceStartupAsDouble + 40;
                    while (Time.realtimeSinceStartupAsDouble < deadline)
                    {
                        var next = UnityEngine.Object.FindObjectOfType<NativeRace>();
                        if (next && next.GetInstanceID() != oldInstance && next.ModePhysicalInitialized
                            && next.ModePhysicalTrack == NativeModeCatalog.CupTrackIds[index + 1]) { race = next; break; }
                        yield return null;
                    }
                    Require(race, "new matching station scene initialized " + (index + 1));
                    // 后场由 Start 消费 pendingRaceStart，重复 Begin 会改写源计时起点。
                }
            }
            if (Report.mode == "gp") yield return race.ModePhysicalObserveCeremony(this);
            var final = NativeModeSaveDiskReceipt.Capture(OwnedSave, Args);
            Require(final.diskMatchesMemory, "final primary bytes match live records and exact double bits");
            if (Report.mode == "tt")
            {
                Require(ModeFinishedCount == 1 && final.timeTrials.Length == 1, "single TT commit callback and record");
                var row = final.timeTrials[0]; var station = Report.stations[0];
                Require(row.trackId == station.track && row.lapCount == 5 && row.bestSeconds == station.humanTotal
                    && row.lapSeconds.SequenceEqual(station.humanLaps), "TT disk exact physical source total/five lap arrays");
            }
            else
            {
                Require(final.cups.Length == 1 && final.cups[0].stationSeconds.SequenceEqual(Report.stations.Select(s => s.humanTotal)),
                    "GP disk four source station arrays exact");
            }
            Report.finalReload = NativeModeSaveDiskReceipt.ReloadAndVerify(final, OwnedSave, Args);
            Require(Report.finalReload.reloadSucceeded && Report.finalReload.reloadPreservedReceipt, "final isolated disk reload exact records/hash/generation");
        }
    }
    public sealed partial class NativeRace
    {
        internal string ModePhysicalResultsState => resultsFlow.State.ToString();
        internal string ModePhysicalActionIdentity(bool bound)
        {
            var asset = bound ? sourceHumanInput.BoundActionAsset(0) : NativeInputBindings.RaceActionAsset(0);
            return asset ? asset.GetInstanceID() + ":" + asset.FindAction("Normal/Ok", true).phase : "missing";
        }
        internal bool ModePhysicalInitialized => hud != null && vehicleRoster;
        internal string ModePhysicalTrack => trackId;
        internal void ModePhysicalBegin(bool gp)
        {
            if (gp && trackId != NativeModeCatalog.CupTrackIds[0]) throw new InvalidOperationException("GP group must start in source first Forest scene");
            EnsureInjectedDiagnosticOwner(Keyboard.current);
            selectedMode = gp ? NativeSessionMode.GrandPrix : NativeSessionMode.TimeTrials;
            selectedTrackId = trackId; autopilot = false;
            sessionId = NativeGameSession.Create(BuildModeConfig());
            Begin();
        }
        internal IEnumerator ModePhysicalKeyboardSubmit(Action receipt)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) throw new InvalidOperationException("Actual keyboard device is required");
            var ok = NativeInputBindings.RaceActionAsset(0).FindAction("Normal/Ok", true);
            if (!ok.enabled || !ok.controls.Any(control => control == keyboard.enterKey))
                throw new InvalidOperationException("Actual human0 Normal/Ok Enter binding is required");
            Action<InputAction.CallbackContext> observed = context => receipt();
            ok.started += observed;
            try
            {
#if ENABLE_INPUT_SYSTEM
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
#else
                throw new InvalidOperationException("Production keyboard verification requires the native Input System runtime");
#endif
            }
            finally { ok.started -= observed; }
            yield break;
        }
        internal IEnumerator ModePhysicalRunStation(NativeModeSavePhysicalRunner audit, int stationIndex)
        {
            audit.Require(!visualTest && !autopilot && raceConfig.laps == 5 && raceConfig.racerCount == (audit.Report.mode == "gp" ? 8 : 1), "source TT1/GP8 roster, five laps, ordinary clock");
            double deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (phase != Phase.Racing && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            audit.Require(phase == Phase.Racing && sourceRanking.Started && raceClock.Running, "real Begin countdown/start " + stationIndex);
            int stationEventStart = audit.Report.events.Count;
            audit.Capture(this, "started");
            var receipts = gates.Select(gate => gate.gameObject.AddComponent<NativeGateProductionReceipt>()).ToArray();
            foreach (var motor in racers) motor.enabled = false;
            var originalScene = gameObject.scene;
            var originalPublishers = UnityEngine.Object.FindObjectsOfType<NativeShaderGlobalsOverTime>(true).Where(p => p.gameObject.scene == originalScene).ToArray();
            double[] frozenLaps = null; double frozenTotal = 0; float endAt = -1; int endFrame = -1;
            var beforeFinish = NativeModeSaveDiskReceipt.Capture(audit.OwnedSave, audit.Args);
            Action<int> endedReceipt = racer => { if (racer == 0) { endAt = Time.time; endFrame = Time.frameCount; audit.Capture(this, "SourceRacerEnded"); } };
            int okStarted = 0;
            bool postCommit = false;
            Action modeObserved = () => { audit.ModeFinishedCount++; audit.Capture(this, "GameModeFinished-before-commit"); };
            Action finishReceipt = () => audit.Capture(this, "RaceFinish-after-manager-reset");
            Action hudReceipt = () => audit.Capture(this, "HudCleanup-after-production-cleanup");
            Action afterCommit = () => { postCommit = true; audit.Capture(this, "GameModeFinished-after-commit"); };
            SourceModeFinishedObserved += modeObserved;
            SourceRacerEndedObserved += endedReceipt;
            SourceRaceFinishObserved += finishReceipt; sourceRaceState.HudCleanup += hudReceipt;
            try
            {
                yield return PassProductionGate(0, 0);
                for (int lap = 0; lap < 5; lap++)
                {
                    for (int gate = 1; gate < gates.Length; gate++) yield return PassProductionGate(0, gate);
                    yield return PassProductionGate(0, 0);
                    var lapArray = sourceLapTimers[0].LapSeconds();
                    audit.Require(sourceLapTimers[0].CurrentLap == lap + 1 && progress[0].CompletedLaps == lap + 1
                        && lapArray.Take(lap + 1).All(value => value > 0) && lapArray.Skip(lap + 2).All(value => value == 0),
                        "physical completed lap count and fixed five-slot source array " + lap);
                }
                audit.Require(sourceLapTimers[0].Ended && sourceResultAppend.Count == racers.Length, "source End and complete publisher");
                audit.Require(endAt >= 0 && endFrame >= 0, "actual human source End timestamp observed");
                frozenLaps = sourceLapTimers[0].LapSeconds(); frozenTotal = SourceTotalSeconds(0);
                audit.Require(frozenLaps.Length == 5 && frozenTotal > 0, "frozen source arrays five laps");
                audit.Capture(this, "physical-end-before-ready");
                audit.Require(NativeModeSaveDiskReceipt.VerifyUnchanged(beforeFinish, NativeModeSaveDiskReceipt.Capture(audit.OwnedSave, audit.Args)),
                    "physical finish before Ready retains original primary/hash/generation");
                deadline = Time.realtimeSinceStartupAsDouble + 8;
                while (resultsFlow.State != NativeResultsFlow.Screen.Cards && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                audit.Require(resultsFlow.State == NativeResultsFlow.Screen.Cards && hud.SourceCardActive(0), "source delayed Cards opened");
                audit.Capture(this, "cards-opened");
                audit.Require(Time.time - endAt >= 3f - Time.deltaTime - 0.002f, "Cards source End three second boundary with actual frame tolerance");
                audit.Require(NativeModeSaveDiskReceipt.VerifyUnchanged(beforeFinish, NativeModeSaveDiskReceipt.Capture(audit.OwnedSave, audit.Args)),
                    "Cards before Ready retains original primary/hash/generation");
                audit.Require(resultsFlow.Rows.Single(row => row.HumanIndex == 0).Seconds == frozenTotal, "Cards exact frozen source total");
                // Cards 前已绑定保存消费者；此处追加监听获得消费者返回后的主文件快照。
                sourceRaceState.GameModeFinished += afterCommit;
                audit.Report.stations.Add(new NativeModePhysicalStation { track = trackId, index = stationIndex,
                    humanLaps = frozenLaps, humanTotal = frozenTotal, sourceEndFrame = endFrame, cardsFrame = Time.frameCount,
                    sourceEndScaledTime = endAt, cardsScaledTime = Time.time, cardsFrameDelta = Time.deltaTime,
                    gateEntries = receipts.Select(r => r.entries).ToArray(), gateExits = receipts.Select(r => r.exits).ToArray() });
                audit.Require(receipts.All(r => r.entries >= 5 && r.exits >= 5 && r.motors.Contains(racers[0].GetInstanceID())), "actual native collider contacts every gate");
                audit.Capture(this, "before-keyboard-submit");
                yield return ModePhysicalKeyboardSubmit(() => { okStarted++; audit.Capture(this, "keyboard-ok-started"); });
                audit.Report.stations.Last().actualOkStarted = okStarted;
                if (this) audit.Capture(this, "after-keyboard-submit");
                if (audit.Report.mode == "gp")
                {
                    audit.Require(resultsFlow.State == NativeResultsFlow.Screen.Scoreboard, "first actual Ready opened scoreboard");
                    if (stationIndex == 3)
                    {
                        while (Time.timeSinceLevelLoadAsDouble < 31) yield return null;
                        audit.Require(Time.timeSinceLevelLoadAsDouble < 190, "Winter actual scene age 31..190 before final submit");
                        audit.Capture(this, "Winter-scoreboard-before-submit");
                        audit.Require(Shader.GetGlobalFloat("_MeltingFactor") > 0
                            && originalPublishers.Any(p => p && p.enabled && p.Elapsed > 0),
                            "Winter active original publisher and positive global before reset submit");
                    }
                    audit.Capture(this, "before-keyboard-submit");
                yield return ModePhysicalKeyboardSubmit(() => { okStarted++; audit.Capture(this, "keyboard-ok-started"); });
                audit.Report.stations.Last().actualOkStarted = okStarted;
                if (this) audit.Capture(this, "after-keyboard-submit");
                    if (stationIndex == 3)
                    {
                        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                        audit.Capture(this, "ceremony-after-two-real-fixed-ticks");
                        audit.Require(ceremony && originalPublishers.Length > 0 && originalPublishers.All(p => p && p.gameObject.scene == originalScene && !p.enabled)
                            && Shader.GetGlobalFloat("_MeltingFactor") == 0, "ceremony retires scene publishers and retains reset global after two fixed ticks");
                    }
                }
                audit.Report.stations.Last().actualOkStarted = okStarted;
                audit.Require(postCommit && okStarted == (audit.Report.mode == "gp" ? 2 : 1), "actual InputAction.started and modefinish-after-commit receipt");
                var stationEvents = audit.Report.events.Skip(stationEventStart).ToArray();
                var requiredOrder = new[] { "RaceFinish-after-manager-reset", "HudCleanup-after-production-cleanup",
                    "GameModeFinished-before-commit", "GameModeFinished-after-commit" };
                int previousEvent = -1, previousFrame = -1;
                foreach (var kind in requiredOrder)
                {
                    audit.Require(stationEvents.Count(e => e.kind == kind) == 1, "exactly one station event " + kind);
                    int at = Array.FindIndex(stationEvents, e => e.kind == kind);
                    var observed = stationEvents[at];
                    audit.Require(at > previousEvent && observed.frame >= previousFrame, "station event order/frame " + kind);
                    if (kind == requiredOrder[0] || kind == requiredOrder[1])
                        audit.Require(observed.meltingGlobal == 0, "actual source cleanup reset global " + kind);
                    previousEvent = at; previousFrame = observed.frame;
                }
                audit.Require(audit.ModeFinishedCount == stationIndex + 1, "exact station ModeFinished count " + stationIndex);
                if (audit.Report.mode == "gp" && stationIndex < 3)
                    audit.Require(NativeModeSaveDiskReceipt.Capture(audit.OwnedSave, audit.Args).cups.Length == 0,
                        "intermediate GP station has no cup write");
            }
            finally
            {
                SourceModeFinishedObserved -= modeObserved; sourceRaceState.GameModeFinished -= afterCommit;
                SourceRacerEndedObserved -= endedReceipt;
                SourceRaceFinishObserved -= finishReceipt; sourceRaceState.HudCleanup -= hudReceipt;
                foreach (var motor in racers) if (motor) motor.enabled = true;
            }
        }
        static string ModePhysicalAccumulation(NativeSessionSnapshot session)
        {
            if (session == null) throw new InvalidOperationException("Missing source accumulation snapshot");
            Func<NativeSessionResult, string> row = value => value.seatId + ":" + BitConverter.DoubleToInt64Bits(value.time)
                + ":" + value.place + ":" + value.score;
            return session.stations.Count + "|" + string.Join("|", session.stations.Select(station => station.index + ":" + station.trackId
                + ":" + station.results.Count + ":" + string.Join(",", station.results.Select(row))))
                + "|totals:" + session.totals.Count + ":" + string.Join(",", session.totals.Select(row))
                + "|pending:" + session.pending.Count + ":" + string.Join(",", session.pending.Select(row));
        }
        internal IEnumerator ModePhysicalObserveCeremony(NativeModeSavePhysicalRunner audit)
        {
            audit.Require(ceremony && ceremonyView && ceremonyView.IsPlaying, "final results routed into real ceremony playback");
            audit.Capture(this, "ceremony-start");
            var beforeCeremony = NativeModeSaveDiskReceipt.Capture(audit.OwnedSave, audit.Args);
            string beforeSession = ModePhysicalAccumulation(NativeGameSession.Current), afterSession = null;
            Action modeObserved = () => { audit.ModeFinishedCount++; audit.Capture(this, "ceremony-GameModeFinished-before-commit"); };
            Action afterCommit = () => { afterSession = ModePhysicalAccumulation(NativeGameSession.Current); audit.Capture(this, "ceremony-GameModeFinished-after-commit"); };
            SourceModeFinishedObserved += modeObserved; sourceRaceState.GameModeFinished += afterCommit;
            float finishAt = ceremonyView.finishSignalTime;
            double deadline = Time.realtimeSinceStartupAsDouble + Mathf.Max(5f, finishAt - ceremonyView.PlaybackTime + 15f);
            try
            {
                while (ceremony && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                audit.Require(!ceremony && phase == Phase.Menu, "natural realtime ceremony finish routed to source cleanup/main menu");
                audit.Require(NativeModeSaveDiskReceipt.VerifyUnchanged(beforeCeremony, NativeModeSaveDiskReceipt.Capture(audit.OwnedSave, audit.Args)),
                    "natural second final ModeFinished preserves primary hash/generation/records");
                audit.Require(afterSession == beforeSession,
                    "natural ceremony final consumer adds no duplicate session stations or totals");
                audit.Require(audit.ModeFinishedCount == 5, "four station modefinished callbacks plus natural ceremony final callback");
            }
            finally { SourceModeFinishedObserved -= modeObserved; sourceRaceState.GameModeFinished -= afterCommit; }
        }
    }
}
