using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class NativeRace : MonoBehaviour
    {
        public Vector3[] route;
        public float[] widths;
        public Transform[] gates;
        public BoxCollider[] gateColliders;
        public NativeKartMotor[] racers;
        public Camera raceCamera;
        public RuntimeAnimatorController finishCameraController;
        public NativeKartFinishCamera FinishCamera { get; private set; }
        public NativeMenuPresentation menuPresentation;
        public NativeKartItemHud itemHud;
        public NativeVehicleRoster vehicleRoster;
        public Vector3 cameraOffset = new Vector3(0, 2.4f, -6.5f);
        public Quaternion cameraAngle = Quaternion.Euler(6, 0, 0);
        public AudioClip music;
        public string trackId = "forest";
        public Vector3[] sourceGridPositions;
        public Quaternion[] sourceGridRotations;
        public string[] racerNames = { "Reimu / YOU", "Marisa", "Cirno", "Sanae" };
        enum Phase { Menu, Countdown, Racing, Finished, Paused }
        Phase phase, beforePause;
        NativeCircuitProgress[] progress;
        Vector3[] starts;
        Quaternion[] headings;
        float[] finishTimes;
        float elapsed, countdown, best, lastTelemetry;
        int countdownAudioSecond;
        float previousVerticalInput;
        float nextMenuRepeat;
        int heldMenuDirection;
        NativeKartCamera cameraRig;
        readonly List<ItemRaceMetrics> itemMetrics = new List<ItemRaceMetrics>();
        bool itemCombatTest;
        NativeItemWorld itemWorld;
        static double sourceNextOfudaTime;
        static int sourceLastOfudaRaceId=-1;
        public double NextOfudaTime { get=>sourceNextOfudaTime;set=>sourceNextOfudaTime=value; }
        public double SourceOfudaEligibilityNext(NativeItemCatalog.Entry entry)
        {
            int raceId=sourceRaceState.RaceId;
            if(sourceLastOfudaRaceId<raceId){sourceNextOfudaTime=entry.ofudaCooldown;sourceLastOfudaRaceId=raceId;}
            return sourceNextOfudaTime;
        }
        public void SourceOfudaCreated(NativeItemCatalog.Entry entry)
        {sourceLastOfudaRaceId=sourceRaceState.RaceId;sourceNextOfudaTime=SourceGameTime+entry.ofudaCooldown;}
        public float ElapsedTime => elapsed;
        public bool IsRaceRunning => phase == Phase.Racing || phase == Phase.Finished;
        bool autopilot, selfTest, visualTest, capturedMid, capturedFinish, captureDetail;
        bool interpolateRendering = true;
        bool captureTestScreenshots = true;
        int completedTests, errorCount;
        string reportDirectory;
        FeedbackSample[] feedbackSamples;
        public NativeRaceHud hud = new NativeRaceHud();
        AudioSource musicSource;
        readonly List<string> testResults = new List<string>();
        readonly List<AIMetrics> aiMetrics = new List<AIMetrics>();
        static string pendingTrack;
        static bool pendingRaceStart;
        // Release verification survives the production Single-mode scene reloads.
        static bool releaseSmokeStarted;
        static NativeRace releaseSmokePrevious;
        static bool commandLineTrackApplied;
        static int preferredCharacterIndex = -1;
        static int preferredVehicleIndex;
        static int preferredSkinIndex;
        static int switchTestStage;
        static NativeRace switchPreviousRace;
        static List<string> switchChecks;
        static bool switchChecksPassed;
        string selectedTrackId;
        int selectedCharacterIndex, selectedVehicleIndex, selectedSkinIndex;
        int[] selectableCharacters = new int[0];
        string sessionId;
        bool verificationRun;
        readonly Queue<int> finishArrivalQueue = new Queue<int>();
        int[] finishPlaces;
        int finishArrivalCount;
        public bool AllowCameraVerificationUpdates { get; set; }
        internal int VisualErrorCount => errorCount;
        internal int DiagnosticPhase => (int)phase;
        internal int DiagnosticCaptureFrame { get; private set; } = -1000;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void SilenceConstructionBuild()
        {
            AudioListener.volume = 0f;
            pendingTrack = null;
            pendingRaceStart = false;
            releaseSmokeStarted = false;
            releaseSmokePrevious = null;
            commandLineTrackApplied = false;
            preferredCharacterIndex = -1;
            preferredVehicleIndex = 0;
            switchTestStage = 0;
            switchPreviousRace = null;
            switchChecks = null;
            switchChecksPassed = true;
        }

        bool automatedSettingsPresentation;

        void Start()
        {
            TelemetrySceneStarting();
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("--winter-nonrendered-diagnostic") && !args.Contains("--winter-runtime-audit"))
                args = args.Concat(new[] { "--winter-runtime-audit" }).ToArray();
            verificationRun = args.Any(a => a.EndsWith("-self-test", StringComparison.Ordinal)
                || a.StartsWith("--check-", StringComparison.Ordinal) || a == "--verify-item-character-emblems"
                || a == "--check-circuit-banner-motion-nonrendered" || a == "--check-circuit-banner-motion-nonrendered" || a == "--check-local-player-selection" || a == "--local-selection-nonrendered-diagnostic"
                || a == "--item-prefab-structure-diagnostic" || a == "--ready-structure-diagnostic"
                || a == "--tree-lod-test" || a == "--menu-display-test" || a == "--winter-runtime-audit" || a == "--winter-muted-settings-audit");
            NativeTelemetry.SetContext("verificationMode", verificationRun ? "true" : "false");
            if (verificationRun && Argument(args, "--save-directory") == null)
                args = args.Concat(new[] { "--save-directory", Path.Combine(Argument(args, "--report-dir")
                    ?? Path.Combine(Application.persistentDataPath, "verification"), "isolated-save") }).ToArray();
            NativeSaveStore.Initialize(args, key => verificationRun ? 0f : PlayerPrefs.GetFloat(key, 0));
            string importPath = Argument(args, "--import-save-exchange");
            if (!string.IsNullOrEmpty(importPath) && !NativeSaveExchange.Import(importPath))
                Debug.LogWarning("NATIVE_IMPORT_FAILED " + NativeSaveExchange.LastError);
            if (NativeSaveStore.SaveBlocked) Debug.LogWarning("NATIVE_SAVE_UNAVAILABLE " + NativeSaveStore.LastError);
            bool sessionSwitchTest = args.Contains("--session-switch-self-test");
            bool switchTest = args.Contains("--track-switch-self-test") || sessionSwitchTest;
            if (switchTest)
            {
                commandLineTrackApplied = true;
                if (switchTestStage == 0 && !string.Equals(trackId, "forest", StringComparison.OrdinalIgnoreCase)
                    && ChangeTrack("forest", false)) return;
            }
            else if (!commandLineTrackApplied)
            {
                commandLineTrackApplied = true;
                string requestedTrack = Argument(args, "--track") ?? NativeSaveStore.Data.preferredTrack;
                if (!string.IsNullOrEmpty(requestedTrack) && NativeTrackCatalog.IndexOf(requestedTrack) < 0)
                    Debug.LogError("NATIVE_TRACK_UNKNOWN " + requestedTrack);
                else if (!string.IsNullOrEmpty(requestedTrack)
                    && NativeTrackCatalog.IndexOf(requestedTrack) != NativeTrackCatalog.IndexOf(trackId))
                {
                    if (ChangeTrack(requestedTrack, false)) return;
                }
            }
            NativeRenderingQuality.Apply();
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / 60;
            Application.runInBackground = true;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            starts = racers.Select(r => r.transform.position).ToArray();
            headings = racers.Select(r => r.transform.rotation).ToArray();
            best = (float)NativeSaveStore.GetLegacyBest(trackId);
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.enabled = false;
            musicSource.clip = music;
            musicSource.loop = true;
            musicSource.volume = 0.23f;
            AudioInitialize(args, verificationRun && !args.Contains("--item-prefab-structure-diagnostic") && !args.Contains("--check-item-prefab-behaviors") && !args.Contains("--winter-runtime-audit") && !args.Contains("--winter-fresh190-self-test") && !(args.Contains("--music-cancel-scaled-self-test") && args.Contains("--music-ordinary-audio-output")) && !(args.Contains("--winter-snowman-contact-self-test") && args.Contains("--winter-ordinary-audio-output")) || args.Contains("--winter-muted-settings-audit"));
            hud.AttachAudio(gameObject.AddComponent<AudioSource>());
            selectedTrackId = trackId;
            if (vehicleRoster)
            {
                selectableCharacters = vehicleRoster.SelectableCharacters();
                hud.ConfigureChoiceCounts(selectableCharacters.Length, vehicleRoster.VehicleCount);
                hud.OriginalCharacterFocusSelected += OnOriginalCharacterFocusSelected;
                selectedCharacterIndex = preferredCharacterIndex >= 0
                    ? Mathf.Clamp(preferredCharacterIndex, 0, vehicleRoster.CharacterCount - 1)
                    : vehicleRoster.CharacterIndexOf(NativeSaveStore.Data.preferredCharacter);
                if (!selectableCharacters.Contains(selectedCharacterIndex)) selectedCharacterIndex = vehicleRoster.DefaultCharacterIndex;
                int savedVehicle = vehicleRoster.VehicleIndexOf(NativeSaveStore.Data.preferredVehicle);
                selectedVehicleIndex = preferredCharacterIndex >= 0 ? preferredVehicleIndex : Mathf.Max(0, savedVehicle);
                selectedVehicleIndex = Mathf.Clamp(selectedVehicleIndex, 0, vehicleRoster.VehicleCount - 1);
                selectedSkinIndex = vehicleRoster.ClampSkin(selectedCharacterIndex, preferredCharacterIndex >= 0 ? preferredSkinIndex : 0);
            }
            itemCombatTest = args.Contains("--items-race-self-test");
            selfTest = args.Contains("--race-self-test") || itemCombatTest;
            if (selfTest && !ReferenceEquals(releaseSmokePrevious, null))
            {
                completedTests = releaseSmokePrevious.completedTests;
                errorCount = releaseSmokePrevious.errorCount;
                testResults.AddRange(releaseSmokePrevious.testResults);
                itemMetrics.AddRange(releaseSmokePrevious.itemMetrics);
                aiMetrics.AddRange(releaseSmokePrevious.aiMetrics);
                releaseSmokePrevious = null;
            }
            if (selfTest && args.Contains("--race-test-capture-clock")) Time.captureDeltaTime = 1f / 60f;
            bool feedbackTest = args.Contains("--feedback-self-test");
            bool wheelTest = args.Contains("--wheel-self-test");
            bool accelerationTest = args.Contains("--acceleration-self-test");
            bool driftTest = args.Contains("--drift-self-test");
            bool airborneTest = args.Contains("--airborne-self-test");
            bool spinTest = args.Contains("--spin-self-test");
            bool particlesTest = args.Contains("--particles-self-test");
            bool surfaceParticlesTest = args.Contains("--surface-particles-self-test");
            bool steeringTest = args.Contains("--steering-self-test");
            bool cameraTest = args.Contains("--camera-self-test");
            bool originalAITest = args.Contains("--ai-original-self-test");
            bool recoveryTest = args.Contains("--recovery-self-test");
            bool hitTest = args.Contains("--hit-self-test");
            bool itemsTest = args.Contains("--items-self-test");
            bool itemCoverageTest = args.Contains("--item-coverage-self-test");
            bool itemLaunchTest = args.Contains("--item-launch-self-test");
            bool aiTest = args.Contains("--ai-self-test");
            bool animationActionsTest = args.Contains("--animation-actions-self-test");
            bool animationTest = args.Contains("--animation-self-test");
            bool finishCameraTest = args.Contains("--finish-camera-self-test");
            bool cameraModesTest = args.Contains("--camera-modes-self-test");
            bool collisionTest = args.Contains("--collision-self-test");
            bool clockTest = args.Contains("--clock-self-test");
            bool trajectoryTest = args.Contains("--trajectory-self-test");
            bool originalTrajectoryTest = args.Contains("--original-trajectory-self-test");
            bool wheelContactTest = args.Contains("--wheel-contact-self-test");
            bool treeLodTest = args.Contains("--tree-lod-test");
            bool mountainsTest = args.Contains("--mountains-self-test");
            bool trackContentTest = args.Contains("--track-content-self-test");
            bool vehicleCoverageTest = args.Contains("--vehicle-coverage-self-test");
            bool settingsRestartTest = args.Contains("--settings-restart-probe-self-test");
            bool inputProfileTest = args.Contains("--input-profile-self-test");
            bool settingsTest = args.Contains("--settings-self-test");
            automatedSettingsPresentation = settingsTest;
            bool ufoTest = args.Contains("--ufo-self-test");
            bool menuParityTest = args.Contains("--menu-parity-self-test");
            bool visualParityTest = args.Contains("--visual-parity-self-test");
            bool audioParityTest = args.Contains("--audio-parity-self-test");
            bool ceremonyTest = args.Contains("--ceremony-self-test");
            bool grandPrixTest = args.Contains("--grand-prix-self-test");
            bool quickRaceTest = args.Contains("--quick-race-self-test");
            bool customRaceTest = args.Contains("--custom-race-self-test");
            bool customRulesTest = args.Contains("--custom-rules-self-test");
            bool progressionTest = args.Contains("--progression-self-test");
            bool timeTrialsTest = args.Contains("--time-trials-self-test");
            bool frogTest = args.Contains("--frog-unlock-self-test");
            bool persistenceTest = args.Contains("--persistence-self-test");
            bool sessionTest = args.Contains("--session-self-test");
            bool persistenceProbe = args.Contains("--persistence-probe-self-test");
            visualTest = args.Contains("--visual-self-test") || switchTest || trackContentTest || vehicleCoverageTest || mountainsTest || treeLodTest || feedbackTest || wheelTest || steeringTest || wheelContactTest || accelerationTest || driftTest || airborneTest || spinTest || particlesTest || surfaceParticlesTest || trajectoryTest || originalTrajectoryTest || clockTest || collisionTest || cameraTest || cameraModesTest || finishCameraTest || animationTest || animationActionsTest || aiTest || itemsTest || itemCoverageTest || hitTest || recoveryTest || originalAITest;
            visualTest |= itemLaunchTest || visualParityTest || audioParityTest;
            visualTest |= persistenceTest || sessionTest || persistenceProbe;
            foreach (var racer in racers) racer.UseFixedVerificationClock = visualTest && !itemLaunchTest && !clockTest && !originalTrajectoryTest && !aiTest;
            hud.SuppressPointerInput = selfTest || visualTest || args.Contains("--menu-display-test");
            autopilot = selfTest || timeTrialsTest || grandPrixTest || quickRaceTest || args.Contains("--autodrive");
            reportDirectory = Argument(args, "--report-dir") ?? Path.Combine(Application.persistentDataPath, "verification");
            if (verificationRun) Directory.CreateDirectory(reportDirectory);
            if (selfTest && args.Contains("--motion-report"))
            {
                captureTestScreenshots = !args.Contains("--motion-no-screenshots");
                interpolateRendering = !args.Contains("--no-render-interpolation");
                int target;
                if (int.TryParse(Argument(args, "--target-framerate"), out target))
                    Application.targetFrameRate = target <= 0 ? -1 : Mathf.Clamp(target, 30, 120);
                int sync;
                if (int.TryParse(Argument(args, "--motion-vsync"), out sync)) QualitySettings.vSyncCount = Mathf.Clamp(sync, 0, 1);
                gameObject.AddComponent<NativeMotionAudit>().Configure(this, reportDirectory);
                gameObject.AddComponent<NativeFrameProbe>();
                NativeFrameProbe.Enable();
            }
            foreach (var racer in racers)
                racer.GetComponent<NativeKartHit>().ItemsFrozenChanged = racer.GetComponent<NativeKartItemInventory>().SetFrozen;
            for (int i = 0; i < racers.Length; i++)
            {
                int index = i;
                var recovery = racers[i].gameObject.AddComponent<NativeKartRecovery>();
                recovery.Initialize(() => CompleteRecovery(index), i == 0, hud.uiImageMaterial);
            }
            itemWorld = gameObject.AddComponent<NativeItemWorld>();
            InitializeModeFlow(args);
            NativeInputBindings.Initialize();
            if(!args.Contains("--menu2-source-lifecycle-self-test") && !args.Contains("--scaled-transform-self-test") && !args.Contains("--music-cancel-scaled-self-test") && !args.Contains("--main-quit-source-state-self-test") && !args.Contains("--main-quit-source-exit-observer-self-test"))InitializeMainQuitSource();
            itemWorld.Configure(GetComponent<NativeItemCatalog>(), racers, route);
            BindNaturalInsanityConsumer();
            ConfigureItemInventories();
            Application.logMessageReceived += CountErrors;
            ResetRace(false, false);
            SnapCamera();
            if (!verificationRun || args.Contains("--winter-runtime-audit") || args.Contains("--winter-fresh190-self-test") || args.Contains("--winter-snowman-contact-self-test") || timeTrialsTest || quickRaceTest || grandPrixTest || customRaceTest || customRulesTest || ceremonyTest)
                NativeUserSettings.ApplyScene(this);
            if (pendingRaceStart && string.Equals(pendingTrack, trackId, StringComparison.OrdinalIgnoreCase))
            {
                pendingRaceStart = false;
                pendingTrack = null;
                Begin();
            }
            BeginStartupSplash(args);
            NetworkInitialize();
            InitializePerformanceRecording(args);
            Debug.Log("NATIVE_READY " + SystemInfo.operatingSystem + " track=" + trackId + " route=" + route.Length + " gates=" + gates.Length);
            TelemetrySceneReady();
            if (args.Contains("--scaled-transform-self-test")) { selfTest = true; scaledTransformCheck = true; StartCoroutine(CheckScaledTransformNonrendered()); }
            else if (args.Contains("--music-cancel-scaled-self-test")) { selfTest = true; musicCancelScaledCheck = true; StartCoroutine(CheckMusicCancelScaledNonrendered()); }
            else if (args.Contains("--main-quit-source-state-self-test")) { selfTest = true; quitSourceNonrenderedCheck = true; StartCoroutine(CheckQuitSourceNonrendered()); }
            else if (args.Contains("--main-quit-source-exit-observer-self-test")) { selfTest = true; quitSourceExitObserver = true; StartCoroutine(CheckQuitSourceExitObserver()); }
            else if (args.Contains("--menu2-source-lifecycle-self-test")) { selfTest = true; menu2SourceLifecycleCheck = true; StartCoroutine(CheckMenu2SourceLifecycleNonrendered()); }
            else if (args.Contains("--winter-fresh190-self-test")) { selfTest = false; StartCoroutine(CheckWinterFresh190()); }
            else if (args.Contains("--winter-snowman-contact-self-test")) { selfTest = false; StartCoroutine(CheckWinterSnowmanContactPhysics()); }
            else if (args.Contains("--winter-muted-settings-audit")) { selfTest = false; StartCoroutine(CheckWinterMutedSettingsLive()); }
            else if (args.Contains("--winter-runtime-audit")) { selfTest = false; StartCoroutine(CheckWinterRuntimeLive()); }
#if ENABLE_INPUT_SYSTEM
            else if (args.Contains("--check-controller-menu")) StartCoroutine(CheckControllerMenu());
            else if (args.Contains("--check-quit-hold-ui")) StartCoroutine(CheckQuitHoldUi());
#endif
            else if (args.Contains("--check-online-menu-flow")) StartCoroutine(NativeRaceNetwork.Instance.CheckOnlineMenuFlow(reportDirectory));
            else if (args.Contains("--check-spectator-director")) StartCoroutine(NativeRaceNetwork.Instance.CheckSpectatorDirector(reportDirectory));
            else if (args.Contains("--check-broadcast-camera")) StartCoroutine(NativeRaceNetwork.Instance.CheckSpectatorDirector(reportDirectory));
            else if (args.Contains("--local-broadcast-demo")) NativeRaceNetwork.Instance.BeginLocalBroadcastDemo();
            else if (args.Contains("--check-settings-pointer")) StartCoroutine(CheckSettingsPointerLive());
            else if (args.Contains("--check-playerbox-selection")) { selfTest = false; StartCoroutine(CheckPlayerBoxSelectionLive()); }
            else if (args.Contains("--item-prefab-structure-diagnostic")) { selfTest=false; StartCoroutine(CheckStructureDiagnostic(false)); }
            else if (args.Contains("--ready-structure-diagnostic")) { selfTest=false; StartCoroutine(CheckStructureDiagnostic(true)); }
            else if (args.Contains("--check-item-prefab-behaviors")) { selfTest = false; StartCoroutine(CheckItemPrefabBehaviorsLive()); }
            else if (args.Contains("--check-original-sprite-bindings")) StartCoroutine(CheckOriginalSpriteBindingsLive());
            else if (args.Contains("--insanity-structure-self-test")) StartCoroutine(CheckInsanityStructureLive());
            else if (args.Contains("--module-world-fx-structure-self-test")) StartCoroutine(CheckModuleWorldFxStructureLive());
            else if (args.Contains("--check-insanity-natural")) { selfTest = true; StartCoroutine(CheckInsanityNaturalConsumersLive()); }
            else if (args.Contains("--module-world-fx-self-test")) StartCoroutine(CheckModuleWorldFxLive());
            else if ((args.Contains("--local-race-views-self-test") || args.Contains("--local-race-views-nonrendered-diagnostic"))) NativeLocalViewsVerifierHost.StartPersistent(CheckLocalRaceViewsLive());
            else if (args.Contains("--vehicle-module-hud-self-test")) StartCoroutine(CheckVehicleModuleHudLive());
            else if (args.Contains("--check-customization-pointer")) StartCoroutine(CheckCustomizationPointerLive());
            else if (args.Contains("--verify-item-character-emblems")) StartCoroutine(CheckItemCharacterEmblemsLive());
            else if (args.Contains("--check-circuit-banner-motion-nonrendered")) { selfTest=false; StartCoroutine(CheckCircuitBannerMotionNonrendered()); }
            else if (args.Contains("--check-circuit-banner-motion-nonrendered")) { selfTest=false; StartCoroutine(CheckCircuitBannerMotionNonrendered()); }
            else if (args.Contains("--check-local-player-selection")) { selfTest=false; StartCoroutine(CheckLocalSelectionLive()); }
            else if (args.Contains("--local-selection-nonrendered-diagnostic")) { selfTest=false; StartCoroutine(CheckLocalSelectionLive(true)); }
            else if (args.Contains("--check-character-profile")) { selfTest = false; StartCoroutine(CheckCharacterProfileLive()); }
            else if (args.Contains("--check-character-navigation")) { selfTest = false; StartCoroutine(CheckCharacterNavigationLive()); }
            else if (args.Contains("--hud-dynamics-self-test")) StartCoroutine(CheckOriginalHudDynamicsLive());
            else if (args.Contains("--text-spacing-self-test")) StartCoroutine(CheckTextSpacingLive());
            else if (args.Contains("--circuit-minimap-self-test")) StartCoroutine(CheckCircuitMinimapsLive());
            else if (args.Contains("--race-hud-restoration-self-test")) StartCoroutine(NativeRaceHudRestorationVerification.Run(this, reportDirectory));
            else if (args.Contains("--audio-events-audit")) StartCoroutine(CheckEventAudioLive());
            else if (args.Contains("--scoreboard-ui-self-test")) StartCoroutine(CheckScoreboardUiReplay());
            else if (args.Contains("--results-ui-self-test")) StartCoroutine(CheckResultsUiReplay(args));
            else if (visualParityTest) StartCoroutine(CheckVisualParityLive());
            else if (audioParityTest) StartCoroutine(CheckAudioParityLive());
            else if (settingsRestartTest) StartCoroutine(CheckSettingsRestartProbe(args));
            else if (inputProfileTest) StartCoroutine(CheckInputProfilesLive());
            else if (args.Contains("--chinese-localization-self-test")) StartCoroutine(CheckChineseLocalization());
            else if (settingsTest) StartCoroutine(CheckSettingsWithBackgroundPresentation(CheckSettingsLive()));
            else if (ufoTest) StartCoroutine(CheckUfoLive());
            else if (menuParityTest) StartMenuParityVerification();
            else if (ceremonyTest) StartCoroutine(CheckCeremonyReplay(args));
            else if (grandPrixTest) StartCoroutine(CheckGrandPrixLive());
            else if (quickRaceTest) StartCoroutine(CheckQuickRaceLive());
            else if (progressionTest) StartCoroutine(CheckProgressionRules());
            else if (customRaceTest) StartCoroutine(CheckCustomRaceLive());
            else if (customRulesTest) StartCoroutine(CheckCustomRulesLive());
            else if (timeTrialsTest) StartCoroutine(CheckTimeTrialsLive());
            else if (frogTest) StartCoroutine(CheckFrogTriggerLive());
            else if (persistenceTest) StartCoroutine(CheckPersistence());
            else if (sessionTest) Application.Quit(NativeSessionVerification.Run(reportDirectory) ? 0 : 2);
            else if (persistenceProbe) StartCoroutine(CheckPersistenceProbe(args));
            else if (sessionSwitchTest) StartCoroutine(CheckSessionSwitches());
            else if (switchTest) StartCoroutine(CheckTrackSwitches());
            else if (trackContentTest) StartCoroutine(NativeTrackContentVerification.Run(this, reportDirectory));
            else if (vehicleCoverageTest) StartCoroutine(NativeVehicleCoverageVerification.Run(this, reportDirectory));
            else if (args.Contains("--check-mountains-bridge-sweep")) StartCoroutine(NativeMountainsBridgeSweepVerification.Run(this, reportDirectory));
            else if (mountainsTest) StartCoroutine(NativeMountainsVerification.Run(this, reportDirectory));
            else if (originalAITest)
            {
                bool passed = NativeKartAIOriginalVerification.Run(Argument(args, "--ai-original-reference"), reportDirectory);
                Application.Quit(passed ? 0 : 2);
            }
            else if (recoveryTest) StartCoroutine(NativeKartRecoveryVerification.Run(this, reportDirectory));
            else if (hitTest) StartCoroutine(NativeKartHitVerification.Run(this, reportDirectory));
            else if (itemLaunchTest) StartCoroutine(NativeItemLaunchVerification.Run(this, reportDirectory));
            else if (itemCoverageTest) StartCoroutine(NativeItemCoverageVerification.Run(this, reportDirectory));
            else if (itemsTest) StartCoroutine(NativeKartItemsVerification.Run(this, reportDirectory));
            else if (aiTest) StartCoroutine(CheckAI());
            else if (animationActionsTest) StartCoroutine(NativeKartAnimationActionsVerification.Run(this, reportDirectory));
            else if (animationTest) StartCoroutine(NativeKartAnimationVerification.Run(this, reportDirectory));
            else if (finishCameraTest) StartCoroutine(NativeKartFinishCameraVerification.Run(this, reportDirectory));
            else if (cameraModesTest) StartCoroutine(NativeKartCameraModesVerification.Run(this, reportDirectory));
            else if (cameraTest) StartCoroutine(NativeKartCameraVerification.Run(this, reportDirectory));
            else if (collisionTest) StartCoroutine(NativeKartCollisionVerification.Run(this, reportDirectory));
            else if (clockTest) StartCoroutine(NativeKartClockVerification.Run(this, reportDirectory));
            else if (originalTrajectoryTest) StartCoroutine(NativeKartOriginalTraceReplay.Run(this, reportDirectory, Argument(args, "--original-trajectory-reference")));
            else if (trajectoryTest) StartCoroutine(NativeKartTrajectoryLiveVerification.Run(this, reportDirectory));
            else if (surfaceParticlesTest) StartCoroutine(CheckSurfaceParticles());
            else if (particlesTest) StartCoroutine(CheckParticles());
            else if (spinTest) StartCoroutine(CheckSpin(Argument(args, "--spin-reference")));
            else if (airborneTest) StartCoroutine(CheckAirborne(Argument(args, "--airborne-reference")));
            else if (driftTest) StartCoroutine(CheckDrift(Argument(args, "--drift-reference")));
            else if (accelerationTest) StartCoroutine(CheckAcceleration(Argument(args, "--acceleration-reference")));
            else if (wheelContactTest) StartCoroutine(CheckWheelContact());
            else if (steeringTest) StartCoroutine(NativeKartSteeringLiveVerification.Run(this, reportDirectory, Argument(args, "--steering-reference")));
            else if (feedbackTest) StartCoroutine(NativeKartFeedbackVerification.Run(this, reportDirectory));
            else if (wheelTest) StartCoroutine(NativeKartWheelVerification.Run(this, reportDirectory));
            else if (treeLodTest) StartCoroutine(NativeTreeLodVerification.Run(this, reportDirectory));
            else if (visualTest) StartCoroutine(NativeVisualVerification.Run(this, reportDirectory, Argument(args, "--visual-fixtures")));
            else if (selfTest && !releaseSmokeStarted)
            {
                releaseSmokeStarted = true;
                NativeLocalViewsVerifierHost.StartPersistent(StartTestFromMenu());
            }
            else if (args.Contains("--race-visual-audit")) StartCoroutine(CheckRaceVisualAudit());
            if (args.Contains("--menu-display-test")) StartCoroutine(DisplayTest());
            else if (args.Contains("--capture-menu")) StartCoroutine(CaptureMenu());
        }

        IEnumerator CheckAI()
        {
            ResetRace(true);
            SetVisualCapture("race", true);
            Time.timeScale = 1f;
            AllowCameraVerificationUpdates = true;
            for (int i = 1; i < racers.Length; i++)
            {
                racers[i].enabled = false;
                racers[i].GetComponent<CharacterController>().enabled = false;
            }
            var driver = racers[0].GetComponent<NativeKartOriginalAI>();
            driver.verificationSeed = 202625;
            driver.ResetAI();
            driver.active = true;
            racers[0].driving = true;
            yield return NativeKartAIVerification.Run(this, driver, reportDirectory,
                result => Application.Quit(result.pass && errorCount == 0 ? 0 : 2));
        }

        IEnumerator CheckSurfaceParticles()
        {
            var result = new NativeKartSurfaceParticlesVerification.Result();
            yield return NativeKartSurfaceParticlesVerification.Run(this, reportDirectory, result);
            bool passed = result.passed && errorCount == 0;
            Debug.Log("NATIVE_SURFACE_PARTICLES_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckParticles()
        {
            var result = new NativeKartParticlesVerification.Result();
            yield return NativeKartParticlesVerification.Run(this, reportDirectory, result);
            bool passed = result.passed && errorCount == 0;
            Debug.Log("NATIVE_PARTICLES_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckSpin(string referencePath)
        {
            bool referencePassed = NativeKartSpinVerification.RunReference(referencePath, reportDirectory);
            var result = new NativeKartSpinLiveVerification.Result();
            yield return NativeKartSpinLiveVerification.Run(this, reportDirectory, result);
            bool passed = referencePassed && result.passed && errorCount == 0;
            Debug.Log("NATIVE_SPIN_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckAirborne(string referencePath)
        {
            bool referencePassed = NativeKartAirborneVerification.RunReference(referencePath, reportDirectory);
            var result = new NativeKartAirborneLiveVerification.Result();
            yield return NativeKartAirborneLiveVerification.Run(this, reportDirectory, result);
            bool passed = referencePassed && result.passed && errorCount == 0;
            Debug.Log("NATIVE_AIRBORNE_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckDrift(string referencePath)
        {
            bool referencePassed = NativeKartDriftVerification.RunReference(referencePath, reportDirectory);
            var result = new NativeKartDriftLiveVerification.Result();
            yield return NativeKartDriftLiveVerification.Run(this, reportDirectory, result);
            bool passed = referencePassed && result.passed && errorCount == 0;
            Debug.Log("NATIVE_DRIFT_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckAcceleration(string referencePath)
        {
            bool referencePassed = NativeKartAccelerationVerification.RunReference(referencePath, reportDirectory);
            var result = new NativeKartAccelerationLiveVerification.Result();
            yield return NativeKartAccelerationLiveVerification.Run(this, reportDirectory, result);
            bool passed = referencePassed && result.passed && errorCount == 0;
            Debug.Log("NATIVE_ACCELERATION_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        IEnumerator CheckWheelContact()
        {
            var result = new NativeKartWheelContactVerification.Result();
            yield return NativeKartWheelContactVerification.Run(this, reportDirectory, result);
            bool passed = result.passed && errorCount == 0;
            Debug.Log("NATIVE_WHEEL_CONTACT_RESULT passed=" + passed);
            Application.Quit(passed ? 0 : 2);
        }

        void CaptureTestScreenshot(string name)
        {
            if (!captureTestScreenshots) return;
            DiagnosticCaptureFrame = Time.frameCount;
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, name));
        }

        IEnumerator CheckTrackSwitches()
        {
            Directory.CreateDirectory(reportDirectory);
            yield return null;
            if (switchChecks == null) switchChecks = new List<string>();
            bool valid = SceneManager.sceneCount == 1
                && SceneManager.GetActiveScene().name == SceneForTrack(trackId)
                && NativeTrackCatalog.IndexOf(trackId) == (switchTestStage == NativeTrackCatalog.Count ? 0 : switchTestStage)
                && FindObjectsOfType<NativeRace>().Length == 1
                && (switchTestStage == 0 || switchPreviousRace == null)
                && route != null && route.Length > 1 && gates != null && gates.Length > 0
                && AudioListener.volume == 0f && Mathf.Approximately(Time.timeScale, 1f)
                && errorCount == 0;
            switchChecks.Add(trackId + ": scene=" + SceneManager.GetActiveScene().name
                + " route=" + (route == null ? 0 : route.Length)
                + " gates=" + (gates == null ? 0 : gates.Length)
                + " oldDestroyed=" + (switchTestStage == 0 || switchPreviousRace == null)
                + " passed=" + valid);
            switchChecksPassed &= valid;
            if (switchTestStage < NativeTrackCatalog.Count && valid)
            {
                if (switchTestStage > 0)
                {
                    Begin();
                    ResetRace(false);
                    bool resetValid = phase == Phase.Menu && progress != null
                        && progress.Length == racers.Length && itemHud && menuPresentation && menuPresentation.IsVisible;
                    switchChecksPassed &= resetValid;
                    switchChecks.Add(trackId + " rematch-reset passed=" + resetValid);
                }
                OpenQuickRaceFromMenu();
                int nextIndex = (switchTestStage + 1) % NativeTrackCatalog.Count;
                hud.SelectMenuIndex(nextIndex);
                bool menuValid = hud.MenuPage == NativeMenuPage.Tracks && hud.SelectedMenuIndex == nextIndex;
                switchChecksPassed &= menuValid;
                switchChecks.Add("track-menu " + NativeTrackCatalog.IdAt(nextIndex) + " selected=" + menuValid);
                if (switchTestStage == 0)
                {
                    yield return new WaitForSecondsRealtime(0.2f);
                    yield return new WaitForEndOfFrame();
                    CaptureTestScreenshot("track-selection.png");
                    yield return new WaitForSecondsRealtime(0.2f);
                }
                switchPreviousRace = this;
                switchTestStage++;
                if (NativeContent.CanLoadScene(NativeTrackCatalog.SceneAt(nextIndex)))
                {
                    ConfirmCurrentTrackSelection();
                    yield break;
                }
                switchChecksPassed = false;
            }
            var report = new TrackSwitchReport { passed = switchChecksPassed && valid && switchTestStage == NativeTrackCatalog.Count,
                scenes = switchChecks.ToArray(), errors = errorCount };
            File.WriteAllText(Path.Combine(reportDirectory, "track-switch-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_TRACK_SWITCH_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }

        IEnumerator CaptureMenu()
        {
            Directory.CreateDirectory(reportDirectory);
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("menu.png");
            Debug.Log("NATIVE_MENU_RENDERED frame=" + Time.frameCount);
        }

        IEnumerator DisplayTest()
        {
            yield return CaptureMenu();
            ValidateMenuState(true);
            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("NATIVE_MENU_DISPLAY_TEST_RESULT errors=" + errorCount);
            Application.Quit(errorCount == 0 ? 0 : 2);
        }

        void OpenQuickRaceFromMenu()
        {
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Main));
            hud.SelectMenuIndex(1);
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.RaceModes));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Characters));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Vehicles));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Skins));
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Cups));
        }

        IEnumerator StartTestFromMenu()
        {
            yield return CaptureMenu();
            ValidateMenuState(true);
            hud.NavigateMenu(NativeMenuPage.Main, 1);
            if (hud.SelectedMenuIndex != 1) Debug.LogError("NATIVE_UI_NAVIGATION_INVALID main");
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("menu-selected.png");
            hud.NavigateMenu(NativeMenuPage.Main, -1);
            OpenQuickRaceFromMenu();
            if (hud.MenuPage != NativeMenuPage.Tracks) Debug.LogError("NATIVE_UI_NAVIGATION_INVALID tracks");
            hud.SelectMenuIndex(Mathf.Max(0, NativeTrackCatalog.IndexOf(trackId)));
            releaseSmokePrevious = this;
            ConfirmCurrentTrackSelection();
            yield return null;
            var current = UnityEngine.Object.FindObjectOfType<NativeRace>();
            if (!current || ReferenceEquals(current, this))
                throw new InvalidOperationException("Release test did not replace the scene instance");
            yield return current.CheckSmokePauseResume();
        }

        IEnumerator CheckSmokePauseResume()
        {
            ValidateMenuState(false);
            Pause();
            float pausedCountdown = countdown;
            var pausedPosition = racers[0].transform.position;
            hud.NavigateMenu(NativeMenuPage.Pause, 1);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("pause.png");
            bool stopped = phase == Phase.Paused && Time.timeScale == 0 && countdown == pausedCountdown
                && racers[0].transform.position == pausedPosition && hud.SelectedMenuIndex == 1;
            if (string.Equals(trackId, "forest", StringComparison.OrdinalIgnoreCase)) yield return CapturePyramid();
            hud.NavigateMenu(NativeMenuPage.Pause, -1);
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Pause));
            if (!stopped || phase != Phase.Countdown || Time.timeScale != 1)
                Debug.LogError("NATIVE_UI_PAUSE_INVALID");
            else Debug.Log("NATIVE_UI_FLOW_VERIFIED navigation,pause,resume");
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("countdown.png");
        }

        IEnumerator CapturePyramid()
        {
            captureDetail = true;
            Vector3 position = raceCamera.transform.position;
            Quaternion rotation = raceCamera.transform.rotation;
            float fov = raceCamera.fieldOfView;
            raceCamera.transform.position = new Vector3(-246, 3, 81);
            raceCamera.transform.LookAt(new Vector3(-254.53f, 1.2f, 95.50f));
            raceCamera.fieldOfView = 50;
            yield return new WaitForSecondsRealtime(0.2f);
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("pyramid.png");
            yield return null;
            raceCamera.transform.SetPositionAndRotation(position, rotation);
            raceCamera.fieldOfView = fov;
            captureDetail = false;
        }

        void ValidateMenuState(bool visible)
        {
            var presentation = menuPresentation;
            bool valid = presentation && presentation.IsVisible == visible
                && presentation.content.activeSelf == visible && presentation.menuCamera.enabled == visible
                && raceCamera.enabled != visible
                && !musicSource.enabled && !musicSource.isPlaying
                && AudioListener.volume == 0f
                // NativeAudioRuntime owns current music; the retired source stays silent.
                && presentation.soundtrack == null && NativeAudioRuntime.VerificationSilent();
            if (valid)
                for (int i = 0; i < presentation.raceLights.Length; i++)
                    if (presentation.raceLights[i] && presentation.raceLights[i].enabled != (!visible && presentation.raceLightEnabled[i])) valid = false;
            if (!valid) Debug.LogError("NATIVE_MENU_STATE_INVALID visible=" + visible);
            else Debug.Log("NATIVE_MENU_STATE_VERIFIED visible=" + visible);
        }

        IEnumerator ReturnToMenuAndRestart()
        {
            hud.NavigateMenu(NativeMenuPage.Results, 1);
            if (hud.SelectedMenuIndex != 1) Debug.LogError("NATIVE_UI_NAVIGATION_INVALID results");
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Results));
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("menu-returned.png");
            ValidateMenuState(true);
            OpenQuickRaceFromMenu();
            hud.SelectMenuIndex(Mathf.Max(0, NativeTrackCatalog.IndexOf(trackId)));
            releaseSmokePrevious = this;
            ConfirmCurrentTrackSelection();
            yield return null;
            var current = UnityEngine.Object.FindObjectOfType<NativeRace>();
            if (!current || ReferenceEquals(current, this))
                throw new InvalidOperationException("Release restart did not replace the scene instance");
            current.ValidateMenuState(false);
            Debug.Log("NATIVE_UI_FLOW_VERIFIED results,menu,restart");
        }

        void ConfirmCurrentTrackSelection()
        {
            ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Tracks));
        }

        static string Argument(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string SceneForTrack(string value)
        {
            int index = NativeTrackCatalog.IndexOf(value);
            return index < 0 ? null : NativeTrackCatalog.SceneAt(index);
        }

        bool ChangeTrack(string selectedTrack, bool startRace)
        {
            string scene = SceneForTrack(selectedTrack);
            if (string.IsNullOrEmpty(scene) || !NativeContent.CanLoadScene(scene))
            {
                NativeTelemetry.Event("scene.load", "rejected", "scene-unavailable", selectedTrack);
                Debug.LogError("NATIVE_TRACK_SCENE_MISSING " + scene);
                return false;
            }
            pendingTrack = NativeTrackCatalog.IdAt(NativeTrackCatalog.IndexOf(selectedTrack));
            pendingRaceStart = startRace;
            Time.timeScale = 1f;
            Debug.Log("NATIVE_TRACK_LOAD " + selectedTrack + " scene=" + scene + " start=" + startRace);
            NativeItemSceneLoading.Begin(scene);
            string telemetryOperation = NativeTelemetry.Begin("scene.load", scene);
            try
            {
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            }
            catch
            {
                NativeTelemetry.End(telemetryOperation, "scene.load", "failed", "load-threw");
                throw;
            }
            NativeTelemetry.End(telemetryOperation, "scene.load", "completed", "engine-load-returned");
            return true;
        }

        void CountErrors(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errorCount++;
        }

        void OnDestroy() { NetworkFinishSourceCircuit(); DisposeLocalRaceViews(false); DisposeLocalSelection(); ClearLocalSelectionPreviews(); DestroyMainQuitSource(); DisposeSourceState(); ClearSelectionPreview(); hud?.SetMenu2SourceScenePresent(false); hud?.DisposeAuthoredUi(); Application.logMessageReceived -= CountErrors; Time.timeScale = 1; TelemetrySceneDisposed(); }

        internal void ResetRace(bool start, bool cancelSession = true)
        {
            if (!start && cancelSession && NativeGameSession.Current != null)
            {
                NativeGameSession.Cancel(NativeGameSession.Current.sessionId);
                sessionId = null;
            }
            if (!start && sourceRaceState!=null && sourceRaceState.InRace) sourceRaceState.Cleanup();
            if (start) { ConfigureSourceHumans(); StartSourceCircuit(); }
            Time.timeScale = 1;
            phase = start ? Phase.Countdown : Phase.Menu;
            NativeTelemetry.Event("race.reset", "applied", detail: start ? "countdown" : "menu");
            hud.SetMenuPage(start ? NativeMenuPage.None : NativeMenuPage.Main);
            heldMenuDirection = 0;
            if (menuPresentation) menuPresentation.SetVisible(!start);
            NativeAudioRuntime.StopCountdown();
            countdownAudioSecond = -1;
            countdown = 3;
            previousVerticalInput = 0f;
            elapsed = 0;
            ApplyCustomMotorRules();
            ResetModeProgress();
            if (itemWorld) itemWorld.ResetWorld();
            capturedMid = capturedFinish = false;
            finishTimes = new float[racers.Length];
            feedbackSamples = new FeedbackSample[racers.Length];
            progress = new NativeCircuitProgress[racers.Length];
            if (itemHud) itemHud.ResetState();
            hud.ResetOriginalRaceDynamics();
            originalWrongWayTime = 0;
            foreach (var itemBox in FindObjectsOfType<NativeKartItemBox>()) itemBox.ResetBox();
            for (int i = 0; i < racers.Length; i++)
            {
                var recovery = racers[i].GetComponent<NativeKartRecovery>();
                if (recovery) recovery.ResetState();
                var hit = racers[i].GetComponent<NativeKartHit>();
                if (hit) hit.ResetState();
                var inventory = racers[i].GetComponent<NativeKartItemInventory>();
                if (inventory) inventory.ResetItems();
                racers[i].ResetAt(starts[i], headings[i], false);
                racers[i].driving = false;
                var driver = racers[i].GetComponent<NativeKartOriginalAI>();
                driver.active = false;
                driver.racerIndex = i;
                driver.verificationSeed = selfTest ? 202625 + i : 0;
                driver.ResetAI();
                NativeNormalCpuAudit.Configure(driver, trackId, Environment.GetCommandLineArgs(), visualTest);
                racers[i].throttle = racers[i].steering = 0;
                racers[i].braking = racers[i].drifting = false;
                foreach (var wheels in racers[i].GetComponentsInChildren<NativeKartWheelVisual>(true)) wheels.ResetState();
                var actions = racers[i].GetComponent<NativeKartAnimationActions>();
                if (actions) actions.ResetActions();
                var animation = racers[i].GetComponent<NativeKartAnimation>();
                if (animation) animation.ResetPlayback();
                feedbackSamples[i] = new FeedbackSample();
                progress[i] = new NativeCircuitProgress(gates.Select(g => g.position).ToArray(), RaceLaps, Net && Net.LocalBroadcastDemo && NativeCameraLab.TrafficEnabled);
            }
            InitializeSourceRanking();
            if (!start && !visualTest && !selfTest)
                for (int i = 1; i < racers.Length; i++) racers[i].gameObject.SetActive(false);
            SnapCamera();
            TelemetryObserveState();
        }

        void Begin()
        {
            EnsureSession();
            PrepareModeRoster(NativeGameSession.Current.config);
            foreach (var racer in racers) racer.GetComponent<NativeKartItemInventory>().ResetItems();
            if (vehicleRoster)
            {
                vehicleRoster.ApplySelection(selectedCharacterIndex, selectedVehicleIndex, selectedSkinIndex, racers[0]);
                racerNames[0] = vehicleRoster.CharacterNameAt(selectedCharacterIndex) + " / YOU";
                hud.SetPlayerCharacterIcon(vehicleRoster.CharacterIconAt(selectedCharacterIndex));
            }
            ConfigureItemInventories();
            ResetRace(true);
            NativeGameSession.Begin(sessionId, trackId);
            RaiseOriginalRaceStarted();
            NativeTelemetry.Event("race.countdown.started", "accepted", detail: trackId);
            Debug.Log("NATIVE_COUNTDOWN track=" + trackId + " character=" + racerNames[0]
                + " vehicle=" + (vehicleRoster ? vehicleRoster.VehicleNameAt(selectedVehicleIndex) : "default"));
        }

        internal void SetVisualCapture(string page, bool showHud)
        {
            Time.timeScale = 0;
            captureDetail = !showHud;
            phase = page == "menu" ? Phase.Menu : page == "pause" ? Phase.Paused : page == "finish" ? Phase.Finished : Phase.Racing;
            hud.SetMenuPage(page == "menu" ? NativeMenuPage.Main : page == "pause" ? NativeMenuPage.Pause : NativeMenuPage.None);
            menuPresentation.SetVisible(page == "menu");
            if (page != "menu") foreach (var racer in racers) racer.gameObject.SetActive(true);
        }

        void Update()
        {
            TelemetryObserveState();
            SynchronizeSharedMenuInput();
            UpdateMainQuitSource();
            if(scaledTransformCheck || musicCancelScaledCheck || quitSourceNonrenderedCheck || quitSourceExitObserver){hud.FinalizeCompletedMenu2SourceTransitionForCheck();return;}
            if(menu2SourceLifecycleCheck){hud.FinalizeCompletedMenu2SourceTransitionForCheck();return;}
            if(sourceRanking!=null)sourceRanking.Update();
            if (StartupSplashActive)
            {
                if (Input.GetKeyDown(KeyCode.Z) || NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok)) startupSplash.Skip();
                return;
            }
            if (NetworkUpdate()) return;
            TickSourceState();
            AudioContextTick();
            TickOriginalWrongWayHud();
            TickSelectionPreview();
            if (ceremony) return;
            if (progress == null || visualTest) return;
            if (hud.MenuTransitionActive) return;
            RecordModeFrame();
            if (HandleSettingsInput()) return;
            if (!selfTest && phase == Phase.Menu) HandleMenuInput();
            if (SourceHumanDrivingEnabled(0)) ApplyCameraInput(NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.LookBehind), NativeInputBindings.GetButtonDown(NativeInputBindings.Action.ChangeView));
            if (phase == Phase.Paused || phase == Phase.Menu) return;
            if (phase == Phase.Countdown)
            {
                countdown = (float)(countdownDeadline - Time.timeSinceLevelLoadAsDouble);
                int second = Mathf.Max(0, Mathf.CeilToInt(countdown));
                if (second != countdownAudioSecond)
                {
                    countdownAudioSecond = second;
                    NativeAudioRuntime.PlayCountdown(second);
                }
                if (countdown <= 0)
                {
                    phase = Phase.Racing;
                    TelemetryObserveState();
                    NativeTelemetry.Event("race.started", "applied");
                    raceClock.StartAt(countdownDeadline);
                    sourceRanking.StartedRace();
                    SourceTimersStartedRace();
                    foreach (var racer in racers) racer.driving = true;
                    Debug.Log("NATIVE_RACE_STARTED");
                    if (selfTest) CaptureTestScreenshot("start.png");
                }
                return;
            }
            if (phase == Phase.Racing)
            {
                if (SourceHumanDrivingEnabled(0) && Input.GetKeyDown(KeyCode.R)) Recover(0, true, "manual");
                if (!autopilot && SourceHumanDrivingEnabled(0))
                {
                    var player = racers[0];
                    player.throttle = NativeInputBindings.GetAxis(NativeInputBindings.Action.Accelerate) * 0.5f;
                    player.steering = NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal);
                    player.braking = NativeInputBindings.GetAxis(NativeInputBindings.Action.Brake) > 0.5f;
                    player.drifting = player.throttle >= 0.4f && player.braking;
                    player.jump |= NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Jump);
                    player.jumpHeld = NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Jump);
                    float verticalInput = NativeInputBindings.GetAxis(NativeInputBindings.Action.Vertical);
                    if (NativeUserSettings.GetInt("alternativeJumping") == 1 && verticalInput >= 0.75f
                        && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Brake)) player.jump = true;
                    if (verticalInput != previousVerticalInput)
                    {
                        player.WheelieInputVertical(verticalInput);
                        previousVerticalInput = verticalInput;
                    }
                    if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Use)) UsePlayerItem();
                }
            }
            if ((phase == Phase.Racing || phase == Phase.Finished) && progress.Any(p => !p.Finished)) elapsed = (float)raceClock.Elapsed;
            ApplySourceAdditionalHumanInput();
            for (int i = 0; i < racers.Length; i++)
            {
                var driver = racers[i].GetComponent<NativeKartOriginalAI>();
                if (sourceRaceState != null)
                {
                    driver.active = SourceAiShouldDrive(i);
                    racers[i].driving = sourceRaceState.InRace;
                }
                TickRacerEmergencyRecovery(i,driver.active);
            }
        }

        internal static void ApplyPlayerDrivingInput(NativeKartMotor player, bool accelerate, bool left, bool right, bool brake)
        {
            // Original PlayerRacingController.OnAccelerate scales the action value by 0.5 (RVA 0x338EB8).
            player.throttle = accelerate ? 0.5f : 0f;
            player.steering = (right ? 1 : 0) - (left ? 1 : 0);
            player.braking = brake;
            player.drifting = player.throttle >= 0.4f && player.braking;
        }

        internal void ApplyCameraInput(bool behind, bool changeView)
        {
            if (!cameraRig || Time.timeScale <= 0f || (phase != Phase.Racing && phase != Phase.Countdown)) return;
            cameraRig.SetLookBehind(behind);
            if (changeView) cameraRig.ToggleView();
        }

        void SampleRace()
        {
            if (phase == Phase.Menu || phase == Phase.Paused || phase == Phase.Countdown) return;
            if (selfTest && (phase == Phase.Racing || phase == Phase.Finished))
            {
                for (int i = 0; i < racers.Length; i++)
                {
                    var feedback = racers[i].GetComponent<NativeKartFeedback>();
                    if (!feedback) continue;
                    var sample = feedbackSamples[i];
                    if (racers[i].OnGrass) sample.grassFrames++;
                    if (racers[i].FeedbackInput.hasSurface) sample.surfaceFrames++;
                    sample.maxDirt = Mathf.Max(sample.maxDirt, feedback.State.dirt);
                    sample.maxWheelDirt = Mathf.Max(sample.maxWheelDirt, feedback.State.wheelDirt);
                    sample.maxSpeedRatio = Mathf.Max(sample.maxSpeedRatio, racers[i].FeedbackInput.speedRatio);
                    sample.maxHeat = Mathf.Max(sample.maxHeat, feedback.State.heat);
                    sample.maxWheelHeat = Mathf.Max(sample.maxWheelHeat, feedback.State.wheelHeat);
                }
                if (!capturedMid && progress[0].Passed > gates.Length / 2)
                {
                    capturedMid = true;
                    CaptureTestScreenshot("mid-race.png");
                }
                if (elapsed > (string.Equals(trackId, "forest", StringComparison.OrdinalIgnoreCase) ? 180f : 360f) * RaceLaps)
                    CompleteTest(false, "timeout track=" + trackId);
            }
            if (selfTest && Time.unscaledTime - lastTelemetry > 2)
            {
                lastTelemetry = Time.unscaledTime;
                File.WriteAllText(Path.Combine(reportDirectory, "live.json"),
                    JsonUtility.ToJson(new Telemetry { trackId = trackId, phase = phase.ToString(), elapsed = elapsed,
                        checkpoint = progress[0].Passed, speed = racers[0].Speed,
                        position = racers[0].transform.position, resets = racers[0].Resets }, true));
            }
            if (selfTest && phase == Phase.Finished && progress.All(p => p.Finished) && !capturedFinish)
            {
                capturedFinish = true;
                StartCoroutine(FinishTest());
            }
        }

        internal void CrossCircuitGate(NativeKartMotor motor, int gate, bool forward)
        {
            if (NativeRaceNetwork.ClientRace) return;
            if (visualTest || progress == null || (phase != Phase.Racing && phase != Phase.Finished)) return;
            int i = Array.IndexOf(racers, motor);
            if (i < 0 || Time.frameCount <= motor.LastResetFrame + 1) return;
            int lap = progress[i].CompletedLaps;
            if (progress[i].Cross(gate, forward))
            {
                if (progress[i].CompletedLaps != lap) SourceCountLap(i, progress[i].CompletedLaps);
                if (progress[i].CompletedLaps > lap)
                {
                    double time = raceClock.Elapsed;
                    lapSeconds[i].Add(time - lapTimestamps[i]); lapTimestamps[i] = time;
                    if(i==0)hud.NotifyOriginalLap();
                    NotifyLocalViewLap(i);
                    if (progress[i].Finished)
                    {
                        SourceNaturalGateFinish(i);
                        if(i==0&&itemHud)itemHud.FreezeForLocalFinisher();
                    }
                    SourceRefreshLapTime(i);
                    Debug.Log("NATIVE_LAP racer=" + i + " lap=" + progress[i].CompletedLaps + " seconds=" + time.ToString("R"));
                }
                if (i == 0) Debug.Log("NATIVE_CHECKPOINT " + progress[i].Passed + "/" + gates.Length);
            }
        }

        void SourceNaturalGateFinish(int i)
        {
            if (Net && Net.InRace) { if(finishTimes[i]<=0) { finishSeconds[i]=raceClock.Elapsed; finishTimes[i]=(float)finishSeconds[i]; sourceRanking?.RecordFinish(i); } return; }
            finishSeconds[i]=SourceTotalSeconds(i);
            finishTimes[i]=(float)finishSeconds[i];
            RecordSourceNaturalFinish(i);
            NativeTelemetry.Event("race.racer.finished", "completed", detail: "racer=" + i + ";seconds=" + finishSeconds[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Debug.Log("NATIVE_FINISH " + racerNames[i] + " time=" + finishSeconds[i] + " distance=" + racers[i].Distance);
            if(i==0&&selectedMode==NativeSessionMode.NativeRace&&!verificationRun&&!autopilot&&(best<=0||finishSeconds[i]<best))
            {
                if(NativeSaveStore.RecordLegacyBest(trackId,(float)finishSeconds[i]))
                {
                    best=(float)finishSeconds[i];
                    if(!NativeSaveStore.Save())Debug.LogWarning("NATIVE_SAVE_FAILED " + NativeSaveStore.LastError);
                }
            }
        }
        void UpdateProgress()
        {
            if(visualTest||progress==null||(phase!=Phase.Racing&&phase!=Phase.Finished))return;
            while(finishArrivalQueue.Count>0)
            {
                int i=finishArrivalQueue.Dequeue();
                if(progress[i].Finished&&!sourceLapTimers[i].Ended)SourceNaturalGateFinish(i);
            }
        }

        void Recover(int index, bool manual, string reason)
        {
            var motor = racers[index];
            if (motor.GetComponent<NativeKartRecovery>().Request(manual))
                Debug.Log("NATIVE_RECOVERY_REQUEST track=" + trackId + " racer=" + index + " reason=" + reason
                    + " checkpoint=" + progress[index].Passed + " position=" + motor.transform.position);
        }
        void TickRacerEmergencyRecovery(int index,bool aiControlled)
        {
            // Same source rule for offline and authoritative online simulation.
            float floor=string.Equals(trackId,"wintermistylake",StringComparison.OrdinalIgnoreCase)?-300f:-15f;
            var motor=racers[index];var driver=motor.GetComponent<NativeKartOriginalAI>();
            if(motor.transform.position.y<floor)Recover(index,false,"deep-fall");
            else if(motor.enabled && aiControlled && driver.ConsumeStuckRequest())Recover(index,false,"ai-stuck");
        }

        void CompleteRecovery(int index)
        {
            int checkpoint = progress[index].PrepareRecovery();
            var gate = gates[checkpoint];
            Vector3 forward = Vector3.ProjectOnPlane(gate.forward, Vector3.up).normalized;
            racers[index].TeleportRecovery(gate.position, Quaternion.LookRotation(forward));
            NativeTelemetry.Event("vehicle.teleported", "applied", detail: "racer=" + index + ";checkpoint=" + checkpoint);
            racers[index].GetComponent<NativeKartOriginalAI>().RecoverFromCheckpoint(gate.position);
        }

        void Pause()
        {
            EnsureSourceRaceState();
            sourceRaceState.TryPause(sourceRaceState.Humans[0].InputUserIndex);
        }

        void Resume()
        {
            EnsureSourceRaceState();
            sourceRaceState.TryUnPause(sourceRaceState.PausedBy);
        }

        void HandleMenuInput()
        {
            if (sourcePauseSubmitFrame == Time.frameCount) return;
            if (phase == Phase.Paused || phase == Phase.Finished) return;
            if (hud.MenuTransitionActive || HandleSettingsInput()) return;
            if (HandleLocalSelectionInput() || HandleCharacterProfileInput() || HandleFullModeInput()) return;
            var page = phase == Phase.Menu ? hud.MenuPage : phase == Phase.Paused ? NativeMenuPage.Pause : NativeMenuPage.Results;
            Vector2 navigation = NativeInputBindings.GetVector();
            bool sourceStartedOnly = page == NativeMenuPage.Characters || page == NativeMenuPage.Vehicles || page == NativeMenuPage.Skins;
            bool navigationStarted = sourceStartedOnly && NativeInputBindings.GetNavigateStarted(out navigation);
            int originalDirection = NativePlayerBoxNavigation.Direction(navigation);
            int direction = page == NativeMenuPage.Vehicles || page == NativeMenuPage.Skins ? NativePlayerBoxNavigation.Step(navigation)
                : page == NativeMenuPage.Characters ? originalDirection < 0 ? 0 : originalDirection == 0 ? -1 : originalDirection == 2 ? 1 : originalDirection == 1 ? 2 : -2
                : Mathf.Abs(navigation.y) > .5f ? navigation.y > 0 ? -1 : 1
                : page == NativeMenuPage.Tracks ? navigation.x > .5f ? 2 : navigation.x < -.5f ? -2 : 0 : 0;
            bool started = sourceStartedOnly ? navigationStarted && direction != 0 : direction != 0 && direction != heldMenuDirection;
            if (started || ((page == NativeMenuPage.Main || page == NativeMenuPage.RaceModes || page == NativeMenuPage.Tracks
                || page == NativeMenuPage.Extras)
                && direction != 0 && Time.unscaledTime >= nextMenuRepeat))
            {
                if (page == NativeMenuPage.Characters || page == NativeMenuPage.Tracks || page == NativeMenuPage.Vehicles || page == NativeMenuPage.Skins)
                    hud.NavigateOriginalChoice(page, navigation, vehicleRoster, selectableCharacters);
                else hud.NavigateMenu(page, direction);
                if (page == NativeMenuPage.Vehicles) selectedVehicleIndex = hud.SelectedMenuIndex;
                if (page == NativeMenuPage.Skins) selectedSkinIndex = hud.SelectedMenuIndex;
                nextMenuRepeat = Time.unscaledTime + (started ? 0.5f : 0.1f);
            }
            heldMenuDirection = direction;
            if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok) || Input.GetKeyDown(KeyCode.Return))
            {
                ExecuteMenuAction(hud.ConfirmMenu(page));
                return;
            }
            if (page != NativeMenuPage.Main && (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Cancel) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)))
                ExecuteMenuAction(hud.CancelMenu(page));
        }

        void ExecuteMenuAction(NativeMenuAction action)
        {
            if (action != NativeMenuAction.None) NativeTelemetry.Event("ui.action", "accepted", detail: action.ToString());
            if (ExecuteFullModeAction(action)) return;
            switch (action)
            {
                case NativeMenuAction.Settings: OpenSettings(); break;
                case NativeMenuAction.StartRace:
                    preferredCharacterIndex = selectedCharacterIndex;
                    preferredVehicleIndex = selectedVehicleIndex;
                    preferredSkinIndex = selectedSkinIndex;
                    NativeSaveStore.SetPreferences(selectedTrackId, vehicleRoster.CharacterIdAt(selectedCharacterIndex), vehicleRoster.VehicleIdAt(selectedVehicleIndex));
                    if (!NativeSaveStore.Save()) Debug.LogWarning("NATIVE_SAVE_FAILED " + NativeSaveStore.LastError);
                    EnsureSession();
                    ChangeTrack(selectedTrackId, true);
                    break;
                case NativeMenuAction.OpenTracks:
                    hud.SetMenuPage(NativeMenuPage.Tracks);
                    hud.SelectMenuIndex(Mathf.Max(0, NativeTrackCatalog.IndexOf(trackId)));
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.TimeTrials:
                    selectedMode = NativeSessionMode.TimeTrials;
                    hud.SetMenuPage(NativeMenuPage.Tracks);
                    hud.SelectMenuIndex(Mathf.Max(0, NativeTrackCatalog.IndexOf(trackId)));
                    break;
                case NativeMenuAction.OpenCharacters:
                    if (!vehicleRoster) { Debug.LogError("NATIVE_ROSTER_MISSING"); break; }
                    hud.SetMenuPage(NativeMenuPage.Characters);
                    hud.SelectMenuIndex(Mathf.Max(0, Array.IndexOf(selectableCharacters, selectedCharacterIndex)));
                    EnterOriginalCharacterSelection();
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.OpenVehicles:
                    if (selectedCharacterIndex != selectableCharacters[hud.SelectedMenuIndex]) selectedSkinIndex = 0;
                    selectedCharacterIndex = selectableCharacters[hud.SelectedMenuIndex];
                    hud.VehicleCharacterSelection = selectedCharacterIndex;
                    hud.SetMenuPage(NativeMenuPage.Vehicles);
                    hud.SelectMenuIndex(selectedVehicleIndex);
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.OpenSkins:
                    selectedVehicleIndex = hud.SelectedMenuIndex;
                    hud.VehicleSelection = selectedVehicleIndex;
                    hud.ConfigureSkinChoices(vehicleRoster.SkinCountAt(selectedCharacterIndex));
                    hud.SetMenuPage(NativeMenuPage.Skins);
                    hud.SelectMenuIndex(selectedSkinIndex);
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.OpenCups:
                    selectedSkinIndex = hud.SelectedMenuIndex;
                    OpenModeCup();
                    break;
                case NativeMenuAction.BackToVehicles:
                    selectedSkinIndex = hud.SelectedMenuIndex;
                    hud.SetMenuPage(NativeMenuPage.Vehicles);
                    hud.SelectMenuIndex(selectedVehicleIndex);
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.BackToTracks:
                    hud.SetMenuPage(NativeMenuPage.Tracks);
                    hud.SelectMenuIndex(Mathf.Max(0, NativeTrackCatalog.IndexOf(selectedTrackId)));
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.BackToCharacters:
                    bool returningFromVehicle = hud.MenuPage == NativeMenuPage.Vehicles;
                    if (returningFromVehicle) selectedVehicleIndex = hud.SelectedMenuIndex;
                    hud.SetMenuPage(NativeMenuPage.Characters);
                    hud.SelectMenuIndex(Mathf.Max(0, Array.IndexOf(selectableCharacters, selectedCharacterIndex)));
                    if (returningFromVehicle) hud.NotifyOriginalCharacterFocusSelected();
                    heldMenuDirection = 0;
                    break;
                case NativeMenuAction.BackToMain: hud.SetMenuPage(NativeMenuPage.Main); heldMenuDirection = 0; break;
                case NativeMenuAction.Restart: if(phase==Phase.Paused)sourceRaceState.Restart();else ChangeTrack(trackId,true); break;
                case NativeMenuAction.Resume: Resume(); break;
                case NativeMenuAction.MainMenu: if(phase==Phase.Paused)sourceRaceState.Quit();else ResetRace(false); break;
                case NativeMenuAction.Quit:
                    NativeTelemetry.Event("app.quit.requested", "accepted", "menu-quit");
                    if (NativeGameSession.Current != null) NativeGameSession.Cancel(NativeGameSession.Current.sessionId);
                    Application.Quit(); break;
            }
        }

        void SnapCamera()
        {
            if (!raceCamera || racers.Length == 0) return;
            if (!cameraRig)
            {
                cameraRig = NativeKartCamera.Attach(raceCamera, racers[0]);
                FinishCamera = NativeKartFinishCamera.Attach(cameraRig, finishCameraController);
            }
            FinishCamera.ResetForRace();
            cameraRig.SnapToTarget();
        }

        internal void UsePlayerItem() => racers[0].GetComponent<NativeKartItemInventory>().RequestUse();

        void LateUpdate()
        {
            hud?.TickOriginalCircuitBannerMotion();
            if (automatedSettingsPresentation) QualitySettings.vSyncCount = 0;
            if (NetworkLateUpdate()) return;
            if (progress == null || !cameraRig) return;
            UpdateProgress();
            SampleRace();
            if ((visualTest && !AllowCameraVerificationUpdates) || !raceCamera || racers.Length == 0 || phase == Phase.Paused || captureDetail) return;
            if (UpdateCeremonyCamera()) return;
            foreach (var racer in racers) racer.PrepareRenderPose(interpolateRendering);
            cameraRig.Step(Time.deltaTime, LocalPrimaryCameraEnded());
            StepLocalRaceViews();
        }

        void ConfigureItemInventories()
        {
            for (int i = 0; i < racers.Length; i++)
            {
                string character = racerNames[i].Split('/')[0].Trim();
                racers[i].GetComponent<NativeKartItemInventory>().Configure(this, itemWorld, character,
                    !visualTest && ((!selfTest && i > 0) || itemCombatTest));
            }
        }

        public float ProgressOf(NativeKartMotor motor)
        {
            int index = Array.IndexOf(racers, motor);
            return progress != null && index >= 0 ? progress[index].Fraction(motor.transform.position) : 0f;
        }

        public bool HasFinished(NativeKartMotor motor)
        {
            int index = Array.IndexOf(racers, motor);
            return progress != null && index >= 0 && progress[index].Finished;
        }

        NativeRacingRanking sourceRanking;
        void InitializeSourceRanking()
        { sourceRanking=new NativeRacingRanking(racers.Length,RaceLaps,gates.Length,i=>progress[i].SourceRankState,i=>racers[i].transform.position,i=>gates[i].position){IsRacerAlive=i=>racers[i],UpdateHumanTimers=TickSourceTimers}; if(raceConfig!=null) sourceRanking.SetOriginalArrayOrder(NativeLocalRosterOrder.Read(raceConfig)); }
        public float SourceFirstRacerTime=>sourceRanking==null?0:sourceRanking.FirstRacerTime;
        public NativeKartMotor SourceMostFirstRacer()
        {int index=sourceRanking==null?-1:sourceRanking.GetMostFirstRacer();return index<0?null:racers[index];}
        public NativeKartMotor SourceRacerAtRank(int zeroBasedRank)
        {int index=sourceRanking==null?-1:sourceRanking.RacerAtRank(zeroBasedRank);return index<0?null:racers[index];}
        public int SourceRankIndex(NativeKartMotor racer)=>sourceRanking==null?-1:sourceRanking.GetRank(Array.IndexOf(racers,racer));
        public int RankOf(NativeKartMotor motor)
        {
            int index=Array.IndexOf(racers,motor);
            if(sourceRanking==null)return 1;
            return sourceRanking.GetRank(index)+1;
        }

        int Position() => RankOf(racers[0]);

        void OnGUI()
        {
            PerformanceDrawProbePreview();
            if (NativeCameraLab.IsActive) { hud?.SetVisible(false); if (itemHud) itemHud.SetVisible(false); return; }
            if(scaledTransformCheck || musicCancelScaledCheck || quitSourceNonrenderedCheck || quitSourceExitObserver)return;
            if(menu2SourceLifecycleCheck)return;
            if (StartupSplashActive && !startupSplash.MenuRevealed) { hud.SetVisible(false); return; }
            hud?.UseOnlineAuthoring(Net && (Net.Open || Net.ProfileFromSettings));
            UseOnlineItemHud(Net && Net.Open);
            if (hud != null && racers != null && racers.Length > 0)
            {
                int moduleSeat = Net && Net.Open && Net.InRace ? Net.LocalRacerIndex : 0;
                var moduleMotor = moduleSeat >= 0 && moduleSeat < racers.Length ? racers[moduleSeat] : null;
                hud.DrawOriginalVehicleModuleBar(moduleMotor, raceCamera, true, progress != null && phase != Phase.Menu && !captureDetail);
                if (Net && Net.Open && Net.InRace) ObserveNetworkModuleHudDraw();
            }
            if (Net && Net.Open) { NetworkDrawHud(); return; }
            if (Net) Net.RenderNetworkUi();
            if (Net && Net.ProfileFromSettings) return;
            var ufo = GetComponent<NativeUfoManager>();
            if (ufo) { ufo.SetWorking(IsRaceRunning); ufo.SetVisible(progress != null && !captureDetail && phase != Phase.Menu && phase != Phase.Finished && !SettingsOpen); }
            if (itemHud) itemHud.SetVisible(progress != null && !captureDetail && phase != Phase.Menu && phase != Phase.Finished);
            if (progress == null || captureDetail) { hud.SetVisible(false); return; }
            if (DrawLocalRaceViews()) return;
            bool holdingQuit = !hud.MenuTransitionActive && ((mainQuitSource != null && mainQuitSource.PanelActive && hud.MenuPage == NativeMenuPage.Main)
                || (localSelectionActive && localQuittingPresses > 0));
            float width = hud.BeginFrame(holdingQuit || (settingsOpen && !hud.MenuTransitionActive &&
                (settingsPage == SettingsPage.Video || settingsPage == SettingsPage.Audio || settingsPage == SettingsPage.Language)));
            ApplySettingsPointerVerificationEvent();
            ApplyCustomPointerVerificationEvent();
            try
            {
                if (hud.DrawMenuTransition(width)) return;
                if (DrawSettings(width)) return;
                if (DrawLocalSelection()) { DrawLocalSelectionPreviews(); return; }
                if (DrawCharacterProfile(width)) return;
                if (ceremony) { ExecuteMenuAction(DrawFullModeResults(width)); return; }
                if (phase == Phase.Menu)
                {
                    NativeMenuAction action;
                    if (TryDrawFullModeMenu(width, out action)) { ExecuteMenuAction(action); return; }
                    if (hud.MenuPage == NativeMenuPage.Tracks) action = hud.TrackMenu(width);
                    else if (hud.MenuPage == NativeMenuPage.Characters && vehicleRoster)
                    { hud.VehicleSelection = selectedVehicleIndex; action = hud.CharacterMenu(width, vehicleRoster, selectableCharacters); }
                    else if ((hud.MenuPage == NativeMenuPage.Vehicles || hud.MenuPage == NativeMenuPage.Skins) && vehicleRoster)
                        action = hud.VehicleMenu(width, vehicleRoster);
                    else action = hud.MainMenu(width);
                    DrawSelectionPreview();
                    ExecuteMenuAction(action);
                    return;
                }
                ConfigureRaceHud(0,finishSeconds[0]>0,progress[0].CompletedLaps+1,RaceLaps);
                hud.RaceLapSeconds = lapSeconds[0];
                hud.CurrentLapSeconds = PreciseElapsed-lapTimestamps[0];
                hud.Race(width, finishSeconds[0] > 0 ? finishSeconds[0] : PreciseElapsed, Position(), racers[0].Speed, route, racers);
                if (phase != Phase.Finished && !string.IsNullOrEmpty(progressionNotice)) hud.Label(new Rect(width / 2 - 280, 145, 560, 50), progressionNotice, 28, Color.yellow, TextAnchor.MiddleCenter);
                if (phase == Phase.Racing)
                {
                    if (PreciseElapsed < 1.5) hud.Countdown("START");
                    return;
                }
                if (phase == Phase.Countdown)
                {
                    hud.Countdown(Mathf.CeilToInt(countdown).ToString());
                    return;
                }
                if (phase == Phase.Paused) ExecuteMenuAction(hud.PauseMenu(width));
                else ExecuteMenuAction(DrawFullModeResults(width));
            }
            finally { hud.EndFrame(); if (automatedSettingsPresentation) QualitySettings.vSyncCount = 0; }
        }

        IEnumerator FinishTest()
        {
            // Canvas renders before OnGUI updates the final racer's result labels.
            yield return null;
            yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("finish-" + completedTests + ".png");
            yield return new WaitForSecondsRealtime(1);
            var feedback = racers.Select(r => r.GetComponent<NativeKartFeedback>()).ToArray();
            bool feedbackPassed = feedback.Select((f, i) => f && (f.enabled ? f.State.heat > 0 : f.State.heat == 0)
                && f.State.wheelHeat >= 0 && f.State.wheelHeat <= 0.9f
                && (!f.enabled || feedbackSamples[i].maxSpeedRatio < f.settings._wheelHeatMinSpd
                    ? feedbackSamples[i].maxWheelHeat == 0f : feedbackSamples[i].maxWheelHeat > 0f)
                && f.State.dirt >= 0 && f.State.dirt <= 1 && f.State.wheelDirt >= 0 && f.State.wheelDirt <= 0.75f).All(p => p);
            File.WriteAllText(Path.Combine(reportDirectory, "feedback-race-" + (completedTests + 1) + ".json"),
                JsonUtility.ToJson(new FeedbackRaceReport { passed = feedbackPassed,
                    states = feedback.Where(f => f).Select(f => f.State).ToArray(), racers = racerNames,
                    kartModuleEnabled = feedback.Select(f => f && f.enabled).ToArray(),
                    samples = feedbackSamples,
                    source = "Live eight-racer five-lap " + trackId + " self-test; Kart heat applies only to enabled Kart modules" }, true));
            CompleteTest((!itemCombatTest || racers.Sum(r => r.GetComponent<NativeKartItemInventory>().UseCount) > 0) && progress.All(p => p.Started && p.CompletedLaps == RaceLaps && p.ExitEvents >= gates.Length * RaceLaps + 1) && FinishCamera.Ended && FinishCamera.EndTriggerCount == 1 && feedbackPassed && racers[0].Resets == 0 && racers.All(r => r.Distance > 1000) && progress.All(p => p.Finished) && errorCount == 0,
                "lap=" + (completedTests + 1) + "; times=" + string.Join(",", finishTimes.Select(t => t.ToString("F3"))) +
                "; checkpoints=" + string.Join(",", progress.Select(p => p.Passed)) +
                "; distances=" + string.Join(",", racers.Select(r => r.Distance.ToString("F2"))) +
                "; circuitExitEvents=" + string.Join(",", progress.Select(p => p.ExitEvents)) + "; finishCameraTriggers=" + FinishCamera.EndTriggerCount + "; resets=" + string.Join(",", racers.Select(r => r.Resets)) + "; itemUse=" + string.Join(",", racers.Select(r => r.GetComponent<NativeKartItemInventory>().UseCount)) + "; errors=" + errorCount);
        }

        void CompleteTest(bool success, string details)
        {
            testResults.Add(details);
            for (int i = 0; i < racers.Length; i++)
            {
                var inventory = racers[i].GetComponent<NativeKartItemInventory>();
                itemMetrics.Add(new ItemRaceMetrics { round = completedTests + 1, racer = i,
                    given = inventory.GiveCount, used = inventory.UseCount, completed = inventory.DoneCount,
                    resets = racers[i].Resets });
                var driver = racers[i].GetComponent<NativeKartOriginalAI>();
                driver.FlushGrassTrace();
                aiMetrics.Add(new AIMetrics { round = completedTests + 1, racer = i,
                    assisted = driver.assistedNavigation, samples = driver.RouteSamples,
                    grassFrames = driver.GrassFrames, waterFrames = driver.WaterFrames,
                    offRouteFrames = driver.OffRouteFrames, maxLaneDeviation = driver.MaxLaneDeviation,
                    meanLaneDeviation = driver.MeanLaneDeviation });
            }
            completedTests++;
            if (success && completedTests < 2) { NativeLocalViewsVerifierHost.StartPersistent(ReturnToMenuAndRestart()); return; }
            var report = new TestReport { passed = success && completedTests == 2, rounds = completedTests,
                trackId = trackId, routeCount = route.Length, gateCount = gates.Length, racers = racers.Length, laps = RaceLaps,
                errors = errorCount, details = testResults.ToArray(), aiMetrics = aiMetrics.ToArray(),
                combat = itemCombatTest, itemMetrics = itemMetrics.ToArray(),
                operatingSystem = SystemInfo.operatingSystem,
                graphics = SystemInfo.graphicsDeviceName };
            File.WriteAllText(Path.Combine(reportDirectory, "result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_TEST_RESULT " + JsonUtility.ToJson(report));
            Application.Quit(report.passed ? 0 : 2);
        }

        [Serializable] sealed class ItemRaceMetrics { public int round, racer, given, used, completed, resets; }
        [Serializable] sealed class TestReport { public bool combat; public ItemRaceMetrics[] itemMetrics; public bool passed; public int rounds, errors, routeCount, gateCount, racers, laps; public string trackId; public string[] details; public AIMetrics[] aiMetrics; public string operatingSystem, graphics; }
        [Serializable] sealed class AIMetrics { public int round, racer, samples, grassFrames, waterFrames, offRouteFrames; public bool assisted; public float maxLaneDeviation, meanLaneDeviation; }
        [Serializable] sealed class TrackSwitchReport { public bool passed; public int errors; public string[] scenes; }
        [Serializable] sealed class FeedbackSample { public int grassFrames, surfaceFrames; public float maxDirt, maxWheelDirt, maxHeat, maxWheelHeat, maxSpeedRatio; }
        [Serializable] sealed class FeedbackRaceReport { public bool passed; public string source; public string[] racers; public bool[] kartModuleEnabled; public NativeKartFeedbackState[] states; public FeedbackSample[] samples; }
        [Serializable] sealed class Telemetry { public string trackId, phase; public float elapsed, speed; public int checkpoint, resets; public Vector3 position; }
    }
}
