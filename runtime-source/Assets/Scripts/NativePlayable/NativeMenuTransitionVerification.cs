using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeMenuTransitionVerification
    {
        [Serializable] public sealed class Report
        {
            public bool passed = true;
            public string source = "Original menu2 serialized RectTransforms and transition duration; IL2CPP MainToRace/MainOptions MoveNext, DOTween.cctor and QuaternionPlugin.SetChangeValue.";
            public List<string> checks = new List<string>();
            public List<NativeMenuTransitionPlayback.State> samples = new List<NativeMenuTransitionPlayback.State>();
        }
        static void Check(Report report, bool value, string text)
        {
            report.passed &= value; report.checks.Add((value ? "PASS " : "FAIL ") + text);
        }
        static bool Near(float a, float b) => Math.Abs(a - b) < .002f;
        public static Report Verify()
        {
            var report = new Report();
            Check(report, Near(NativeMenuTransitionPlayback.OutQuad(.5f), .75f), "Default ease6 OutQuad midpoint");
            Check(report, Near(NativeMenuTransitionPlayback.Duration("Main", "RaceModes"), 1)
                && Near(NativeMenuTransitionPlayback.Duration("Main", "Options"), 5f / 6)
                && Near(NativeMenuTransitionPlayback.Duration("Options", "Main"), 1), "Authored duration and await phase totals");
            var begin = NativeMenuTransitionPlayback.Evaluate("Main", "RaceModes", 0, 1066.6667f);
            Check(report, begin.mainVisible && begin.mainWheelVisible && !begin.destinationWheelVisible && Near(begin.destinationClipWidth, 0)
                && Near(begin.mainWheel.x, 4.4445f), "Race forward initial visibility and original wheel rect");
            var middle = NativeMenuTransitionPlayback.Evaluate("Main", "RaceModes", .5f, 1066.6667f);
            Check(report, !middle.mainLowerVisible && middle.mainWheelVisible && Near(middle.mainWheel.width, (330 + (506 - 330) * .75f) / 1.8f)
                && middle.mainUpperClip.x > 96.6667f && middle.inputBlocked, "Race middle stage resizes wheel and crops upper options");
            var reveal = NativeMenuTransitionPlayback.Evaluate("Main", "RaceModes", 5f / 6, 1066.6667f);
            Check(report, !reveal.mainWheelVisible && reveal.destinationWheelVisible && Near(reveal.destinationClipWidth, 1291.1409f / 1.8f * .75f), "Race third stage exposes original options mask");
            var raceEnd = NativeMenuTransitionPlayback.Evaluate("Main", "RaceModes", 1, 1066.6667f);
            Check(report, raceEnd.completed && !raceEnd.inputBlocked && !raceEnd.mainVisible && raceEnd.destinationPromptsVisible
                && Near(raceEnd.destinationWheel.x, 1066.6667f - 318.3334f), "Race forward final handoff and focus availability");
            var reverse = NativeMenuTransitionPlayback.Evaluate("RaceModes", "Main", .5f, 1066.6667f, 120, 30);
            Check(report, reverse.mainWheelVisible && !reverse.destinationWheelVisible && reverse.mainWheelUsesRetainedAngle
                && Near(reverse.requestedWheelDegrees, -1440) && Near(reverse.effectiveWheelDegrees, -1080)
                && Near(reverse.mainWheelAngle, 30 - 1080 * .75f), "Original negative Fast rotation retains three reverse turns");
            Check(report, Near(middle.requestedWheelDegrees, 1440) && Near(middle.effectiveWheelDegrees, 0), "Original positive Fast rotation wraps four requested turns");
            var returnMain = NativeMenuTransitionPlayback.Evaluate("RaceModes", "Main", 1, 1066.6667f);
            Check(report, returnMain.mainVisible && returnMain.mainLowerVisible && returnMain.mainPromptsVisible && !returnMain.destinationVisible
                && Near(returnMain.mainLowerOffset.y, 0) && Near(returnMain.logoOffsetY, 0), "Race reverse restores lower menu and logo");
            Check(report, Near(middle.selectorResetAmount, 0) && Near(reverse.selectorResetAmount, 0)
                && Near(NativeMenuTransitionPlayback.Evaluate("RaceModes", "Main", 5f / 6, 1066.6667f).selectorResetAmount, 0)
                && Near(returnMain.selectorResetAmount, 1), "Race selector retains its pivot until final main selection");
            foreach (string destination in new[] { "Options", "Extras", "Online" })
            {
                var initial = NativeMenuTransitionPlayback.Evaluate("Main", destination, 0, 1066.6667f);
                var swap = NativeMenuTransitionPlayback.Evaluate("Main", destination, 1f / 3, 1066.6667f);
                var end = NativeMenuTransitionPlayback.Evaluate("Main", destination, 5f / 6, 1066.6667f);
                Check(report, initial.mainVisible && !initial.destinationVisible && swap.destinationVisible && !swap.mainVisible
                    && Near(swap.destinationOffset.x, -(793.1f + .45007324f) / 1.8f), destination + " original visibility swap at one third");
                Check(report, Near(initial.selectorResetAmount, 0) && Near(swap.selectorResetAmount, 1)
                    && Near(NativeMenuTransitionPlayback.Evaluate("Main", destination, 1f / 6, 1066.6667f).selectorResetAmount, .75f)
                    && Near(NativeMenuTransitionPlayback.Evaluate(destination, "Main", 1f / 6, 1066.6667f).selectorResetAmount, .75f),
                    destination + " source selector resets with original one-third OutQuad tween");
                Check(report, end.completed && Near(end.destinationOffset.x, 0) && end.destinationPromptsVisible, destination + " forward finishes at five sixths");
                var early = NativeMenuTransitionPlayback.Evaluate("Main", destination, 1f / 6, 1066.6667f);
                Check(report, Near(early.mainUpperOffset.x, -752.39404f / 1.8f * (5f / 9f))
                    && Near(early.mainLowerOffset.x, -419.4276f / 1.8f * .75f),
                    destination + " original upper moves left over half second and lower over one third");
                var back = NativeMenuTransitionPlayback.Evaluate(destination, "Main", .5f, 1066.6667f);
                Check(report, back.mainWheelVisible && !back.destinationVisible && Near(back.mainLowerOffset.y, 0)
                    && Near(back.mainLowerOffset.x, -419.4276f / 1.8f), destination + " reverse begins main slide at half second");
                var backEnd = NativeMenuTransitionPlayback.Evaluate(destination, "Main", 1, 1066.6667f);
                Check(report, Near(backEnd.mainUpperOffset.x, 0) && Near(backEnd.mainLowerOffset.x, 0)
                    && Near(backEnd.logoOffsetY, 0) && backEnd.mainPromptsVisible, destination + " reverse restores authored endpoints");
            }
            Check(report, !NativeMenuTransitionPlayback.Supports("Characters", "Vehicles")
                && !NativeMenuTransitionPlayback.Evaluate("Characters", "Vehicles", 0, 1000).supported, "Unrelated page lifecycle is excluded from original transition claims");
            foreach (string destination in new[] { "RaceModes", "Options", "Extras", "Online" })
                foreach (bool backwards in new[] { false, true })
                {
                    string from = backwards ? destination : "Main", to = backwards ? "Main" : destination;
                    foreach (float time in new[] { 0, 1f / 6, 1f / 3, .5f, 2f / 3, 5f / 6, 1 })
                        report.samples.Add(NativeMenuTransitionPlayback.Evaluate(from, to, time, 1066.6667f, 45, 30));
                }
            return report;
        }
        public static bool Run(string directory)
        {
            var report = Verify();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "menu-transition-rules-report.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("NATIVE_MENU_TRANSITIONS " + (report.passed ? "PASS" : "FAIL"));
            return report.passed;
        }
    }
}
