using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class SettingsUiReport
        {
            public bool passed;
            public string error;
            public string presentationPolicy;
            public string[] checks, rootRows, videoRows, audioRows, languageRows, profileRows, editRows;
            public string createdProfile, selectedProfile, savePath;
        }
        public bool SettingsUiVerificationPassed { get; private set; }
        internal IEnumerator CheckSettingsUiLive()
        {
            var verification=CheckSettingsUiLiveCore();
            while(true)
            {
                bool next=false;object instruction=null;Exception failure=null;
                try {next=verification.MoveNext();if(next)instruction=verification.Current;}
                catch(Exception error){failure=error;}
                if(failure!=null)
                {
                    SettingsUiVerificationPassed=false;string path=Path.Combine(reportDirectory,"settings-ui-verification.json");
                    var report=File.Exists(path)?JsonUtility.FromJson<SettingsUiReport>(File.ReadAllText(path)):new SettingsUiReport();report.passed=false;report.error=failure.ToString();
                    Directory.CreateDirectory(reportDirectory);File.WriteAllText(path,JsonUtility.ToJson(report,true));break;
                }
                if(!next)break;yield return instruction;
            }
        }
        IEnumerator WaitForSettingsRender()
        {
            while (hud.MenuTransitionActive) yield return null;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
        }
        IEnumerator CaptureSettingsUi(string filename)
        {
            yield return WaitForSettingsRender();
            ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, filename));
            yield return null;
            yield return new WaitForEndOfFrame();
        }
        void CheckSettingsVisible(string page, string control, string expected, List<string> checks)
        {
            var ui = GameObject.Find("Original authored menus");
            var viewport = ui ? ui.transform.Find("menu2 authored Canvas/" + page + "/VideoBox/Scroll View/Viewport") as RectTransform : null;
            var property = viewport ? viewport.Find("Content/" + control + "/Name/Property") : null;
            var text = property ? property.GetComponent<TMPro.TMP_Text>() : null;
            SettingsUiRequire(viewport && viewport.gameObject.activeInHierarchy && viewport.rect.width > 100 && viewport.rect.height > 100,
                page + "-visible-nonzero-viewport", checks);
            SettingsUiRequire(text && text.gameObject.activeInHierarchy && text.rectTransform.rect.height > 10 && text.text == expected,
                page + "-visible-translated-source-label", checks);
        }
        IEnumerator CheckSettingsUiLiveCore()
        {
            var result=new SettingsUiReport{presentationPolicy=automatedSettingsPresentation?"automated-background-vsync-off":"user-settings"};var checks=new List<string>();var original=NativeUserSettings.CopyData();
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard=null;Mouse mouse=null;var disabled=new List<InputDevice>();var previousBackground=InputSystem.settings.backgroundBehavior;
#endif
            try
            {
#if ENABLE_INPUT_SYSTEM
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                foreach(var device in InputSystem.devices)if(device.enabled && (device is Keyboard || device is Gamepad || device is Mouse))disabled.Add(device);
                foreach(var device in disabled)InputSystem.DisableDevice(device);
                keyboard=InputSystem.AddDevice<Keyboard>("settings-ui-verification-keyboard");
#endif
                Directory.CreateDirectory(reportDirectory);OpenSettings();yield return CheckSettingsIntegerControls();yield return CheckSettingsFollowupControls();result.rootRows=SettingsUiRows();SettingsUiRequire(settingsRows.Count==4,"original-four-root-pages",checks);
                yield return CaptureSettingsUi("settings-ui-root.png");
                ActivateSettingsRow(0);yield return WaitForSettingsRender();CheckSettingsVisible("VideoOptions","Mode",NativeLocalization.Get("VIDEO_MODE"),checks);result.videoRows=SettingsUiRows();int texture=NativeUserSettings.GetInt("video_texture");ActivateSettingsRow(4);
                SettingsUiRequire(settingsDropdownOpen,"video-dropdown-opens",checks);
                yield return CaptureSettingsUi("settings-ui-video-dropdown.png");
                var originalUi = GameObject.Find("Original authored menus");
                var optionViewport = originalUi.transform.Find("menu2 authored Canvas/VideoOptions/VideoBox/Scroll View/Viewport/Content/TextureResolution/Value/Dropdown/Template/Viewport") as RectTransform;
                SettingsUiRequire(optionViewport && optionViewport.rect.width > 100 && optionViewport.rect.height > 100,
                    "dropdown-visible-nonzero-viewport", checks);
                var visibleOptions = optionViewport.GetComponentsInChildren<TMPro.TMP_Text>()
                    .Where(text => text.rectTransform.rect.width > 10 && text.rectTransform.rect.height > 10).Select(text => text.text).ToArray();
                SettingsUiRequire(new[] { "Full", "Half", "Quarter", "Eighth" }.All(value => visibleOptions.Contains(value)),
                    "dropdown-four-source-labels-have-visible-layout", checks);

#if ENABLE_INPUT_SYSTEM
                yield return MenuParityKey(keyboard,Key.DownArrow);
                yield return MenuParityKey(keyboard,Key.Enter);
#endif
                SettingsUiRequire(!settingsDropdownOpen,"video-dropdown-confirms",checks);
                SettingsUiRequire(NativeUserSettings.GetInt("video_texture")!=texture,"video-control-persists",checks);SettingsBack();
                ActivateSettingsRow(1);yield return WaitForSettingsRender();CheckSettingsVisible("AudioOptions","MasterVolumeSlider",NativeLocalization.Get("AUDIO_VOLUME_MAIN"),checks);yield return CaptureSettingsUi("settings-ui-audio.png");result.audioRows=SettingsUiRows();float volume=NativeUserSettings.GetFloat("mainVolume");settingsRows[0].adjust(-1);BuildSettingsRows();
                SettingsUiRequire(Mathf.Abs(NativeUserSettings.GetFloat("mainVolume")-Mathf.Clamp((int)(volume*100f)-1,0,100)/100f)<.001f && AudioListener.volume==0,"audio-control-construction-silence",checks);SettingsBack();
                ActivateSettingsRow(3);yield return WaitForSettingsRender();CheckSettingsVisible("LanguageOptions","Language",NativeLocalization.Get("LANGUAGE_GAME"),checks);yield return CaptureSettingsUi("settings-ui-language.png");result.languageRows=SettingsUiRows();int language=NativeUserSettings.GetInt("lang_game");ActivateSettingsRow(0);
#if ENABLE_INPUT_SYSTEM
                yield return MenuParityKey(keyboard,Key.DownArrow);
                yield return MenuParityKey(keyboard,Key.Enter);
#endif
                SettingsUiRequire(NativeUserSettings.GetInt("lang_game")!=language,"language-control-persists",checks);SettingsBack();
                ActivateSettingsRow(0);int previous=NativeUserSettings.GetInt("fov");ActivateSettingsRow(9);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==previous,"FOV-slider-submit-keeps-value",checks);
                var fovDefinition=NativeUserSettings.Find("fov");int fovDelta=previous>=fovDefinition.maximum?-1:1;
                settingsRows[9].adjust(fovDelta);BuildSettingsRows();
                int expectedFov=Mathf.Clamp(previous+fovDelta,(int)fovDefinition.minimum,(int)fovDefinition.maximum);
                SettingsUiRequire(NativeUserSettings.GetInt("fov")==expectedFov && Mathf.Abs(raceCamera.fieldOfView-expectedFov)<.01f,"FOV-slider-one-unit-clamped-persists-and-applies",checks);SettingsBack();
                ActivateSettingsRow(2);SettingsUiRequire(settingsPage==SettingsPage.Profiles,"profiles-open",checks);
                int count=NativeInputBindings.Profiles.Length;result.profileRows=SettingsUiRows();
                SettingsUiRequire(settingsRows.Count==count+1,"profiles-only-saved-and-create",checks);
                ActivateSettingsRow(count);SettingsUiRequire(settingsPage==SettingsPage.ProfileDevices,"create-opens-device-list",checks);
                var sourceDeviceSnapshot=InputSystem.devices.ToArray();
                SettingsUiRequire(settingsRows.Select(row=>row.label).SequenceEqual(sourceDeviceSnapshot.Select(device=>device.displayName)),"device-selector-original-all-devices-and-raw-labels",checks);
                SettingsUiRequire(settingsProfileDeviceOptions.SequenceEqual(sourceDeviceSnapshot),"device-selector-snapshot-preserves-object-order",checks);
#if ENABLE_INPUT_SYSTEM
                var hotplug=InputSystem.AddDevice<Gamepad>("settings-ui-device-snapshot-hotplug");
                try
                {
                    BuildSettingsRows();
                    SettingsUiRequire(settingsRows.Select(row=>row.label).SequenceEqual(sourceDeviceSnapshot.Select(device=>device.displayName)) && !settingsProfileDeviceOptions.Contains(hotplug),"active-device-selector-keeps-OnEnable-snapshot-after-hotplug",checks);
                }
                finally {InputSystem.RemoveDevice(hotplug);}
                Keyboard selectionKeyboard=keyboard;
#else
                Keyboard selectionKeyboard=Keyboard.current;
#endif
                int keyboardRow=Array.FindIndex(settingsProfileDeviceOptions,device=>ReferenceEquals(device,selectionKeyboard));
                SettingsUiRequire(keyboardRow>=0,"device-selector-actual-fixture-keyboard-found",checks);
                ActivateSettingsRow(keyboardRow);
                SettingsUiRequire(settingsProfileDeviceOptions==null,"device-selector-disable-clears-snapshot",checks);
                SettingsUiRequire(settingsPage==SettingsPage.EditProfile && NativeInputBindings.Profiles.Length==count+1,"create-keyboard-control",checks);
                string id=settingsEditingId;result.createdProfile=id;result.editRows=SettingsUiRows();
                SettingsUiRequire(settingsRows.Count==18,"original-eighteen-rebinding-rows",checks);
#if ENABLE_INPUT_SYSTEM
                ActivateSettingsRow(4);SettingsUiRequire(NativeInputBindings.IsRebinding,"binding-control-start",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return null;yield return new WaitForSecondsRealtime(.2f);
                SettingsUiRequire(!NativeInputBindings.IsRebinding && NativeInputBindings.Draft.overrides.Length==1 && NativeInputBindings.Draft.overrides[0].path=="<Keyboard>/a","binding-control-capture",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                yield return MenuParityKey(keyboard,Key.X);
                SettingsUiRequire(settingsPage==SettingsPage.Profiles && NativeInputBindings.Draft==null && NativeInputBindings.Profiles[count].overrides.Length==0,"rebindings-cancel-returns-list-and-discards-draft",checks);
                EditSettingsProfile(id);
                ActivateSettingsRow(4);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return null;yield return new WaitForSecondsRealtime(.2f);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
#else
                throw new InvalidOperationException("Settings UI binding verification requires real InputSystem");
#endif
                yield return CaptureSettingsUi("settings-ui-profile.png");
                settingsIndex=settingsRows.Count-1;
#if ENABLE_INPUT_SYSTEM
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));yield return null;yield return new WaitForSecondsRealtime(.25f);
                SettingsUiRequire(settingsPage==SettingsPage.Profiles && NativeInputBindings.Draft==null,"done-held-confirm-stays-in-profile-list",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
#else
                ActivateSettingsRow(settingsRows.Count-1);
#endif
                SettingsUiRequire(settingsPage==SettingsPage.Profiles,"done-returns-profile-list",checks);
                var saved=NativeInputBindings.Profiles[count];
                SettingsUiRequire(saved.profileId==id && saved.overrides.Length==1 && saved.overrides[0].path=="<Keyboard>/a","done-persists-binding",checks);
                result.selectedProfile=NativeInputBindings.SelectedProfile();
                settingsIndex=count;
#if ENABLE_INPUT_SYSTEM
                yield return MenuParityKey(keyboard,Key.Space);
#endif
                SettingsUiRequire(settingsPage==SettingsPage.DeleteProfile,"space-opens-delete-confirmation",checks);
                yield return CaptureSettingsUi("settings-ui-delete-confirm.png");
#if ENABLE_INPUT_SYSTEM
                yield return MenuParityKey(keyboard,Key.X);
#endif
                SettingsUiRequire(settingsPage==SettingsPage.Profiles && NativeInputBindings.Profiles.Length==count+1,"delete-cancel-keeps-profile",checks);
                settingsIndex=count;
#if ENABLE_INPUT_SYSTEM
                yield return MenuParityKey(keyboard,Key.Space);
#endif
                ActivateSettingsRow(0);
                SettingsUiRequire(settingsPage==SettingsPage.Profiles && NativeInputBindings.Profiles.Length==count,"delete-confirm-removes-profile",checks);
                SettingsUiRequire(settingsEditingId==null && settingsProfileFocusPending && settingsIndex==-1,"delete-confirm-clears-target-and-defers-GoBack-focus",checks);
                float savedTimeScale=Time.timeScale;Time.timeScale=0f;
                try {yield return new WaitForSecondsRealtime(Time.fixedDeltaTime*2f);SettingsUiRequire(settingsProfileFocusPending && settingsIndex==-1,"delete-GoBack-Invoke-uses-scaled-time",checks);}
                finally {Time.timeScale=savedTimeScale;}
                yield return new WaitForSeconds(Time.fixedDeltaTime);yield return null;
                SettingsUiRequire(!settingsProfileFocusPending && settingsIndex==0 && settingsPage==SettingsPage.Profiles,"delete-GoBack-focus-after-fixedDeltaTime",checks);
                SettingsBack();
                SettingsUiRequire(!SettingsOpen && hud.MenuPage==NativeMenuPage.Main,"profiles-return-original-home",checks);
#if ENABLE_INPUT_SYSTEM
                mouse=InputSystem.AddDevice<Mouse>("settings-ui-verification-mouse");
                OpenSettings();ActivateSettingsRow(2);count=NativeInputBindings.Profiles.Length;ActivateSettingsRow(count);
                int mouseRow=Array.FindIndex(settingsProfileDeviceOptions,device=>ReferenceEquals(device,mouse));
                SettingsUiRequire(mouseRow>=0,"device-selector-actual-fixture-mouse-found",checks);
                ActivateSettingsRow(mouseRow);id=settingsEditingId;
                SettingsUiRequire(settingsRows.Count==18 && string.IsNullOrEmpty(settingsRows[4].control.value),"mouse-original-empty-binding-rows",checks);
                ActivateSettingsRow(4);SettingsUiRequire(NativeInputBindings.IsRebinding,"mouse-empty-binding-starts-capture",checks);
                InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});yield return null;yield return new WaitForSecondsRealtime(.2f);
                SettingsUiRequire(!NativeInputBindings.IsRebinding && NativeInputBindings.Draft.overrides.Length==1 && NativeInputBindings.Draft.overrides[0].path=="<Mouse>/leftButton","mouse-button-captured",checks);
                InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
                ActivateSettingsRow(settingsRows.Count-1);
                SettingsUiRequire(settingsPage==SettingsPage.Profiles && NativeInputBindings.Profiles[count].overrides[0].path=="<Mouse>/leftButton","mouse-done-persists-binding",checks);
                SettingsUiRequire(NativeInputBindings.SelectProfile(id),"mouse-profile-selects",checks);NativeInputBindings.Initialize();
                InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});yield return null;
                SettingsUiRequire(NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate),"mouse-binding-drives-original-action-after-reload",checks);
                InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;NativeInputBindings.SelectProfile("");
                settingsIndex=count;yield return MenuParityKey(keyboard,Key.Space);ActivateSettingsRow(0);SettingsBack();
