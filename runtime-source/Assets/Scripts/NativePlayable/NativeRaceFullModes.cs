using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        public GameObject ceremonyTemplate;
        GameObject ceremonyRoot;
        NativeCeremonyView ceremonyView;
        Renderer[] ceremonyHiddenRenderers;
        Light[] ceremonyHiddenLights;
        Vector3 ceremonyCameraPosition;
        Quaternion ceremonyCameraRotation;
        int customHeldAxis;
        float customRepeatAt;
        bool customDropdownOpen;
        int customDropdownRow, customDropdownOption;

        void ConfigureFullModeMenu()
        {
            hud.ConfigureMainMenu(new[] { "Race!", "Time Trials", "Online", "Extras", "Options" },
                new[] { NativeMenuAction.OpenRaceModes, NativeMenuAction.TimeTrials, NativeMenuAction.Online, NativeMenuAction.Extras, NativeMenuAction.Settings });
            hud.EnableOnlineMenu();
            if (!verificationRun && selectedMode == NativeSessionMode.NativeRace) selectedMode = NativeSessionMode.QuickRace;
            UpdateModeSummary();
        }
        void UpdateModeSummary()
        {
            hud.SelectionRecordMode = selectedMode;
            hud.AdditionalPlayersCanJoin = selectedMode != NativeSessionMode.TimeTrials;
            var definition = NativeModeCatalog.Get(selectedMode);
            int count = selectedMode == NativeSessionMode.CustomRace ? customRacers : definition.DefaultRacerCount;
            int laps = selectedMode == NativeSessionMode.CustomRace ? customLaps : definition.DefaultLaps;
            string items = selectedMode == NativeSessionMode.CustomRace ? new[] { "Default", "UFO", "None" }[customItemType]
                : selectedMode == NativeSessionMode.TimeTrials ? "None" : "Default";
            hud.TrackSummary = count + " racers / " + laps + " laps / " + items + " items";
        }
        void OpenModeTracks()
        {
            UpdateModeSummary();
            hud.SetMenuPage(NativeMenuPage.Tracks);
            hud.SelectMenuIndex(Mathf.Max(0, NativeTrackCatalog.IndexOf(selectedTrackId)));
            heldMenuDirection = 0;
        }
        void OpenModeCharacters()
        {
            hud.SetMenuPage(NativeMenuPage.Characters);
            hud.SelectMenuIndex(Mathf.Max(0, Array.IndexOf(selectableCharacters, selectedCharacterIndex)));
            EnterOriginalCharacterSelection();
            heldMenuDirection = 0;
        }
        void OpenModeCup(bool preserveRecords = false)
        {
            UpdateModeSummary();
            if (!preserveRecords) hud.LoadOriginalCupRecords();
            hud.SetMenuPage(NativeMenuPage.Cups);
            heldMenuDirection = 0;
        }
        bool ExecuteFullModeAction(NativeMenuAction action)
        {
            switch (action)
            {
                case NativeMenuAction.Online: if (Net) Net.OpenMenu(NativeInputBindings.ActualConfirmDevice() ?? hud.ConsumeActualMenuPointerDevice()); return true;
                case NativeMenuAction.Extras: hud.SetMenuPage(NativeMenuPage.Extras); return true;
                case NativeMenuAction.Credits: hud.SetMenuPage(NativeMenuPage.Credits); return true;
                case NativeMenuAction.MusicRoom: hud.SetMenuPage(NativeMenuPage.MusicRoom); NativeAudioRuntime.PreviewMusicRoom(hud.MusicSelection); return true;
                case NativeMenuAction.BackToExtras: if(hud.MenuPage==NativeMenuPage.MusicRoom){StartMusicRoomSourceCancel();return true;} hud.SetMenuPage(NativeMenuPage.Extras); NativeAudioRuntime.PlayMusic("Menu",true); return true;
                case NativeMenuAction.OpenRaceModes:
                    hud.SetMenuPage(NativeMenuPage.RaceModes); hud.SelectMenuIndex(0); heldMenuDirection = 0; return true;
                case NativeMenuAction.BackToRaceModes:
                    hud.SetMenuPage(NativeMenuPage.RaceModes); heldMenuDirection = 0; return true;
                case NativeMenuAction.OpenTracks:
                    OpenModeTracks(); return true;
                case NativeMenuAction.QuickRace:
                    selectedMode = NativeSessionMode.QuickRace; UpdateModeSummary(); OpenModeCharacters(); return true;
                case NativeMenuAction.TimeTrials:
                    selectedMode = NativeSessionMode.TimeTrials; UpdateModeSummary(); OpenModeCharacters(); return true;
                case NativeMenuAction.GrandPrix:
                    selectedMode = NativeSessionMode.GrandPrix;
                    selectedTrackId = NativeModeCatalog.CupTrackIds[0];
                    UpdateModeSummary(); OpenModeCharacters(); return true;
                case NativeMenuAction.CustomRace:
                    selectedMode = NativeSessionMode.CustomRace;
                    UpdateModeSummary(); OpenModeCharacters(); return true;
                case NativeMenuAction.ConfirmCup:
                    if (selectedMode == NativeSessionMode.GrandPrix)
                    {
                        selectedTrackId = NativeModeCatalog.CupTrackIds[0];
                        ExecuteMenuAction(NativeMenuAction.StartRace);
                    }
                    else OpenModeTracks();
                    return true;
                case NativeMenuAction.ConfirmCircuit:
                    selectedTrackId = NativeTrackCatalog.IdAt(hud.SelectedMenuIndex);
                    if (selectedMode == NativeSessionMode.CustomRace)
                    {
                        hud.SetMenuPage(NativeMenuPage.Custom); hud.ResetCustomizationFocus(); CloseCustomDropdown(); heldMenuDirection = 0; customHeldAxis = 0; customRepeatAt = 0;
                    }
                    else ExecuteMenuAction(NativeMenuAction.StartRace);
                    return true;
                case NativeMenuAction.BackToCup:
                    CloseCustomDropdown();
                    bool returningFromCircuit = hud.MenuPage == NativeMenuPage.Tracks;
                    if (returningFromCircuit) selectedTrackId = NativeTrackCatalog.IdAt(hud.SelectedMenuIndex);
                    OpenModeCup(returningFromCircuit); return true;
                case NativeMenuAction.CustomLaps: return true;
                case NativeMenuAction.CustomRacers: return true;
                case NativeMenuAction.CustomItems: OpenCustomDropdown(3); return true;
                case NativeMenuAction.CustomRespawn: OpenCustomDropdown(4); return true;
                case NativeMenuAction.CustomModifier: return true;
                case NativeMenuAction.BackToTracks:
                    if (phase == Phase.Finished) ResetRace(false);
                    if (selectedMode == NativeSessionMode.GrandPrix) { hud.SetMenuPage(NativeMenuPage.RaceModes); return true; }
                    OpenModeTracks(); return true;
                case NativeMenuAction.BackToMain:
                    hud.SetMenuPage(NativeMenuPage.Main); heldMenuDirection = 0; return true;
                case NativeMenuAction.Continue:
                    ContinueGrandPrix(); return true;
                case NativeMenuAction.MainMenu:
                    if (phase == Phase.Paused) return false;
                    ClearCeremony(); ResetRace(false); return true;
                case NativeMenuAction.Restart:
                    if (selectedMode == NativeSessionMode.GrandPrix && NativeGameSession.Current != null
                        && NativeGameSession.Current.state != NativeSessionState.Racing)
                    {
                        NativeGameSession.Cancel(sessionId); sessionId = null;
                        selectedTrackId = NativeModeCatalog.CupTrackIds[0];
                        EnsureSession();
                        ChangeTrack(selectedTrackId, true);
                        return true;
                    }
                    return false;
            }
            return false;
        }
        void AdjustCustom(int row, int delta)
        {
            SetCustomValue(row, CustomValue(row) + delta);
            UpdateModeSummary();
        }
        int CustomValue(int row) => row == 1 ? customLaps : row == 2 ? customRacers : row == 3 ? customItemType : customRules.Value(row);
        void SetCustomValue(int row, int value)
        {
            if (row == 0) return;
            if (row == 1) customLaps = Mathf.Clamp(value, 1, 100);
            else if (row == 2) customRacers = Mathf.Clamp(value, 1, 100);
            else if (row == 3) customItemType = (value % 3 + 3) % 3;
            else if (row == 4) customRules.itemRespawn = (value % 2 + 2) % 2;
            else customRules.Set(row, value);
            UpdateModeSummary();
        }
        void OpenCustomDropdown(int row)
        {
            if (customDropdownOpen) return;
            customDropdownRow = row; customDropdownOption = CustomValue(row); customDropdownOpen = true;
            hud.PlayCustomizationSound(0);
        }
        void CloseCustomDropdown() => CloseCustomDropdown(false);
        void CloseCustomDropdown(bool canceled)
        {
            if (!customDropdownOpen) return;
            if (canceled) hud.PlayCustomizationSound(2);
            customDropdownOpen = false;
            hud.CloseCustomDropdown();
            hud.PlayCustomizationSound(0);
        }
        void HoverCustomDropdown(int option)
        {
            if (!customDropdownOpen || customDropdownOption == option) return;
            customDropdownOption = option; hud.PlayCustomizationSound(0);
        }
        void ConfirmCustomDropdown(int option)
        {
            if (!customDropdownOpen) return;
            SetCustomValue(customDropdownRow, option); CloseCustomDropdown();
        }
        bool HandleFullModeInput()
        {
            if (phase == Phase.Menu && hud.MenuPage == NativeMenuPage.MusicRoom)
            {
                float musicAxis = NativeInputBindings.GetVector().y;
                int step = musicAxis > .5f ? -1 : musicAxis < -.5f ? 1 : 0;
                if (step != 0 && (step != customHeldAxis || Time.unscaledTime >= customRepeatAt))
                {
                    int count = NativeMusicRoomData.Count;
                    if (count > 0) { hud.MusicSelection = (hud.MusicSelection + step + count) % count; }
                    customRepeatAt = Time.unscaledTime + (step != customHeldAxis ? .35f : .15f);
                }
                customHeldAxis = step;
                if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok)) NativeAudioRuntime.PreviewMusicRoom(hud.MusicSelection);
                if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Use)) NativeAudioRuntime.ToggleMusicRoomPause();
                if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Cancel)) ExecuteFullModeAction(NativeMenuAction.BackToExtras);
                return true;
            }
            if (ceremony)
            {
                hud.SetMenuPage(NativeMenuPage.Ceremony);
                return true;
            }
            if (phase != Phase.Menu || hud.MenuPage != NativeMenuPage.Custom) return false;
            Vector2 input = NativeInputBindings.GetVector();
            int vertical = Mathf.Abs(input.y) > 0.5f ? (input.y > 0 ? -1 : 1) : (Input.GetKey(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKey(KeyCode.UpArrow) ? 1 : 0);
            int horizontal = Mathf.Abs(input.x) > 0.5f ? (input.x > 0 ? 1 : -1) : (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            int axis = vertical != 0 ? vertical : horizontal * 2;
            StepCustomizationAxis(axis, vertical, horizontal, Time.unscaledTime);
            if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Z))
            {
                if (customDropdownOpen) ConfirmCustomDropdown(customDropdownOption);
                else ExecuteMenuAction(hud.ConfirmMenu(NativeMenuPage.Custom));
            }
            else if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Cancel) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.X))
            {
                if (customDropdownOpen) CloseCustomDropdown(true);
                else ExecuteMenuAction(hud.CancelMenu(NativeMenuPage.Custom));
            }
            return true;
        }
        bool TryDrawFullModeMenu(float width, out NativeMenuAction action)
        {
            action = NativeMenuAction.None;
            if (hud.MenuPage == NativeMenuPage.Extras) { action = hud.ExtrasMenu(width); return true; }
            if (hud.MenuPage == NativeMenuPage.Credits) { action = hud.CreditsMenu(width); return true; }
            if (hud.MenuPage == NativeMenuPage.MusicRoom) { action = hud.MusicRoomMenu(width); return true; }
            if (hud.MenuPage == NativeMenuPage.Online) { if (Net) Net.OpenMenu(); action = NativeMenuAction.None; return true; }
            if (hud.MenuPage == NativeMenuPage.Cups) { action = hud.AuthoredCup(width); return true; }
            if (hud.MenuPage == NativeMenuPage.RaceModes) { action = hud.RaceModeMenu(width); return true; }
            if (hud.MenuPage != NativeMenuPage.Custom) return false;
            action = hud.CustomModeMenu(width, CustomValue, SetCustomValue, customDropdownOpen, customDropdownOption, ConfirmCustomDropdown, CloseCustomDropdown, HoverCustomDropdown);
            return true;
        }
        NativeMenuAction DrawFullModeResults(float width)
        {
            if (ceremony) return hud.CeremonyMenu(width, NativeGameSession.Current, vehicleRoster, progressionNotice, ceremonyView);
            if (hud.UsesSourceResults) return hud.DrawSourceResults();
            if (raceConfig == null || raceConfig.mode == NativeSessionMode.NativeRace) return hud.ResultsMenu(width, finishTimes, racerNames);
            NativeMenuAction action;
            if (raceConfig.mode == NativeSessionMode.TimeTrials)
                action = hud.TimeTrialResults(width, finishSeconds[0], lapSeconds[0], NativeProgression.GetTimeTrial(trackId), progressionNotice, racerNames[0]);
            else action = hud.FullRaceResults(width, finishSeconds, racerNames, NativeGameSession.Current, progressionNotice,
                raceConfig.mode == NativeSessionMode.GrandPrix, finishPlaces, vehicleRoster);
            DrawResultsCharacterPreview();
            return action;
        }
        void CompleteGrandPrixProgress()
        {
            var session = NativeGameSession.Current;
            if (session == null || session.state != NativeSessionState.Completed) return;
            var candidates = session.config.roster.Where(s => s.isHuman).Select(seat => new NativeProgression.GrandPrixCandidate
            {
                human = true, characterId = seat.characterId, vehicleId = seat.vehicleId,
                stationSeconds = session.stations.OrderBy(s => s.index).Select(s => s.results.Single(r => r.seatId == seat.seatId).time).ToArray()
            }).ToArray();
            var result = NativeProgression.RecordGrandPrix(NativeModeCatalog.CupId, candidates);
            ShowProgression(result);
            progressionCommitted = result.success;
        }
        void ContinueGrandPrix()
        {
            var session = NativeGameSession.Current;
            if (session == null || session.config.mode != NativeSessionMode.GrandPrix) return;
            if (session.state == NativeSessionState.Completed)
            {
                CompleteModeProgress();
                if (progressionCommitted) ShowCeremony(session);
                return;
            }
            if (session.state != NativeSessionState.StationComplete || !NativeGameSession.Advance(sessionId)) return;
            selectedTrackId = NativeGameSession.CurrentTrackId;
            if (!ChangeTrack(selectedTrackId, true)) progressionNotice = "Track load failed";
        }
        void ShowCeremony(NativeSessionSnapshot session)
        {
            ceremony = true;
            hud.SetMenuPage(NativeMenuPage.Ceremony);
            if (itemHud) itemHud.SetVisible(false);
            var order = NativeModeCatalog.SortScoreboard(session.totals, true);
            if (!ceremonyTemplate) throw new InvalidOperationException("Original ceremony template is missing.");
            foreach (var sceneRoot in gameObject.scene.GetRootGameObjects())
                foreach (var publisher in sceneRoot.GetComponentsInChildren<NativeShaderGlobalsOverTime>(true))
                    publisher.enabled = false;
            ceremonyHiddenRenderers = FindObjectsOfType<Renderer>().Where(r => r.enabled).ToArray();
            ceremonyHiddenLights = FindObjectsOfType<Light>().Where(l => l.enabled).ToArray();
            foreach (var renderer in ceremonyHiddenRenderers) renderer.enabled = false;
            foreach (var light in ceremonyHiddenLights) light.enabled = false;
            ceremonyRoot = Instantiate(ceremonyTemplate);
            ceremonyView = ceremonyRoot.GetComponent<NativeCeremonyView>();
            ceremonyRoot.SetActive(true);
            ceremonyView.ApplyEnvironment();
            for (int i = 0; i < Mathf.Min(3, order.Count); i++)
            {
                var seat = session.config.roster.Single(s => s.seatId == order[i].seatId);
                int racerIndex = session.config.roster.FindIndex(candidate => candidate.seatId == seat.seatId);
                var template = racers[racerIndex].driftPivot.gameObject;
                var anchor = ceremonyView.podiumAnchors[i];
                foreach (Transform child in anchor) child.gameObject.SetActive(false);
                var avatar = Instantiate(template, anchor, false);
                avatar.name = seat.characterId + " place " + (i + 1);
                avatar.transform.localPosition = Vector3.zero;
                avatar.transform.localRotation = Quaternion.identity;
                var sourceRenderers = template.GetComponentsInChildren<Renderer>(true);
                var podiumRenderers = avatar.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < sourceRenderers.Length; rendererIndex++)
                    podiumRenderers[rendererIndex].enabled = ceremonyHiddenRenderers.Contains(sourceRenderers[rendererIndex]);
                foreach (var animator in avatar.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var audio in avatar.GetComponentsInChildren<AudioSource>(true)) { audio.Stop(); audio.enabled = false; }
                foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            }
            Vector3 target = ceremonyView.podiumAnchors[0].position + Vector3.up * 0.5f;
            ceremonyCameraPosition = target + ceremonyView.podiumAnchors[0].forward * 11f + Vector3.up * 3f;
            ceremonyCameraRotation = Quaternion.LookRotation(target - ceremonyCameraPosition);
            var champion = session.config.roster.Single(seat => seat.seatId == order[0].seatId);
            ceremonyView.BindChampion(vehicleRoster.DriverModelAt(vehicleRoster.CharacterIndexOf(champion.characterId), champion.skinIndex));
            ceremonyView.BeginPlayback(raceCamera);
            UpdateCeremonyCamera();
        }
        bool UpdateCeremonyCamera()
        {
            if (!ceremony || !raceCamera) return false;
            bool sampled = ceremonyView.TickPlayback(raceCamera);
            if (ceremonyView.Finished) CompleteSourceCeremony();
            return sampled;
        }
        void ClearCeremony()
        {
            if (ceremonyRoot) { ceremonyRoot.SetActive(false); Destroy(ceremonyRoot); }
            if (ceremonyView) ceremonyView.RestoreEnvironment();
            if (ceremonyHiddenRenderers != null) foreach (var renderer in ceremonyHiddenRenderers) if (renderer) renderer.enabled = true;
            if (ceremonyHiddenLights != null) foreach (var light in ceremonyHiddenLights) if (light) light.enabled = true;
            ceremonyHiddenRenderers = null; ceremonyHiddenLights = null;
            ceremonyView = null; ceremonyRoot = null; ceremony = false;
        }
    }
}
