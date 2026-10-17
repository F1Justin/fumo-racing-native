using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeUserSettings
    {
        public sealed class Definition
        {
            public readonly string key; public readonly float minimum, maximum, defaultValue; public readonly bool floating;
            public Definition(string key, float min, float max, float value, bool floating = false) { this.key = key; minimum = min; maximum = max; defaultValue = value; this.floating = floating; }
        }
        static readonly Definition[] definitions = {
            new Definition("video_texture",0,3,0),new Definition("video_aniso",0,2,1),new Definition("video_aa",0,3,3),
            new Definition("video_model",0,1,0),new Definition("video_grass",0,1,0),new Definition("video_draw_distance",0,4,2),
            new Definition("video_dynamiclights",0,4,2),new Definition("video_shadowquality",0,4,2),new Definition("video_shadowdistance",0,4,2),
            new Definition("video_shadowdy",0,1,0),new Definition("video_particlequality",0,2,0),new Definition("video_particlelighting",0,1,0),
            new Definition("video_ppquality",0,1,0),new Definition("video_bloom",0,1,0),new Definition("video_ao",0,1,1),new Definition("video_cc",0,1,0),
            new Definition("lang_game",0,3,0),new Definition("speedometerEnabled",0,1,1),new Definition("alternativeJumping",0,1,0),
            new Definition("fov",40,150,60),new Definition("targetFramerate",30,300,60),new Definition("vsync",0,1,1),
            new Definition("mainVolume",0,1,1,true),new Definition("volumeGroup0",0,1,1,true),new Definition("volumeGroup1",0,1,1,true),
            new Definition("volumeGroup2",0,1,1,true),new Definition("volumeGroup3",0,1,1,true),new Definition("volumeGroup4",0,1,1,true),
            new Definition("volumeGroup5",0,1,1,true),new Definition("volumeGroup6",0,1,1,true)
        };
        public static IReadOnlyList<Definition> Definitions => Array.AsReadOnly(definitions);
        static int requestedWidth, requestedHeight, requestedRefreshRate;
        public static int RequestedDisplayRefreshRate => requestedRefreshRate;
        static bool requestedFullscreen, hasDisplayRequest;
        public static event System.Action Changed;
        public static string LastError { get; private set; } = "";
        public static Definition Find(string key) { foreach(var d in definitions) if(d.key==key) return d; if(key=="qualityLevel") return new Definition(key,0,Mathf.Max(0,QualitySettings.names.Length-1),QualitySettings.GetQualityLevel()); return null; }
        public static float GetFloat(string key)
        {
            Definition d=Find(key); if(d==null) return 0;
            string raw=ReadValue(key); float value;
            if(!float.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out value) || float.IsNaN(value) || float.IsInfinity(value)) value=key=="lang_game"?NativeLocalization.SystemDefaultLanguage:d.defaultValue;
            return Mathf.Clamp(value,d.minimum,d.maximum);
        }
        public static int GetInt(string key) => Mathf.RoundToInt(GetFloat(key));
        public static string ReadValue(string key) { foreach(var s in NativeSaveStore.Data.settings) if(s.key==key) return s.value; return ""; }
        public static bool TrySetInt(string key,int value) { var d=Find(key); if(d==null || d.floating) return Fail("Unknown integer setting"); return WriteValue(key,Mathf.Clamp(value,(int)d.minimum,(int)d.maximum).ToString(CultureInfo.InvariantCulture),"int"); }
        public static bool TrySetFloat(string key,float value) { var d=Find(key); if(d==null || !d.floating || float.IsNaN(value) || float.IsInfinity(value)) return Fail("Invalid float setting"); return WriteValue(key,Mathf.Clamp(value,d.minimum,d.maximum).ToString("R",CultureInfo.InvariantCulture),"float"); }
        public static NativeSaveData CopyData() => NativeSaveJson.FromJson<NativeSaveData>(JsonUtility.ToJson(NativeSaveStore.Data));
        public static void Put(NativeSaveData candidate,string key,string value,string type)
        {
            var entries=new List<NativeSaveData.Setting>(candidate.settings); entries.RemoveAll(x=>x.key==key); entries.Add(new NativeSaveData.Setting {key=key,value=value,type=type}); candidate.settings=entries.ToArray();
        }
        public static bool WriteValue(string key,string value,string type)
        {
            var copy=CopyData(); Put(copy,key,value,type);
            if(!NativeSaveStore.ReplaceData(copy)) { NativeTelemetry.Event("settings.save", "failed", "save-failed", key); return Fail(NativeSaveStore.LastError); }
            // Profile names and arbitrary string values are deliberately not copied to telemetry.
            NativeTelemetry.Event("settings.save", "completed", detail: key);
            LastError=""; Changed?.Invoke(); return true;
        }
        public static bool TrySetFullscreen(bool fullscreen)
        {
            var candidate=CopyData();
            int width,height;
            if(!int.TryParse(ReadValue("native.screen.width"),out width) || width<320 || width>16384)width=Screen.width;
            if(!int.TryParse(ReadValue("native.screen.height"),out height) || height<240 || height>16384)height=Screen.height;
            Put(candidate,"native.screen.width",width.ToString(CultureInfo.InvariantCulture),"int");
            Put(candidate,"native.screen.height",height.ToString(CultureInfo.InvariantCulture),"int");
            Put(candidate,"native.screen.fullscreen",fullscreen.ToString(),"bool");
            if(!NativeSaveStore.ReplaceData(candidate))return Fail(NativeSaveStore.LastError);
            int refreshRate;int.TryParse(ReadValue("native.screen.refreshRate"),out refreshRate);
            // The source fullscreen event changes only the boolean; retain the stored resolution for reopening.
            requestedWidth=width;requestedHeight=height;requestedFullscreen=fullscreen;requestedRefreshRate=Mathf.Clamp(refreshRate,0,1000);hasDisplayRequest=true;
            Screen.fullScreen=fullscreen;
            NativeTelemetry.Event("settings.display", "requested", detail: fullscreen ? "fullscreen" : "windowed");
            LastError="";Changed?.Invoke();return true;
        }
        public static bool TrySetDisplay(int width,int height,bool fullscreen,int refreshRate=0)
        {
            if(width<320 || height<240 || width>16384 || height>16384 || refreshRate<0 || refreshRate>1000)return Fail("Invalid display size or refresh rate");
            var candidate=CopyData();Put(candidate,"native.screen.width",width.ToString(CultureInfo.InvariantCulture),"int");Put(candidate,"native.screen.height",height.ToString(CultureInfo.InvariantCulture),"int");Put(candidate,"native.screen.fullscreen",fullscreen.ToString(),"bool");
            Put(candidate,"native.screen.refreshRate",refreshRate.ToString(CultureInfo.InvariantCulture),"int");
            if(!NativeSaveStore.ReplaceData(candidate))return Fail(NativeSaveStore.LastError);
            ApplyDisplay(width,height,fullscreen,refreshRate,true);LastError="";Changed?.Invoke();return true;
        }
        public static void Apply(Camera camera=null,AudioSource music=null)
        {
            int width,height;bool fullscreen;
            if(int.TryParse(ReadValue("native.screen.width"),out width) && int.TryParse(ReadValue("native.screen.height"),out height) && bool.TryParse(ReadValue("native.screen.fullscreen"),out fullscreen) && width>=320 && height>=240 && width<=16384 && height<=16384)
            {int refreshRate;int.TryParse(ReadValue("native.screen.refreshRate"),out refreshRate);ApplyDisplay(width,height,fullscreen,Mathf.Clamp(refreshRate,0,1000));}
            if(camera!=null) camera.fieldOfView=GetInt("fov");
            int quality=GetInt("qualityLevel");
            if(!string.IsNullOrEmpty(ReadValue("qualityLevel")) && QualitySettings.GetQualityLevel()!=quality) QualitySettings.SetQualityLevel(quality,true);
            int vsync=NativeRenderingQuality.EffectiveVSyncCount(GetInt("vsync"));if(QualitySettings.vSyncCount!=vsync)QualitySettings.vSyncCount=vsync;
            Application.targetFrameRate=GetInt("targetFramerate");
            QualitySettings.globalTextureMipmapLimit=GetInt("video_texture");
            QualitySettings.anisotropicFiltering=GetInt("video_aniso")==0?AnisotropicFiltering.ForceEnable:GetInt("video_aniso")==1?AnisotropicFiltering.Enable:AnisotropicFiltering.Disable;
            QualitySettings.antiAliasing=new[]{8,4,2,0}[GetInt("video_aa")];
            QualitySettings.maximumLODLevel=GetInt("video_model");
            QualitySettings.lodBias=new[]{2f,1.5f,1f,0.5f,0.3f}[GetInt("video_draw_distance")];
            QualitySettings.pixelLightCount=GetInt("video_dynamiclights");
            int shadows=GetInt("video_shadowquality"); QualitySettings.shadows=shadows==4?ShadowQuality.Disable:ShadowQuality.All;
            if(shadows<4) QualitySettings.shadowResolution=(ShadowResolution)(3-shadows);
            QualitySettings.shadowDistance=new[]{100f,85f,75f,45f,20f}[GetInt("video_shadowdistance")];
            QualitySettings.shadowmaskMode=(ShadowmaskMode)(1-GetInt("video_shadowdy"));
            NativeAudioRuntime.ApplyVolumes(); if(music!=null && NativeAudioRuntime.Muted) {music.Stop();music.enabled=false;}
        }
        static void ApplyDisplay(int width,int height,bool fullscreen,int refreshRate=0,bool explicitRequest=false)
        {
            // Unity applies display changes asynchronously. Reapplying a saved target during
            // unrelated settings changes queues redundant native window transitions.
            if(!explicitRequest && hasDisplayRequest && requestedWidth==width && requestedHeight==height && requestedFullscreen==fullscreen && requestedRefreshRate==refreshRate)return;
            requestedWidth=width;requestedHeight=height;requestedFullscreen=fullscreen;requestedRefreshRate=refreshRate;hasDisplayRequest=true;
            if(!explicitRequest && Screen.width==width && Screen.height==height && Screen.fullScreen==fullscreen && (refreshRate==0 || Screen.currentResolution.refreshRate==refreshRate))return;
            Screen.SetResolution(width,height,fullscreen,refreshRate);
            NativeTelemetry.Event("settings.display", "requested", detail: width + "x" + height + ";fullscreen=" + fullscreen + ";refresh=" + refreshRate);
        }
        public static NativeSceneSettings.EffectReport ApplyScene(NativeRace race)
        { Apply(race==null?null:race.raceCamera); return NativeSceneSettings.Apply(race); }
        public static float VolumeDb(float volume) => volume<=0 ? -80 : Mathf.Clamp(20*Mathf.Log10(volume),-80,0);
        static bool Fail(string error) { LastError=error; return false; }
    }
}
