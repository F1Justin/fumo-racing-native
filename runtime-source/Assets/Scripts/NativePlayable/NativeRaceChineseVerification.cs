using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ChineseRow { public string key,value; }
        [Serializable] sealed class ChineseWords { public ChineseRow[] phrases,texts,prefixes,patterns; }
        [Serializable] sealed class ChineseReport
        {
            public bool passed;
            public string error,fontName;
            public int phraseCount,uniqueCharacters,engineErrors;
            public string[] checks;
        }
        IEnumerator CheckChineseLocalization()
        {
            var report=new ChineseReport();var checks=new List<string>();
            var original=NativeUserSettings.CopyData();
            var routine=CheckChineseLocalizationCore(report,checks);
            while(true)
            {
                bool next=false;object instruction=null;
                try{next=routine.MoveNext();if(next)instruction=routine.Current;}
                catch(Exception error){report.error=error.ToString();break;}
                if(!next){report.engineErrors=errorCount;report.passed=errorCount==0;break;}
                yield return instruction;
            }
            NativeSaveStore.ReplaceData(original);
            report.checks=checks.ToArray();Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"chinese-verification.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.passed?0:2);
        }
        static void ChineseRequire(bool value,string name,List<string> checks)
        {
            if(!value)throw new InvalidOperationException(name);checks.Add(name);
        }
        static TMP_Text[] ChineseVisibleText()=>FindObjectsOfType<TMP_Text>().Where(t=>t.isActiveAndEnabled && !string.IsNullOrWhiteSpace(t.text)).ToArray();
        void CheckChineseVisible(string expected,string name,List<string> checks)
        {
            var visible=ChineseVisibleText();
            ChineseRequire(visible.Any(t=>t.text.Contains(expected)),name+"-translated",checks);
            ChineseRequire(visible.All(t=>t.font==NativeChineseFont.Font),name+"-maiyuan-font",checks);
            ChineseRequire(!visible.Any(t=>t.text.Contains("□")),name+"-no-replacement-text",checks);
            ChineseRequire(visible.All(t=>t.textInfo.characterInfo.Take(t.textInfo.characterCount).Any(c=>c.isVisible)),name+"-rendered-glyph-mesh",checks);
        }
        IEnumerator CheckChineseLocalizationCore(ChineseReport report,List<string> checks)
        {
            yield return new WaitForSecondsRealtime(4);
            phase=Phase.Menu;hud.SetMenuPage(NativeMenuPage.Main);
            ChineseRequire(NativeUserSettings.TrySetInt("lang_game",0),"select-english",checks);
            yield return WaitForSettingsRender();
            ChineseRequire(ChineseVisibleText().Any(t=>t.text.Contains("Race!")),"english-menu-preserved",checks);
            OpenSettings();SettingsGo(SettingsPage.Language);
            var control=settingsRows[0].control;
            ChineseRequire(control.options.Length==4 && control.options[3]=="简体中文","language-dropdown-chinese-option",checks);
            foreach(char glyph in "简体中文")ChineseRequire(hud.menuFont.HasCharacter(glyph),"english-language-menu-glyph-"+((int)glyph).ToString("X4"),checks);
            control.setOption(NativeLocalization.ChineseLanguage);BuildSettingsRows();
            ChineseRequire(NativeLocalization.LanguageName=="简体中文","selected-language-name",checks);
            var catalog=JsonUtility.FromJson<ChineseWords>(NativeContent.Load<TextAsset>("NativeSettings/chinese").text);
            report.phraseCount=catalog.phrases.Length;
            foreach(var row in catalog.phrases)ChineseRequire(NativeLocalization.Get(row.key)==row.value,"phrase-"+row.key,checks);
            ChineseRequire(NativeLocalization.PhraseCount==241,"original-catalog-preserved",checks);
            ChineseRequire(NativeLocalization.TranslateText("Room a7c9  /  Round 2")=="房间 a7c9  /  第2轮","dynamic-room-id-preserved",checks);
            ChineseRequire(NativeLocalization.TranslateText("Room Winter  /  Round 2")=="房间 Winter  /  第2轮","room-id-matching-phrase-preserved",checks);
            ChineseRequire(NativeLocalization.TranslateText("Server room  /  3 of 8 players")=="服务器房间  /  3/8位玩家","dynamic-player-count",checks);
            ChineseRequire(NativeLocalization.TranslateText("5 laps")=="5圈","dynamic-lap-count",checks);
            ChineseRequire(NativeLocalization.TranslateText("router.f1justin.com:27777")=="router.f1justin.com:27777","hostname-preserved",checks);
            string glyphs=new string(string.Concat(catalog.phrases.Concat(catalog.texts).Concat(catalog.prefixes).Concat(catalog.patterns).Select(r=>r.value)).Concat("简体中文").Where(c=>!char.IsWhiteSpace(c)).Distinct().ToArray());
            report.uniqueCharacters=glyphs.Length;report.fontName=NativeChineseFont.Font.faceInfo.familyName;
            foreach(char glyph in glyphs)ChineseRequire(NativeChineseFont.Font.HasCharacter(glyph,true,true),"glyph-"+((int)glyph).ToString("X4"),checks);
            ChineseRequire(report.fontName=="KN Maiyuan","font-family",checks);
            yield return CaptureSettingsUi("chinese-language.png");CheckChineseVisible("游戏语言","language-page",checks);
            SettingsGo(SettingsPage.Video);yield return CaptureSettingsUi("chinese-video.png");CheckChineseVisible("纹理分辨率","video-page",checks);
            SettingsGo(SettingsPage.Audio);yield return CaptureSettingsUi("chinese-audio.png");CheckChineseVisible("环境音","audio-page",checks);
            settingsOpen=false;hud.SetMenuPage(NativeMenuPage.Main);
            yield return CaptureSettingsUi("chinese-main.png");CheckChineseVisible("开始比赛","main-menu",checks);
            hud.SetMenuPage(NativeMenuPage.RaceModes);yield return CaptureSettingsUi("chinese-race-modes.png");CheckChineseVisible("大奖赛","race-modes",checks);
            hud.SetMenuPage(NativeMenuPage.Tracks);yield return CaptureSettingsUi("chinese-tracks.png");CheckChineseVisible("魔法之森","tracks-menu",checks);
            hud.SetMenuPage(NativeMenuPage.Characters);hud.SelectMenuIndex(0);
            yield return CaptureSettingsUi("chinese-characters.png");CheckChineseVisible(NativeLocalization.Get("CHAR_"+vehicleRoster.CharacterIdAt(selectableCharacters[0]).ToUpperInvariant()),"character-menu",checks);
            hud.SetMenuPage(NativeMenuPage.Vehicles);hud.SelectMenuIndex(0);
            yield return CaptureSettingsUi("chinese-vehicles.png");CheckChineseVisible("卡丁车","vehicle-menu",checks);
            hud.SetMenuPage(NativeMenuPage.Main);
            var splash=Instantiate(NativeContent.Load<NativeSplashAudioProducer>("NativeMenu/Splash"),transform,false);
            splash.gameObject.SetActive(true);splash.Begin(()=>{});
            while(splash.IsRunning && splash.textAlpha.Evaluate(splash.Elapsed)<.5f)yield return null;
            ChineseRequire(splash.IsRunning,"splash-source-animation-running",checks);
            var splashText=splash.textRect.GetComponent<TMP_Text>();
            ChineseRequire(splashText.text.Contains("同人作品") && splashText.font==NativeChineseFont.Font,"splash-chinese-maiyuan",checks);
            yield return CaptureSettingsUi("chinese-splash.png");
            ChineseRequire(splashText.textInfo.characterInfo.Take(splashText.textInfo.characterCount).Any(c=>c.isVisible),"splash-rendered-glyph-mesh",checks);
            splash.Skip();Destroy(splash.gameObject);yield return null;
            if(Net){Net.OpenMenu();yield return CaptureSettingsUi("chinese-online.png");CheckChineseVisible("创建房间","online-menu",checks);Net.CloseMenu();}
            string savedDirectory=Path.GetDirectoryName(NativeSaveStore.FilePath);NativeSaveStore.ResetForVerification();
            ChineseRequire(NativeSaveStore.Initialize(new[]{"--save-directory",savedDirectory}),"saved-settings-store",checks);
            ChineseRequire(NativeUserSettings.GetInt("lang_game")==3,"chinese-setting-saved",checks);
            ChineseRequire(NativeUserSettings.TrySetInt("lang_game",0),"switch-back-english",checks);
            hud.SetMenuPage(NativeMenuPage.Main);yield return CaptureSettingsUi("english-restored.png");
            ChineseRequire(ChineseVisibleText().Any(t=>t.text.Contains("Race!")) && ChineseVisibleText().All(t=>t.font!=NativeChineseFont.Font),"english-font-restored",checks);
        }
    }
}
