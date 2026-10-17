using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class InsanitySourceBaseline { public float effectDuration,hudDuration; public int imageCount;public InsanityExpectedLeaf[] leaves; }
        [Serializable] sealed class InsanityExpectedLeaf {public string name,spriteName;public bool enabled,hasSprite;}
        [Serializable] sealed class InsanityUiLeaf { public string name,sprite;public bool active,enabled,cull;public float effectiveAlpha;public Rect screenRect;public int changedPixels;public string leafOffScreenshot;public Color color;public Vector2 position,size;public Vector3 scale; }
        [Serializable] sealed class InsanityNaturalReport
        { public bool passed,visualParityAccepted;public int humanCapturedFrame;public float humanScaledTime,humanAnimatorNormalizedTime;public string[] checks;public InsanityUiLeaf[] humanUiLeaves;public string humanScreenshot;public string compatibilityScope="CPU6s recovery and early-destroy lease cleanup intentionally differ from original partial exception/no rollback; P2-P4 ordinary joining remains open."; }
        static Rect InsanityImageScreenRect(Image image)
        {
            var canvas=image.GetComponentInParent<Canvas>(true);var corners=new Vector3[4];image.rectTransform.GetWorldCorners(corners);
            var projected=corners.Select(p=>RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,p)).ToArray();
            return Rect.MinMaxRect(projected.Min(p=>p.x),projected.Min(p=>p.y),projected.Max(p=>p.x),projected.Max(p=>p.y));
        }
        internal IEnumerator CheckInsanityNaturalConsumersLive()
        {
            QualitySettings.vSyncCount=0;
            var baseline=JsonUtility.FromJson<InsanitySourceBaseline>(NativeContent.Load<TextAsset>("NativeVerification/InsanityNaturalSource").text);
            int humanCapturedFrame=0;float humanScaledTime=0,humanAnimatorNormalizedTime=0;
            InsanityUiLeaf[] humanLeaves=null;string humanScreenshot="insanity-natural-human.png";
            var checks=new List<string>();bool passed=true;
            Action<bool,string> require=(ok,name)=>{passed&=ok;checks.Add((ok?"PASS ":"FAIL ")+name);};
            SetVisualCapture("race",true);
            require(PrepareHudFixtureQuickRace(vehicleRoster.VehicleIdAt(selectedVehicleIndex)),"actual one-human QuickRace fixture");
            BindNaturalInsanityConsumer();
            require(itemWorld.catalog.Find("Insanity").projectile.effectDuration==baseline.effectDuration,"exported original effect-duration constant");
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble-2);
            foreach(var motor in racers)
            { motor.enabled=false;var ai=motor.GetComponent<NativeKartOriginalAI>();if(ai)ai.enabled=false; }
            for(int i=0;i<racers.Length;i++)racers[i].transform.position=new Vector3(i*40,20,0);
            Time.timeScale=1;
            var router=itemWorld.GetComponent<NativeInsanityHudRouter>();
            require(router.Initialized&&router.FindHumanSlot(racers[0])==0&&router.FindHumanSlot(racers[1])==-1,"production router has human0 and CPU absence");
            foreach(int receiverIndex in new[]{0,1})
            {
                var receiver=racers[receiverIndex];var owner=racers[receiverIndex==0?1:0];
                var controller=receiver.GetComponent<CharacterController>();
                var center=receiver.transform.position+(controller?controller.center:Vector3.up);
                var actor=itemWorld.Spawn("Insanity",owner,center,Quaternion.identity,Vector3.zero,false);
                require(actor!=null,"exported Insanity projectile spawned");
                float deadline=Time.realtimeSinceStartup+2;
                while(actor&&!actor.InsanityStarted&&Time.realtimeSinceStartup<deadline)yield return null;
                require(actor&&actor.InsanityStarted&&actor.InsanityEffected&&receiver.ControlReverseCount==1,
                    "natural "+receiverIndex+" collision starts actor-owned reversal");
                require(actor&&(actor.InsanityCollisionConsumer=="OnTriggerEnter"||actor.InsanityCollisionConsumer=="world-sweep"),"actual production collision consumer, no direct ResolveHit test injection");
                require(actor&&actor.InsanityHudCount==(receiverIndex==0?1:0),"human actual HUD / CPU explicit no-HUD compatibility");
                var marker=actor?actor.GetComponentInChildren<NativeInsanityHudSource>(true):null;
                GameObject ui=marker?marker.LastSpawn:null;
                if(receiverIndex==0)
                {
                    require(ui&&ui.transform.parent==router.hudCanvases[0],"natural human UI under exact authored PlayerHUD Canvas");
                    yield return MinimapSettle();
                    var lifetime=ui?ui.GetComponent<NativeItemPrefabLifetime>():null;
                    require(lifetime&&lifetime.duration==baseline.hudDuration&&lifetime.StartScheduled,"actual independent source UI lifetime");
                    humanLeaves=ui.GetComponentsInChildren<Image>(true).Select(x=>new InsanityUiLeaf{name=x.name,sprite=x.sprite?x.sprite.name:"",active=x.gameObject.activeInHierarchy,enabled=x.enabled,cull=x.canvasRenderer.cull,effectiveAlpha=x.color.a*x.GetComponentsInParent<CanvasGroup>(true).Aggregate(1f,(a,g)=>a*g.alpha),screenRect=InsanityImageScreenRect(x),color=x.color,position=x.rectTransform.anchoredPosition,size=x.rectTransform.rect.size,scale=x.rectTransform.localScale}).ToArray();
                    require(humanLeaves.Length==baseline.imageCount,"all original seven Image leaves reach actual human consumer");
                    Time.timeScale=0;yield return MinimapSettle();
                    humanCapturedFrame=Time.frameCount;humanScaledTime=Time.time;var animator=ui.GetComponent<Animator>();humanAnimatorNormalizedTime=animator?animator.GetCurrentAnimatorStateInfo(0).normalizedTime:0;
                    Directory.CreateDirectory(reportDirectory);var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(reportDirectory,humanScreenshot),texture.EncodeToPNG());var onPixels=texture.GetPixels32();Destroy(texture);
                    var graphics=ui.GetComponentsInChildren<Image>(true);
                    for(int leaf=0;leaf<graphics.Length;leaf++)
                    {
                        var graphic=graphics[leaf];var expected=baseline.leaves[leaf];var sample=humanLeaves[leaf];
                        require(sample.name.Replace("(Clone)","").Trim()==expected.name&&sample.enabled==expected.enabled&&sample.sprite==expected.spriteName,"actual source Image identity/enabled/sprite "+expected.name);
                        if(!expected.enabled)continue;
                        Rect clipped=Rect.MinMaxRect(Mathf.Max(0,sample.screenRect.xMin),Mathf.Max(0,sample.screenRect.yMin),Mathf.Min(Screen.width,sample.screenRect.xMax),Mathf.Min(Screen.height,sample.screenRect.yMax));
                        require(sample.active&&!sample.cull&&sample.effectiveAlpha>0&&clipped.width>0&&clipped.height>0,"actual nonzero alpha and viewport region "+expected.name);
                        graphic.enabled=false;yield return MinimapSettle();var off=ScreenCapture.CaptureScreenshotAsTexture();var offPixels=off.GetPixels32();
                        sample.leafOffScreenshot="insanity-natural-leaf-off-"+leaf+".png";File.WriteAllBytes(Path.Combine(reportDirectory,sample.leafOffScreenshot),off.EncodeToPNG());Destroy(off);
                        int changed=0;for(int y=Mathf.CeilToInt(clipped.yMin);y<Mathf.FloorToInt(clipped.yMax);y++)for(int x=Mathf.CeilToInt(clipped.xMin);x<Mathf.FloorToInt(clipped.xMax);x++)
                        {int index=y*Screen.width+x;if(!onPixels[index].Equals(offPixels[index]))changed++;}
                        sample.changedPixels=changed;require(changed>0,"isolated actual Image pixel contribution in own ROI "+expected.name);
                        graphic.enabled=true;yield return MinimapSettle();
                    }
                    Time.timeScale=1;
                }
                Time.timeScale=0;yield return new WaitForSecondsRealtime(.25f);
                require(actor&&receiver.ControlReverseCount==1,"paused actor timer keeps reverse");
                Time.timeScale=1;yield return new WaitForSeconds(baseline.effectDuration+.05f);
                require(!actor&&receiver.ControlReverseCount==0,"natural actor deadline6s decrements once and destroys");
                if(receiverIndex==0)
                {require(ui,"source HUD survives actor6s");yield return new WaitForSeconds(.6f);require(!ui,"HUD own6.5s expiry");}
            }
            var target=racers[0];var enemy=racers[1];var cc=target.GetComponent<CharacterController>();
            Vector3 at=target.transform.position+(cc?cc.center:Vector3.up);
            var first=itemWorld.Spawn("Insanity",enemy,at,Quaternion.identity,Vector3.zero,false);
            var second=itemWorld.Spawn("Insanity",enemy,at,Quaternion.identity,Vector3.zero,false);
            yield return null;yield return null;
            require(first&&second&&first.InsanityEffected&&second.InsanityEffected&&target.ControlReverseCount==2,"two natural projectiles stack independent integer counts");
            if(first)first.DestroyNow();yield return null;
            require(target.ControlReverseCount==1,"explicit Native early-destroy compatibility releases exactly one lease");
            target.ClearControlReverse();if(second)second.DestroyNow();yield return null;
            require(target.ControlReverseCount==0,"reset generation prevents stale callback decrement after target reset");
            itemWorld.ResetWorld();yield return null;
            require(itemWorld.ActiveActorCount==0&&target.ControlReverseCount==0,"production world cleanup clears actors and owned reversal");
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"insanity-natural-verification.json"),JsonUtility.ToJson(new InsanityNaturalReport{passed=passed,visualParityAccepted=false,checks=checks.ToArray(),humanUiLeaves=humanLeaves,humanScreenshot=humanScreenshot,humanCapturedFrame=humanCapturedFrame,humanScaledTime=humanScaledTime,humanAnimatorNormalizedTime=humanAnimatorNormalizedTime},true));
            Debug.Log("NATIVE_INSANITY_NATURAL_VERIFICATION "+passed);Application.Quit(passed?0:2);
        }
    }
}
