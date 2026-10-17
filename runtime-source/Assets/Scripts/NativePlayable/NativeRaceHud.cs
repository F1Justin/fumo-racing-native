using System;
using System.Linq;
using UnityEngine;
using TMPro;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed partial class NativeRaceHud
    {
        public NativeAuthoredUiData authoredUiData;
        [NonSerialized] NativeAuthoredUi authoredUi;
        public Font font;
        public NativeUiFontData menuFont, hudFont;
        public Material uiImageMaterial;
        [NonSerialized] NativeUiCanvas canvas;
        NativeUiFontData activeFont;
        public Texture2D timeBackground, lapBackground, placeBackground, speedBackground;
        public Texture2D speedBar, speedFill, itemBox, logo, wheel, selector;
        public Texture2D raceButton, raceButtonHover, menuButton, menuButtonHover;
        public Texture2D soloButton, soloButtonHover;
        public Texture2D midButton, midButtonHover, topButton, topButtonHover, lowButton, lowButtonHover, focusSelector;
        public Sprite resultPanel, resultOutline, resultEntry, keyConfirm, keyCancel;
        public AudioClip menuMoveSound, menuConfirmSound, menuCancelSound, gameModeConfirmSound;
        public Texture2D[] characterIcons;
        public bool showItemSlot;
        public Matrix4x4 minimapWorldToLocal = Matrix4x4.identity;
        GUIStyle hitArea;
        [NonSerialized] NativeMenuNavigation navigation;
        [NonSerialized] AudioSource menuAudio;
        [NonSerialized] Sprite playerCharacterIcon;
        readonly float[] buttonFrom = new float[8], buttonTo = new float[8], buttonStarted = new float[8];
        readonly AnimationCurve buttonIn = new AnimationCurve(new Keyframe(0, 0, 1.9287574f, 1.9287574f),
            new Keyframe(0.5058899f, 0.9103776f, 0.4229123f, 0.4229123f), new Keyframe(1, 1, 0, 0));
        readonly AnimationCurve buttonOut = new AnimationCurve(new Keyframe(0, 0, 0, 0), new Keyframe(1, 1, 2, 2));
        Vector2 lastPointer;
        int pointerFrame = -1;
        bool pointerMoved;
        NativeMenuNavigation Navigation => navigation ?? (navigation = new NativeMenuNavigation());
        public int SelectedMenuIndex => Navigation.SelectedIndex;
        public NativeMenuPage MenuPage => Navigation.Page;
        public bool SuppressPointerInput { get; set; }
        string[] mainLabels = { "Race!", "Quit" };
        public int CurrentLap = 1, RaceLapCount = 1;
        public bool RaceFinished, ShowRaceLapTimes, ShowRaceRank = true;
        public System.Collections.Generic.IList<double> RaceLapSeconds = Array.Empty<double>();
        bool raceLapTimesRequested;
        string countdownValue;
        float countdownStarted;
        int countdownFrame = -1;
        public string TrackSummary = "4 racers / 1 lap";
        public void ConfigureMainMenu(string[] labels, NativeMenuAction[] actions)
        {
            mainLabels = (string[])labels.Clone(); Navigation.MainActions = (NativeMenuAction[])actions.Clone();
            Navigation.MainDisabled = Array.ConvertAll(actions, action => action == NativeMenuAction.Online);
        }

        public void ConfigureChoiceCounts(int characterCount, int vehicleCount)
        {
            Navigation.CharacterCount = Mathf.Max(1, characterCount);
            Navigation.VehicleCount = Mathf.Max(1, vehicleCount);
        }
        public void ConfigureSkinChoices(int skinCount) { Navigation.SkinCount = Mathf.Max(1, skinCount); }

        public void SelectMenuIndex(int index) { Navigation.Select(index); }
        public void SetPlayerCharacterIcon(Sprite icon) { playerCharacterIcon = icon; }

        public void AttachAudio(AudioSource source)
        {
            menuAudio = source;
            if (!source) return;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.pitch = 1;
            source.volume = 0.9f;
            source.ignoreListenerPause = true;
        }

        public void SetMenuPage(NativeMenuPage page)
        {
            var previous = Navigation.Page;
            if (!Navigation.SetPage(page)) return;
            NativeTelemetry.SetContext("menuPage", page.ToString());
            NativeTelemetry.Event("ui.page.changed", "applied", detail: previous + "->" + page);
            authoredUi?.ResetDiscordPointerForPage(page == NativeMenuPage.Main);
            ChoosingSkin = page == NativeMenuPage.Skins;
            if(page==NativeMenuPage.Tracks) originalTrackBannerSelection=-1;
            BeginMenuTransition(previous, page);
            Array.Clear(buttonFrom, 0, buttonFrom.Length);
            Array.Clear(buttonTo, 0, buttonTo.Length);
            Array.Clear(buttonStarted, 0, buttonStarted.Length);
            ResetMenuFocus();
        }

        public void NavigateMenu(NativeMenuPage page, int delta)
        {
            SetMenuPage(page);
            if (Navigation.Move(delta)) PlaySelectionSound(page);
        }

        public NativeMenuAction ConfirmMenu(NativeMenuPage page)
        {
            SetMenuPage(page);
            NativeMenuAction action = Navigation.Confirm();
            if (action != NativeMenuAction.None)
            {
                if (action == NativeMenuAction.TimeTrials || page == NativeMenuPage.RaceModes
                    && (action == NativeMenuAction.GrandPrix || action == NativeMenuAction.QuickRace || action == NativeMenuAction.CustomRace))
                    PlaySound(gameModeConfirmSound);
                else if (page == NativeMenuPage.Characters) PlayOriginalSelectionSound(NativePlayerBoxSoundData.CharacterSelect);
                else if (page == NativeMenuPage.Vehicles || page == NativeMenuPage.Skins) PlayOriginalSelectionSound(NativePlayerBoxSoundData.PlayerBoxSelect);
                else PlaySound(page == NativeMenuPage.Pause ? menuMoveSound : menuConfirmSound);
            }
            return action;
        }

        public NativeMenuAction CancelMenu(NativeMenuPage page)
        {
            SetMenuPage(page);
            NativeMenuAction action = Navigation.Cancel();
            if (action != NativeMenuAction.None)
            {
                if (page == NativeMenuPage.Vehicles || page == NativeMenuPage.Skins) PlayOriginalSelectionSound(NativePlayerBoxSoundData.PlayerBoxCancel);
                else PlaySound(menuCancelSound);
            }
            return action;
        }

        void PlaySelectionSound(NativeMenuPage page)
        {
            if (page == NativeMenuPage.Characters) PlayOriginalSelectionMoveSound(false);
            else PlaySound(page == NativeMenuPage.Pause ? menuConfirmSound : menuMoveSound);
        }
        void PlaySound(AudioClip clip) { NativeAudioRuntime.PlayUi(menuAudio, clip); }

        public float BeginFrame(bool preserveAuthoredPage = false)
        {
            if (hitArea == null) hitArea = new GUIStyle();
            if (!usingOnlineAuthoring)
            {
                if (canvas == null) canvas = new NativeUiCanvas(uiImageMaterial);
                canvas.Begin();
            }
            else canvas?.SetVisible(false);
            EnsureAuthoredMenus();
            PrepareMenu2SourceRenderedConsumers();
            if (usingOnlineAuthoring && !preserveAuthoredPage) authoredUi?.Hide();
            authoredUi?.BeginPageFrame(preserveAuthoredPage || usingOnlineAuthoring);
            BindLocalViewCanvases();
            raceLapTimesRequested = false;
            restoredRankRequested = false;
            raceMarkerRacers = null;
            activeFont = hudFont;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * (Screen.height / 600f));
            GUI.color = Color.white;
            if (pointerFrame != Time.frameCount)
            {
                Vector2 pointer = Input.mousePosition;
                pointerMoved = Application.isFocused && !SuppressPointerInput && pointerFrame >= 0 && (pointer - lastPointer).sqrMagnitude > 0.01f;
                lastPointer = pointer;
                pointerFrame = Time.frameCount;
            }
            return Screen.width * 600f / Screen.height;
        }

        public void EndFrame()
        {
            authoredUi?.EndPageFrame();
            if (countdownFrame != Time.frameCount) ResetCountdown();
            authoredUi?.SetActive("GlobalHUD/Canvas/v", false);
            authoredUi?.SetActive("menu2/Canvas/MainMenu/discordserverbutton", false);
            authoredUi?.SetActive("menu2/Canvas/MainMenu/tooltip", false);
            if (raceLapTimesRequested && authoredUi != null)
            {
                const string root = "HUD/Canvas/lapTimes";
                authoredUi.SetActive(root, true);
                for (int lap = 0; lap < 5; lap++)
                {
                    string key = root + "/LapTime" + (lap == 0 ? "" : " (" + lap + ")");
                    authoredUi.SetActive(key, true);
                    authoredUi.Size(key, new Vector2(236.47f, 23.1533f));
                    authoredUi.Offset(key, new Vector2(0, -(117.1003f - (lap + 1) * 23.1533f) / 1.8f));
                    authoredUi.SetText(key, OriginalTimeText(lap < RaceLapSeconds.Count ? RaceLapSeconds[lap] : 0, 16));
                }
            }
            if (raceMarkerRacers != null)
            {
                if (!localViewCamera) DrawOriginalMinimap(raceMarkerRacers);
                if (!RaceFinished) DrawOriginalFrontIndicators(raceMarkerRacers);
                DrawRestoredRaceIndicators(raceMarkerRacers);
                DrawRestoredRaceDynamics(raceMarkerRacers[0]);
            }
            else ResetRestoredRaceHud();
            if (restoredRankRequested) authoredUi?.RestoreOriginalRaceRankVisibility();
            authoredUi?.RefreshInputPrompts();
            if (!usingOnlineAuthoring && canvas != null) canvas.End();
        }
        public void SetVisible(bool visible)
        {
            if (canvas != null) canvas.SetVisible(visible);
            if (!visible) { ResetCountdown(); authoredUi?.Hide(); }
        }

        void ResetCountdown()
        {
            countdownValue = null;
            countdownFrame = -1;
            authoredUi?.ResetCountdownAnimation();
        }

        public void Label(Rect rect, string value, float size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft, float stroke = 1.6f, string sourcePath = null)
        {
            canvas.Label(rect, NativeLocalization.TranslateText(value), size, color, alignment, activeFont ?? hudFont, sourcePath: sourcePath);
        }

        void OriginalLabel(Rect rect, string value, float size, Color color, TextAlignmentOptions alignment, bool richText = false, bool enableKerning = true)
        {
            canvas.Label(rect, NativeLocalization.TranslateText(value), size, color, TextAnchor.MiddleLeft, activeFont ?? hudFont, alignment, richText, enableKerning: enableKerning);
        }

        public void Picture(Rect rect, Texture texture, float alpha = 1)
        {
            canvas.Picture(rect, texture, new Color(1, 1, 1, alpha));
        }

        public bool Button(Rect rect, string caption, bool primary = false)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Picture(rect, primary ? (hover ? raceButtonHover : raceButton) : (hover ? menuButtonHover : menuButton));
            Label(rect, caption, primary ? 54 : 32, Color.white, TextAnchor.MiddleCenter, 1);
            return PointerButton(rect);
        }

        public void Shade(float width, float alpha)
        {
            Fill(new Rect(0, 0, width, 600), new Color(0, 0, 0, alpha));
        }

        public void MenuBackdrop(string sourcePath = null)
        {
            canvas.Picture(new Rect(21.63f, 29.32f, 440.75f, 155.8f), logo, Color.white, sourcePath: sourcePath);
        }



        public NativeMenuAction TrackMenu(float width)
        {
            return AuthoredTracks(width);
        }

        public NativeMenuAction CharacterMenu(float width, NativeVehicleRoster roster)
        {
            return AuthoredCharacters(width, roster, Enumerable.Range(0, roster.CharacterCount).ToArray());
        }

        public NativeMenuAction CharacterMenu(float width, NativeVehicleRoster roster, int[] choices)
        {
            return AuthoredCharacters(width, roster, choices);
        }

        public NativeMenuAction VehicleMenu(float width, NativeVehicleRoster roster)
        {
            return AuthoredVehicles(width, roster);
        }

        NativeMenuAction ChoiceMenu(float width, NativeMenuPage page, string title, int count,
            Func<int, string> labelAt, string hint)
        {
            SetMenuPage(page);
            activeFont = menuFont;
            Shade(width, 0.7f);
            Label(new Rect(width / 2 - 330, 80, 660, 70), title, 50, Color.white, TextAnchor.MiddleCenter);
            int first = Mathf.Clamp(SelectedMenuIndex - 3, 0, Mathf.Max(0, count - 7));
            int last = Mathf.Min(count, first + 7);
            NativeMenuAction action = NativeMenuAction.None;
            for (int i = first; i < last; i++)
                if (MenuTextButton(i, new Rect(width / 2 - 290, 169 + (i - first) * 48, 580, 47), labelAt(i)))
                    action = ConfirmMenu(page);
            Label(new Rect(width / 2 - 290, 509, 580, 35), hint + "  " + (SelectedMenuIndex + 1) + "/" + count,
                22, Color.white, TextAnchor.MiddleCenter);
            Prompt(new Rect(width / 2 - 176, 554.67f, 155, 45.33f), keyConfirm, "Select", 24);
            Prompt(new Rect(width / 2 + 22, 554.67f, 160, 45.33f), keyCancel, "Back", 24);
            return action;
        }

        public NativeMenuAction PauseMenu(float width)
        {
            SetMenuPage(NativeMenuPage.Pause);
            activeFont = menuFont;
            authoredUi.Show("GlobalHUD", "Pause");
            string[] labels = { NativeLocalization.Get("PAUSE_RESUME", "Resume"), NativeLocalization.Get("PAUSE_RESTART", "Restart"), NativeLocalization.Get("PAUSE_QUIT", "Quit") };
            string[] paths = { "Resume", "Restart", "Quit" };
            NativeMenuAction action = NativeMenuAction.None;
            for (int i = 0; i < paths.Length; i++)
            {
                string key = "GlobalHUD/Canvas/Pause/" + paths[i];
                var rect = authoredUi.RectAt(key); HoverMenu(i, rect);
                authoredUi.SetText(key, labels[i]);
                authoredUi.TextColor(key, SelectedMenuIndex == i ? new Color(1, .77873516f, 0) : Color.white);
                if (!SuppressPointerInput && PointerButton(rect)) { Navigation.Select(i); action = ConfirmMenu(MenuPage); }
            }
            return action;
        }

        public NativeMenuAction ResultsMenu(float width, float[] finishTimes, string[] names)
        {
            SetMenuPage(NativeMenuPage.Results);
            activeFont = menuFont;
            Shade(width, 0.78431374f);
            var panel = new Rect(width / 2 - 369.72f, 63.55f, 739.44f, 519.24f);
            SpritePicture(panel, resultPanel, true);
            Label(new Rect(width / 2 - 223.5f, 17.21f, 447, 45.33f), "RESULTS", 32, Color.white, TextAnchor.MiddleCenter);
            int rank = 1;
            foreach (int racer in Enumerable.Range(0, finishTimes.Length).OrderBy(i => finishTimes[i] > 0 ? finishTimes[i] : float.MaxValue))
            {
                ResultRow(new Rect(width / 2 - 369.72f, 63.55f + (rank - 1) * 64.71f, 740.68f, 68), rank, racer, names[racer], finishTimes[racer]);
                rank++;
            }
            NativeMenuAction action = NativeMenuAction.None;
            string[] labels = { "Race again", "Main menu", "Quit to desktop" };
            for (int i = 0; i < labels.Length; i++)
                if (MenuTextButton(i, new Rect(width / 2 - 250, 365 + i * 53.69f, 500, 53.69f), labels[i])) action = ConfirmMenu(MenuPage);
            SpritePicture(panel, resultOutline, true);
            Prompt(new Rect(width / 2 - 75, 537, 150, 40), keyConfirm, "Select", 24);
            return action;
        }

        bool MenuTextButton(int index, Rect rect, string caption)
        {
            HoverMenu(index, rect);
            Color color = SelectedMenuIndex == index ? new Color(1, 0.77873516f, 0) : Color.white;
            Label(rect, caption, 32, color, TextAnchor.MiddleCenter, 1);
            if (SuppressPointerInput || !PointerButton(rect)) return false;
            Navigation.Select(index);
            return true;
        }

        void HoverMenu(int index, Rect rect)
        {
            // A stationary cursor must leave the keyboard selection intact.
            if (!SuppressPointerInput && Application.isFocused && (pointerMoved || Event.current.type == EventType.MouseDown) && rect.Contains(Event.current.mousePosition)
                && Navigation.Select(index))
            {
                if (MenuPage == NativeMenuPage.Characters) NotifyOriginalCharacterFocusSelected();
                if (MenuPage == NativeMenuPage.Tracks) HoverOriginalCircuitBanner(index);
                PlaySelectionSound(MenuPage);
            }
        }

        float ButtonAmount(int index, bool selected)
        {
            float target = selected ? 1 : 0;
            var motion = authoredUi?.Motion;
            float duration = motion ? (buttonTo[index] > 0 ? motion.menuFocusInSeconds : motion.menuFocusOutSeconds) : (buttonTo[index] > 0 ? .2f : .15f);
            float progress = Mathf.Clamp01((Time.unscaledTime - buttonStarted[index]) / Mathf.Max(.001f, duration));
            float value = Mathf.LerpUnclamped(buttonFrom[index], buttonTo[index],
                (motion ? (buttonTo[index] > 0 ? motion.menuFocusIn : motion.menuFocusOut) : (buttonTo[index] > 0 ? buttonIn : buttonOut)).Evaluate(progress));
            if (buttonTo[index] != target)
            {
                buttonFrom[index] = value;
                buttonTo[index] = target;
                buttonStarted[index] = Time.unscaledTime;
            }
            return value;
        }

        void Prompt(Rect rect, Sprite key, string caption, int size, string sourcePath = null)
        {
            var promptState = NativeMainPromptSource.Read(key == keyCancel ? NativeInputBindings.Action.Cancel : NativeInputBindings.Action.Ok);
            key = promptState.sprite;
            if (!promptState.spriteFound && !string.IsNullOrEmpty(promptState.displayBinding)) caption = "[" + promptState.displayBinding + "] " + caption;
            SpritePicture(new Rect(rect.x, rect.y + 5, rect.height - 10, rect.height - 10), key, sourcePath: sourcePath == null ? null : sourcePath + "/icon");
            NativeUiFontData previous = activeFont;
            activeFont = hudFont;
            Label(new Rect(rect.x + rect.height - 4, rect.y, rect.width - rect.height + 4, rect.height), caption, size, Color.white, sourcePath: sourcePath);
            activeFont = previous;
        }

        void SpritePicture(Rect rect, Sprite sprite, bool sliced = false, string sourcePath = null) { canvas.Sprite(rect, sprite, sliced, sourcePath); }
        void Fill(Rect rect, Color color) { canvas.Picture(rect, Texture2D.whiteTexture, color); }

        public void Countdown(string value)
        {
            if (authoredUi == null) return;
            const string key = "GlobalHUD/Canvas/starttext";
            authoredUi.ShowPages(key);
            authoredUi.SetText(key, value);
            if (countdownValue != value)
            {
                countdownValue = value;
                countdownStarted = Time.time;
            }
            countdownFrame = Time.frameCount;
            authoredUi.ApplyCountdownAnimation(Time.time - countdownStarted);
        }

        public void Race(float width, double seconds, int rank, float speed, Vector3[] route, NativeKartMotor[] racers)
        {
            activeFont = hudFont;
            // HUD.prefab uses a 600 px reference height with height matching enabled.
            if (ShowRaceRank)
            {
                restoredRankRequested = true;
                authoredUi.OriginalRaceRank(rank, racers.Length, RaceFinished);
            }
            raceLapTimesRequested = ShowRaceLapTimes;
            if (showItemSlot) Picture(new Rect(width / 2 - 50, 8.6f, 100, 100), itemBox);
            raceMarkerRacers = racers;
        }

        public void ResultRow(Rect rect, int rank, int racer, string name, float seconds)
        {
            SpritePicture(rect, resultEntry, true);
            Label(new Rect(rect.x + 5, rect.y + 4.72f, 67.29f, 60.25f), rank.ToString(), 50.55f, Color.white, TextAnchor.MiddleCenter);
            Rect icon = new Rect(rect.x + 75.57f, rect.y + 4.23f, 59.54f, 59.54f);
            if (racer == 0 && playerCharacterIcon)
                SpritePicture(icon, playerCharacterIcon);
            else if (characterIcons != null && racer < characterIcons.Length)
                Picture(icon, characterIcons[racer]);
            SpritePicture(new Rect(rect.x + 72.34f, rect.y + 1, 66, 66), resultOutline, true);
            Label(new Rect(rect.x + 138.34f, rect.y + 12.18f, 232, 45.33f), name.Replace(" / YOU", ""), 32, Color.white, TextAnchor.MiddleCenter);
            OriginalLabel(new Rect(rect.x + 370.34f, rect.y + 12.18f, 250.92f, 45.33f), seconds > 0 ? OriginalTimeText(seconds, 18) : "Racing...", 32,
                new Color(0.8820755f, 0.9228188f, 1), TextAlignmentOptions.Center, seconds > 0);
        }

        void Minimap(float width, Vector3[] route, NativeKartMotor[] racers) => DrawOriginalMinimap(racers);

        public static string TimeText(double seconds)
        {
            // The original IL2CPP TimeSpan.Interval rounds to whole milliseconds before splitting components.
            long milliseconds = (long)(seconds * 1000 + (seconds >= 0 ? 0.5 : -0.5));
            var time = new System.TimeSpan(milliseconds * System.TimeSpan.TicksPerMillisecond);
            return System.Math.Abs(time.Minutes).ToString("00") + ":" + System.Math.Abs(time.Seconds).ToString("00") + "." + System.Math.Abs(time.Milliseconds).ToString("000");
        }
        public static string OriginalTimeText(double seconds, int spacing)
        { return "<mspace=" + spacing + ">" + TimeText(seconds) + "</mspace>"; }
    }
}
