using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        enum SettingsPage { Root,Video,Audio,Gameplay,Language,Profiles,ProfileDevices,EditProfile,DeleteProfile }
        sealed class SettingRow {public string label;public NativeSettingsControl control;public System.Action<int> adjust;public System.Action confirm;}
        readonly List<SettingRow> settingsRows=new List<SettingRow>();
        readonly Dictionary<string,int> settingsVolumePercent=new Dictionary<string,int>();
        int SettingsVolumePercent(string key)
        {
            if(!settingsVolumePercent.TryGetValue(key,out int value))
            {
                value=(int)(NativeUserSettings.GetFloat(key)*100f);settingsVolumePercent.Add(key,value);
                // Awake assigns the integer slider after subscribing, so values below its serialized 100 invoke SetValue.
                if(value!=100)SettingsApply(NativeUserSettings.TrySetFloat(key,value/100f));
            }
            return value;
        }
        void SetSettingsVolumePercent(string key,int value)
        {
            value=Mathf.Clamp(value,0,100);
            if(value==SettingsVolumePercent(key))return;
            bool saved=NativeUserSettings.TrySetFloat(key,value/100f);
            if(saved)settingsVolumePercent[key]=value;
            SettingsApply(saved);
        }
        readonly Dictionary<string,NativeSettingsSliderNavigation> settingsSliderNavigation=new Dictionary<string,NativeSettingsSliderNavigation>();
        string settingsActiveSlider;
        NativeSettingsSliderNavigation SettingsSliderState(string node)
        {
            if(!settingsSliderNavigation.TryGetValue(node,out var state))
            {state=new NativeSettingsSliderNavigation();settingsSliderNavigation.Add(node,state);}
            return state;
        }
        void DeselectSettingsSlider()
        {
            if(settingsActiveSlider!=null)SettingsSliderState(settingsActiveSlider).Deselect();
            settingsActiveSlider=null;
        }
        void TickSettingsSliderNavigation(Vector2 navigation,bool started,float deltaTime)
        {
            var control=settingsRows.Count==0?null:settingsRows[settingsIndex].control;
            string node=!settingsDropdownOpen && control!=null && control.setNormalized!=null?control.node:null;
            if(node!=settingsActiveSlider)
            {DeselectSettingsSlider();settingsActiveSlider=node;if(node!=null)SettingsSliderState(node).Select();}
            if(node==null)return;
            var state=SettingsSliderState(node);if(started)state.Started();
            int delta=state.Tick(navigation.x,deltaTime);
            if(delta!=0){settingsRows[settingsIndex].adjust(delta);hud.PlayCustomizationSound(0);BuildSettingsRows();}
        }
        SettingsPage settingsPage;
        NativeMenuPage settingsReturnPage;
        bool settingsOpen,settingsNameEditing;
        bool settingsConfirmAwaitingRelease;
        NativeSaveData settingsRowsData;
        int settingsRowsLanguage = -1;
        int settingsIndex,settingsDirection;
        int settingsCategoryIndex;
        bool settingsDropdownOpen;int settingsDropdownOption;
        float settingsRepeat,rebindStart;
        string settingsNotice="",settingsEditingId="",settingsName="",settingsNameBeforeEdit="";
        public bool SettingsOpen => settingsOpen;
        static string SettingsText(string key,string english,string spanish=null)=>NativeLocalization.Get(key,NativeLocalization.Language==1?(spanish??english):english);
        public void OpenSettings()
        {
            settingsReturnPage=hud.MenuPage;settingsOpen=true;settingsPage=SettingsPage.Root;settingsIndex=0;settingsDirection=0;settingsNotice="";hud.SetMenuPage(NativeMenuPage.Options);NativeInputBindings.Initialize();BuildSettingsRows();
        }
        public bool HandleSettingsInput()
        {
            if(!settingsOpen){DeselectSettingsSlider();return false;}
            if (Net && Net.ProfileFromSettings) return true;
            if(hud.MenuTransitionActive)return true;
            NativeInputBindings.Tick();
            bool completingInput = settingsConfirmAwaitingRelease;
            if (settingsConfirmAwaitingRelease)
            {
                if (NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Ok) || (Keyboard.current != null && Keyboard.current.enterKey.isPressed)) return true;
                settingsConfirmAwaitingRelease = false;
            }
            if(NativeInputBindings.IsRebinding) {DeselectSettingsSlider();if(Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame)NativeInputBindings.CancelRebind();return true;}
            if(settingsNameEditing)
            {
                DeselectSettingsSlider();
                if(Keyboard.current!=null && Keyboard.current.enterKey.wasPressedThisFrame){CompleteSettingsName(true);}
                if(Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame){CompleteSettingsName(false);}
                return true;
            }
            if(NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Cancel) || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)){if(settingsDropdownOpen){settingsDropdownOpen=false;hud.CloseSettingsDropdown();}else SettingsBack();return true;}
            if(settingsPage==SettingsPage.Profiles && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.DeleteProfile) && settingsIndex>=0 && settingsIndex<NativeInputBindings.Profiles.Length)
            {settingsEditingId=NativeInputBindings.Profiles[settingsIndex].profileId;SettingsGo(SettingsPage.DeleteProfile);return true;}
            int priorIndex=settingsIndex;var priorPage=settingsPage;
            bool navigateStarted=NativeInputBindings.GetNavigateStarted(out var startedNavigation);
            if(navigateStarted && settingsActiveSlider!=null)SettingsSliderState(settingsActiveSlider).Started();
            Vector2 navigation=NativeInputBindings.GetVector();int direction=navigation.y>0.5f?-1:navigation.y<-.5f?1:0;
            bool started=direction!=settingsDirection;if(direction!=0 && (started || Time.unscaledTime>=settingsRepeat)){if(settingsDropdownOpen){int count=settingsRows[settingsIndex].control.options.Length;settingsDropdownOption=(settingsDropdownOption+direction+count)%count;}else settingsIndex=(settingsIndex+direction+settingsRows.Count)%settingsRows.Count;settingsRepeat=Time.unscaledTime+(started?0.35f:0.12f);}settingsDirection=direction;
            int horizontal=navigation.x>0.5f?1:navigation.x<-.5f?-1:0;
            if(settingsIndex<0)return true;
            bool slider=settingsRows[settingsIndex].control?.setNormalized!=null;
            if(slider)TickSettingsSliderNavigation(navigation,navigateStarted && priorIndex==settingsIndex && priorPage==settingsPage,Time.deltaTime);
            else {DeselectSettingsSlider();if(!settingsDropdownOpen && horizontal!=0 && Time.unscaledTime>=settingsRepeat){settingsRows[settingsIndex].adjust?.Invoke(horizontal);settingsRepeat=Time.unscaledTime+0.15f;BuildSettingsRows();}}
            if(!completingInput && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok)){if(settingsDropdownOpen){settingsRows[settingsIndex].control.setOption(settingsDropdownOption);settingsDropdownOpen=false;hud.CloseSettingsDropdown();BuildSettingsRows();}else ActivateSettingsRow(settingsIndex);}
            return true;
        }
        string SettingsInputHint()
        {
            string navigation = NativeInputBindings.PromptDevice() is Gamepad ? NativeLocalization.IsChinese ? "方向键／左摇杆" : "D-pad / Left Stick" : "↑↓ / ←→";
            string format = NativeLocalization.IsChinese ? "{0} 选择／调整 · {1} 确认 · {2} 返回"
                : SettingsText("NATIVE_SETTINGS_HINT_BOUND", "{0} select / adjust · {1} confirm · {2} back", "{0} elegir / ajustar · {1} aceptar · {2} atrás");
            return string.Format(format, navigation, NativeMainPromptSource.Display(NativeInputBindings.Action.Ok), NativeMainPromptSource.Display(NativeInputBindings.Action.Cancel));
        }
        public bool DrawSettings(float width)
        {
            if(!settingsOpen)return false;
            if (Net && Net.ProfileFromSettings) return true;
            if ((settingsPage != SettingsPage.Video && settingsPage != SettingsPage.Audio && settingsPage != SettingsPage.Language) ||
                !ReferenceEquals(settingsRowsData, NativeSaveStore.Data) || settingsRowsLanguage != NativeLocalization.Language ||
                (settingsPage == SettingsPage.Video && (settingsDisplayWidth != Screen.width || settingsDisplayHeight != Screen.height || settingsDisplayFullscreen != Screen.fullScreen))) BuildSettingsRows();
            var labels=new string[settingsRows.Count];for(int i=0;i<labels.Length;i++)labels[i]=settingsRows[i].label;
            string title=settingsPage==SettingsPage.Root?SettingsPageTitle(settingsPage):SettingsText("MENU_OPTIONS","Settings","Opciones")+" / "+SettingsPageTitle(settingsPage);
            string status=NativeInputBindings.IsRebinding?SettingsText("NATIVE_REBIND_WAIT","Press a control · Escape cancels · ","Pulsa un control · Esc cancela · ")+Mathf.Max(0,10-(Time.unscaledTime-rebindStart)).ToString("0")+"s":string.IsNullOrEmpty(settingsNotice)?SettingsInputHint():settingsNotice;
            int clicked=hud.SettingsMenu(width,title,labels,settingsIndex,status,settingsPage.ToString(),settingsRows.ConvertAll(r=>r.control).ToArray(),settingsDropdownOpen,settingsDropdownOption,option=>{settingsRows[settingsIndex].control.setOption(option);settingsDropdownOpen=false;hud.CloseSettingsDropdown();BuildSettingsRows();},()=>{settingsDropdownOpen=false;hud.CloseSettingsDropdown();});
            if(clicked==NativeRaceHud.SettingsPointerBack)
            {
                if(NativeInputBindings.IsRebinding)NativeInputBindings.CancelRebind();
                else if(settingsNameEditing)CompleteSettingsName(false);
                else if(settingsDropdownOpen){settingsDropdownOpen=false;hud.CloseSettingsDropdown();}
                else SettingsBack();
                return true;
            }
            if(hud.SettingsPointerFocus>=0 && !settingsDropdownOpen && !settingsNameEditing && !NativeInputBindings.IsRebinding)settingsIndex=hud.SettingsPointerFocus;
            if(clicked==NativeRaceHud.SettingsPointerConfirm && !NativeInputBindings.IsRebinding)
            {
                if(settingsNameEditing)CompleteSettingsName(true);
                else if(settingsDropdownOpen){settingsRows[settingsIndex].control.setOption(settingsDropdownOption);settingsDropdownOpen=false;hud.CloseSettingsDropdown();BuildSettingsRows();}
                else if(settingsIndex>=0 && settingsIndex<settingsRows.Count)ActivateSettingsRow(settingsIndex);
                return true;
            }
            if(settingsNameEditing){settingsName=hud.SettingsNameField(width,settingsName);NativeInputBindings.SetDraftName(settingsName);}
            if(clicked>=0 && !NativeInputBindings.IsRebinding && !settingsNameEditing){ActivateSettingsRow(clicked);}
            return true;
        }
        void ActivateSettingsRow(int index)
        {
            settingsIndex=index;var row=settingsRows[index];if(row.control!=null && row.control.options!=null){if(row.control.options.Length>0){settingsDropdownOpen=true;settingsDropdownOption=row.control.option;}return;}if(row.confirm!=null)row.confirm();else if(row.control==null)row.adjust?.Invoke(1);BuildSettingsRows();
        }
        void CompleteSettingsName(bool confirmed)
        {
            NativeInputBindings.SetDraftName(confirmed?settingsName:settingsNameBeforeEdit);settingsNameEditing=false;BuildSettingsRows();
        }
        string SettingsPageTitle(SettingsPage page)
        {
            switch(page){case SettingsPage.Video:return SettingsText("OPTIONS_VIDEO","Video");case SettingsPage.Audio:return SettingsText("OPTIONS_AUDIO","Audio");case SettingsPage.Gameplay:return SettingsText("OPTIONS_GAME","Gameplay","Juego");case SettingsPage.Language:return SettingsText("OPTIONS_LANGUAGE","Language");case SettingsPage.Profiles:return SettingsText("OPTIONS_INPUT","Input","Entrada");case SettingsPage.EditProfile:return SettingsText("OPTIONS_INPUT","Input configuration","Configuración de entrada");default:return SettingsText("MENU_OPTIONS","Settings");}
        }
        void SettingsGo(SettingsPage page){if(settingsPage==SettingsPage.ProfileDevices && page!=SettingsPage.ProfileDevices)settingsProfileDeviceOptions=null;if(page==SettingsPage.ProfileDevices && settingsPage!=SettingsPage.ProfileDevices)settingsProfileDeviceOptions=new List<InputDevice>(InputSystem.devices).ToArray();DeselectSettingsSlider();settingsDropdownOpen=false;hud.CloseSettingsDropdown();if(settingsPage==SettingsPage.Root)settingsCategoryIndex=settingsIndex;settingsPage=page;settingsIndex=page==SettingsPage.Root?settingsCategoryIndex:page==SettingsPage.DeleteProfile?1:0;settingsNotice="";BuildSettingsRows();}
        void SettingsBack()
        {
            DeselectSettingsSlider();
            settingsDropdownOpen=false;hud.CloseSettingsDropdown();
            if(settingsPage==SettingsPage.EditProfile){NativeInputBindings.CancelEdit();settingsEditingId=null;SettingsGo(SettingsPage.Profiles);return;}
            if(settingsPage==SettingsPage.ProfileDevices || settingsPage==SettingsPage.DeleteProfile){SettingsGo(SettingsPage.Profiles);return;}
            if(settingsPage==SettingsPage.Profiles && settingsIdentityReturn != null) { var callback = settingsIdentityReturn; settingsIdentityReturn = null; callback(); return; }
            if(settingsPage==SettingsPage.Profiles){settingsOpen=false;hud.SetMenuPage(NativeMenuPage.Main);heldMenuDirection=0;return;}
            if(settingsPage!=SettingsPage.Root){SettingsGo(SettingsPage.Root);return;}
            settingsOpen=false;hud.SetMenuPage(settingsReturnPage);heldMenuDirection=0;
        }
        void SettingsApply(bool saved, string changedKey = null)
        {
            settingsNotice=saved?"":SettingsText("NATIVE_SAVE_FAILED","Save failed: ","Error al guardar: ")+NativeSaveStore.LastError+NativeUserSettings.LastError+NativeInputBindings.LastError;
            if (!saved) { NativeTelemetry.Event("settings.apply", "rejected", "not-saved", changedKey); return; }
            var consumer = raceCamera ? raceCamera.GetComponent<NativeSceneSettings>() : null;
            if (changedKey == "fov")
            {
                if (raceCamera) raceCamera.fieldOfView = NativeUserSettings.GetInt(changedKey);
                if (consumer && consumer.Report != null) consumer.Report.fov = raceCamera.fieldOfView;
            }
            else if (changedKey == "targetFramerate") Application.targetFrameRate = NativeUserSettings.GetInt(changedKey);
            else if (changedKey == "vsync") QualitySettings.vSyncCount = NativeRenderingQuality.EffectiveVSyncCount(NativeUserSettings.GetInt(changedKey));
            else if (changedKey == "video_dynamiclights")
            {
                QualitySettings.pixelLightCount = NativeUserSettings.GetInt(changedKey);
                if (consumer && consumer.Report != null) consumer.Report.dynamicLights = QualitySettings.pixelLightCount;
            }
            else NativeUserSettings.ApplyScene(this);
            NativeTelemetry.Event("settings.apply", "applied", detail: changedKey ?? "scene-settings");
        }
        void AddSettingsLink(string label,System.Action action){settingsRows.Add(new SettingRow{label=label,confirm=action});}
        void AddSettingsValue(string key)
        {
            var definition=NativeUserSettings.Find(key);if(definition==null)return;
            string display=SettingsLabel(key)+"  ‹ "+SettingsValue(key)+" ›";
            var control=CreateSettingsControl(key);settingsRows.Add(new SettingRow{label=display,control=control,adjust=delta=>{bool saved;if(definition.floating){SetSettingsVolumePercent(key,SettingsVolumePercent(key)+delta);return;}else {int value=NativeUserSettings.GetInt(key)+delta;bool slider=key=="fov" || key=="targetFramerate" || key=="video_dynamiclights";if(slider)value=Mathf.Clamp(value,(int)definition.minimum,(int)definition.maximum);else {if(value>definition.maximum)value=(int)definition.minimum;if(value<definition.minimum)value=(int)definition.maximum;}saved=NativeUserSettings.TrySetInt(key,value);}SettingsApply(saved,key);}});
        }
        string SettingsLabel(string key)
        {
            var keys=new Dictionary<string,string>{{"video_particlelighting","VIDEO_PARTICLELIGHT"},{"targetFramerate","VIDEO_FRAMERATELIMIT"},{"fov","VIDEO_FOV"},{"vsync","VIDEO_VSYNC"},{"lang_game","LANGUAGE_GAME"},{"mainVolume","AUDIO_VOLUME_MAIN"},{"volumeGroup0","AUDIO_VOLUME_SFX"},{"volumeGroup1","AUDIO_VOLUME_MUSIC"},{"volumeGroup2","AUDIO_VOLUME_AMBIENT"},{"volumeGroup3","AUDIO_VOLUME_ENGINE"},{"volumeGroup4","AUDIO_VOLUME_ITEM"},{"volumeGroup5","AUDIO_VOLUME_EXPLOSION"},{"volumeGroup6","AUDIO_VOLUME_UI"}};
            string phrase;if(!keys.TryGetValue(key,out phrase))phrase=key.ToUpperInvariant();
            string fallback=key=="speedometerEnabled"?SettingsText("NATIVE_SPEEDOMETER","Speedometer","Velocímetro"):key=="alternativeJumping"?SettingsText("NATIVE_ALT_JUMP","Alternative jumping","Salto alternativo"):key=="qualityLevel"?SettingsText("NATIVE_QUALITY_PRESET","Quality preset","Calidad general"):key;
            return NativeLocalization.Get(phrase,fallback);
        }
        string SettingsValue(string key,int? option=null)
        {
            var definition=NativeUserSettings.Find(key);if(definition.floating)return SettingsVolumePercent(key).ToString()+"%";
            int value=option??NativeUserSettings.GetInt(key);if(key=="lang_game")return NativeLocalization.LanguageNames[value];if(key=="qualityLevel")return NativeLocalization.TranslateText(QualitySettings.names[value]);
            if(key=="video_aa")return new[]{"8x","4x","2x",SettingsText("VIDEO_OPTION_OFF","Off","Desactivado")}[value];
            if(key=="video_texture")return NativeLocalization.Get(new[]{"VIDEO_TEXTURE_FULL","VIDEO_TEXTURE_HALF","VIDEO_TEXTURE_QUARTER","VIDEO_TEXTURE_EIGHTH"}[value]);
            if(key=="video_aniso")return NativeLocalization.Get(new[]{"VIDEO_ANISO_FORCED","VIDEO_ANISO_TEXTURE","VIDEO_OPTION_OFF"}[value]);
            if(key=="video_shadowquality" || key=="video_draw_distance" || key=="video_shadowdistance")return NativeLocalization.Get(new[]{"VIDEO_QUALITY_ULTRA","VIDEO_QUALITY_HIGH","VIDEO_QUALITY_MEDIUM","VIDEO_QUALITY_LOW",key=="video_shadowquality"?"VIDEO_OPTION_OFF":"VIDEO_QUALITY_POTATO"}[value]);
            if(key=="video_particlequality")return NativeLocalization.Get(new[]{"VIDEO_QUALITY_ULTRA","VIDEO_QUALITY_MEDIUM","VIDEO_QUALITY_LOW"}[value]);
            if(key=="video_model")return NativeLocalization.Get(value==0?"VIDEO_MODEL_STANDARD":"VIDEO_MODEL_REDUCED");
            if(key=="video_shadowdy")return NativeLocalization.Get(value==0?"VIDEO_SHADOWDY_EVERYTHING":"VIDEO_SHADOWDY_NONSTATIC");
            if(key=="video_ppquality")return NativeLocalization.Get(value==0?"VIDEO_QUALITY_HIGH":"VIDEO_QUALITY_LOW");
            if(definition.maximum==1){bool on=key.StartsWith("video_",StringComparison.Ordinal)?value==0:value==1;return SettingsText(on?"VIDEO_OPTION_ON":"VIDEO_OPTION_OFF",on?"On":"Off",on?"Activado":"Desactivado");}
            return value.ToString();
        }
        void BuildSettingsRows()
        {
            settingsRowsData = NativeSaveStore.Data; settingsRowsLanguage = NativeLocalization.Language;
            settingsRows.Clear();
            if(settingsPage==SettingsPage.Root){foreach(var page in new[]{SettingsPage.Video,SettingsPage.Audio,SettingsPage.Profiles,SettingsPage.Language}){var target=page;AddSettingsLink(target == SettingsPage.Profiles ? NativeLocalization.TranslateText("Profile") : SettingsPageTitle(page),()=> { if (target == SettingsPage.Profiles) OpenIdentityProfileFromSettings(); else SettingsGo(target); });}}
            if(settingsPage==SettingsPage.Video){AddDisplayRows();foreach(string key in new[]{"vsync","targetFramerate","video_texture","video_aniso","video_model","video_grass","video_draw_distance","fov","video_dynamiclights","video_shadowquality","video_shadowdistance","video_shadowdy","video_particlequality","video_ppquality","video_bloom","video_ao","video_cc"})AddSettingsValue(key);}
            if(settingsPage==SettingsPage.Audio)foreach(string key in new[]{"mainVolume","volumeGroup0","volumeGroup2","volumeGroup1","volumeGroup6","volumeGroup3","volumeGroup4","volumeGroup5"})AddSettingsValue(key);
            if(settingsPage==SettingsPage.Gameplay){AddSettingsValue("speedometerEnabled");AddSettingsValue("alternativeJumping");}
            if(settingsPage==SettingsPage.Language)AddSettingsValue("lang_game");
            if(settingsPage==SettingsPage.Profiles)BuildProfileRows();if(settingsPage==SettingsPage.EditProfile)BuildProfileEditRows();
            if(settingsPage==SettingsPage.ProfileDevices)foreach(var device in settingsProfileDeviceOptions??Array.Empty<InputDevice>()){var target=device;AddSettingsLink(device.displayName,()=>CreateSettingsProfile("<"+target.layout+">",target.displayName.Trim()));}
            if(settingsPage==SettingsPage.DeleteProfile){AddSettingsLink(SettingsText("PROMPT_YES","Yes"),()=>{ConfirmSettingsProfileDeletion();});AddSettingsLink(SettingsText("PROMPT_NO","No"),()=>SettingsGo(SettingsPage.Profiles));}
            if(settingsPage==SettingsPage.Gameplay)AddSettingsLink(SettingsText("PROMPT_GOBACK","Go Back","Atrás"),SettingsBack);settingsIndex=settingsProfileFocusPending && settingsPage==SettingsPage.Profiles && settingsIndex<0?-1:Mathf.Clamp(settingsIndex,0,settingsRows.Count-1);
        }
        NativeSettingsControl CreateSettingsControl(string key)
        {
            var nodes=new Dictionary<string,string>{{"lang_game","Language"},{"vsync","VSync"},{"targetFramerate","FramerateLimit"},{"video_texture","TextureResolution"},{"video_aniso","Anisotropic"},{"video_model","ModelQuality"},{"video_grass","Grass"},{"video_draw_distance","DrawDistance"},{"fov","FieldOfView"},{"video_dynamiclights","MaxDynamicLights"},{"video_shadowquality","ShadowQuality"},{"video_shadowdistance","ShadowDistance"},{"video_shadowdy","DynamicShadows"},{"video_particlequality","ParticleQuality"},{"video_ppquality","PostProcessingQuality"},{"video_bloom","Bloom"},{"video_ao","Ambient Occlusion"},{"video_cc","ColorCorrection"},{"mainVolume","MasterVolumeSlider"},{"volumeGroup0","SFXVolumeSlider"},{"volumeGroup2","AMBIENTVolumeSlider"},{"volumeGroup1","MUSICVolumeSlider"},{"volumeGroup6","UIVolumeSlider"},{"volumeGroup3","EngineVolumeSlider"},{"volumeGroup4","ItemVolumeSlider"},{"volumeGroup5","ExplosionVolumeSlider"}};
            if(!nodes.TryGetValue(key,out var node))return null;
            var d=NativeUserSettings.Find(key);var control=new NativeSettingsControl{node=node,value=SettingsValue(key)};
            if(d.floating || key=="fov" || key=="targetFramerate" || key=="video_dynamiclights")
            {
                float current=d.floating?SettingsVolumePercent(key):NativeUserSettings.GetInt(key);
                control.minimum=d.floating?0:d.minimum;control.maximum=d.floating?100:d.maximum;control.wholeNumbers=true;
                control.normalized=Mathf.InverseLerp(control.minimum,control.maximum,current);
                control.setNormalized=value=>{int raw=Mathf.RoundToInt(Mathf.Lerp(control.minimum,control.maximum,value));if(d.floating)SetSettingsVolumePercent(key,raw);else if(raw!=NativeUserSettings.GetInt(key))SettingsApply(NativeUserSettings.TrySetInt(key,raw),key);};
            }
            else if(key=="vsync")
            {
                control.option=NativeUserSettings.GetInt(key)>0?0:1;
                control.options=new[]{SettingsValue(key,1),SettingsValue(key,0)};
                control.setOption=value=>SettingsApply(NativeUserSettings.TrySetInt(key,value<=0?1:0),key);
            }
            else {control.option=NativeUserSettings.GetInt(key);var options=new List<string>();for(int i=(int)d.minimum;i<=d.maximum;i++)options.Add(SettingsValue(key,i));control.options=options.ToArray();control.setOption=value=>SettingsApply(NativeUserSettings.TrySetInt(key,value),key);}
            return control;
        }
        InputDevice[] settingsProfileDeviceOptions;
        bool settingsProfileFocusPending;
        int settingsFullscreenIndex=-1;
        void SetSettingsFullscreenOption(int index)
        {
            bool saved=NativeUserSettings.TrySetFullscreen(index==0);
            if(saved)settingsFullscreenIndex=index==0?0:1;
            SettingsApply(saved);
        }
        Resolution[] settingsResolutionOptions;
        string[] settingsResolutionLabels;
        int settingsResolutionIndex;
        int settingsDisplayWidth, settingsDisplayHeight;
        bool settingsDisplayFullscreen;
        static string SettingsResolutionLabel(Resolution resolution)
        {
            string size = resolution.width + " × " + resolution.height;
            double rate = resolution.refreshRateRatio.value;
            if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate)) return size;
            if (Math.Abs(rate - Math.Round(rate)) < .01) rate = Math.Round(rate);
            return size + " @ " + rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " Hz";
        }
        void InitializeSettingsResolutionOptions()
        {
            if(settingsResolutionOptions!=null)return;
            settingsResolutionOptions=Screen.resolutions;
            settingsResolutionLabels=Array.ConvertAll(settingsResolutionOptions,SettingsResolutionLabel);
            settingsResolutionIndex=0;
            for(int i=0;i<settingsResolutionOptions.Length;i++)
            {
                var r=settingsResolutionOptions[i];
                if(r.width==Screen.width && r.height==Screen.height && Mathf.Abs(r.refreshRate-Screen.currentResolution.refreshRate)<=2)
                {settingsResolutionIndex=i;break;}
            }
        }
        void SetSettingsResolutionOption(int value)
        {
            var resolutions=Screen.resolutions;
            if(value<0 || value>=resolutions.Length)throw new ArgumentOutOfRangeException(nameof(value));
            var r=resolutions[value];
            bool saved=NativeUserSettings.TrySetDisplay(r.width,r.height,Screen.fullScreen,r.refreshRate);
            if(saved)settingsResolutionIndex=value;
            SettingsApply(saved);
        }
        void AddDisplayRows()
        {
            settingsDisplayWidth = Screen.width;
            settingsDisplayHeight = Screen.height;
            settingsDisplayFullscreen = Screen.fullScreen;
            settingsFullscreenIndex = Screen.fullScreen ? 0 : 1;
            var modeLabels=new[]{NativeLocalization.Get("VIDEO_MODE_FULLSCREEN","Fullscreen"),NativeLocalization.Get("VIDEO_MODE_WINDOWED","Windowed")};
            var mode=new NativeSettingsControl{node="Mode",value=modeLabels[settingsFullscreenIndex],option=settingsFullscreenIndex,options=modeLabels,setOption=SetSettingsFullscreenOption};
            settingsRows.Add(new SettingRow{label="Mode",control=mode,adjust=delta=>mode.setOption(1-mode.option)});
            InitializeSettingsResolutionOptions();
            // Window resizing is independent of the saved display mode. Show the live size
            // without writing it to disk or reapplying a stale resolution request.
            var current = new Resolution { width = Screen.width, height = Screen.height };
            if (Screen.fullScreen) current.refreshRateRatio = Screen.currentResolution.refreshRateRatio;
            var control=new NativeSettingsControl{node="Resolution",value=SettingsResolutionLabel(current),option=settingsResolutionIndex,options=settingsResolutionLabels,setOption=SetSettingsResolutionOption};
            settingsRows.Add(new SettingRow{label="Resolution",control=control,adjust=delta=>{if(settingsResolutionLabels.Length>0)SetSettingsResolutionOption((settingsResolutionIndex+delta+settingsResolutionLabels.Length)%settingsResolutionLabels.Length);}});
        }
        void BuildProfileRows()
        {
            foreach(var profile in NativeInputBindings.Profiles){var target=profile;AddSettingsLink(target.name,()=>EditSettingsProfile(target.profileId));}
            AddSettingsLink(SettingsText("PROFILES_NEW","Create new Profile"),()=>SettingsGo(SettingsPage.ProfileDevices));
        }
        void CreateSettingsProfile(string device,string displayName){string id=NativeInputBindings.CreateProfile("Profile "+(NativeInputBindings.Profiles.Length+1)+" ("+displayName+")",device);if(id.Length==0){SettingsApply(false);return;}EditSettingsProfile(id);}
        void ConfirmSettingsProfileDeletion()
        {
            SettingsApply(NativeInputBindings.DeleteProfile(settingsEditingId));settingsEditingId=null;
            settingsProfileFocusPending=true;SettingsGo(SettingsPage.Profiles);settingsIndex=-1;
            Invoke(nameof(CompleteSettingsProfileDeletionFocus),Time.fixedDeltaTime);
        }
        void CompleteSettingsProfileDeletionFocus()
        {
            settingsProfileFocusPending=false;settingsIndex=0;BuildSettingsRows();
        }
        void EditSettingsProfile(string id){if(!NativeInputBindings.BeginEdit(id)){SettingsApply(false);return;}settingsEditingId=id;settingsName=NativeInputBindings.Draft.name;SettingsGo(SettingsPage.EditProfile);}
        void BuildProfileEditRows()
        {
            var draft=NativeInputBindings.Draft;if(draft==null)return;
            AddProfileBinding("HorizontalLeft",NativeInputBindings.Action.Horizontal,0);
            AddProfileBinding("HorizontalRight",NativeInputBindings.Action.Horizontal,1);
            AddProfileBinding("VerticalDown",NativeInputBindings.Action.Vertical,0);
            AddProfileBinding("VerticalUp",NativeInputBindings.Action.Vertical,1);
            AddProfileBinding("Accelerate",NativeInputBindings.Action.Accelerate);
            AddProfileBinding("BreakDrift",NativeInputBindings.Action.Brake);
            AddProfileBinding("UseItem",NativeInputBindings.Action.Use);
            AddProfileBinding("Jump",NativeInputBindings.Action.Jump);
            AddProfileBinding("LookBack",NativeInputBindings.Action.LookBehind);
            AddProfileBinding("ChangeView",NativeInputBindings.Action.ChangeView);
            AddProfileBinding("OK",NativeInputBindings.Action.Ok);
            AddProfileBinding("Cancel",NativeInputBindings.Action.Cancel);
            AddProfileBinding("NavigateLeft",NativeInputBindings.Action.Navigate,2);
            AddProfileBinding("NavigateRight",NativeInputBindings.Action.Navigate,3);
            AddProfileBinding("NavigateDown",NativeInputBindings.Action.Navigate,1);
            AddProfileBinding("NavigateUp",NativeInputBindings.Action.Navigate,0);
            AddProfileBinding("Pause",NativeInputBindings.Action.Pause);
            settingsRows.Add(new SettingRow{label="Done",control=new NativeSettingsControl{node="Done"},confirm=()=>{bool ok=NativeInputBindings.ConfirmEdit();SettingsApply(ok);if(ok){SettingsGo(SettingsPage.Profiles);settingsConfirmAwaitingRelease=true;}}});
        }
        void AddProfileBinding(string node,NativeInputBindings.Action action,int part=-1)
        {
            int count=NativeInputBindings.DraftPartCount(action);if(count==1 && part>0)return;
            int bindingPart=count==1?-1:part;
            settingsRows.Add(new SettingRow{label=BindingLabel(action,Mathf.Max(0,part),count),control=new NativeSettingsControl{node=node,value=NativeInputBindings.DraftBindingDisplay(action,bindingPart)},confirm=()=>{bool ok=NativeInputBindings.BeginRebind(action,bindingPart);rebindStart=Time.unscaledTime;if(!ok)SettingsApply(false);}});
        }
        string BindingLabel(NativeInputBindings.Action action,int part,int count)
        {
            string phrase="REBIND_"+action.ToString().ToUpperInvariant();if(action==NativeInputBindings.Action.Brake)phrase="REBIND_BREAKDRIFT";if(action==NativeInputBindings.Action.Use)phrase="REBIND_USEITEM";
            if(count>1)phrase+="_"+(action==NativeInputBindings.Action.Horizontal?(part==0?"LEFT":"RIGHT"):action==NativeInputBindings.Action.Vertical?(part==0?"DOWN":"UP"):new[]{"UP","DOWN","LEFT","RIGHT"}[part]);return NativeLocalization.Get(phrase,action+" "+(count>1?(part+1).ToString():""));
        }
    }
}
