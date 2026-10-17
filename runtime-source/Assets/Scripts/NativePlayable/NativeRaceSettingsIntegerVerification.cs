using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class SettingsIntegerReport
        {
            public bool passed;
            public bool visualParityAccepted;
            public string scope="Injected original-domain callbacks and rendered leaf values; pointer hardware, audible output and matched original frames are separate checks";
            public string[] checks;
        }
        IEnumerator CheckSettingsIntegerControls()
        {
            var checks=new List<string>();var report=new SettingsIntegerReport();
            var saved=NativeUserSettings.CopyData();
            var percentages=new Dictionary<string,int>(settingsVolumePercent);
            var keys=new[]{"mainVolume","volumeGroup0","volumeGroup2","volumeGroup1","volumeGroup6","volumeGroup3","volumeGroup4","volumeGroup5"};
            try
            {
                settingsVolumePercent.Clear();
                foreach(string key in keys)SettingsUiRequire(NativeUserSettings.TrySetFloat(key,.509f),key+"-seed",checks);
                SettingsGo(SettingsPage.Audio);
                foreach(string key in keys)
                {
                    var control=CreateSettingsControl(key);
                    SettingsUiRequire(control.value=="50%" && control.minimum==0 && control.maximum==100 && control.wholeNumbers && Mathf.Abs(control.normalized-.5f)<.00001f,key+"-initial-truncation-domain",checks);
                    SettingsUiRequire(NativeUserSettings.GetFloat(key)==.5f,key+"-awake-change-set-value",checks);
                    control.setNormalized(.59f);
                    for(int i=0;i<3;i++)BuildSettingsRows();
                    control=CreateSettingsControl(key);
                    SettingsUiRequire(control.value=="59%" && Mathf.Abs(control.normalized-.59f)<.00001f && NativeUserSettings.GetFloat(key)==.59f,key+"-event-integer-survives-rebuild",checks);
                    settingsIndex=Array.IndexOf(keys,key);
                    yield return WaitForSettingsRender();
                    var canvas=GameObject.Find("Original authored menus");
                    var row=canvas.transform.Find("menu2 authored Canvas/AudioOptions/VideoBox/Scroll View/Viewport/Content/"+control.node);
                    var slider=row.Find("Value/Slider").GetComponent<Slider>();
                    var leaf=row.Find("Value/SliderValue").GetComponent<TMP_Text>();
                    SettingsUiRequire(slider && slider.minValue==0 && slider.maxValue==100 && slider.wholeNumbers && slider.value==59 && leaf && leaf.text=="59%" && leaf.gameObject.activeInHierarchy,key+"-rendered-leaf-domain",checks);
                    settingsRows[settingsIndex].adjust(1);BuildSettingsRows();
                    SettingsUiRequire(CreateSettingsControl(key).value=="60%" && NativeUserSettings.GetFloat(key)==.60f,key+"-keyboard-one-percent",checks);
                    control=CreateSettingsControl(key);control.setNormalized(-1);control=CreateSettingsControl(key);
                    SettingsUiRequire(control.value=="0%" && NativeUserSettings.GetFloat(key)==0,key+"-lower-end",checks);
                    control.setNormalized(2);control=CreateSettingsControl(key);
                    SettingsUiRequire(control.value=="100%" && NativeUserSettings.GetFloat(key)==1,key+"-upper-end",checks);
                    control.setNormalized(.59f);
                }
                SettingsGo(SettingsPage.Root);SettingsGo(SettingsPage.Audio);
                foreach(string key in keys)SettingsUiRequire(CreateSettingsControl(key).value=="59%",key+"-same-instance-return",checks);
                settingsVolumePercent.Clear();BuildSettingsRows();
                foreach(string key in keys)SettingsUiRequire(CreateSettingsControl(key).value=="58%" && NativeUserSettings.GetFloat(key)==.58f,key+"-fresh-awake-float32-truncation",checks);
                foreach(int language in new[]{0,1})
                {
                    SettingsUiRequire(NativeUserSettings.TrySetInt("lang_game",language),"language-seed-"+language,checks);NativeUserSettings.ApplyScene(this);
                    SettingsGo(SettingsPage.Video);
                    foreach(int backend in new[]{0,1})
                    {
                        NativeUserSettings.TrySetInt("vsync",backend);BuildSettingsRows();var control=CreateSettingsControl("vsync");
                        string on=NativeLocalization.Get("VIDEO_OPTION_ON"),off=NativeLocalization.Get("VIDEO_OPTION_OFF");
                        SettingsUiRequire(control.options.SequenceEqual(new[]{on,off}) && control.option==(backend==1?0:1) && control.value==(backend==1?on:off),"vsync-order-chosen-"+language+"-"+backend,checks);
                        settingsIndex=2;ActivateSettingsRow(2);yield return WaitForSettingsRender();
                        var canvas=GameObject.Find("Original authored menus");
                        var content=canvas.transform.Find("menu2 authored Canvas/VideoOptions/VideoBox/Scroll View/Viewport/Content/VSync/Value/Dropdown/Template/Viewport/Content");
                        var leaves=content.Cast<Transform>().Where(t=>t.gameObject.activeInHierarchy).Select(t=>t.GetComponentInChildren<TMP_Text>().text).ToArray();
                        SettingsUiRequire(leaves.SequenceEqual(new[]{on,off}),"vsync-popup-actual-leaves-"+language+"-"+backend,checks);
                        settingsDropdownOpen=false;hud.CloseSettingsDropdown();
                        control.setOption(0);SettingsUiRequire(NativeUserSettings.GetInt("vsync")==1 && QualitySettings.vSyncCount==1,"vsync-index0-immediate-"+language+"-"+backend,checks);
                        control.setOption(1);SettingsUiRequire(NativeUserSettings.GetInt("vsync")==0 && QualitySettings.vSyncCount==0,"vsync-index1-immediate-"+language+"-"+backend,checks);
                    }
                }
                report.passed=true;
            }
            finally
            {
                NativeSaveStore.ReplaceData(saved);NativeUserSettings.ApplyScene(this);
                settingsVolumePercent.Clear();foreach(var pair in percentages)settingsVolumePercent.Add(pair.Key,pair.Value);
                settingsDropdownOpen=false;hud.CloseSettingsDropdown();SettingsGo(SettingsPage.Root);
                report.checks=checks.ToArray();Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory,"settings-integer-verification.json"),JsonUtility.ToJson(report,true));
            }
        }
    }
}
