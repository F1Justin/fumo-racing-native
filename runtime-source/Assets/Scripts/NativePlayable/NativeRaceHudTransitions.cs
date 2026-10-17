using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        NativeMenuPage transitionFrom, transitionTo;
        float transitionStarted, transitionWheelAngle, retainedMainWheelAngle;
        bool transitionPending, transitionSelectorVisible;
        string telemetryTransitionOperation;
        float transitionSelectorY;
        MenuFocusSnapshot transitionSourceFocus;
        public void ClearMenuTransition() { if (transitionPending) NativeTelemetry.End(telemetryTransitionOperation, "ui.transition", "cancelled", "cleared"); telemetryTransitionOperation = null; transitionPending = false; }
        float MenuWheelAngle(NativeMenuPage page) => page == NativeMenuPage.RaceModes
            ? RenderSourceWheelAngle(RaceSourceWheel, sourceRaceWheelAngle)
            : RenderSourceWheelAngle(MainSourceWheel, sourceMainWheelAngle);
        public NativeMenuTransitionPlayback.State TransitionState { get; private set; }
        public bool MenuTransitionActive => transitionPending && Time.unscaledTime - transitionStarted < NativeMenuTransitionPlayback.Duration(transitionFrom.ToString(), transitionTo.ToString());
        void BeginMenuTransition(NativeMenuPage from, NativeMenuPage to)
        {
            if (!NativeMenuTransitionPlayback.Supports(from.ToString(), to.ToString())) return;
            if (transitionPending) NativeTelemetry.End(telemetryTransitionOperation, "ui.transition", "cancelled", "superseded");
            telemetryTransitionOperation = NativeTelemetry.Begin("ui.transition", from + "->" + to);
            transitionFrom = from; transitionTo = to; transitionStarted = Time.unscaledTime; transitionPending = true;
            transitionWheelAngle = MenuWheelAngle(from);
            menu2SourceWheelHandoff = false;
            ResetSourceMainTween();
            var focus = from == NativeMenuPage.Online ? NetworkMenuFocus : MenuFocus;
            transitionSourceFocus = focus != null && focus.page == from.ToString() ? focus : null;
            transitionSelectorVisible = focus != null && focus.page == from.ToString() && focus.selectorVisible;
            transitionSelectorY = transitionSelectorVisible ? focus.selectorY : 0;
            if (from == NativeMenuPage.Main) retainedMainWheelAngle = transitionWheelAngle;
        }
        public bool DrawMenuTransition(float width)
        {
            if (!transitionPending) return false;
            TransitionState = NativeMenuTransitionPlayback.Evaluate(transitionFrom, transitionTo,
                Time.unscaledTime - transitionStarted, width, transitionWheelAngle, retainedMainWheelAngle);
            if (TransitionState.completed)
            {
                transitionPending = false;
                NativeTelemetry.End(telemetryTransitionOperation, "ui.transition", "completed");
                telemetryTransitionOperation = null;
                if (transitionFrom == NativeMenuPage.RaceModes && transitionTo == NativeMenuPage.Main) SyncSourceMainWheelAngleFromTransform();
                return false;
            }
            LoadMenuTextures(); var state = TransitionState;
            string destination = transitionFrom == NativeMenuPage.Main ? transitionTo.ToString() : transitionFrom.ToString();
            string originalPage = destination == "RaceModes" ? "RaceMenu" : destination == "Options" || destination == "Online" ? "OptionsMenu" : "ExtrasMenu";
            authoredUi.ShowPages(true, "menu2/Canvas/MainMenu", "menu2/Canvas/" + originalPage);
            string main = "menu2/Canvas/MainMenu/", other = "menu2/Canvas/" + originalPage + "/";
            authoredUi.SetActive(main.TrimEnd('/'), state.mainVisible);
            authoredUi.SetActive(other.TrimEnd('/'), state.destinationVisible);
            authoredUi.SetActive(main + "WheelRoot/WheelOptions", state.mainUpperVisible);
            authoredUi.SetActive(main + "LowerOptionsRoot", state.mainLowerVisible);
            authoredUi.SetActive(main + "Logo", state.logoVisible);
            authoredUi.SetActive(main + "Prompts", state.mainPromptsVisible);
            authoredUi.SetActive(other + "Prompts", state.destinationPromptsVisible);
            LocalizeOriginalPrompts(main);
            LocalizeOriginalPrompts(other);
            if (originalPage == "OptionsMenu") authoredUi.SetActive(other + "LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/AssistButton", false);
            if (destination == "Online") SetOnlineWheelCaptions();
            else if (originalPage == "OptionsMenu") RestoreOptionsWheelCaptions();
            else if (originalPage == "ExtrasMenu") BindExtrasWheelCaptions();
            ApplyTransitionButtons(main, other, state);
            ApplyMainQuitSourcePanel();
            authoredUi.Offset(main + "Logo", new Vector2(0,state.logoOffsetY));
            string lower = "LowerOptionsRoot/LowerOptionsPivot/";
            string sourceSelector = (transitionFrom == NativeMenuPage.Main ? main : other) + lower + "SelectorBG/SelectorPivot";
            if (transitionSelectorVisible)
            {
                float authoredY = authoredUi.RectAt(sourceSelector).center.y;
                authoredUi.Offset(sourceSelector, new Vector2(0, (transitionSelectorY - authoredY) * (1 - state.selectorResetAmount)));
            }
            if (originalPage == "RaceMenu")
            {
                authoredUi.Offset(main + lower.TrimEnd('/'), state.mainLowerOffset);
                authoredUi.Size(main + "WheelRoot", authoredUi.Definition(main + "WheelRoot").size + new Vector2(state.mainUpperClip.width * NativeMenuTransitionPlayback.SourceScale - 1895.9755f, 0));
                authoredUi.Size(main + "WheelRoot/Wheel", authoredUi.Definition(main + "WheelRoot/Wheel").size * (state.mainWheel.width / (183.3333f)));
                authoredUi.Offset(main + "WheelRoot/Wheel", new Vector2(0, state.mainWheel.center.y - 300));
                authoredUi.SetActive(main + "WheelRoot/Wheel", state.mainWheelVisible);
                authoredUi.SetActive(other + "Wheel", state.destinationWheelVisible);
                authoredUi.Size(other + "OptionsRoot/OptionsPivot", new Vector2(authoredUi.Definition(other + "OptionsRoot/OptionsPivot").size.x * state.destinationClipWidth / (1291.1409f / NativeMenuTransitionPlayback.SourceScale), authoredUi.Definition(other + "OptionsRoot/OptionsPivot").size.y));
            }
            else
            {
                authoredUi.Offset(main + lower + "LowerOptionsMask/LowerOptionsPIvot2", new Vector2(state.mainLowerOffset.x, 0));
                authoredUi.Offset(main + lower.TrimEnd('/'), new Vector2(0, state.mainLowerOffset.y));
                authoredUi.Offset(main + "WheelRoot/WheelOptions", state.mainUpperOffset);
                authoredUi.Offset(other + "LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot",state.destinationOffset);
                authoredUi.SetActive(main + "WheelRoot/Wheel",state.mainWheelVisible);
                authoredUi.SetActive(other + "WheelRoot/Wheel",state.destinationWheelVisible);
            }
            return true;
        }
        void ApplyTransitionButtons(string main, string other, NativeMenuTransitionPlayback.State state)
        {
            var mainPaths = MainButtonPaths;
            bool race = other.Contains("RaceMenu");
            string[] otherNames = race ? RaceModeButtonNames : other.Contains("ExtrasMenu") ? new[] { "CreditsButton", "MusicRoomButton" } : OnlineWheelNames;
            bool reverse = transitionTo == NativeMenuPage.Main;
            for (int i = 0; i < mainPaths.Length; i++)
                ApplyTransitionButton(main + mainPaths[i], i, !reverse, state.elapsed);
            int otherCount = otherNames == OnlineWheelNames && !usingOnlineAuthoring ? 4 : otherNames.Length;
            for (int i = 0; i < otherCount; i++)
                ApplyTransitionButton(other + (race ? "OptionsRoot/OptionsPivot/" : "LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/") + otherNames[i], i, reverse, state.elapsed);
        }
        void ApplyTransitionButton(string key, int index, bool outgoing, float elapsed)
        {
            float amount = outgoing && transitionSourceFocus?.buttonAmounts != null && index < transitionSourceFocus.buttonAmounts.Length
                ? transitionSourceFocus.buttonAmounts[index] * (1 - (authoredUi.Motion ? authoredUi.Motion.menuFocusOut : buttonOut).Evaluate(Mathf.Clamp01(elapsed / (authoredUi.Motion ? Mathf.Max(.001f, authoredUi.Motion.menuFocusOutSeconds) : .15f)))) : 0;
            bool disabled = key == "menu2/Canvas/MainMenu/" + MainButtonPaths[2] && Navigation.MainDisabled != null && Navigation.MainDisabled.Length > 2 && Navigation.MainDisabled[2];
            authoredUi.ApplyMenuButton(key, amount, false, disabled);
        }
    }
}
