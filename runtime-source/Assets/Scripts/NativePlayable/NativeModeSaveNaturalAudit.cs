using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeModeSaveAuditReceipt
    {
        public string kind, utc, runId, phase, track, scene, mode, menu, resultScreen, sessionState, sessionId, savePath, saveHash, consumerIdentity = "pending: before-mode event to LateUpdate observation";
        public bool accepted = false, injectedState = false, directSaveCallObserved = false, ceremony, inRace, endedRace, resultsActive, showsScoreboard;
        public int frame, raceId, pausedBy, stationIndex, stationCount, sceneHandle, beforeModeFrame = -1, hashFrame;
        public bool ownerReboundAfterBeforeMode;
        public NativeModeSaveAuditOrigin originBeforeMode;
        public long saveGeneration;
        public float scaledTime, unscaledTime, timeScale, fixedDeltaTime, meltingFactor;
        public double sceneAge, fixedTime;
        public NativeModeSaveAuditHuman[] humans;
        public NativeModeSaveAuditTimer[] sourceTimerArray;
        public NativeModeSaveAuditResult[] pendingRows, currentStationRows, totals;
        public NativeModeSaveAuditMelting[] meltingPublishers;
        public NativeModeSaveAuditRecord[] saveRecords;
    }
    [Serializable] public sealed class NativeModeSaveAuditOrigin
    {
        public int frame, raceId, sceneHandle, stationIndex;
        public string scene, track, mode, sessionId;
    }
    [Serializable] public sealed class NativeModeSaveAuditHuman
    {
        public int slot, inputUserIndex, racerIndex;
        public string selected;
        public bool joined, racerValid, racerObjectAlive, racerActiveSelf, finished, justStarted, cardActive, actualCardActive, ready;
    }
    [Serializable] public sealed class NativeModeSaveAuditTimer
    {
        public int racer, currentLap;
        public bool humanTimerProducer, ended, humanDriving;
        public double totalSeconds;
        public double[] laps;
    }
    [Serializable] public sealed class NativeModeSaveAuditResult
    {
        public string seatId;
        public int place, score;
        public double seconds;
    }
    [Serializable] public sealed class NativeModeSaveAuditMelting
    {
        public string name, shaderKey;
        public bool enabled, activeSelf, activeInHierarchy;
        public float elapsed;
    }
    [Serializable] public sealed class NativeModeSaveAuditRecord
    {
        public string kind, id, characterId, vehicleId;
        public int version, bestRank, points;
        public double bestSeconds;
        public double[] splits;
    }
    public sealed class NativeModeSaveNaturalAudit : MonoBehaviour
    {
        static readonly string runId = Guid.NewGuid().ToString("N");
        static NativeModeSaveNaturalAudit instance;
        NativeRace owner;
        NativeRaceStateFlow state;
        string outputPath, cachedSavePath, cachedHash;
        long cachedGeneration = long.MinValue;
        int hashFrame, beforeModeFrame = -1, postModeFrames;
        float nextPulse;
        NativeModeSaveAuditOrigin originBeforeMode;
        public string LastError { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--mode-save-natural-audit") < 0 || instance) return;
            var host = new GameObject("Natural mode save observer");
            instance = host.AddComponent<NativeModeSaveNaturalAudit>();
            DontDestroyOnLoad(host);
            string directory = Application.persistentDataPath;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--report-dir") directory = args[i + 1];
            Directory.CreateDirectory(directory);
            instance.outputPath = Path.Combine(directory, "mode-save-natural-" + runId + ".jsonl");
        }
        void Bind(NativeRace next)
        {
            Unbind(); owner = next;
            if (!owner) return;
            owner.SourceRaceFinishObserved += RaceFinish;
            owner.SourceModeFinishedObserved += BeforeMode;
            owner.SourceRacerEndedObserved += RacerEnded;
            Write("scene-owner-bound", true);
        }
        void RefreshState()
        {
            var next = owner ? owner.SourceRaceState : null;
            if (ReferenceEquals(next, state)) return;
            if (state != null) state.ReadySubmitted -= ReadySubmitted;
            state = next;
            if (state != null) state.ReadySubmitted += ReadySubmitted;
        }
        void RaceFinish() => Write("source-race-finish-event", true);
        void RacerEnded(int racer) => Write("source-racer-ended-" + racer, false);
        void ReadySubmitted(int slot) => Write("source-ready-submitted-" + slot, false);
        void BeforeMode() => Write("before-existing-mode-consumer", true);
        void LateUpdate()
        {
            if (!owner)
            {
                var next = FindObjectOfType<NativeRace>();
                if (next) Bind(next);
                else { Unbind(); return; }
            }
            RefreshState();
            if (postModeFrames > 0)
            {
                Write("late-after-before-mode-observation", true); postModeFrames--;
            }
            else if (Time.unscaledTime >= nextPulse)
            {
                nextPulse = Time.unscaledTime + .5f; Write("natural-late-pulse", false);
            }
        }
        void Write(string kind, bool refreshHash)
        {
            if (!string.IsNullOrEmpty(LastError)) return;
            try { WriteCore(kind, refreshHash); }
            catch (Exception error)
            {
                // 审计写盘失败保留生产事件与保存流程。
                LastError = error.ToString();
                Debug.LogWarning("NATIVE_MODE_SAVE_AUDIT_FAILURE " + LastError);
            }
        }
        void WriteCore(string kind, bool refreshHash)
        {
            if (!owner || string.IsNullOrEmpty(outputPath)) return;
            var receipt = owner.ReadModeSaveAuditSnapshot();
            receipt.kind = kind; receipt.runId = runId; receipt.utc = DateTime.UtcNow.ToString("o");
            if (kind == "before-existing-mode-consumer")
            {
                beforeModeFrame = receipt.frame; postModeFrames = 3;
                originBeforeMode = new NativeModeSaveAuditOrigin { frame = receipt.frame, raceId = receipt.raceId,
                    sceneHandle = receipt.sceneHandle, scene = receipt.scene, track = receipt.track, mode = receipt.mode,
                    sessionId = receipt.sessionId, stationIndex = receipt.stationIndex };
            }
            receipt.beforeModeFrame = beforeModeFrame; receipt.originBeforeMode = originBeforeMode;
            receipt.ownerReboundAfterBeforeMode = originBeforeMode != null && receipt.sceneHandle != originBeforeMode.sceneHandle;
            string file = NativeSaveStore.FilePath;
            long generation = NativeSaveStore.Data == null ? -1 : NativeSaveStore.Data.generation;
            if (refreshHash || file != cachedSavePath || generation != cachedGeneration)
            {
                cachedSavePath = file; cachedGeneration = generation; hashFrame = Time.frameCount;
                try
                {
                    if (File.Exists(file))
                    {
                        using (var stream = File.OpenRead(file))
                        using (var sha = SHA256.Create()) cachedHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    }
                    else cachedHash = "missing";
                }
                catch (IOException error) { cachedHash = "read-error:" + error.GetType().Name; }
            }
            receipt.savePath = cachedSavePath; receipt.saveHash = cachedHash; receipt.hashFrame = hashFrame; receipt.saveGeneration = generation;
            File.AppendAllText(outputPath, JsonUtility.ToJson(receipt) + "\n");
        }
        void Unbind()
        {
            if (!ReferenceEquals(owner, null))
            {
                owner.SourceRaceFinishObserved -= RaceFinish;
                owner.SourceModeFinishedObserved -= BeforeMode;
                owner.SourceRacerEndedObserved -= RacerEnded;
            }
            if (state != null) state.ReadySubmitted -= ReadySubmitted;
            owner = null; state = null;
        }
        void OnDestroy() { Unbind(); if (instance == this) instance = null; }
    }
    public sealed partial class NativeRace
    {
        internal NativeModeSaveAuditReceipt ReadModeSaveAuditSnapshot()
        {
            int racerCount = racers == null ? 0 : racers.Length;
            var session = NativeGameSession.Current;
            var currentStation = session == null ? null : session.stations.LastOrDefault(s => s.index == session.stationIndex);
            var receipt = new NativeModeSaveAuditReceipt
            {
                phase = phase.ToString(), track = trackId, scene = gameObject.scene.name, sceneHandle = gameObject.scene.handle, mode = selectedMode.ToString(),
                menu = hud == null ? "uninitialized" : hud.MenuPage.ToString(), ceremony = ceremony, resultScreen = resultsFlow.State.ToString(),
                frame = Time.frameCount, scaledTime = Time.time, unscaledTime = Time.unscaledTime, timeScale = Time.timeScale,
                sceneAge = Time.timeSinceLevelLoadAsDouble, fixedTime = Time.fixedTimeAsDouble, fixedDeltaTime = Time.fixedDeltaTime,
                meltingFactor = Shader.GetGlobalFloat("_MeltingFactor"),
                raceId = sourceRaceState == null ? -1 : sourceRaceState.RaceId,
                pausedBy = sourceRaceState == null ? -1 : sourceRaceState.PausedBy,
                inRace = sourceRaceState != null && sourceRaceState.InRace, endedRace = sourceRaceState != null && sourceRaceState.EndedRace,
                resultsActive = sourceRaceState != null && sourceRaceState.ResultsActive, showsScoreboard = sourceRaceState != null && sourceRaceState.ShowsScoreboard,
                sessionState = session == null ? "none" : session.state.ToString(), sessionId = session == null ? null : session.sessionId,
                stationIndex = session == null ? -1 : session.stationIndex, stationCount = session == null ? 0 : session.stations.Count,
                humans = sourceRaceState == null ? new NativeModeSaveAuditHuman[0] : sourceRaceState.Humans.Select((h, slot) => new NativeModeSaveAuditHuman
                {
                    slot = slot, inputUserIndex = h.InputUserIndex, racerIndex = h.RacerIndex, selected = h.Selected.ToString(), joined = h.Joined,
                    racerValid = h.RacerValid, racerObjectAlive = h.RacerIndex >= 0 && h.RacerIndex < racerCount && racers[h.RacerIndex],
                    racerActiveSelf = h.RacerIndex >= 0 && h.RacerIndex < racerCount && racers[h.RacerIndex] && racers[h.RacerIndex].gameObject.activeSelf, finished = h.Finished, justStarted = h.JustStarted, cardActive = h.CardActive,
                    actualCardActive = h.Joined && hud != null && SourceCardActive(slot), ready = h.Ready
                }).ToArray(),
                sourceTimerArray = sourceLapTimers == null ? new NativeModeSaveAuditTimer[0] : sourceLapTimers.Select((timer, i) => new NativeModeSaveAuditTimer
                {
                    racer = i, currentLap = timer.CurrentLap, ended = timer.Ended, totalSeconds = SourceTotalSeconds(i), laps = timer.LapSeconds(),
                    humanTimerProducer = raceConfig != null && i < raceConfig.roster.Count && raceConfig.roster[i].isHuman, humanDriving = sourceHumanDriving != null && i < sourceHumanDriving.Length && sourceHumanDriving[i]
                }).ToArray(),
                pendingRows = session == null ? new NativeModeSaveAuditResult[0] : session.pending.Select(r => new NativeModeSaveAuditResult { seatId = r.seatId, place = r.place, score = r.score, seconds = r.time }).ToArray(),
                currentStationRows = currentStation == null ? new NativeModeSaveAuditResult[0] : currentStation.results.Select(r => new NativeModeSaveAuditResult { seatId = r.seatId, place = r.place, score = r.score, seconds = r.time }).ToArray(),
                totals = session == null ? new NativeModeSaveAuditResult[0] : session.totals.Select(r => new NativeModeSaveAuditResult { seatId = r.seatId, score = r.score, seconds = r.time }).ToArray(),
                saveRecords = NativeSaveStore.Data == null ? new NativeModeSaveAuditRecord[0] : NativeSaveStore.Data.timeTrialRecords.Select(r => new NativeModeSaveAuditRecord
                {
                    kind = "TimeTrials", id = r.trackId, characterId = r.characterId, vehicleId = r.vehicleId,
                    version = r.version, bestSeconds = r.bestSeconds, splits = r.lapSeconds == null ? null : (double[])r.lapSeconds.Clone()
                }).Concat(NativeSaveStore.Data.cupRecords.Select(r => new NativeModeSaveAuditRecord
                {
                    kind = "GrandPrix", id = r.cupId, characterId = r.characterId, vehicleId = r.vehicleId, version = r.version,
                    bestRank = r.bestRank, points = r.points, bestSeconds = r.bestSeconds, splits = r.stationSeconds == null ? null : (double[])r.stationSeconds.Clone()
                })).ToArray(),
                meltingPublishers = FindObjectsOfType<NativeShaderGlobalsOverTime>(true).Where(p => p.gameObject.scene == gameObject.scene).Select(p => new NativeModeSaveAuditMelting
                {
                    name = p.name, shaderKey = p.shaderKey, enabled = p.enabled, activeSelf = p.gameObject.activeSelf,
                    activeInHierarchy = p.gameObject.activeInHierarchy, elapsed = p.Elapsed
                }).ToArray()
            };
            return receipt;
        }
    }
}
