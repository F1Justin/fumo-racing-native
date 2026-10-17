using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        InputDevice originalMenuPointerDevice;
        int originalMenuPointerFrame=-1;
        public InputDevice ConsumeActualMenuPointerDevice()
        {
            var device=originalMenuPointerFrame==Time.frameCount ? originalMenuPointerDevice : null;
            originalMenuPointerDevice=null; originalMenuPointerFrame=-1; return device;
        }

        [Serializable] public sealed class MenuFocusSnapshot
        {
            public string page;
            public int index;
            public bool selectorVisible;
            public float selectorY, wheelAngle;
            public float[] buttonAmounts;
            public Rect[] buttonRects;
        }
        public MenuFocusSnapshot MenuFocus { get; private set; }
        readonly AnimationCurve selectorCurve = new AnimationCurve(
            new Keyframe(0, 0, 1.1909018f, 1.1909018f),
            new Keyframe(.58855224f, .79717046f, 1.0621257f, 1.0621257f),
            new Keyframe(1, 1, 0, 0));
        bool selectorReady;
        float selectorFrom, selectorTo, selectorStarted;

        void ResetMenuFocus() { selectorReady = false; }
        void LoadMenuTextures()
        {
            if (!midButton) midButton = NativeContent.Load<Texture2D>("NativeMenu/midbutton");
            if (!midButtonHover) midButtonHover = NativeContent.Load<Texture2D>("NativeMenu/midbuttonhover");
            if (!topButton) topButton = NativeContent.Load<Texture2D>("NativeMenu/topbutton");
            if (!topButtonHover) topButtonHover = NativeContent.Load<Texture2D>("NativeMenu/topbuttonhover");
            if (!lowButton) lowButton = NativeContent.Load<Texture2D>("NativeMenu/lowbutton");
            if (!lowButtonHover) lowButtonHover = NativeContent.Load<Texture2D>("NativeMenu/lowbuttonhover");
            if (!focusSelector) focusSelector = NativeContent.Load<Texture2D>("NativeMenu/selector");
        }
        float SelectorPosition(float target)
        {
            if (!selectorReady) { selectorFrom = selectorTo = target; selectorReady = true; }
            float value = Mathf.LerpUnclamped(selectorFrom, selectorTo,
                (authoredUi?.Motion ? authoredUi.Motion.menuSelectorCurve : selectorCurve).Evaluate(Mathf.Clamp01((Time.unscaledTime - selectorStarted) / (authoredUi?.Motion ? Mathf.Max(.001f, authoredUi.Motion.menuSelectorSeconds) : .15f))));
            if (target != selectorTo) { selectorFrom = value; selectorTo = target; selectorStarted = Time.unscaledTime; }
            return value;
        }
        static readonly string[] MainButtonPaths = {
            "WheelRoot/WheelOptions/RaceButton", "WheelRoot/WheelOptions/SoloButton",
            "LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/OnlineButton",
            "LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/ExtrasButton",
            "LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/OptionsButton" };
        public NativeMenuAction MainMenu(float width, float quitHold = 0)
        {
            SetMenuPage(NativeMenuPage.Main); activeFont = menuFont;
            const string root = "menu2/Canvas/MainMenu/";
            authoredUi.Show("menu2", "MainMenu", "/discordserverbutton", "/tooltip", "/Quitting2");
            var rectangles = new Rect[MainButtonPaths.Length];
            var amounts = new float[rectangles.Length];
            var action = NativeMenuAction.None;
            for (int i = 0; i < rectangles.Length; i++)
            {
                string key = root + MainButtonPaths[i];
                bool focused = SelectedMenuIndex == i;
                amounts[i] = ButtonAmount(i, focused);
                if (!authoredUi.ApplyMenuButton(key, amounts[i], focused, Navigation.IsDisabled(i))) continue;
                rectangles[i] = authoredUi.RectAt(key);
                HoverMenu(i, rectangles[i]);
                if (!Navigation.IsDisabled(i) && !SuppressPointerInput && PointerButton(rectangles[i]))
                {
                    originalMenuPointerDevice = Mouse.current;
                    originalMenuPointerFrame = Time.frameCount;
                    Navigation.Select(i); action = ConfirmMenu(MenuPage);
                }
            }
            const string selectorPivot = root + "LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot";
            authoredUi.Offset(selectorPivot, Vector2.zero);
            float target = SelectorPosition(rectangles[SelectedMenuIndex].center.y + MenuSelectorOffset);
            authoredUi.Offset(selectorPivot, new Vector2(0, target - authoredUi.RectAt(selectorPivot).center.y));
            LocalizeOriginalPrompts(root);
            ApplyMainQuitSourcePanel();
            if (Event.current.type == EventType.Repaint)
                MenuFocus = new MenuFocusSnapshot { page = "Main", index = SelectedMenuIndex,
                    selectorVisible = true, selectorY = target, wheelAngle = MenuWheelAngle(NativeMenuPage.Main),
                    buttonAmounts = amounts, buttonRects = rectangles };
            return OriginalPromptPointer(root, action);
        }
        float MenuSelectorOffset => authoredUi?.Motion ? authoredUi.Motion.selectorOffset : 16f / 1.8f;
        public NativeMenuAction RaceModeMenu(float width)
        {
            SetMenuPage(NativeMenuPage.RaceModes); activeFont = menuFont; LoadMenuTextures();
            authoredUi.Show("menu2", "RaceMenu");
            var rectangles = new Rect[RaceModeButtonNames.Length];
            var amounts = new float[rectangles.Length];
            var action = NativeMenuAction.None;
            for (int i = 0; i < rectangles.Length; i++)
            {
                string key = "menu2/Canvas/RaceMenu/OptionsRoot/OptionsPivot/" + RaceModeButtonNames[i];
                amounts[i] = ButtonAmount(i, SelectedMenuIndex == i);
                if (!authoredUi.ApplyMenuButton(key, amounts[i], SelectedMenuIndex == i)) continue;
                rectangles[i] = authoredUi.RectAt(key);
                HoverMenu(i, rectangles[i]);
                var pointer = Mouse.current != null && Mouse.current.enabled ? Mouse.current : null;
                if (!SuppressPointerInput && PointerButton(rectangles[i]))
                {
                    originalMenuPointerDevice = pointer; originalMenuPointerFrame = pointer != null ? Time.frameCount : -1;
                    Navigation.Select(i); action = ConfirmMenu(MenuPage);
                }
            }
            LocalizeOriginalPrompts("menu2/Canvas/RaceMenu/");
            if (Event.current.type == EventType.Repaint)
                MenuFocus = new MenuFocusSnapshot { page = "RaceModes", index = SelectedMenuIndex, selectorVisible = false,
                    wheelAngle = MenuWheelAngle(NativeMenuPage.RaceModes), buttonAmounts = amounts, buttonRects = rectangles };
            return OriginalPromptPointer("menu2/Canvas/RaceMenu/", action);
        }
        static readonly string[] RaceModeButtonNames = { "GrandPrixButton", "QuickRaceButton", "CustomButton" };
    }
}
