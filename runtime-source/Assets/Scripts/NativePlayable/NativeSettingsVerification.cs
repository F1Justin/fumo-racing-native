using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSettingsVerification
    {
        [Serializable] public sealed class Result {public bool passed;public string[] checks;public string error;}
        public static Result Run(string isolatedDirectory)
        {
            var checks=new List<string>();var result=new Result();
            try
            {
                Require(NativeSaveStore.Initialize(new[]{"--save-directory",isolatedDirectory}),"isolated-store",checks);
                var original=NativeUserSettings.CopyData();var clean=NativeUserSettings.CopyData();clean.settings=new NativeSaveData.Setting[0];clean.inputProfiles=new NativeSaveData.InputProfile[0];Require(NativeSaveStore.ReplaceData(clean),"clean-settings",checks);
                Require(NativeUserSettings.GetInt("video_aa")==3 && NativeUserSettings.GetInt("video_aniso")==1 && NativeUserSettings.GetInt("video_ao")==1 && NativeUserSettings.GetInt("fov")==60,"original-defaults",checks);
                Require(NativeUserSettings.TrySetInt("fov",999) && NativeUserSettings.GetInt("fov")==150,"clamped-fov",checks);
                Require(NativeUserSettings.TrySetFloat("mainVolume",0.5f) && Math.Abs(NativeUserSettings.VolumeDb(0.5f)+6.0206f)<0.001f,"volume-db",checks);
                Require(!NativeUserSettings.TrySetFloat("mainVolume",float.NaN) && NativeUserSettings.GetFloat("mainVolume")==0.5f,"invalid-keeps-previous",checks);
                NativeInputBindings.Initialize();string id=NativeInputBindings.CreateProfile("测试 Keyboard","<Keyboard>");Require(!string.IsNullOrEmpty(id),"profile-create",checks);
                string action=NativeInputBindings.ActionId(NativeInputBindings.Action.Accelerate);Require(action=="242f65e1-7562-4495-9e2d-62c15044c567","original-action-guid",checks);
                Require(NativeInputBindings.BeginEdit(id) && NativeInputBindings.SetDraftOverride(action,"<Keyboard>/a"),"profile-draft",checks);NativeInputBindings.CancelEdit();Require(NativeInputBindings.Profiles[0].overrides.Length==0,"cancel-keeps-bindings",checks);
                Require(NativeInputBindings.BeginEdit(id) && NativeInputBindings.SetDraftOverride(action,"<Keyboard>/a") && NativeInputBindings.ConfirmEdit(),"confirm-profile",checks);
                Require(NativeInputBindings.SelectProfile(id),"profile-selection",checks);
                Require(NativeUserSettings.TrySetInt("lang_game",1),"spanish-select",checks);NativeLocalization.ResetForVerification();Require(NativeLocalization.PhraseCount==241,"original-phrase-count",checks);
                Require(NativeLocalization.Get("__missing_phrase__","fallback")=="fallback","missing-phrase-fallback",checks);
                string csv=Path.Combine(isolatedDirectory,"customlang-test.csv");File.WriteAllText(csv,"VIDEO_TEXTURE_FULL,自定义,逗号\n");Require(NativeLocalization.ReloadCustom(csv) && NativeUserSettings.TrySetInt("lang_game",2) && NativeLocalization.Get("VIDEO_TEXTURE_FULL")=="自定义,逗号","custom-first-comma",checks);
                NativeSaveStore.ResetForVerification();Require(NativeSaveStore.Initialize(new[]{"--save-directory",isolatedDirectory}),"restart-load",checks);NativeInputBindings.Initialize();Require(NativeUserSettings.GetInt("fov")==150 && NativeInputBindings.Profiles.Length==1 && NativeInputBindings.Profiles[0].overrides[0].path=="<Keyboard>/a","restart-settings-bindings",checks);
                Require(NativeInputBindings.SelectedProfile()==id,"restart-selected-profile",checks);
                Require(NativeInputBindings.DeleteProfile(id) && NativeInputBindings.Profiles.Length==0,"profile-delete",checks);
                Require(NativeSaveStore.ReplaceData(original),"restore-isolated-save",checks);NativeInputBindings.Initialize();result.passed=true;
            }
            catch(Exception e){result.passed=false;result.error=e.Message;}
            result.checks=checks.ToArray();Directory.CreateDirectory(isolatedDirectory);File.WriteAllText(Path.Combine(isolatedDirectory,"settings-verification.json"),JsonUtility.ToJson(result,true));return result;
        }
        public static bool PrepareCustomFontProbe(NativeRace race,string directory)
        {
            Directory.CreateDirectory(directory);string csv=Path.Combine(directory,"customlang-font-probe.csv");File.WriteAllText(csv,"MENU_OPTIONS,中文設定\nOPTIONS_VIDEO,中文設定\nPROMPT_GOBACK,返回\n");
            if(race==null || race.hud.menuFont==null)return false;File.WriteAllText(Path.Combine(directory,"custom-font-probe.json"),JsonUtility.ToJson(race.hud.menuFont.DiagnoseCharacters("中文設定返回"),true));foreach(char character in "中文設定返回")if(!race.hud.menuFont.HasCharacter(character))return false;
            if(!NativeLocalization.ReloadCustom(csv) || !NativeUserSettings.TrySetInt("lang_game",2))return false;race.OpenSettings();return true;
        }
        [Serializable] sealed class SceneResult {public bool passed;public string[] checks;public string error;public NativeSceneSettings.EffectReport[] effects;}
        public static bool RunScene(NativeRace race,string directory)
        {
            var checks=new List<string>();var effects=new List<NativeSceneSettings.EffectReport>();var result=new SceneResult();var original=NativeUserSettings.CopyData();
            try
            {
                Require(race!=null && race.raceCamera!=null,"scene-camera",checks);
                Require(NativeUserSettings.TrySetInt("qualityLevel",0) && NativeUserSettings.TrySetInt("vsync",0),"save-quality-vsync0",checks);NativeUserSettings.ApplyScene(race);Require(QualitySettings.vSyncCount==0,"quality-preset-preserves-vsync0",checks);
                Require(NativeUserSettings.TrySetInt("qualityLevel",QualitySettings.names.Length-1) && NativeUserSettings.TrySetInt("vsync",1),"save-quality-vsync1",checks);NativeUserSettings.ApplyScene(race);Require(NativeUserSettings.GetInt("vsync")==1 && QualitySettings.vSyncCount==1,"quality-preset-preserves-saved-and-synchronous-vsync1",checks);
                Require(NativeUserSettings.TrySetInt("video_aa",0),"save-aa8",checks);effects.Add(NativeUserSettings.ApplyScene(race));Require(QualitySettings.antiAliasing==8,"engine-aa8",checks);
                Require(NativeUserSettings.TrySetInt("video_aa",3),"save-aaoff",checks);NativeUserSettings.ApplyScene(race);Require(QualitySettings.antiAliasing==0,"engine-aaoff",checks);
                Require(NativeUserSettings.TrySetInt("video_draw_distance",0) && NativeUserSettings.TrySetInt("video_model",1),"save-lod",checks);NativeUserSettings.ApplyScene(race);Require(QualitySettings.lodBias==2 && QualitySettings.maximumLODLevel==1,"engine-lod",checks);
                Require(NativeUserSettings.TrySetInt("video_dynamiclights",4) && NativeUserSettings.TrySetInt("video_shadowquality",0) && NativeUserSettings.TrySetInt("video_shadowdistance",0) && NativeUserSettings.TrySetInt("video_shadowdy",0),"save-shadow-high",checks);effects.Add(NativeUserSettings.ApplyScene(race));Require(QualitySettings.pixelLightCount==4 && QualitySettings.shadows==ShadowQuality.All && QualitySettings.shadowResolution==ShadowResolution.VeryHigh && QualitySettings.shadowDistance==100 && QualitySettings.shadowmaskMode==ShadowmaskMode.DistanceShadowmask,"engine-shadow-high",checks);
                Require(NativeUserSettings.TrySetInt("video_shadowquality",4) && NativeUserSettings.TrySetInt("video_shadowdistance",4),"save-shadow-off",checks);NativeUserSettings.ApplyScene(race);Require(QualitySettings.shadows==ShadowQuality.Disable && QualitySettings.shadowDistance==20,"engine-shadow-off",checks);
                Require(NativeUserSettings.TrySetInt("video_bloom",1) && NativeUserSettings.TrySetInt("video_ao",1) && NativeUserSettings.TrySetInt("video_cc",1) && NativeUserSettings.TrySetInt("video_ppquality",1) && NativeUserSettings.TrySetInt("video_grass",1) && NativeUserSettings.TrySetInt("video_particlelighting",1) && NativeUserSettings.TrySetInt("video_particlequality",2),"save-effects-off",checks);var off=NativeUserSettings.ApplyScene(race);effects.Add(off);Require(!off.bloom && !off.ao && !off.cc && !off.highPostQuality && off.litParticles==0,"scene-effects-off",checks);Require(off.realBloomEnabled==0 && off.realAOEnabled==0 && off.realCCEnabled==0,"real-post-effects-off",checks);
                Require(NativeUserSettings.TrySetInt("video_bloom",0) && NativeUserSettings.TrySetInt("video_ao",0) && NativeUserSettings.TrySetInt("video_cc",0) && NativeUserSettings.TrySetInt("video_ppquality",0) && NativeUserSettings.TrySetInt("video_grass",0) && NativeUserSettings.TrySetInt("video_particlelighting",0) && NativeUserSettings.TrySetInt("video_particlequality",0),"save-effects-on",checks);var on=NativeUserSettings.ApplyScene(race);effects.Add(on);Require(on.bloom && on.ao && on.cc && on.highPostQuality && on.litParticles==on.particles,"scene-effects-on",checks);Require(on.realBloomEnabled==on.realBloomCount && on.realAOEnabled==on.realAOCount && on.realCCEnabled==on.realCCCount,"real-post-effects-on",checks);
                Shader post=NativeContent.Load<Shader>("NativeSettings/post"),particles=NativeContent.Load<Shader>("NativeSettings/particles");Require(post!=null && post.isSupported && particles!=null && particles.isSupported,"native-effect-shaders-supported",checks);
                File.WriteAllText(Path.Combine(directory,"settings-font-probe.json"),JsonUtility.ToJson(race.hud.menuFont.DiagnoseCharacters("中文設定返回"),true));
                foreach(char character in "中文設定")Require(race.hud.menuFont!=null && race.hud.menuFont.HasCharacter(character),"custom-font-glyph-"+((int)character).ToString("X4"),checks);
                Require(AudioListener.volume==0,"construction-silence",checks);result.passed=true;
            }
            catch(Exception e){result.error=e.Message;}
            finally {NativeSaveStore.ReplaceData(original);NativeUserSettings.ApplyScene(race);result.checks=checks.ToArray();result.effects=effects.ToArray();Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings-scene-verification.json"),JsonUtility.ToJson(result,true));}
            return result.passed;
        }
        static void Require(bool condition,string name,List<string> checks){if(!condition)throw new InvalidOperationException(name+": "+NativeSaveStore.LastError+NativeUserSettings.LastError+NativeInputBindings.LastError+NativeLocalization.LastError);checks.Add(name);}
    }
}
