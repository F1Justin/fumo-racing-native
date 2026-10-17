using System.Collections;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        IEnumerator CheckNestedOptionsSourceWheelsLive()
        {
            float savedScale=Time.timeScale;
            var savedPage=hud.MenuPage;
            bool savedOpen=settingsOpen;
            var savedSettingsPage=settingsPage;
            var savedReturn=settingsReturnPage;
            int savedIndex=settingsIndex, savedCategory=settingsCategoryIndex;
            try
            {
                Time.timeScale=1;
                hud.SetMenuPage(NativeMenuPage.Main);
                while(hud.MenuTransitionActive)yield return null;
                OpenSettings();
                while(hud.MenuTransitionActive)yield return null;
                yield return null;
                int generation=hud.Menu2SourceSceneGeneration;
                int[] rows={0,1,3};
                SettingsPage[] pages={SettingsPage.Video,SettingsPage.Audio,SettingsPage.Language};
                for(int i=0;i<rows.Length;i++)
                {
                    ActivateSettingsRow(rows[i]);
                    yield return null;
                    var before=hud.Menu2SourceWheelAngles();
                    float holding=mainQuitSource.HoldingTime;
                    int count=mainQuitSource.HumansPressing;
                    yield return new WaitForSecondsRealtime(.08f);
                    var after=hud.Menu2SourceWheelAngles();
                    ModeCheck(settingsPage==pages[i] && Mathf.Abs(after[2]-before[2])<.001f
                        && Mathf.Abs(after[0]-before[0])<.001f
                        && hud.Menu2SourceSceneGeneration==generation,
                        "actual Settings "+pages[i]+" route freezes inactive Options and Main wheels in same menu2 generation");
                    ModeCheck(mainQuitSource.HoldingTime==holding && mainQuitSource.HumansPressing==count,
                        "nested "+pages[i]+" retains inactive Main quit state");
                    SettingsBack();
                    yield return null;
                    before=hud.Menu2SourceWheelAngles();
                    float start=Time.time;
                    yield return new WaitForSecondsRealtime(.08f);
                    after=hud.Menu2SourceWheelAngles();
                    ModeCheck(settingsPage==SettingsPage.Root && hud.Menu2SourceSceneGeneration==generation
                        && Mathf.Abs((after[2]-before[2])+65*(Time.time-start))<2
                        && Mathf.Abs(after[0]-before[0])<.001f,
                        "actual "+pages[i]+" SettingsBack restores Root Options65 clock without scene recreation");
                }
                SettingsBack();
                while(hud.MenuTransitionActive)yield return null;
            }
            finally
            {
                Time.timeScale=savedScale;
                settingsOpen=savedOpen;settingsPage=savedSettingsPage;settingsReturnPage=savedReturn;
                settingsIndex=savedIndex;settingsCategoryIndex=savedCategory;
                BuildSettingsRows();hud.SetMenuPage(savedPage);
                hud.SetMenu2SourceOptionsParentActive(!(settingsOpen && (settingsPage==SettingsPage.Video || settingsPage==SettingsPage.Audio || settingsPage==SettingsPage.Language)));
            }
        }
    }
}
