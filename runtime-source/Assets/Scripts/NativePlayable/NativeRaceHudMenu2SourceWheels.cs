using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        bool menu2SourceScenePresent, menu2SourceWheelHandoff;
        bool menu2SourceOptionsParentActive = true;
        public void SetMenu2SourceOptionsParentActive(bool active) => menu2SourceOptionsParentActive = active;
        int menu2SourceWheelFrame = -1;
        float sourceMainWheelAngle, sourceExtrasWheelAngle, sourceOptionsWheelAngle, sourceRaceWheelAngle;
        public float SourceRaceWheelAngle => sourceRaceWheelAngle;
        public int Menu2SourceSceneGeneration { get; private set; }
        public bool Menu2SourceMainParentActive => menu2SourceScenePresent && (transitionPending
            ? SourceWheelTransitionState().mainVisible : MenuPage == NativeMenuPage.Main);
        NativeMenuTransitionPlayback.State SourceWheelTransitionState() => NativeMenuTransitionPlayback.Evaluate(
            transitionFrom, transitionTo, Time.unscaledTime - transitionStarted, 600, transitionWheelAngle, retainedMainWheelAngle);
        float AuthoredWheelSeed(string page) => authoredUi != null && authoredUi.PrefabAuthored
            ? authoredUi.Definition("menu2/Canvas/" + page + "/WheelRoot/Wheel").rotation.eulerAngles.z
            : authoredUiData.nodes.Single(n => n.scene == "menu2" && n.path == "Canvas/" + page + "/WheelRoot/Wheel").rotation.eulerAngles.z;
        public void SetMenu2SourceScenePresent(bool present)
        {
            if (present == menu2SourceScenePresent)
            { if (!present) DestroySourceClockGraph(); return; }
            menu2SourceScenePresent = present;
            if (!present) { sourceMainTweenRunning = false; sourceMainTweenStarted = false; DestroySourceClockGraph(); }
            ClearMenuTransition();
            menu2SourceWheelHandoff = false;
            menu2SourceWheelFrame = -1;
            if (!present) return;
            EnsureAuthoredMenus();
            Menu2SourceSceneGeneration++;
            menu2SourceOptionsParentActive = true;
            sourceMainWheelAngle = AuthoredWheelSeed("MainMenu");
            sourceExtrasWheelAngle = AuthoredWheelSeed("ExtrasMenu");
            sourceOptionsWheelAngle = AuthoredWheelSeed("OptionsMenu");
            sourceRaceWheelAngle = authoredUi != null && authoredUi.PrefabAuthored ? authoredUi.Definition(RaceSourceWheel).rotation.eulerAngles.z
                : authoredUiData.nodes.Single(n => n.scene == "menu2" && n.path == "Canvas/RaceMenu/Wheel").rotation.eulerAngles.z;
            retainedMainWheelAngle = sourceMainWheelAngle;
            BeginSourceWheelTransformGeneration();
        }
        bool SourceOptionWheelTransition => transitionPending && (transitionFrom == NativeMenuPage.Extras
            || transitionTo == NativeMenuPage.Extras || transitionFrom == NativeMenuPage.Options || transitionTo == NativeMenuPage.Options
            || transitionFrom == NativeMenuPage.Online || transitionTo == NativeMenuPage.Online);
        ref float SourceOptionWheelAngle(NativeMenuPage page)
        {
            if (page == NativeMenuPage.Extras) return ref sourceExtrasWheelAngle;
            return ref sourceOptionsWheelAngle;
        }
        public void TickMenu2SourceWheels(float deltaTime)
        {
            if (!menu2SourceScenePresent || menu2SourceWheelFrame == Time.frameCount) return;
            menu2SourceWheelFrame = Time.frameCount;
            BindNormalSourceWheelGraph();
            bool mainParent = false, mainLeaf = true, extrasParent = false, optionsParent = false, raceParent = false, raceLeaf = true;
            if (SourceOptionWheelTransition)
            {
                var state = SourceWheelTransitionState();
                bool reverse = transitionTo == NativeMenuPage.Main;
                var otherPage = reverse ? transitionFrom : transitionTo;
                ref float otherAngle = ref SourceOptionWheelAngle(otherPage);
                bool handoff = reverse ? state.mainWheelVisible : !state.mainVisible;
                if (!menu2SourceWheelHandoff && handoff)
                {
                    string otherPath = otherPage == NativeMenuPage.Extras ? ExtrasSourceWheel : OptionsSourceWheel;
                    if (sourceWheelTransforms != null)
                    {
                        if (reverse) CopySourceWheelWorldRotation(otherPath, MainSourceWheel, ref sourceMainWheelAngle);
                        else CopySourceWheelWorldRotation(MainSourceWheel, otherPath, ref otherAngle);
                    }
                    else if (reverse) sourceMainWheelAngle = otherAngle;
                    else otherAngle = sourceMainWheelAngle;
                    menu2SourceWheelHandoff = true;
                }
                mainParent = state.mainVisible; mainLeaf = state.mainWheelVisible;
                bool otherParent = !reverse || state.destinationVisible;
                extrasParent = otherParent && otherPage == NativeMenuPage.Extras;
                optionsParent = otherParent && (otherPage == NativeMenuPage.Options || otherPage == NativeMenuPage.Online);
            }
            else if (transitionPending && (transitionFrom == NativeMenuPage.RaceModes || transitionTo == NativeMenuPage.RaceModes))
            {
                var state = SourceWheelTransitionState();
                mainParent = state.mainVisible; mainLeaf = state.mainWheelVisible;
                raceParent = state.destinationVisible; raceLeaf = state.destinationWheelVisible;
                if (transitionTo == NativeMenuPage.RaceModes && !menu2SourceWheelHandoff && raceLeaf)
                    menu2SourceWheelHandoff = CopySourceWheelWorldRotation(MainSourceWheel, RaceSourceWheel, ref sourceRaceWheelAngle);
            }
            else
            {
                mainParent = MenuPage == NativeMenuPage.Main;
                extrasParent = MenuPage == NativeMenuPage.Extras;
                optionsParent = (MenuPage == NativeMenuPage.Options || MenuPage == NativeMenuPage.Online) && menu2SourceOptionsParentActive;
                raceParent = MenuPage == NativeMenuPage.RaceModes;
            }
            WriteSourceWheelActivity(mainParent, mainLeaf, extrasParent, optionsParent, raceParent, raceLeaf);
            var motion = authoredUi?.Motion;
            if (mainParent && mainLeaf) RotateSourceWheel(MainSourceWheel, ref sourceMainWheelAngle, motion ? motion.mainWheelDegreesPerSecond : -90, deltaTime);
            if (extrasParent) RotateSourceWheel(ExtrasSourceWheel, ref sourceExtrasWheelAngle, motion ? motion.optionsWheelDegreesPerSecond : -65, deltaTime);
            if (optionsParent) RotateSourceWheel(OptionsSourceWheel, ref sourceOptionsWheelAngle, motion ? motion.optionsWheelDegreesPerSecond : -65, deltaTime);
            if (raceParent && raceLeaf) RotateSourceWheel(RaceSourceWheel, ref sourceRaceWheelAngle, motion ? motion.raceWheelDegreesPerSecond : -360, deltaTime);
            TickScaledSourceMainTween(deltaTime);
            PrepareMenu2SourceRenderedConsumers();
        }
        public float[] Menu2SourceWheelAngles() => new[] { sourceMainWheelAngle, sourceExtrasWheelAngle, sourceOptionsWheelAngle };
    }
}
