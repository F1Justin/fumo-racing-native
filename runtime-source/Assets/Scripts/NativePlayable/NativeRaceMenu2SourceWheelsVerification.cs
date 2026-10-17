using System.Collections;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        IEnumerator CheckMenu2SourceWheelsLive()
        {
            float savedScale = Time.timeScale;
            var savedPage = hud.MenuPage;
            try
            {
                hud.SetMenu2SourceScenePresent(false);
                hud.SetMenuPage(NativeMenuPage.Main);
                hud.SetMenu2SourceScenePresent(true);
                Time.timeScale = 0;
                yield return null;
                float frozenMain = hud.Menu2SourceWheelAngles()[0];
                yield return new WaitForSecondsRealtime(.06f);
                ModeCheck(Mathf.Abs(hud.Menu2SourceWheelAngles()[0] - frozenMain) < .001f,
                    "source Main wheel idle freezes with scaled time zero");
                Time.timeScale = 1;
                hud.SetMenuPage(NativeMenuPage.Extras);
                while (hud.MenuTransitionActive) yield return null;
                yield return null;
                var before = hud.Menu2SourceWheelAngles();
                float beforeTime = Time.time;
                yield return new WaitForSecondsRealtime(.08f);
                var after = hud.Menu2SourceWheelAngles();
                float elapsed = Time.time - beforeTime;
                ModeCheck(Mathf.Abs(after[0] - before[0]) < .001f,
                    "source Main wheel retains its angle while Extras owns active parent");
                ModeCheck(Mathf.Abs((after[1] - before[1]) + 65 * elapsed) < 2,
                    "source Extras wheel continues at serialized scaled speed after handoff");
                ModeCheck(Mathf.Abs((after[1] - after[0]) - (before[1] - before[0])) > .5f,
                    "source wheel handoff copies once and subsequent rotations diverge");
                hud.SetMenuPage(NativeMenuPage.Main);
                while (hud.MenuTransitionActive) yield return null;
                yield return null;
                before = hud.Menu2SourceWheelAngles();
                beforeTime = Time.time;
                yield return new WaitForSecondsRealtime(.08f);
                after = hud.Menu2SourceWheelAngles();
                elapsed = Time.time - beforeTime;
                ModeCheck(Mathf.Abs((after[0] - before[0]) + 90 * elapsed) < 2
                    && Mathf.Abs(after[1] - before[1]) < .001f,
                    "reverse source handoff resumes Main90 and freezes inactive Extras");
                int generation = hud.Menu2SourceSceneGeneration;
                hud.SetMenu2SourceScenePresent(false);
                var departed = hud.Menu2SourceWheelAngles();
                yield return new WaitForSecondsRealtime(.06f);
                ModeCheck(hud.Menu2SourceWheelAngles().SequenceEqual(departed),
                    "Single scene departure freezes departed wheel clocks");
                hud.SetMenu2SourceScenePresent(true);
                string[] pages = { "MainMenu", "ExtrasMenu", "OptionsMenu" };
                var seeds = pages.Select(page => hud.authoredUiData.nodes.Single(n => n.scene == "menu2"
                    && n.path == "Canvas/" + page + "/WheelRoot/Wheel").rotation.eulerAngles.z).ToArray();
                ModeCheck(hud.Menu2SourceSceneGeneration == generation + 1
                    && hud.Menu2SourceWheelAngles().SequenceEqual(seeds),
                    "fresh menu2 scene generation restores authored local rotation seeds");
            }
            finally
            {
                Time.timeScale = savedScale;
                hud.SetMenuPage(savedPage);
            }
            yield return CheckNestedOptionsSourceWheelsLive();
        }
    }
}