#endif
                result.savePath=NativeSaveStore.FilePath;result.passed=true;
            }
            finally
            {
                SettingsUiVerificationPassed=result.passed;result.checks=checks.ToArray();
                if(!result.passed)result.error="UI verification stopped after "+checks.Count+" checks";
                NativeInputBindings.CancelEdit();settingsNameEditing=false;settingsOpen=false;
#if ENABLE_INPUT_SYSTEM
                if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);foreach(var device in disabled)if(device.added)InputSystem.EnableDevice(device);
                InputSystem.settings.backgroundBehavior=previousBackground;
#endif
                NativeSaveStore.ReplaceData(original);NativeInputBindings.Initialize();NativeUserSettings.ApplyScene(this);hud.SetMenuPage(settingsReturnPage);
                Directory.CreateDirectory(reportDirectory);File.WriteAllText(Path.Combine(reportDirectory,"settings-ui-verification.json"),JsonUtility.ToJson(result,true));
            }
        }
        string[] SettingsUiRows(){var rows=new string[settingsRows.Count];for(int i=0;i<rows.Length;i++)rows[i]=settingsRows[i].label;return rows;}
        static void SettingsUiRequire(bool condition,string name,List<string> checks)
        {if(!condition)throw new InvalidOperationException(name+": "+NativeInputBindings.LastError+NativeSaveStore.LastError);checks.Add(name);}
    }
}
