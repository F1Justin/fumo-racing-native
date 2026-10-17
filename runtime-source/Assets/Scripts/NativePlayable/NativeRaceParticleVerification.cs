using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ParticleMaterialRow
        {
            public string item,renderer,shader;
            public int renderQueue;
            public float srcBlend,dstBlend,zWrite;
        }
        [Serializable] sealed class ParticleProbeReport
        {
            public bool passed,accepted=false;
            public string scope="Injected item Give/Use through production FX and settings material refresh; actual rendered screenshots require visual review",error;
            public List<ParticleMaterialRow> materials=new List<ParticleMaterialRow>();
            public List<string> screenshots=new List<string>();
        }
        internal IEnumerator CheckParticleMaterialsLive()
        {
            Directory.CreateDirectory(reportDirectory);
            verificationRun=true;selfTest=false;
            var report=new ParticleProbeReport{passed=true};
            if(!PrepareHudFixtureQuickRace("kart"))
            {
                report.passed=false;report.error="QuickRace fixture initialization failed";
            }
            else
            {
                SetVisualCapture("race",true);Time.timeScale=1;
                var motor=racers[0];var inventory=motor.GetComponent<NativeKartItemInventory>();
                foreach(var racer in racers){racer.enabled=false;racer.driving=false;var ai=racer.GetComponent<NativeKartOriginalAI>();if(ai)ai.active=false;}
                foreach(string id in new[]{"Power","FullPower","WindBoost"})
                {
                    bool used=false;
                    try
                    {
                        inventory.ResetItems();
                        int beforeUse=inventory.UseCount;
                        used=inventory.TryGive(id,false) && (inventory.UseCount>beforeUse || inventory.UseItem());
                        used &= inventory.Effects.IsActive(id);
                    }
                    catch(Exception error){report.error=error.ToString();report.passed=false;}
                    if(!used){report.passed=false;if(report.error==null)report.error="Item Give/Use failed: "+id;break;}
                    yield return new WaitForSecondsRealtime(.6f);
                    NativeSceneSettings.Apply(this);
                    var renderers=motor.GetComponentsInChildren<ParticleSystemRenderer>(true);
                    foreach(var renderer in renderers)foreach(var material in renderer.sharedMaterials)
                    {
                        if(!material)continue;
                        report.materials.Add(new ParticleMaterialRow{item=id,renderer=renderer.name,shader=material.shader?material.shader.name:"missing",renderQueue=material.renderQueue,
                            srcBlend=material.HasProperty("_SrcBlend")?material.GetFloat("_SrcBlend"):-1,
                            dstBlend=material.HasProperty("_DstBlend")?material.GetFloat("_DstBlend"):-1,
                            zWrite=material.HasProperty("_ZWrite")?material.GetFloat("_ZWrite"):-1});
                        if(!material.shader || material.shader.name=="Fumo/NativeSettingsParticles")report.passed=false;
                    }
                    string image="item-material-refresh-"+id+".png";
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory,image));report.screenshots.Add(image);
                    yield return null;
                }
                inventory.ResetItems();
            }
            report.passed &= report.materials.Count>0 && report.screenshots.Count==3;
            File.WriteAllText(Path.Combine(reportDirectory,"item-material-refresh-report.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.passed?0:2);
        }
    }
}
