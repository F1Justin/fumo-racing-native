using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    public sealed class NativeSceneSettings : MonoBehaviour
    {
        [Serializable] public sealed class EffectReport {public string track;public int texture,aniso,aa,model,dynamicLights,shadowQuality,shadowMask;public float lodBias,shadowDistance,fov;public int grassRenderers,particles,litParticles,activatedTargets,realPostProfiles,realPostEffects,realBloomCount,realBloomEnabled,realAOCount,realAOEnabled,realCCCount,realCCEnabled;public bool realPostProcessing;public string[] missingTargets;public bool bloom,ao,cc,highPostQuality,constructionSilence;}
        [Serializable] sealed class Rule {public string track,key,targetPath;public int value;public bool active;}
        [Serializable] sealed class Rules {public Rule[] rules;}
        static Rules rules;
        public EffectReport Report {get;private set;}
        public static EffectReport Apply(NativeRace race)
        {
            var scene=race==null?SceneManager.GetActiveScene():race.gameObject.scene;
            var camera=race==null?Camera.main:race.raceCamera;
            NativeSceneSettings consumer=camera==null?null:camera.GetComponent<NativeSceneSettings>();
            if(camera!=null && consumer==null)consumer=camera.gameObject.AddComponent<NativeSceneSettings>();
            var report=new EffectReport{track=race==null?"":race.trackId,texture=QualitySettings.globalTextureMipmapLimit,aniso=(int)QualitySettings.anisotropicFiltering,aa=QualitySettings.antiAliasing,model=QualitySettings.maximumLODLevel,lodBias=QualitySettings.lodBias,dynamicLights=QualitySettings.pixelLightCount,shadowQuality=(int)QualitySettings.shadows,shadowMask=(int)QualitySettings.shadowmaskMode,shadowDistance=QualitySettings.shadowDistance,fov=camera==null?0:camera.fieldOfView,bloom=NativeUserSettings.GetInt("video_bloom")==0,ao=NativeUserSettings.GetInt("video_ao")==0,cc=NativeUserSettings.GetInt("video_cc")==0,highPostQuality=NativeUserSettings.GetInt("video_ppquality")==0,constructionSilence=AudioListener.volume==0};
            var missing=new List<string>();
            if(rules==null){var resource=NativeContent.Load<TextAsset>("NativeSettings/scene-options");if(resource!=null)rules=JsonUtility.FromJson<Rules>(resource.text);}
            if(rules!=null)foreach(var rule in rules.rules)if(rule.track==report.track && NativeUserSettings.GetInt(rule.key)==rule.value)
            {
                Transform target=Find(scene,rule.targetPath);if(target==null){missing.Add(rule.targetPath);continue;}target.gameObject.SetActive(rule.active);report.activatedTargets++;
            }
            foreach(var root in scene.GetRootGameObjects())foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool grass=false;foreach(var material in renderer.sharedMaterials)if(material!=null && material.shader!=null && material.shader.name=="Fumo/NativeGrass")grass=true;
                if(grass){renderer.enabled=NativeUserSettings.GetInt("video_grass")==0;report.grassRenderers++;}
            }
            ConfigureRealPostProcessing(scene,report);
            NativeParticleQualityRegistry.ApplyScene(scene,report);
            foreach(var root in scene.GetRootGameObjects())
            {
                if(NativeAudioRuntime.Muted)
                    foreach(var audio in root.GetComponentsInChildren<AudioSource>(true)){audio.Stop();audio.enabled=false;}
                else NativeAudioEmitter.ReapplyHierarchy(root, true);
            }
            report.missingTargets=missing.ToArray(); if(consumer!=null){consumer.Report=report;camera.depthTextureMode|=DepthTextureMode.Depth;consumer.ConfigureFallback();}
            Debug.Log("NATIVE_SETTINGS_EFFECTS "+JsonUtility.ToJson(report));return report;
        }
        static object Member(object value,string name)
        {
            if(value==null)return null;var type=value.GetType();var field=type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);if(field!=null)return field.GetValue(value);var property=type.GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);return property==null?null:property.GetValue(value,null);
        }
        static void Parameter(object effect,string name,object value)
        {
            object parameter=Member(effect,name);if(parameter==null)return;var type=parameter.GetType();var field=type.GetField("value");var property=type.GetProperty("value");Type target=field!=null?field.FieldType:property==null?null:property.PropertyType;if(target==null)return;
            object converted=target.IsEnum?Enum.ToObject(target,value):Convert.ChangeType(value,target);if(field!=null)field.SetValue(parameter,converted);else if(property.CanWrite)property.SetValue(parameter,converted,null);
            var state=type.GetField("overrideState");if(state!=null)state.SetValue(parameter,true);else {var setting=type.GetProperty("overrideState");if(setting!=null && setting.CanWrite)setting.SetValue(parameter,true,null);}
        }
        static void ConfigureRealPostProcessing(Scene scene,EffectReport report)
        {
            foreach(var root in scene.GetRootGameObjects())foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null || component.GetType().FullName!="UnityEngine.Rendering.PostProcessing.PostProcessVolume")continue;
                var profile=Member(component,"profile");var settings=Member(profile,"settings") as IEnumerable;if(settings==null)continue;report.realPostProfiles++;
                foreach(var effect in settings)
                {
                    if(effect==null)continue;string name=effect.GetType().Name;
                    if(name=="Bloom"){Parameter(effect,"enabled",report.bloom);Parameter(effect,"fastMode",!report.highPostQuality);report.realPostEffects++;report.realBloomCount++;if((bool)(Member(Member(effect,"enabled"),"value")??false))report.realBloomEnabled++;}
                    if(name=="AmbientOcclusion"){Parameter(effect,"enabled",report.ao);Parameter(effect,"quality",report.highPostQuality?2:1);report.realPostEffects++;report.realAOCount++;if((bool)(Member(Member(effect,"enabled"),"value")??false))report.realAOEnabled++;}
                    if(name=="ColorGrading"){Parameter(effect,"enabled",report.cc);report.realPostEffects++;report.realCCCount++;if((bool)(Member(Member(effect,"enabled"),"value")??false))report.realCCEnabled++;}
                }
            }
            report.realPostProcessing=report.realPostProfiles>0;
        }
        static Transform Find(Scene scene,string path)
        {
            int slash=path.IndexOf('/');string rootName=slash<0?path:path.Substring(0,slash);string tail=slash<0?"":path.Substring(slash+1);
            foreach(var root in scene.GetRootGameObjects())if(root.name==rootName)return tail.Length==0?root.transform:root.transform.Find(tail);return null;
        }
        void ConfigureFallback()
        {
            bool legacyCopy = Array.IndexOf(Environment.GetCommandLineArgs(), "--performance-legacy-post-copy") >= 0;
            var fallback = GetComponent<NativeFallbackPostEffect>();
            bool required = Report != null && (!Report.realPostProcessing || legacyCopy);
            if (required && !fallback) fallback = gameObject.AddComponent<NativeFallbackPostEffect>();
            if (fallback) { fallback.Configure(Report, legacyCopy); fallback.enabled = required; }
        }
    }
}
