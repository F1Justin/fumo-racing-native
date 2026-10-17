using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class DiscordPointerSample
        {
            public string phase, page;
            public int frame, drawCount, resetCount, state, nextState;
            public float gameTime, normalizedTime, nextNormalizedTime, transitionNormalizedTime;
            public bool tooltipGraphic, buttonGraphic, watermarkGraphic, inTransition;
            public Vector3 scale;
        }
        [Serializable] sealed class DiscordPointerReport
        {
            public bool passed, hardwarePointerVerified, originalPixelReviewAccepted;
            public string inputClass = "Injected fixture screen-position override through actual Main draw and production RectangleContainsScreenPoint; actual InputSystem keyboard events for real page transitions; no mouse click or external URL navigation.";
            public string[] checks;
            public DiscordPointerSample[] samples;
        }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
        IEnumerator CheckDiscordPointerFixtureLive(Keyboard keyboard, Action<bool,string> aggregate)
        {
            var checks = new List<string>(); var samples = new List<DiscordPointerSample>();
            Action<bool,string> check = (ok,label) => { string item = (ok ? "PASS " : "FAIL ") + label; checks.Add(item); aggregate(ok,"Discord fixture: " + label); };
            Vector2? previous = hud.DiscordPointerFixtureOverride;
            var ui = hud.OriginalSpriteUi;
            try
            {
                check(hud.MenuPage == NativeMenuPage.Main && ui != null, "actual Main initial state");
                if (ui == null) yield break;
                hud.DiscordPointerFixtureOverride = new Vector2(-1000,-1000);
                yield return DiscordFixtureFrames("idle", .1f, samples);
                Rect formerButton = ui.RectAt("menu2/Canvas/MainMenu/discordserverbutton");
                float scale = Screen.height / 600f;
                hud.DiscordPointerFixtureOverride = new Vector2(formerButton.center.x * scale, Screen.height - formerButton.center.y * scale);
                yield return DiscordFixtureFrames("former-button", .35f, samples);
                check(samples.Count >= 3 && samples.All(s => !s.buttonGraphic && !s.tooltipGraphic && !s.watermarkGraphic),
                    "removed Discord button, tooltip and watermark stay hidden over former button bounds");
                yield return MenuParityKey(keyboard, Key.Enter);
                check(hud.MenuPage == NativeMenuPage.RaceModes, "actual RaceModes route remains available");
                yield return DiscordFixtureFrames("race-modes", .4f, samples);
                yield return MenuParityKey(keyboard, Key.Escape);
                check(hud.MenuPage == NativeMenuPage.Main, "actual return Main remains available");
                yield return DiscordFixtureFrames("return-main", .4f, samples);
                check(samples.All(s => !s.buttonGraphic && !s.tooltipGraphic && !s.watermarkGraphic),
                    "menu transition and return never restore removed UI");
            }
            finally
            {
                hud.DiscordPointerFixtureOverride = previous;
                var report = new DiscordPointerReport { passed=checks.Count>0 && checks.All(c=>c.StartsWith("PASS ")), checks=checks.ToArray(), samples=samples.ToArray(), hardwarePointerVerified=false, originalPixelReviewAccepted=false };
                File.WriteAllText(Path.Combine(reportDirectory,"discord-pointer-fixture.json"),JsonUtility.ToJson(report,true));
            }
        }
        IEnumerator DiscordFixtureFrames(string phase, float seconds, List<DiscordPointerSample> samples)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            do
            {
                yield return null; yield return new WaitForEndOfFrame();
                var ui=hud.OriginalSpriteUi; var animator=ui.DiscordAnimator;
                var state=animator.GetCurrentAnimatorStateInfo(0); var next=animator.GetNextAnimatorStateInfo(0);
                samples.Add(new DiscordPointerSample { phase=phase,page=hud.MenuPage.ToString(),frame=Time.frameCount,drawCount=hud.DiscordPointerDrawCount,
                    resetCount=ui.DiscordAnimatorPageResetCount,gameTime=Time.time,state=state.fullPathHash,nextState=next.fullPathHash,
                    normalizedTime=state.normalizedTime,nextNormalizedTime=next.normalizedTime,inTransition=animator.IsInTransition(0),
                    transitionNormalizedTime=animator.GetAnimatorTransitionInfo(0).normalizedTime,scale=animator.transform.localScale,
                    tooltipGraphic=ui.VisibleGraphic("menu2/Canvas/MainMenu/tooltip"),
                    buttonGraphic=ui.VisibleGraphic("menu2/Canvas/MainMenu/discordserverbutton"),
                    watermarkGraphic=ui.VisibleGraphic("GlobalHUD/Canvas/v") });
            } while(Time.realtimeSinceStartup < deadline);
        }
#endif
    }
}
