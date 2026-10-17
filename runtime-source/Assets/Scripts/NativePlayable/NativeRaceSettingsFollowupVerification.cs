using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class SettingsFollowupReport
        {
            public bool passed,visualParityAccepted;
            public int resolutionCount,requestedRefreshRate,engineRefreshRate;
            public string scope="Injected input through production slider route with actual audio callbacks; source hardware resolution list and actual request; separate-process reload uses settings-restart phase";
            public string[] checks;
        }
        IEnumerator CheckSettingsFollowupControls()
        {
            var report=new SettingsFollowupReport();var checks=new List<string>();
            var saved=NativeUserSettings.CopyData();var states=new Dictionary<string,NativeSettingsSliderNavigation>(settingsSliderNavigation);
            int previousFullscreenIndex=settingsFullscreenIndex;
            var previousResolutions=settingsResolutionOptions;var previousLabels=settingsResolutionLabels;int previousResolutionIndex=settingsResolutionIndex;
            bool previousPlayback=NativeAudioRuntime.UiVerificationPlayback;float previousListener=AudioListener.volume;
            var sounds=new List<AudioClip>();Action<AudioSource,AudioClip> observer=(source,clip)=>{if(source==hud.CustomUiAudioSource)sounds.Add(clip);};
            try
            {
                NativeAudioRuntime.UiVerificationPlayback=true;AudioListener.volume=0;NativeAudioRuntime.UiOneShotPlayed+=observer;
                DeselectSettingsSlider();settingsSliderNavigation.Clear();
                NativeUserSettings.TrySetInt("fov",80);SettingsGo(SettingsPage.Video);settingsIndex=9;
                TickSettingsSliderNavigation(new Vector2(.001f,0),true,0);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==81 && sounds.Count==1 && sounds[0]==hud.CustomUiSound(0),"slider-first-small-positive-source-sound",checks);
                TickSettingsSliderNavigation(Vector2.right,false,.299f);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==81 && sounds.Count==1,"slider-before-first-delay",checks);
                TickSettingsSliderNavigation(Vector2.right,false,.002f);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==82 && sounds.Count==2,"slider-first-delay-source-clock",checks);
                TickSettingsSliderNavigation(Vector2.right,false,.039f);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==82 && sounds.Count==2,"slider-before-repeat-rate",checks);
                TickSettingsSliderNavigation(Vector2.right,false,.002f);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==83 && sounds.Count==3,"slider-repeat-rate",checks);
                TickSettingsSliderNavigation(Vector2.right,false,0);
                SettingsUiRequire(sounds.Count==3,"slider-zero-scaled-time-stops-countdown",checks);
                TickSettingsSliderNavigation(Vector2.left,true,0);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==82 && sounds.Count==4,"slider-started-resets-first-step",checks);
                NativeUserSettings.TrySetInt("fov",150);BuildSettingsRows();TickSettingsSliderNavigation(Vector2.right,true,0);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==150 && sounds.Count==5,"slider-upper-limit-still-source-move-sound",checks);
                DeselectSettingsSlider();settingsIndex=2;
                TickSettingsSliderNavigation(Vector2.left,true,.5f);
                SettingsUiRequire(sounds.Count==5,"dropdown-focus-no-slider-event",checks);
                settingsIndex=9;TickSettingsSliderNavigation(Vector2.left,false,0);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==149 && sounds.Count==6,"slider-reselected-held-moves-immediately",checks);
                TickSettingsSliderNavigation(Vector2.left,false,.041f);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==148 && sounds.Count==7,"slider-reselected-preserves-moved-rate",checks);
                settingsResolutionOptions=null;InitializeSettingsResolutionOptions();var hardware=Screen.resolutions;
                report.resolutionCount=hardware.Length;
                SettingsUiRequire(settingsResolutionLabels.SequenceEqual(hardware.Select(SettingsResolutionLabel)),"resolution-readable-labels-original-order-no-dedup",checks);
                int expectedIndex=Array.FindIndex(hardware,r=>r.width==Screen.width && r.height==Screen.height && Mathf.Abs(r.refreshRate-Screen.currentResolution.refreshRate)<=2);
                SettingsUiRequire(settingsResolutionIndex==(expectedIndex<0?0:expectedIndex),"resolution-source-first-match-tolerance",checks);
                if(hardware.Length>0)
                {
                    int index=Array.FindIndex(hardware,r=>r.width==Screen.width && r.height==Screen.height);
                    if(index>=0)
                    {
                        var resolution=hardware[index];SetSettingsResolutionOption(index);
                        SettingsUiRequire(NativeUserSettings.ReadValue("native.screen.refreshRate")==resolution.refreshRate.ToString() && NativeUserSettings.RequestedDisplayRefreshRate==resolution.refreshRate && settingsResolutionIndex==index,"resolution-selected-source-rate-saved-and-requested",checks);
                        yield return WaitForSettingsRender();report.requestedRefreshRate=NativeUserSettings.RequestedDisplayRefreshRate;report.engineRefreshRate=Screen.currentResolution.refreshRate;
                        SettingsUiRequire(Screen.width==resolution.width && Screen.height==resolution.height,"resolution-actual-dimensions",checks);
                    }
                }
                int rate=Screen.currentResolution.refreshRate;
                SettingsUiRequire(NativeUserSettings.TrySetDisplay(Screen.width,Screen.height,Screen.fullScreen,rate) && NativeUserSettings.RequestedDisplayRefreshRate==rate,"display-explicit-refresh-request",checks);
                NativeUserSettings.ApplyScene(this);
                SettingsUiRequire(NativeUserSettings.RequestedDisplayRefreshRate==rate,"display-unrelated-apply-preserves-refresh",checks);
                bool currentFullscreen=Screen.fullScreen;int modeIndex=currentFullscreen?0:1;
                string retainedWidth=NativeUserSettings.ReadValue("native.screen.width"),retainedHeight=NativeUserSettings.ReadValue("native.screen.height");
                SetSettingsFullscreenOption(modeIndex);BuildSettingsRows();
                var modeRow=settingsRows.Single(row=>row.control!=null && row.control.node=="Mode").control;
                SettingsUiRequire(Screen.fullScreen==currentFullscreen && NativeUserSettings.ReadValue("native.screen.fullscreen")==currentFullscreen.ToString(),"fullscreen-source-boolean-setter-same-state",checks);
                SettingsUiRequire(NativeUserSettings.ReadValue("native.screen.width")==retainedWidth && NativeUserSettings.ReadValue("native.screen.height")==retainedHeight && NativeUserSettings.RequestedDisplayRefreshRate==rate,"fullscreen-preserves-existing-resolution-and-refresh-request",checks);
                SettingsUiRequire(modeRow.option==modeIndex && modeRow.value==NativeLocalization.Get(modeIndex==0?"VIDEO_MODE_FULLSCREEN":"VIDEO_MODE_WINDOWED") && modeRow.options.SequenceEqual(new[]{NativeLocalization.Get("VIDEO_MODE_FULLSCREEN"),NativeLocalization.Get("VIDEO_MODE_WINDOWED")}),"fullscreen-event-chosen-and-original-localized-options",checks);
                report.passed=true;
            }
            finally
            {
                NativeAudioRuntime.UiOneShotPlayed-=observer;NativeAudioRuntime.UiVerificationPlayback=previousPlayback;
                NativeSaveStore.ReplaceData(saved);NativeUserSettings.ApplyScene(this);AudioListener.volume=previousListener;
                DeselectSettingsSlider();settingsSliderNavigation.Clear();foreach(var pair in states)settingsSliderNavigation.Add(pair.Key,pair.Value);
                settingsFullscreenIndex=previousFullscreenIndex;
                settingsResolutionOptions=previousResolutions;settingsResolutionLabels=previousLabels;settingsResolutionIndex=previousResolutionIndex;
                SettingsGo(SettingsPage.Root);report.checks=checks.ToArray();Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory,"settings-followup-verification.json"),JsonUtility.ToJson(report,true));
            }
        }
    }
}
