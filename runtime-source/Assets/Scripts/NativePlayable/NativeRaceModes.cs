using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        NativeSessionMode selectedMode = NativeSessionMode.NativeRace;
        NativeSessionConfig raceConfig;
        readonly NativeRaceClock raceClock = new NativeRaceClock();
        readonly List<NativeKartMotor> rosterPool = new List<NativeKartMotor>();
        GameObject rosterTemplate;
        Vector3[] sceneStarts;
        Quaternion[] sceneHeadings;
        string[] sceneRacerNames;
        double countdownDeadline;
        double[] finishSeconds;
        List<double>[] lapSeconds;
        double[] lapTimestamps;
        int customLaps = 5, customRacers = 8, customItemType;
        NativeCustomRaceRules customRules = new NativeCustomRaceRules();
        string progressionNotice = "";
        bool progressionCommitted;
        bool ceremony;
        int RaceLaps => raceConfig == null ? 1 : raceConfig.laps;
        internal double PreciseElapsed => raceClock.Elapsed;

        void InitializeModeFlow(string[] args)
        {
            rosterPool.AddRange(racers);
            if(sourceGridPositions==null||sourceGridPositions.Length==0||sourceGridRotations==null||sourceGridRotations.Length!=sourceGridPositions.Length)throw new InvalidOperationException("Authored complete source starting grid missing");
            sceneStarts = (Vector3[])sourceGridPositions.Clone();
            sceneHeadings = (Quaternion[])sourceGridRotations.Clone();
            sceneRacerNames = (string[])racerNames.Clone();
            rosterTemplate = Instantiate(racers[1].gameObject);
            rosterTemplate.name = "Session racer template"; rosterTemplate.SetActive(false);
            string mode = Argument(args, "--mode");
            for (int i = 0; i < 5; i++)
                if (mode == NativeModeCatalog.Get((NativeSessionMode)i).Id) selectedMode = (NativeSessionMode)i;
            var session = NativeGameSession.Current;
            if (session != null && session.CurrentTrackId == trackId)
            {
                selectedMode = session.config.mode;
                if (selectedMode == NativeSessionMode.CustomRace)
                {
                    customLaps = session.config.laps; customRacers = session.config.racerCount;
                    customItemType = session.config.itemType == "ufo" ? 1 : session.config.itemType == "none" ? 2 : 0;
                    customRules = (session.config.customRules ?? new NativeCustomRaceRules()).Copy();
                }
                sessionId = session.sessionId;
                selectedCharacterIndex = vehicleRoster.CharacterIndexOf(session.config.roster[0].characterId);
                selectedVehicleIndex = vehicleRoster.VehicleIndexOf(session.config.roster[0].vehicleId);
                selectedSkinIndex = vehicleRoster.ClampSkin(selectedCharacterIndex, session.config.roster[0].skinIndex);
                PrepareModeRoster(session.config);
            }
            ConfigureFullModeMenu();
            foreach (var t in FindObjectsOfType<Transform>(true))
                if (trackId == "wintermistylake" && t.name == "EETrigger" && t.GetComponent<BoxCollider>())
                {
                    var trigger = t.GetComponent<NativeUnlockTrigger>() ?? t.gameObject.AddComponent<NativeUnlockTrigger>();
                    trigger.race = this;
                }
        }

        NativeSessionConfig BuildModeConfig()
        {
            var definition = NativeModeCatalog.Get(selectedMode);
            int count = selectedMode == NativeSessionMode.CustomRace ? customRacers : definition.DefaultRacerCount;
            var owners = LocalSelectionEnabled && NativeLocalSelectionOwners.Instance ? NativeLocalSelectionOwners.Instance.Players.Take(selectedMode==NativeSessionMode.TimeTrials?1:4).ToArray() : new UnityEngine.InputSystem.PlayerInput[0];
            int humanCount = owners.Length > 0 ? owners.Length : 1;
            if (selectedMode == NativeSessionMode.CustomRace && owners.Length > 0) count = Mathf.Clamp(count,humanCount,100);
            var roster = new List<NativeSessionSeat>();
            var random = verificationRun ? new System.Random(20260930) : new System.Random();
            var available = vehicleRoster.SelectableCharacters().ToList();
            available.Remove(selectedCharacterIndex);
            foreach(var owner in owners) { var choice=NativeLocalSelectionOwners.Instance.SavedChoice(owner); if(choice==null)throw new InvalidOperationException("Joined local owner has no committed original selection"); available.Remove(choice.Character); }
            for (int i = 0; i < count; i++)
            {
                int character = selectedCharacterIndex, vehicle = selectedVehicleIndex;
                if (i < owners.Length)
                {var choice=NativeLocalSelectionOwners.Instance.SavedChoice(owners[i]);character=choice.Character;vehicle=choice.Vehicle;}
                else if (i >= humanCount)
                {
                    if (selectedMode == NativeSessionMode.NativeRace)
                    {
                        character = vehicleRoster.CharacterIndexOf(sceneRacerNames[i].Split('/')[0].Trim()); vehicle = 0;
                    }
                    else
                    {
                        if (available.Count == 0) available.AddRange(vehicleRoster.SelectableCharacters());
                        int index = random.Next(available.Count); character = available[index]; available.RemoveAt(index);
                        vehicle = random.Next(vehicleRoster.VehicleCount);
                        if (vehicleRoster.VehicleIdAt(vehicle) == "shoppingcart") vehicle = 0;
                    }
                }
                roster.Add(new NativeSessionSeat { seatId = i < owners.Length ? "human-" + owners[i].user.id : "seat-" + i, isHuman = i < humanCount,
                    characterId = vehicleRoster.CharacterIdAt(character), vehicleId = vehicleRoster.VehicleIdAt(vehicle), skinIndex = i < owners.Length ? NativeLocalSelectionOwners.Instance.SavedChoice(owners[i]).Skin : i == 0 ? selectedSkinIndex : 0 });
            }
            var config = NativeModeCatalog.CreateConfig(selectedMode, selectedTrackId, roster);
            config.sourceStorageOrder=NativeLocalRosterOrder.Create(count,humanCount).ToList();
            if (selectedMode == NativeSessionMode.CustomRace)
            {
                config.laps = customLaps;
                config.customRules = customRules.Copy();
                config.itemType = new[] { "normal", "ufo", "none" }[customItemType];
            }
            return config;
        }

        void PrepareModeRoster(NativeSessionConfig config)
        {
            raceConfig = config;
            while (rosterPool.Count < config.racerCount)
            {
                var clone = Instantiate(rosterTemplate).GetComponent<NativeKartMotor>();
                clone.name = "Session racer " + rosterPool.Count;
                rosterPool.Add(clone);
            }
            for (int i = 0; i < rosterPool.Count; i++) rosterPool[i].gameObject.SetActive(i < config.racerCount);
            racers = rosterPool.Take(config.racerCount).ToArray();
            starts = new Vector3[racers.Length]; headings = new Quaternion[racers.Length];
            racerNames = new string[racers.Length];
            for (int i = 0; i < racers.Length; i++)
            {
                int slot = NativeLocalRosterOrder.GridSlot(config,i,sceneStarts.Length);
                headings[i] = sceneHeadings[slot];
                // Source SpawnPlayers wraps authored points; excess participants share the same point.
                starts[i] = sceneStarts[slot];
                var motor = racers[i];
                var entry = config.roster[i];
                int character = vehicleRoster.CharacterIndexOf(entry.characterId), vehicle = vehicleRoster.VehicleIndexOf(entry.vehicleId);
                NativeVehicleRoster display = i == 0 ? vehicleRoster : motor.GetComponent<NativeVehicleRoster>();
                if (!display)
                {
                    display = motor.gameObject.AddComponent<NativeVehicleRoster>();
                    display.characters = vehicleRoster.characters; display.vehicles = vehicleRoster.vehicles;
                    display.DefaultCharacterIndex = vehicleRoster.CharacterIndexOf(sceneRacerNames[i < sceneRacerNames.Length ? i : 1].Split('/')[0].Trim());
                }
                display.ApplySelection(character, vehicle, entry.skinIndex, motor);
                racerNames[i] = vehicleRoster.CharacterNameAt(character) + (entry.isHuman ? " / YOU" : "");
                var driver = motor.GetComponent<NativeKartOriginalAI>();
                driver.motor = motor; driver.racerIndex = i;
                int index = i;
                var recovery = motor.GetComponent<NativeKartRecovery>() ?? motor.gameObject.AddComponent<NativeKartRecovery>();
                bool online=Net && Net.Open;
                recovery.CanRequest=online ? (Func<bool>)(()=>Net && Net.IsHost) : null;
                recovery.Initialize(() => CompleteRecovery(index), online ? !Net.requestedSpectator && i==Net.LocalRacerIndex : entry.isHuman, hud.uiImageMaterial);
                var hit = motor.GetComponent<NativeKartHit>();
                hit.ItemsFrozenChanged = motor.GetComponent<NativeKartItemInventory>().SetFrozen;
            }
            if (itemWorld) itemWorld.Configure(GetComponent<NativeItemCatalog>(), racers, route);
        }

        void ApplyCustomMotorRules()
        {
            if (raceConfig == null || raceConfig.mode != NativeSessionMode.CustomRace) return;
            for (int i = 0; i < racers.Length; i++)
            {
                int vehicle = vehicleRoster.VehicleIndexOf(raceConfig.roster[i].vehicleId);
                racers[i].stats = raceConfig.customRules.Apply(vehicleRoster.vehicles[vehicle].settings);
                var ai = racers[i].GetComponent<NativeKartOriginalAI>();
                if (ai) ai.defTopSpeed = racers[i].stats._topSpeed;
            }
        }

        void ResetModeProgress()
        {
            ClearCeremony();
            modeFrames.Clear(); maximumModeDelta = 0;
            raceClock.Reset(); progressionCommitted = false; ceremony = false; progressionNotice = "";
            finishArrivalQueue.Clear(); finishArrivalCount = 0; finishPlaces = new int[racers.Length];
            finishSeconds = new double[racers.Length]; lapTimestamps = new double[racers.Length];
            lapSeconds = racers.Select(r => new List<double>()).ToArray();
            countdown = raceConfig == null ? 3 : NativeModeCatalog.Get(raceConfig.mode).Countdown;
            countdownDeadline = Time.timeSinceLevelLoadAsDouble + countdown;
            bool instantRespawn = raceConfig != null && raceConfig.mode == NativeSessionMode.CustomRace && raceConfig.customRules.itemRespawn == 1;
            foreach (var box in FindObjectsOfType<NativeKartItemBox>(true))
            {
                box.ApplyRespawnMode(instantRespawn && !box.isFixed);
                box.gameObject.SetActive(box.isFixed || raceConfig == null || raceConfig.itemType != "none");
            }
            hud.RaceLapCount = RaceLaps;
            UpdateModeSummary();
            var ufo = GetComponent<NativeUfoManager>();
            if (ufo) ufo.Configure(raceConfig == null ? 0 : raceConfig.itemType == "ufo" ? 1 : raceConfig.itemType == "none" ? 2 : 0,
                racers.Select(r => r.GetComponent<NativeKartItemInventory>()).ToArray(), FindObjectsOfType<NativeKartItemBox>(true), instantRespawn);
        }

        void CompleteModeProgress()
        {
            if (progressionCommitted || raceConfig == null || finishSeconds[0] <= 0) return;
            if (raceConfig.mode == NativeSessionMode.GrandPrix) { CompleteGrandPrixProgress(); return; }
            if (raceConfig.mode == NativeSessionMode.TimeTrials)
            {
                var player = raceConfig.roster[0];
                var result = NativeProgression.RecordTimeTrial(trackId, player.characterId, player.vehicleId,
                    finishSeconds[0], lapSeconds[0].ToArray());
                ShowProgression(result);
                progressionCommitted = result.success;
            }
        }

        void ShowProgression(NativeProgression.Result result)
        {
            NativeTelemetry.Event("progression.commit", result.success ? "completed" : "failed", result.success ? null : "save-failed", result.success ? "unlocks=" + result.newlyUnlocked.Length + ";recordChanged=" + result.recordChanged : result.error);
            if (!result.success) { progressionNotice = "Save failed: " + result.error; return; }
            if (result.newlyUnlocked.Length > 0)
            {
                foreach (var unlockedId in result.newlyUnlocked)
                {
                    NativeAudioRuntime.StopEvent("progression.unlock", transform);
                    NativeAudioRuntime.PlayEvent("progression.unlock", transform);
                }
                progressionNotice = "UNLOCKED: " + string.Join(", ", result.newlyUnlocked.Select(id => id.ToUpperInvariant()));
                selectableCharacters = vehicleRoster.SelectableCharacters();
                hud.ConfigureChoiceCounts(selectableCharacters.Length, vehicleRoster.VehicleCount);
            }
            else if (result.recordChanged) progressionNotice = "NEW RECORD";
        }

        public bool IsHumanRacer(NativeKartMotor motor)
        {
            int index = Array.IndexOf(racers, motor);
            return index >= 0 && (raceConfig == null ? index == 0 : raceConfig.roster[index].isHuman);
        }
        public void EnterFrogTrigger(NativeKartMotor motor)
        {
            if (IsRaceRunning && IsHumanRacer(motor)) ShowProgression(NativeProgression.TriggerFrog(trackId, true));
        }
    }
}
