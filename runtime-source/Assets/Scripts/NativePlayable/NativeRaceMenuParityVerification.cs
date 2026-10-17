using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        static bool menuParityVerificationRunning;
        void StartMenuParityVerification()
        {
            if(menuParityVerificationRunning)return;
            menuParityVerificationRunning=true;
            NativeLocalViewsVerifierHost.StartPersistent(CheckMenuParityLive());
        }
#if ENABLE_INPUT_SYSTEM
        [Serializable] sealed class MenuParitySample
        {
            public string name, page;
            public int index;
            public bool selectorVisible;
            public float seconds, scaledSeconds, selectorY, wheelAngle;
            public float[] buttonAmounts;
            public Rect[] buttonRects;
        }
        [Serializable] sealed class MenuParityReport
        {
            public bool passed;
            public string source = "Unity macOS Player; actual InputSystem keyboard events and rendered menu focus snapshots";
            public string[] checks;
            public MenuParitySample[] focusSamples;
            public float wallSeconds;
        }
        readonly List<MenuParitySample> menuParitySamples = new List<MenuParitySample>();
        float menuParityStarted;

        MenuParitySample CaptureMenuParityFocus(string name)
        {
            var focus = hud.MenuFocus;
            var sample = new MenuParitySample {
                name = name, page = focus.page.ToString(), index = focus.index,
                seconds = Time.realtimeSinceStartup - menuParityStarted, scaledSeconds = Time.time,
                selectorY = focus.selectorY, wheelAngle = focus.wheelAngle, selectorVisible = focus.selectorVisible,
                buttonAmounts = focus.buttonAmounts == null ? new float[0] : (float[])focus.buttonAmounts.Clone(),
                buttonRects = focus.buttonRects == null ? new Rect[0] : (Rect[])focus.buttonRects.Clone()
            };
            menuParitySamples.Add(sample);
            ModeCheck(sample.page == hud.MenuPage.ToString() && sample.index == hud.SelectedMenuIndex,
                "rendered focus matches navigation " + name);
            return sample;
        }

        IEnumerator MenuParityKey(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            var receiver=UnityEngine.Object.FindObjectOfType<NativeRace>();
            while(!receiver || receiver.hud.MenuTransitionActive){yield return null;receiver=UnityEngine.Object.FindObjectOfType<NativeRace>();}
            while(receiver.musicRoomSourceCancelPending) yield return null;
            yield return new WaitForEndOfFrame();
        }

        IEnumerator MenuParityOptionTransitionKey(Keyboard keyboard, Key key, string destination, bool reverse)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(.14f);
            yield return new WaitForEndOfFrame();
            var state = hud.TransitionState;
            ModeCheck(hud.MenuTransitionActive && state != null && state.sourceTransition == "MainOptionsTransition"
                && state.from == (reverse ? destination : "Main") && state.to == (reverse ? "Main" : destination),
                destination + " actual rendered " + (reverse ? "return" : "entry") + " transition active");
            if (state != null)
            {
                ModeCheck(reverse ? state.destinationOffset.x < 0 : state.mainUpperOffset.x < 0 && state.mainLowerOffset.x < 0,
                    destination + " rendered buttons slide left during " + (reverse ? "return" : "entry"));
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, "transition-" + destination.ToLowerInvariant() + (reverse ? "-return" : "-entry") + ".png"));
            while (hud.MenuTransitionActive) yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
        }

        IEnumerator MenuParityScreenshot(string name)
        {
            while (hud.MenuTransitionActive) yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, name + ".png"));
            yield return null;
        }

        void MenuParityRoute(NativeMenuPage page, int index, string label)
        {
            ModeCheck(!settingsOpen && hud.MenuPage == page && hud.SelectedMenuIndex == index, label);
        }

        internal IEnumerator CheckMenuParityLive()
        {
            menuParityStarted = Time.realtimeSinceStartup;
            modeChecks.Clear(); modePassed = true; menuParitySamples.Clear();
            Directory.CreateDirectory(reportDirectory);
            ModeCheck(NativeMenuTransitionVerification.Run(reportDirectory), "original transition rules");
            int observedEngineErrors=errorCount;
            Application.LogCallback observeErrors=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)observedEngineErrors++;};
            Application.logMessageReceived+=observeErrors;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            bool pointerSuppressed = hud.SuppressPointerInput;
            hud.SuppressPointerInput = true;
            try
            {
                yield return new WaitForSecondsRealtime(.25f);
                yield return new WaitForEndOfFrame();
                yield return CheckMainQuitSourceLive(keyboard);
                while (hud.MenuTransitionActive) yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                MenuParityRoute(NativeMenuPage.Main, 0, "initial Race focus");
                var initial = CaptureMenuParityFocus("main-initial");
                ModeCheck(initial.buttonRects.Length == 5 && initial.buttonAmounts.Length == 5,
                    "original five visible focus targets");
                ModeCheck(initial.selectorVisible, "main selector texture and clipping region active");
                yield return new WaitForSecondsRealtime(.12f);
                yield return new WaitForEndOfFrame();
                var rotating = CaptureMenuParityFocus("main-wheel");
                float elapsed = rotating.scaledSeconds - initial.scaledSeconds;
                ModeCheck(elapsed > 0 && Mathf.Abs(Mathf.DeltaAngle(initial.wheelAngle, rotating.wheelAngle) / elapsed + 90) < 8,
                    "main wheel minus ninety degrees per second");
                yield return MenuParityScreenshot("menu-parity-main");

                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return new WaitForSecondsRealtime(.04f);
                yield return new WaitForEndOfFrame();
                var moving = CaptureMenuParityFocus("main-solo-moving");
                yield return new WaitForSecondsRealtime(.22f);
                yield return new WaitForEndOfFrame();
                var settled = CaptureMenuParityFocus("main-solo-settled");
                MenuParityRoute(NativeMenuPage.Main, 1, "keyboard down selects Time Trials");
                if (initial.buttonRects.Length == 5 && settled.buttonRects.Length == 5 && moving.buttonAmounts.Length == 5)
                {
                    ModeCheck(moving.buttonAmounts[1] > 0 && moving.buttonAmounts[1] < 1,
                        "selected button has a real intermediate expansion");
                    ModeCheck(settled.buttonAmounts[1] > .99f && settled.buttonAmounts[0] < .01f,
                        "selected expansion ends and previous button contracts");
                    ModeCheck(Mathf.Abs(settled.selectorY - settled.buttonRects[1].center.y - 16f / 1.8f) < .1f,
                        "selector reaches original scaled offset");
                    ModeCheck(Mathf.Abs(settled.selectorY - initial.selectorY) > 1
                        && Mathf.Abs(moving.selectorY - initial.selectorY) > .01f
                        && Mathf.Abs(moving.selectorY - settled.selectorY) > .01f,
                        "selector moves continuously before reaching next target");
                    ModeCheck(settled.buttonRects[1].width > initial.buttonRects[1].width
                        && settled.buttonRects[0].width < initial.buttonRects[0].width,
                        "rendered button widths follow focus expansion");
                }
                else ModeCheck(false, "animation snapshot arrays available");
                yield return MenuParityKey(keyboard, Key.UpArrow);
                yield return new WaitForSecondsRealtime(.22f);
                MenuParityRoute(NativeMenuPage.Main, 0, "keyboard up returns Race");
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return new WaitForSecondsRealtime(.25f);
                yield return new WaitForEndOfFrame();
                MenuParityRoute(NativeMenuPage.RaceModes, 0, "Race opens mode submenu");
                var raceInitial = CaptureMenuParityFocus("race-modes-initial");
                ModeCheck(raceInitial.buttonRects.Length == 3, "race submenu three visible targets");
                ModeCheck(!raceInitial.selectorVisible, "race submenu retains original absence of selector");
                yield return new WaitForSecondsRealtime(.12f);
                yield return new WaitForEndOfFrame();
                var raceRotating = CaptureMenuParityFocus("race-modes-wheel");
                elapsed = raceRotating.seconds - raceInitial.seconds;
                ModeCheck(elapsed > 0 && Mathf.Abs(Mathf.DeltaAngle(raceInitial.wheelAngle, raceRotating.wheelAngle) / elapsed + 360) < 15,
                    "race wheel minus three hundred sixty degrees per second");
                yield return MenuParityScreenshot("menu-parity-race");

                yield return MenuParityKey(keyboard, Key.Enter);
                yield return new WaitForSecondsRealtime(.2f);
                ModeCheck(hud.MenuPage == NativeMenuPage.Characters && selectedMode == NativeSessionMode.GrandPrix,
                    "Grand Prix confirm routes to character selection");
                yield return MenuParityScreenshot("menu-parity-characters");
                ModeCheck(SelectionPreviewUsesOriginalLighting(out string previewDetail), "original preview lights " + previewDetail);
                var screenCamera = menuPresentation.SelectionScreenCamera;
                ModeCheck(menuPresentation.SelectionVisible && !menuPresentation.content.activeSelf
                    && !menuPresentation.menuCamera.enabled && screenCamera && screenCamera.enabled
                    && screenCamera.clearFlags == selectionPreviewDefinition.clearFlags
                    && screenCamera.backgroundColor == selectionPreviewDefinition.background,
                    "selection enables its source screen camera and disables the main-menu camera");
                ModeCheck(screenCamera && screenCamera.transform.position == selectionPreviewDefinition.screenCameraPosition
                    && screenCamera.transform.rotation == selectionPreviewDefinition.screenCameraRotation
                    && screenCamera.transform.localScale == selectionPreviewDefinition.screenCameraScale
                    && Mathf.Approximately(screenCamera.fieldOfView, selectionPreviewDefinition.fieldOfView)
                    && Mathf.Approximately(screenCamera.nearClipPlane, selectionPreviewDefinition.screenCameraNear)
                    && Mathf.Approximately(screenCamera.farClipPlane, selectionPreviewDefinition.screenCameraFar)
                    && Mathf.Approximately(screenCamera.depth, selectionPreviewDefinition.screenCameraDepth),
                    "selection screen camera uses source transform, projection range and depth");
                var background = menuPresentation.SelectionBackground;
                var sourceBackground = NativeContent.Load<GameObject>("NativeSelectionPreview/screen-background");
                var backgroundRenderer = background ? background.GetComponent<SpriteRenderer>() : null;
                var sourceBackgroundRenderer = sourceBackground ? sourceBackground.GetComponent<SpriteRenderer>() : null;
                ModeCheck(background && background.activeInHierarchy && backgroundRenderer && backgroundRenderer.enabled
                    && sourceBackgroundRenderer && backgroundRenderer.sprite == sourceBackgroundRenderer.sprite
                    && backgroundRenderer.sharedMaterial == sourceBackgroundRenderer.sharedMaterial
                    && backgroundRenderer.color == sourceBackgroundRenderer.color
                    && backgroundRenderer.sortingOrder == sourceBackgroundRenderer.sortingOrder
                    && backgroundRenderer.sortingLayerID == sourceBackgroundRenderer.sortingLayerID,
                    "selection renders the original Square sprite, material, tint and sorting fields");
                ModeCheck(background && sourceBackground && background.transform.position == sourceBackground.transform.position
                    && background.transform.rotation == sourceBackground.transform.rotation
                    && background.transform.localScale == sourceBackground.transform.localScale
                    && screenCamera && screenCamera.cullingMask == 1 << background.layer,
                    "selection Square retains source transform and its isolated camera layer");
                var authoredSelection = GameObject.Find("selection authored Canvas");
                var authoredBackground = authoredSelection ? authoredSelection.transform.Find("bg") : null;
                ModeCheck(authoredBackground && !authoredBackground.gameObject.activeSelf,
                    "selection keeps the original full-screen UI background inactive");
                int originalCharacterChoice = hud.SelectedMenuIndex;
                yield return CheckCharacterArrowInput(keyboard, ModeCheck);
                hud.SelectMenuIndex(originalCharacterChoice);
                yield return new WaitForEndOfFrame();
                yield return MenuParityKey(keyboard, Key.RightArrow);
                ModeCheck(vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == "marisa", "original Reimu slot navigates right to Marisa");
                yield return MenuParityKey(keyboard, Key.LeftArrow);
                ModeCheck(hud.SelectedMenuIndex == originalCharacterChoice, "original character navigation returns to Reimu");
                yield return MenuParityKey(keyboard, Key.Enter);
                MenuParityRoute(NativeMenuPage.Vehicles, selectedVehicleIndex, "Grand Prix character confirms to Vehicle");
                yield return MenuParityKey(keyboard, Key.Enter);
                MenuParityRoute(NativeMenuPage.Skins, selectedSkinIndex, "Vehicle confirms to Skin");
                int retainedVehicle = selectedVehicleIndex;
                yield return MenuParityKey(keyboard, Key.RightArrow);
                int retainedSkin = hud.SelectedMenuIndex;
                ModeCheck(retainedSkin > 0, "Skin selector changes to original alternate model");
                yield return MenuParityScreenshot("menu-parity-skin");
                yield return MenuParityKey(keyboard, Key.Enter);
                MenuParityRoute(NativeMenuPage.Cups, 0, "Skin confirms to Cup");
                yield return MenuParityScreenshot("menu-parity-cup");
                yield return MenuParityKey(keyboard, Key.Escape);
                ModeCheck(hud.MenuPage == NativeMenuPage.Characters && selectedVehicleIndex == retainedVehicle && selectedSkinIndex == retainedSkin,
                    "Cup cancellation returns Character retaining Vehicle and Skin");
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                MenuParityRoute(NativeMenuPage.Skins, retainedSkin, "retained Skin is restored after Vehicle confirm");
                yield return MenuParityKey(keyboard, Key.Escape);
                MenuParityRoute(NativeMenuPage.Vehicles, retainedVehicle, "Skin cancellation returns retained Vehicle");
                yield return MenuParityKey(keyboard, Key.Escape);
                yield return MenuParityKey(keyboard, Key.Escape);
                MenuParityRoute(NativeMenuPage.Main, 0, "first Character module cancellation returns Main");
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.Characters && selectedMode == NativeSessionMode.QuickRace,
                    "Quick Race confirm routes to Character");
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.Tracks, "Quick Race Cup confirms to Circuit");
                yield return MenuParityScreenshot("menu-parity-tracks");
                ModeCheck(hud.OriginalTrackBannersBound(out string bannerDetail), "original circuit banners " + bannerDetail);
                yield return MenuParityKey(keyboard, Key.Escape);
                MenuParityRoute(NativeMenuPage.Cups, 0, "Circuit cancellation returns Cup");
                yield return MenuParityKey(keyboard, Key.Escape);
                yield return MenuParityKey(keyboard, Key.Escape);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.Characters && selectedMode == NativeSessionMode.CustomRace,
                    "Custom confirms to Character");
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.Custom, "Custom Circuit confirms to Customization");
                yield return MenuParityScreenshot("menu-parity-custom");
                yield return MenuParityKey(keyboard, Key.Escape);
                MenuParityRoute(NativeMenuPage.Cups, 0, "Customization cancellation returns Cup");
                yield return MenuParityKey(keyboard, Key.Escape);
                yield return MenuParityKey(keyboard, Key.Escape);
                MenuParityRoute(NativeMenuPage.Main, 0, "Custom Character cancellation returns Main");
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.Characters && selectedMode == NativeSessionMode.TimeTrials,
                    "Time Trials confirms to Character with one human");
                ModeCheck(!hud.AdditionalPlayersCanJoin, "Time Trials other player boxes remain blocked");
                yield return MenuParityKey(keyboard, Key.Escape);
                MenuParityRoute(NativeMenuPage.Main, 1, "Time Trials Character cancellation restores Main focus");
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityOptionTransitionKey(keyboard, Key.Enter, "Online", false);
                ModeCheck(hud.MenuPage == NativeMenuPage.Online && Net && Net.Open, "Online opens native networking page");
                yield return MenuParityScreenshot("menu-parity-online");
                var onlineFirst = hud.NetworkMenuFocus;
                yield return new WaitForSecondsRealtime(.12f);
                yield return new WaitForEndOfFrame();
                var onlineNext = hud.NetworkMenuFocus;
                ModeCheck(onlineFirst != null && onlineNext != null && Mathf.Abs(Mathf.DeltaAngle(onlineFirst.wheelAngle, onlineNext.wheelAngle)) > 4,
                    "Online original wheel continues rotating after entry");
                ModeCheck(onlineNext != null && Mathf.Abs(onlineNext.selectorY - onlineNext.buttonRects[onlineNext.index].center.y - 16f / 1.8f) < .1f,
                    "Online authored selector aligns with focused button");
                yield return MenuParityOptionTransitionKey(keyboard, Key.Escape, "Online", true);
                ModeCheck(hud.MenuPage == NativeMenuPage.Main && Net && !Net.Open, "Online cancellation returns Main");
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityOptionTransitionKey(keyboard, Key.Enter, "Extras", false);
                ModeCheck(hud.MenuPage == NativeMenuPage.Extras, "Extras opens original submenu");
                yield return MenuParityScreenshot("menu-parity-extras");
                ModeCheck(hud.OriginalMenuVisualsBound("ExtrasMenu", out string extrasVisuals), extrasVisuals);
                ModeCheck(Mathf.Abs(hud.MenuFocus.selectorY - hud.MenuFocus.buttonRects[hud.MenuFocus.index].center.y - 16f / 1.8f) < .1f,
                    "Extras actual authored selector pivot aligns with focused button");
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.Credits, "Credits opens original art and text");
                yield return MenuParityScreenshot("menu-parity-credits");
                yield return MenuParityKey(keyboard, Key.Escape);
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(hud.MenuPage == NativeMenuPage.MusicRoom && NativeMusicRoomData.Count == 8, "Music Room opens original eight entries");
                string[] sourceMusicOrder = { "Menu", "CharSelect", "Forest", "Mountains", "Purplefield", "WistyLake", "Results", "FullPower" };
                for (int musicIndex = 0; musicIndex < sourceMusicOrder.Length; musicIndex++)
                    ModeCheck(NativeMusicRoomData.At(musicIndex)?.id == sourceMusicOrder[musicIndex], "Music Room source order " + musicIndex);
                ModeCheck(!NativeAudioRuntime.MusicPaused, "Music Room opens with unpaused default track");
                yield return MenuParityScreenshot("menu-parity-music-room");
                ModeCheck(hud.OriginalMenuVisualsBound("MusicRoom", out string musicVisuals), musicVisuals);
                string playingMusic = NativeAudioRuntime.CurrentMusicRoomPreview;
                yield return MenuParityKey(keyboard, Key.DownArrow);
                ModeCheck(NativeAudioRuntime.CurrentMusicRoomPreview == playingMusic, "Music Room navigation preserves playing track until Select");
                yield return new WaitForEndOfFrame();
                bool originalCoverBound = false;
                foreach (var image in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>())
                    if (image.name == "label" && image.transform.parent && image.transform.parent.name == "labelmask" &&
                        image.transform.parent.parent && image.transform.parent.parent.name == "Disc")
                        originalCoverBound |= image.sprite && image.sprite.texture == NativeMusicRoomData.At(hud.MusicSelection).cover.texture;
                ModeCheck(originalCoverBound, "Music Room original masked disc label binds selected cover");
                yield return MenuParityKey(keyboard, Key.Enter);
                ModeCheck(NativeAudioRuntime.CurrentMusicRoomPreview == NativeMusicRoomData.At(hud.MusicSelection).id, "Music Room Select plays source entry");
                yield return MenuParityKey(keyboard, Key.Space);
                ModeCheck(NativeAudioRuntime.MusicPaused, "Music Room Space pauses");
                yield return MenuParityKey(keyboard, Key.Space);
                ModeCheck(!NativeAudioRuntime.MusicPaused, "Music Room Space resumes");
                yield return NativeAudioRuntime.VerifyMusicRoomPlayback(hud.MusicSelection, ModeCheck);
                yield return MenuParityKey(keyboard, Key.Escape);
                yield return MenuParityOptionTransitionKey(keyboard, Key.Escape, "Extras", true);
                MenuParityRoute(NativeMenuPage.Main, 3, "Extras returns to original focus");
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityOptionTransitionKey(keyboard, Key.Enter, "Options", false);
                ModeCheck(settingsOpen, "Options confirm opens actual Settings");
                yield return MenuParityScreenshot("menu-parity-settings");
                ModeCheck(hud.OriginalMenuVisualsBound("OptionsMenu", out string optionsVisuals), optionsVisuals);
                ModeCheck(Mathf.Abs(hud.MenuFocus.selectorY - hud.MenuFocus.buttonRects[hud.MenuFocus.index].center.y - 16f / 1.8f) < .1f,
                    "Options actual authored selector pivot aligns with focused button");
                yield return MenuParityOptionTransitionKey(keyboard, Key.Escape, "Options", true);
                MenuParityRoute(NativeMenuPage.Main, 4, "Settings back restores Options focus");
                yield return MenuParityScreenshot("menu-parity-settings-returned");
                yield return new WaitForEndOfFrame();
                CaptureMenuParityFocus("main-settings-returned");
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.DownArrow);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityScreenshot("menu-parity-vehicles");
                int originalVehicleChoice = hud.SelectedMenuIndex;
                yield return MenuParityKey(keyboard, Key.RightArrow);
                ModeCheck(hud.SelectedMenuIndex == (originalVehicleChoice + 1) % vehicleRoster.VehicleCount, "original vehicle selector moves right");
                yield return MenuParityKey(keyboard, Key.LeftArrow);
                ModeCheck(hud.SelectedMenuIndex == originalVehicleChoice, "original vehicle selector moves left");
                var ownerService=NativeLocalSelectionOwners.Instance??NativeLocalSelectionOwners.Create();
                if(ownerService.Players.Length==0 && !ownerService.Begin(false,keyboard))throw new InvalidOperationException("Menu parity race requires actual fixture keyboard PIM owner");
                ownerService.End();ownerService.MarkDiagnosticOwner(true);
                var fixtureOwner=ownerService.AtSlot(0);
                ModeCheck(fixtureOwner&&fixtureOwner.user.valid&&ownerService.Paired(fixtureOwner,keyboard),"race fixture keyboard belongs to actual PIM/InputUser owner");
                if(!fixtureOwner||!fixtureOwner.user.valid||!ownerService.Paired(fixtureOwner,keyboard))throw new InvalidOperationException("Menu parity fixture owner device differs");
                uint fixtureUser=fixtureOwner.user.id;
                NativeInputBindings.SetActualLocalOwners(ownerService.Players);
                localSelectionVerification=true;keyboard.MakeCurrent();hud.SetMenuPage(NativeMenuPage.Characters);
                if(!BeginLocalSelection())throw new InvalidOperationException("Menu parity actual selection entry failed");
                ModeCheck(localSelection.ReadBox(0).InputUserId==checked((int)fixtureUser),"race actual selection uses the fixture stable owner");
                Debug.Log("NATIVE_MENU_PARITY_OWNER scope=NativeInjected actual PIM/User and Character/Vehicle/Skin/ready callbacks; physicalHumanAccepted=false user="+fixtureUser+" device="+keyboard.deviceId);
                for(int stage=0;stage<3;stage++)yield return MenuParityKey(keyboard, Key.Enter);
                var committedChoice=ownerService.SavedChoice(fixtureOwner);var committedBox=localSelection.ReadBox(0);
                bool committed=!localSelectionActive&&localSelection.AllReady&&committedBox.Selection==NativeLocalSelectionState.Stage.Ready&&committedChoice!=null
                    &&committedChoice.Character==committedBox.Character&&committedChoice.Vehicle==committedBox.Vehicle&&committedChoice.Skin==committedBox.Skin;
                ModeCheck(committed,"actual Character Vehicle Skin ready callbacks commit owner choice before mode config");
                if(!committed)throw new InvalidOperationException("Menu parity actual owner selection did not commit");
                MenuParityRoute(NativeMenuPage.Cups,0,"actual ready owner routes to Quick Race Cup");
                yield return MenuParityKey(keyboard, Key.Enter);
                yield return MenuParityKey(keyboard, Key.Enter);
                var receiver=UnityEngine.Object.FindObjectOfType<NativeRace>();
                float receiverDeadline=Time.realtimeSinceStartup+15;
                while((!receiver||receiver==this)&&Time.realtimeSinceStartup<receiverDeadline){yield return null;receiver=UnityEngine.Object.FindObjectOfType<NativeRace>();}
                ModeCheck(receiver&&receiver!=this,"race transition replaces the actual scene receiver");
                if(!receiver)throw new InvalidOperationException("Menu parity scene receiver missing");
                receiver.hud.SuppressPointerInput=true;
                ModeCheck(receiver.SourceActualLocalOwner(0)==fixtureOwner&&fixtureOwner.user.valid&&fixtureOwner.user.id==fixtureUser,
                    "race scene borrows the same actual fixture PlayerInput/User");
                ModeCheck(receiver.phase == Phase.Countdown && receiver.raceConfig.mode == NativeSessionMode.QuickRace &&
                    receiver.raceConfig.racerCount == 8 && receiver.raceConfig.laps == 5, "Quick Race menu starts eight racers and five laps");
                yield return receiver.MenuParityKey(keyboard, Key.Escape);
                ModeCheck(receiver.phase == Phase.Paused, "new menu race countdown pauses");
                yield return receiver.MenuParityScreenshot("menu-parity-pause");
                var pauseRoot = GameObject.Find("Original authored menus").transform.Find("GlobalHUD authored Canvas/Pause");
                var pausedText = pauseRoot.Find("Paused").GetComponent<TMPro.TMP_Text>();
                var selectText = pauseRoot.Find("Prompt").GetComponent<TMPro.TMP_Text>();
                var resumeText = pauseRoot.Find("Prompt (1)").GetComponent<TMPro.TMP_Text>();
                ModeCheck(pausedText.text == "Paused" && selectText.text == "Select" && resumeText.text == "Resume",
                    "visible source pause title and prompts contain translated text");

                yield return receiver.MenuParityKey(keyboard, Key.DownArrow);
                yield return receiver.MenuParityKey(keyboard, Key.DownArrow);
                yield return receiver.MenuParityKey(keyboard, Key.Enter);
                ModeCheck(receiver.phase == Phase.Menu && receiver.hud.MenuPage == NativeMenuPage.Main && NativeGameSession.Current == null,
                    "pause returns from new menu race to clean main phase=" + receiver.phase + " page=" + receiver.hud.MenuPage
                    + " session=" + (NativeGameSession.Current == null ? "null" : NativeGameSession.Current.sessionId));
                UnityEngine.Object.Destroy(ownerService.gameObject);yield return null;yield return null;
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                var receiver=UnityEngine.Object.FindObjectOfType<NativeRace>();
                if(receiver)receiver.hud.SuppressPointerInput=pointerSuppressed;
                Application.logMessageReceived-=observeErrors;
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
            ModeCheck(Time.realtimeSinceStartup - menuParityStarted < 40, "targeted check finishes within forty seconds");
            ModeCheck(observedEngineErrors == 0, "runtime errors=" + observedEngineErrors);
            File.WriteAllText(Path.Combine(reportDirectory, "menu-parity-report.json"), JsonUtility.ToJson(new MenuParityReport {
                passed = modePassed, checks = modeChecks.ToArray(), focusSamples = menuParitySamples.ToArray(),
                wallSeconds = Time.realtimeSinceStartup - menuParityStarted
            }, true) + "\n");
            Application.Quit(modePassed ? 0 : 2);
        }
#else
        internal IEnumerator CheckMenuParityLive() { throw new InvalidOperationException("Input System package is required for Player verification."); }
#endif
    }
}
