using System; using System.Collections; using System.Collections.Generic; using System.IO; using System.Linq; using UnityEngine; using UnityEngine.UI;
namespace Fumo.NativePlayable { public sealed partial class NativeRace {
[Serializable] sealed class StructureDiagnosticReport { public bool passed,structurePassed,accepted,renderPending=true,hardwarePending=true,naturalDrivingPending=true; public uint actualInputUserId;public string scope,failure; public string[] checks; }
internal IEnumerator CheckInsanityStructureLive()=>GuardStructureDiagnostic(CheckInsanityStructureBody(),"insanity-structure-report.json");
IEnumerator GuardStructureDiagnostic(IEnumerator body,string report)
{
 var stack=new Stack<IEnumerator>();stack.Push(body);Exception failure=null;
 while(stack.Count>0 && failure==null) {object value=null;bool next=false;
 try{var it=stack.Peek();next=it.MoveNext();if(next)value=it.Current;else{stack.Pop();(it as IDisposable)?.Dispose();}}catch(Exception ex){failure=ex;}
 if(failure!=null)break;if(!next)continue;if(value is IEnumerator child && !(value is CustomYieldInstruction))stack.Push(child);else yield return value;
 }
 while(stack.Count>0)try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception ex){failure=failure==null?ex:new AggregateException(failure,ex);}
 if(failure!=null){Directory.CreateDirectory(reportDirectory);File.WriteAllText(Path.Combine(reportDirectory,report),JsonUtility.ToJson(new StructureDiagnosticReport{failure=failure.ToString(),scope="Structure diagnostic failed; actual coverage pending"},true));Application.Quit(2);}
}
        internal IEnumerator CheckInsanityStructureBody()
        {
            QualitySettings.vSyncCount=0;
            var baseline=JsonUtility.FromJson<InsanitySourceBaseline>(NativeContent.Load<TextAsset>("NativeVerification/InsanityNaturalSource").text);
            InsanityUiLeaf[] humanLeaves=null;
            var checks=new List<string>();bool passed=true;
            Action<bool,string> require=(ok,name)=>{passed&=ok;checks.Add((ok?"PASS ":"FAIL ")+name);};
            var device=UnityEngine.InputSystem.Keyboard.current;
            if(device==null)throw new InvalidOperationException("Diagnostic keyboard device missing");
            var owners=NativeLocalSelectionOwners.Instance;
            if(!owners)owners=NativeLocalSelectionOwners.Create();
            if(!owners.Begin(false,device))throw new InvalidOperationException("Actual PIM diagnostic owner join failed");
            owners.End();owners.MarkDiagnosticOwner(true);
            sourceInjectedDiagnosticOwner=true;
            NativeInputBindings.SetActualLocalOwners(owners.Players);
            SetVisualCapture("race",true);
            require(PrepareHudFixtureQuickRace(vehicleRoster.VehicleIdAt(selectedVehicleIndex)),"actual one-human QuickRace fixture");
            var actualOwner=SourceActualLocalOwner(0);
            require(actualOwner && actualOwner.user.valid,"actual local PlayerInput/InputUser exists for HUD slot0");
            if(!actualOwner || !actualOwner.user.valid)throw new InvalidOperationException("Actual diagnostic InputUser missing");
            ConfigureSourceHumans();
            require(SourceActualLocalOwner(0)==actualOwner,"injected actual InputUser retains slot0/racer0 production binding");
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
                    yield return null; yield return null;
                    var lifetime=ui?ui.GetComponent<NativeItemPrefabLifetime>():null;
                    require(lifetime&&lifetime.duration==baseline.hudDuration&&lifetime.StartScheduled,"actual independent source UI lifetime");
                    humanLeaves=ui.GetComponentsInChildren<Image>(true).Select(x=>new InsanityUiLeaf{name=x.name,sprite=x.sprite?x.sprite.name:"",active=x.gameObject.activeInHierarchy,enabled=x.enabled,cull=x.canvasRenderer.cull,effectiveAlpha=x.color.a*x.GetComponentsInParent<CanvasGroup>(true).Aggregate(1f,(a,g)=>a*g.alpha),screenRect=InsanityImageScreenRect(x),color=x.color,position=x.rectTransform.anchoredPosition,size=x.rectTransform.rect.size,scale=x.rectTransform.localScale}).ToArray();
                    require(humanLeaves.Length==baseline.imageCount,"all original seven Image leaves reach actual human consumer");
                    for(int leaf=0;leaf<humanLeaves.Length;leaf++)
                    {
                        var expected=baseline.leaves[leaf];var sample=humanLeaves[leaf];
                        require(sample.name.Replace("(Clone)","").Trim()==expected.name&&sample.enabled==expected.enabled&&sample.sprite==expected.spriteName,"original seven Image identity/enabled/sprite "+expected.name);
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
            File.WriteAllText(Path.Combine(reportDirectory,"insanity-structure-report.json"),JsonUtility.ToJson(new StructureDiagnosticReport{actualInputUserId=actualOwner?actualOwner.user.id:0,passed=passed,structurePassed=passed,accepted=false,renderPending=true,hardwarePending=true,naturalDrivingPending=true,scope="Injected true PIM keyboard InputUser identity and placement; actual production projectile collision/effect/canvas/TTL. Original pixels and ordinary hardware remain pending",checks=checks.ToArray()},true));
            Debug.Log("NATIVE_INSANITY_NATURAL_VERIFICATION "+passed);Application.Quit(passed?0:2);
        }
} }
