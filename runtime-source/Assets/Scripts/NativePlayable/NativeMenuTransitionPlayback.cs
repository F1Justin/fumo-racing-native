using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeMenuTransitionPlayback
    {
        [Serializable] public sealed class State
        {
            public string from, to, sourceTransition;
            public bool supported, completed, inputBlocked;
            public float elapsed, duration;
            public bool mainVisible, destinationVisible, mainUpperVisible, mainLowerVisible, logoVisible;
            public bool mainWheelVisible, destinationWheelVisible, mainPromptsVisible, destinationPromptsVisible;
            public Rect mainWheel, destinationWheel, mainUpperClip;
            public Vector2 mainUpperOffset, mainLowerOffset, destinationOffset;
            public float destinationClipWidth, logoOffsetY, selectorResetAmount;
            public float mainWheelAngle, destinationWheelAngle;
            public float requestedWheelDegrees, effectiveWheelDegrees;
            public bool mainWheelUsesRetainedAngle;
        }
        public const float SourceScale = 1.8f;
        public const float RaceDuration = 1;
        public const float OptionsForwardDuration = 5f / 6f;
        public const float OptionsReverseDuration = 1;
        const float Stage = 1f / 3f;
        const float LowerHeight = 387.4594f / SourceScale;
        const float LogoTravel = 2 * 280.43347f / SourceScale;
        const float UpperTravel = 752.39404f / SourceScale;
        const float LowerTravel = 419.4276f / SourceScale;
        const float OptionsTravel = (793.1f + .45007324f) / SourceScale;
        const float RaceOptionsWidth = 1291.1409f / SourceScale;

        public static float OutQuad(float progress)
        {
            progress = Mathf.Clamp01(progress);
            return progress * (2 - progress);
        }
        static float Tween(float time, float start, float duration) => OutQuad((time - start) / duration);
        static bool Options(string page) => page == "Options" || page == "Settings" || page == "Extras" || page == "Online";
        public static bool Supports(string from, string to) => (from == "Main" && (to == "RaceModes" || Options(to)))
            || (to == "Main" && (from == "RaceModes" || Options(from)));
        public static float Duration(string from, string to) => !Supports(from, to) ? 0 : from == "RaceModes" || to == "RaceModes"
            ? RaceDuration : from == "Main" ? OptionsForwardDuration : OptionsReverseDuration;
        public static State Evaluate(NativeMenuPage from, NativeMenuPage to, float elapsed, float width, float startWheelAngle = 0, float retainedMainWheelAngle = 0)
            => Evaluate(from.ToString(), to.ToString(), elapsed, width, startWheelAngle, retainedMainWheelAngle);
        public static State Evaluate(string from, string to, float elapsed, float width, float startWheelAngle = 0, float retainedMainWheelAngle = 0)
        {
            if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || float.IsNaN(width) || float.IsInfinity(width) || width <= 0)
                throw new ArgumentException("Menu transition time and width must be finite.");
            float duration = Duration(from, to);
            var state = new State { from = from, to = to, supported = Supports(from, to), duration = duration,
                elapsed = Mathf.Clamp(elapsed, 0, duration), completed = elapsed >= duration, inputBlocked = elapsed < duration };
            if (!state.supported) { state.completed = true; state.inputBlocked = false; return state; }
            bool reverse = to == "Main";
            float time = state.elapsed;
            state.mainPromptsVisible = reverse && state.completed;
            state.destinationPromptsVisible = !reverse && state.completed;
            state.mainWheel = new Rect(4.4445f, 208.3333f, 183.3333f, 183.3333f);
            state.destinationWheel = new Rect(4.4445f, 208.3333f, 183.3333f, 183.3333f);
            state.mainUpperClip = new Rect(0, 0, width, 600);
            state.destinationClipWidth = float.MaxValue;
            if (from == "RaceModes" || to == "RaceModes") Race(state, reverse, time, width, startWheelAngle, retainedMainWheelAngle);
            else Option(state, reverse, time, startWheelAngle);
            return state;
        }
        static void Race(State state, bool reverse, float time, float width, float angle, float retainedMainAngle)
        {
            state.sourceTransition = "MainToRaceTransition";
            state.selectorResetAmount = reverse && state.completed ? 1 : 0;
            float wheel = Tween(time, Stage, Stage);
            float rootWidth = Mathf.Lerp(1895.9755f / SourceScale, 2069.9756f / SourceScale - width + 320.00012f / SourceScale, reverse ? 1 - wheel : wheel);
            float centerX = 2069.9756f / SourceScale - rootWidth - 1 / SourceScale;
            float diameter = Mathf.Lerp(330f / SourceScale, 506f / SourceScale, reverse ? 1 - wheel : wheel);
            float centerY = Mathf.Lerp(300, 300 - 81 / SourceScale, reverse ? 1 - wheel : wheel);
            state.mainWheel = new Rect(centerX - diameter / 2, centerY - diameter / 2, diameter, diameter);
            state.destinationWheel = new Rect(width - 318.3334f, 114.4444f, 506f / SourceScale, 506f / SourceScale);
            state.mainUpperClip = new Rect(2069.9756f / SourceScale - rootWidth, 208.3333f, rootWidth, 330f / SourceScale);
            state.mainVisible = reverse || !state.completed;
            state.destinationVisible = !reverse || !state.completed;
            state.mainUpperVisible = state.mainVisible;
            state.mainLowerVisible = reverse ? time >= 2 * Stage : time < Stage;
            state.logoVisible = state.mainVisible;
            state.logoOffsetY = -LogoTravel * (reverse ? 1 - Tween(time, 0, RaceDuration) : Tween(time, 0, RaceDuration));
            state.mainLowerOffset.y = -LowerHeight * (reverse ? 1 - Tween(time, 2 * Stage, Stage) : Tween(time, 0, Stage));
            state.destinationClipWidth = RaceOptionsWidth * (reverse ? 1 - Tween(time, 0, Stage) : Tween(time, 2 * Stage, Stage));
            state.mainWheelVisible = reverse ? time >= Stage : time < 2 * Stage;
            state.destinationWheelVisible = !state.mainWheelVisible && state.destinationVisible;
            state.requestedWheelDegrees = reverse ? -1440 : 1440;
            // Fast normalizes positive endpoints only, then folds the delta once: +1440 becomes 0; -1440 becomes -1080.
            state.effectiveWheelDegrees = reverse ? -1080 : 0;
            if (reverse)
            {
                state.mainWheelUsesRetainedAngle = true;
                state.mainWheelAngle = retainedMainAngle + state.effectiveWheelDegrees * wheel - 90 * Mathf.Max(0, time - 2 * Stage);
                state.destinationWheelAngle = angle - 360 * Mathf.Min(time, Stage);
            }
            else
            {
                state.mainWheelAngle = angle - 90 * Mathf.Min(time, Stage);
                state.destinationWheelAngle = angle - 90 * Stage - 360 * Mathf.Max(0, time - 2 * Stage);
            }
        }
        static void Option(State state, bool reverse, float time, float angle)
        {
            state.sourceTransition = "MainOptionsTransition";
            state.selectorResetAmount = Tween(time, 0, Stage);
            state.mainVisible = reverse || time < Stage;
            state.destinationVisible = !reverse ? time >= Stage : time < .5f;
            state.mainUpperVisible = state.mainVisible;
            state.mainLowerVisible = state.mainVisible && (!reverse || time >= .5f);
            state.logoVisible = state.mainVisible;
            state.mainWheelVisible = reverse ? time >= .5f : time < Stage;
            state.destinationWheelVisible = state.destinationVisible;
            state.logoOffsetY = -LogoTravel * (reverse ? 1 - Tween(time, .5f, .5f) : Tween(time, 0, .5f));
            // Original _mainPivot2 subtracts its own width over half a second; _mainPivot finishes first.
            state.mainUpperOffset.x = -UpperTravel * (reverse ? 1 - Tween(time, .5f, .5f) : Tween(time, 0, .5f));
            state.mainLowerOffset.x = -LowerTravel * (reverse ? 1 - Tween(time, .5f, .5f) : Tween(time, 0, Stage));
            state.mainLowerOffset.y = reverse && time < .5f ? -LowerHeight : 0;
            state.destinationOffset.x = -OptionsTravel * (reverse ? Tween(time, 0, .5f) : 1 - Tween(time, Stage, .5f));
            // Options and Extras retain the wheel's current rotation at the authored visibility handoff.
            state.mainWheelAngle = state.destinationWheelAngle = angle - 90 * time;
        }
    }
}
