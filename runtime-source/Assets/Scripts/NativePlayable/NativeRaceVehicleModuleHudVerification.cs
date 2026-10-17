using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class VehicleBarOverlayLeaf
        {
            public string path,text;public bool active;public int renderMode,sortingOrder;
            public float alpha;public Color color;public Vector2[] corners;public Rect screenRect;
        }
        [Serializable] sealed class VehicleBarOverlayProbe
        {
            public Vector2 screenPosition;public Color renderedPixel;public string[] foregroundLeaves;
            public float survivingBackgroundAlpha;
        }
        [Serializable] sealed class VehicleBarCapture
        {
            public string id,screenshot,phase,scope,mode,sessionMode,resultPath;
            public string sessionId;public bool resultPlayerCardVisible,resultsScoreboardVisible,resultsDelayElapsed;
            public NativeSessionSeat[] roster;public NativeSessionResult[] pendingResults;
            public VehicleBarOverlayLeaf[] resultLeaves;public VehicleBarOverlayProbe[] moduleOverlayProbes;
            public int capturedFrame;public float scaledTime;public double elapsed;
            public bool coveredByResults,startCaptionVisible;
            public int laps,currentLap;
            public int[] completedLaps;
            public bool[] finished;
            public double[] lapRecords;
            public NativeVehicleModuleHud.State actual;
        }
        [Serializable] sealed class VehicleBarReport
        {
            public bool passed,visualParityAccepted;
            public int sourceFamilyCount;
            public string[] checks;
            public VehicleBarCapture[] states;
            public string evidenceScope="Injected module boundary states and actual StepVehicleModule production events; five-lap racing screenshots settle two rendered frames. Final single-human Results sample covers lifecycle fields. Original paired rendering is a separate acceptance obligation.";
        }
        static bool BarNear(float a,float b)=>Mathf.Abs(a-b)<.001f;
        static bool BarColor(Color a,Color b)=>BarNear(a.r,b.r)&&BarNear(a.g,b.g)&&BarNear(a.b,b.b)&&BarNear(a.a,b.a);
        static void InjectVehicleBar(NativeKartMotor motor,float charge,int stage,bool ready)
        {
            SetMotorHudField(motor,"<ModuleCharge>k__BackingField",charge);
            SetMotorHudField(motor,"moduleSteps",stage);SetMotorHudField(motor,"moduleReady",ready);
        }
        static bool VehicleBarInside(Vector2 point,Vector2[] polygon)
        {
            if(polygon==null||polygon.Length!=4)return false;float sign=0;
            for(int i=0;i<4;i++){var a=polygon[i];var b=polygon[(i+1)%4];float cross=(b.x-a.x)*(point.y-a.y)-(b.y-a.y)*(point.x-a.x);if(Mathf.Abs(cross)<.001f)continue;if(sign==0)sign=Mathf.Sign(cross);else if(sign*Mathf.Sign(cross)<0)return false;}
            return true;
        }
        VehicleBarOverlayLeaf ReadVehicleBarResultLeaf(string path)
        {
            var root=GameObject.Find("Original authored menus");var node=root?root.transform.Find("GlobalHUD authored Canvas/Results/"+path) as RectTransform:null;
            if(!node)return new VehicleBarOverlayLeaf{path=path,corners=Array.Empty<Vector2>()};
            var image=node.GetComponent<Image>();var text=node.GetComponent<TMP_Text>();var canvas=node.GetComponentInParent<Canvas>(true);
            float alpha=image?image.color.a:text?text.color.a:1;
            for(Transform at=node;at;at=at.parent){var groups=at.GetComponents<CanvasGroup>();foreach(var group in groups)if(group.enabled)alpha*=group.alpha;if(groups.Any(group=>group.enabled&&group.ignoreParentGroups))break;}
            var world=new Vector3[4];node.GetWorldCorners(world);var corners=world.Select(c=>RectTransformUtility.WorldToScreenPoint(canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null,c)).ToArray();
            return new VehicleBarOverlayLeaf{path=path,text=text?text.text:"",active=node.gameObject.activeInHierarchy&&(!image||image.enabled)&&(!text||text.enabled)&&canvas&&canvas.enabled,
                alpha=alpha,color=image?image.color:text?text.color:Color.white,renderMode=canvas?(int)canvas.renderMode:-1,sortingOrder=canvas?canvas.sortingOrder:0,corners=corners,
                screenRect=Rect.MinMaxRect(corners.Min(c=>c.x),corners.Min(c=>c.y),corners.Max(c=>c.x),corners.Max(c=>c.y))};
        }
        VehicleBarCapture CaptureVehicleBar(string id)
        {
            string screenshot="vehicle-module-hud-"+id+".png";
            var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(reportDirectory,screenshot),texture.EncodeToPNG());
            var race=CaptureDynamicHud("module-context-"+id);var actual=hud.ReadOriginalVehicleModuleBar();
            var leaves=new[]{"Fade","playersresult/ResultPlayerUI","playersresult/ResultPlayerUI/banner","playersresult/ResultPlayerUI/character","playersresult/ResultPlayerUI/rank","playersresult/ResultPlayerUI/Time"}.Select(ReadVehicleBarResultLeaf).ToArray();
            var probes=new List<VehicleBarOverlayProbe>();
            if(actual!=null&&actual.nodes.Length>0)
            {
                var rect=actual.nodes.Single(n=>n.path.EndsWith("/bar",StringComparison.Ordinal)).screenRect;
                foreach(float t in new[]{.1f,.3f,.5f,.7f,.9f})
                {
                    var point=new Vector2(Mathf.Lerp(rect.xMin,rect.xMax,t),rect.center.y);
                    var layers=leaves.Take(3).Where(n=>n.active&&n.alpha>0&&n.renderMode==(int)RenderMode.ScreenSpaceOverlay&&n.sortingOrder>actual.sortingOrder&&VehicleBarInside(point,n.corners)).ToArray();
                    float survival=1;foreach(var layer in layers)survival*=1-layer.alpha;
                    probes.Add(new VehicleBarOverlayProbe{screenPosition=point,renderedPixel=texture.GetPixel(Mathf.Clamp(Mathf.RoundToInt(point.x),0,texture.width-1),Mathf.Clamp(Mathf.RoundToInt(point.y),0,texture.height-1)),foregroundLeaves=layers.Select(n=>n.path).ToArray(),survivingBackgroundAlpha=survival});
                }
            }
            Destroy(texture);var session=NativeGameSession.Current;
            return new VehicleBarCapture{id=id,screenshot=screenshot,phase=phase.ToString(),capturedFrame=Time.frameCount,scaledTime=Time.time,elapsed=PreciseElapsed,
                coveredByResults=probes.Any(p=>p.foregroundLeaves.Length>0),startCaptionVisible=race.startCaptionVisible,laps=RaceLaps,currentLap=hud.CurrentLap,
                completedLaps=progress.Select(p=>p.CompletedLaps).ToArray(),finished=progress.Select(p=>p.Finished).ToArray(),
                lapRecords=lapSeconds[0].ToArray(),actual=actual,mode=raceConfig.mode.ToString(),sessionMode=session?.config.mode.ToString(),sessionId=session?.sessionId,
                resultPath=raceConfig.mode==NativeSessionMode.NativeRace?"legacy ResultsMenu":"FullRaceResults",roster=raceConfig.roster.ToArray(),pendingResults=session?.pending.ToArray(),
                resultPlayerCardVisible=leaves[1].active&&leaves[1].alpha>0,resultsScoreboardVisible=hud.ResultsScoreboardVisible,resultsDelayElapsed=hud.ResultsVisible,
                resultLeaves=leaves,moduleOverlayProbes=probes.ToArray(),
                scope=phase==Phase.Racing?"Visible human module HUD; injected charge/environment":"Real QuickRace result-player flow; known source Fade/card/banner overlay polygons and alpha plus actual framebuffer probes; world image parity remains separately reviewed"};
        }
        internal IEnumerator CheckVehicleModuleHudLive()
        {
            QualitySettings.vSyncCount=0;Screen.SetResolution(1280,800,false);Directory.CreateDirectory(reportDirectory);
            yield return null;
            var source=NativeVehicleModuleHudSource.Read();var resource=NativeContent.Load<NativeAuthoredUiData>("NativeVehicleModuleHud/OriginalModuleBars");
            var checks=new List<string>();var captures=new List<VehicleBarCapture>();bool passed=true;
            Action<bool,string> require=(ok,name)=>{passed&=ok;checks.Add((ok?"PASS ":"FAIL ")+name);Debug.Log("NATIVE_VEHICLE_BAR_CHECK "+ok+" "+name);};
            require(source.familyCount==3&&source.families.Length==3&&source.nodes.Length==8&&resource&&resource.nodes.Length==8,"three source families and two independent four-node trees");
            SetVisualCapture("race",true);
            require(PrepareHudFixtureQuickRace("broom")&&raceConfig.mode==NativeSessionMode.QuickRace&&raceConfig.roster.Count==racers.Length&&raceConfig.roster.Count(seat=>seat.isHuman)==1,"real QuickRace session/config/roster selects FullRaceResults");
            hud.RaceLapCount=5;
            for(int i=0;i<racers.Length;i++)
            {
                progress[i]=new NativeCircuitProgress(gates.Select(g=>g.position).ToArray(),5);lapSeconds[i].Clear();lapTimestamps[i]=0;finishSeconds[i]=0;finishTimes[i]=0;
                racers[i].gameObject.SetActive(true);racers[i].enabled=false;var ai=racers[i].GetComponent<NativeKartOriginalAI>();if(ai)ai.enabled=false;
            }
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble-2);
            var motor=racers[0];var original=motor.stats;
            require(original._moduleKind==NativeVehicleModuleKind.BroomAirBoost&&raceConfig.roster[0].vehicleId=="broom"
                &&(int)typeof(NativeVehicleRoster).GetField("activeVehicle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(vehicleRoster)==selectedVehicleIndex,
                "actual local model selection and source Broom stats agree with QuickRace roster");
            Action<NativeVehicleModuleHudFamily> family=f=>
            {
                var settings=JsonUtility.FromJson<NativeKartSettings>(JsonUtility.ToJson(original));
                settings._moduleKind=(NativeVehicleModuleKind)Enum.Parse(typeof(NativeVehicleModuleKind),f.id);settings._moduleLimit=f.limit;
                motor.stats=settings;
                motor.GetComponent<NativeVehicleModuleEffects>().binding=vehicleRoster.vehicles.First(entry=>entry.settings._moduleKind==settings._moduleKind).moduleEffects;
                InjectVehicleBar(motor,0,0,false);
            };
            Action<VehicleBarCapture,NativeVehicleModuleHudFamily,float,Color> validate=(capture,f,charge,color)=>
            {
                var actual=capture.actual;
                require(actual!=null&&actual.family==f.id&&actual.nodeCount==4&&actual.canvasEnabled&&actual.cameraBound,"production family/camera "+capture.id);
                require(actual.renderMode==source.canvas.renderMode&&actual.sortingOrder==source.canvas.sortingOrder&&BarNear(actual.planeDistance,source.canvas.planeDistance)&&actual.referenceResolution==source.canvas.referenceResolution,"source Canvas geometry/order "+capture.id);
                require(BarNear(actual.groupAlpha,charge>0?1:0),"source positive-charge visibility "+capture.id);
                foreach(var definition in source.nodes.Where(n=>n.scene==f.scene))
                {
                    var node=actual.nodes.Single(n=>n.path==definition.path);bool fill=definition.path.EndsWith("/bar",StringComparison.Ordinal);
                    require(node.active&&node.enabled&&node.anchorMin==definition.anchorMin&&node.anchorMax==definition.anchorMax&&node.pivot==definition.pivot
                        &&Vector2.Distance(node.position,definition.position)<.001f&&Vector2.Distance(node.size,definition.size)<.001f&&Vector3.Distance(node.scale,definition.scale)<.001f,"actual leaf geometry "+capture.id+" "+node.path);
                    require(node.type==definition.imageType&&node.fillMethod==definition.imageFillMethod&&node.fillOrigin==definition.imageFillOrigin
                        &&node.fillClockwise==definition.imageFillClockwise&&node.useSpriteMesh==definition.imageUseSpriteMesh&&BarNear(node.pixelsPerUnitMultiplier,definition.imagePixelsPerUnitMultiplier)
                        &&node.maskable==definition.maskable&&node.preserveAspect==definition.preserveAspect&&node.defaultMaterial,"actual source Image flags "+capture.id+" "+node.path);
                    require(BarColor(node.color,fill?color:definition.color)&&BarNear(node.fillAmount,fill?Mathf.Clamp01(charge/f.limit):definition.imageFillAmount),"actual leaf color/fill "+capture.id+" "+node.path);
                    if(charge>0)require(node.alpha>0&&node.screenRect.width>0&&node.screenRect.height>0&&node.screenRect.xMin>=0&&node.screenRect.xMax<=Screen.width&&node.screenRect.yMin>=0&&node.screenRect.yMax<=Screen.height,"visible unclipped leaf "+capture.id+" "+node.path);
                    if(!string.IsNullOrEmpty(definition.spriteGuid))
                    {
                        var sprite=source.sprites.Single(s=>s.guid==definition.spriteGuid);
                        var exported=resource.nodes.Single(n=>n.scene==f.scene&&n.path==node.path).sprite;
                        require(motor.GetComponent<NativeVehicleModuleHud>().OriginalSpriteBound(node.path,exported)&&node.spriteRect==sprite.rect.Value
                            &&Vector2.Distance(node.spritePivot,Vector2.Scale(sprite.pivot,sprite.rect.Value.size))<.001f&&node.spriteTextureRect==sprite.textureRect.Value
                            &&Vector2.Distance(node.spriteTextureRectOffset,sprite.textureRectOffset)<.001f&&BarNear(node.spritePixelsPerUnit,sprite.pixelsPerUnit)
                            &&node.spriteVertices.Length==sprite.vertices.Length&&node.spriteVertices.Zip(sprite.vertices,(a,b)=>Vector2.Distance(a,b)<.001f).All(v=>v)
                            &&node.spriteTriangles.Select(v=>(int)v).SequenceEqual(sprite.triangles),"exact source Sprite identity/bounds/mesh "+capture.id+" "+node.path);
                    }
                }
                if(capture.phase=="Racing")require(!capture.coveredByResults&&!capture.startCaptionVisible&&capture.laps==5&&capture.lapRecords.Length==progress[0].CompletedLaps,"coherent visible Racing state "+capture.id);
            };
            foreach(var f in source.families)
            {
                family(f);
                float[] charges=f.id=="BroomAirBoost"?new[]{0f,.5f,3.999f,4f}:f.id=="GenjiGrassBoost"?new[]{0f,.5f,1f,2.999f,3f}:new[]{0f,1.999f,2f,2.001f};
                for(int i=0;i<charges.Length;i++)
                {
                    float charge=charges[i];int stage=f.id=="GenjiGrassBoost"&&i>=2?1:0;
                    InjectVehicleBar(motor,charge,stage,false);yield return MinimapSettle();
                    var capture=CaptureVehicleBar(f.id+"-"+i);captures.Add(capture);
                    Color color=f.id=="BroomAirBoost"?(charge>=f.limit?f.fullColor:f.baseColor):f.id=="GenjiGrassBoost"?(charge>=f.limit?f.fullColor:stage>0?f.baseColor:f.noneColor):charge>f.danger?f.dangerColor:f.baseColor;
                    validate(capture,f,charge,color);
                }
            }
            var broom=source.families.Single(f=>f.id=="BroomAirBoost");family(broom);
            motor.stats._moduleUpRate=1;motor.stats._moduleGrassRate=1;
            SetMotorHudField(motor,"<Grounded>k__BackingField",true);SetMotorHudField(motor,"<OnGrass>k__BackingField",false);
            InjectVehicleBar(motor,3.9f,0,false);
            typeof(NativeKartMotor).GetMethod("StepVehicleModule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,new object[]{.2f});
            yield return MinimapSettle();var ready=CaptureVehicleBar("production-air-ready");captures.Add(ready);validate(ready,broom,4,broom.fullColor);
            require(motor.ModuleReady&&motor.ModuleCharge==4,"actual motor event produces Air full charge and Ready");
            SetMotorHudField(motor,"<Grounded>k__BackingField",false);int trigger=motor.ModuleTriggerCount;
            typeof(NativeKartMotor).GetMethod("StepVehicleModule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,new object[]{.1f});
            yield return MinimapSettle();var consumed=CaptureVehicleBar("production-air-consumed");captures.Add(consumed);validate(consumed,broom,0,broom.baseColor);
            require(!motor.ModuleReady&&motor.ModuleCharge==0&&motor.ModuleTriggerCount==trigger+1,"actual airborne event consumes Ready and hides charge bar");
            var grass=source.families.Single(f=>f.id=="GenjiGrassBoost");family(grass);
            motor.stats._moduleGrassRate=1;motor.stats._moduleSteps=3;motor.stats._moduleSpeedBoosts=new[]{1f,2f,3f};
            SetMotorHudField(motor,"<Grounded>k__BackingField",true);SetMotorHudField(motor,"<OnGrass>k__BackingField",true);SetMotorHudField(motor,"<DriftState>k__BackingField",default(NativeKartDriftState));
            InjectVehicleBar(motor,.9f,0,false);
            typeof(NativeKartMotor).GetMethod("StepVehicleModule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,new object[]{.2f});
            yield return MinimapSettle();var grassStage=CaptureVehicleBar("production-grass-stage");captures.Add(grassStage);validate(grassStage,grass,1.1f,grass.baseColor);
            require(motor.ModuleStage==1&&BarNear(motor.ModuleCharge,1.1f),"actual grass accumulation advances source Stage color eligibility");
            SetMotorHudField(motor,"<OnGrass>k__BackingField",false);trigger=motor.ModuleTriggerCount;
            typeof(NativeKartMotor).GetMethod("StepVehicleModule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,new object[]{.1f});
            yield return MinimapSettle();var grassSpent=CaptureVehicleBar("production-grass-consumed");captures.Add(grassSpent);validate(grassSpent,grass,0,grass.noneColor);
            require(motor.ModuleStage==0&&motor.ModuleCharge==0&&motor.ModuleTriggerCount==trigger+1,"actual grass exit consumes Stage and hides bar");
            var drift=source.families.Single(f=>f.id=="ShoppingCartDriftCap");family(drift);motor.stats._moduleUpRate=1;
            SetMotorHudField(motor,"<DriftState>k__BackingField",new NativeKartDriftState{direction=1});InjectVehicleBar(motor,1.9f,0,false);
            typeof(NativeKartMotor).GetMethod("StepVehicleModule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,new object[]{.2f});
            yield return MinimapSettle();var driftDanger=CaptureVehicleBar("production-drift-danger");captures.Add(driftDanger);validate(driftDanger,drift,2.1f,drift.dangerColor);
            require(BarNear(motor.ModuleCharge,2.1f),"actual drift accumulation crosses strict danger threshold");
            SetMotorHudField(motor,"<DriftState>k__BackingField",default(NativeKartDriftState));family(broom);
            var cpu=racers[1].gameObject.AddComponent<NativeVehicleModuleHud>();cpu.Present(racers[1],raceCamera,false,true);
            require(cpu.ReadState().nodeCount==0&&!cpu.ReadState().canvasEnabled,"CPU ownership creates no module HUD Canvas or leaves");Destroy(cpu);
            require(UnityEngine.Object.FindObjectsOfType<Transform>(true).Count(t=>t.name=="Original authored menus")==1,"independent bars preserve single generic authored UI root");
            var generic=NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");require(generic&&generic.nodes.Count(n=>n.image)==823,"generic Image denominator remains 823");
            InjectVehicleBar(motor,.75f,0,false);SetMotorHudField(motor,"<Grounded>k__BackingField",true);
            foreach(double seconds in new[]{20.553,18.600,19.351,18.900})DeliverHudFixtureLap(motor,seconds);
            yield return MinimapSettle();var fifth=CaptureVehicleBar("lap-five-visible");captures.Add(fifth);validate(fifth,broom,.75f,broom.baseColor);
            require(phase==Phase.Racing&&!progress[0].Finished&&progress[0].CompletedLaps==4&&lapSeconds[0].Count==4,"four completed laps retain real visible fifth-lap HUD");
            DeliverHudFixtureLap(motor,20.100);yield return MinimapSettle();
            var pending=CaptureVehicleBar("quick-results-before-source-delay");captures.Add(pending);
            require(phase==Phase.Finished&&!hud.ResultsVisible&&!pending.resultPlayerCardVisible&&!pending.coveredByResults,"QuickRace result player waits original three scaled seconds; module has no premature Results cover");
            require(NativeGameSession.Current.pending.Count==1&&NativeGameSession.Current.pending[0].seatId=="seat-0"&&NativeGameSession.Current.pending[0].place==finishPlaces[0]&&Math.Abs(NativeGameSession.Current.pending[0].time-finishSeconds[0])<.0001,"real local finisher callback records matching session result");
            float delayStarted=Time.time;Time.timeScale=1;
            yield return new WaitForSeconds(3.15f);yield return MinimapSettle();
            var finished=CaptureVehicleBar("single-human-results-lifecycle");captures.Add(finished);validate(finished,broom,.75f,broom.baseColor);
            require(phase==Phase.Finished&&finished.mode=="QuickRace"&&finished.sessionMode=="QuickRace"&&finished.resultPath=="FullRaceResults"&&finished.resultsDelayElapsed&&Time.time-delayStarted>=3
                &&finished.resultPlayerCardVisible&&!finished.resultsScoreboardVisible,"finished fixture renders real QuickRace original-delay player card, excluding legacy ResultRow");
            var fade=finished.resultLeaves[0];var player=finished.resultLeaves[1];
            require(fade.active&&fade.alpha>0&&fade.renderMode==(int)RenderMode.ScreenSpaceOverlay&&fade.sortingOrder==200&&finished.actual.renderMode==(int)RenderMode.ScreenSpaceCamera&&finished.actual.sortingOrder==113
                &&finished.moduleOverlayProbes.All(p=>p.foregroundLeaves.Contains("Fade")&&p.survivingBackgroundAlpha<1),"actual source Overlay200 Fade polygon and alpha cover all measured Camera113 module bar probes");
            var resultSource=NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi").nodes.Single(n=>n.scene=="GlobalHUD"&&n.path=="Canvas/Results/Fade");
            require(BarColor(fade.color,resultSource.color)&&BarNear(fade.alpha,resultSource.color.a)&&BarNear(finished.resultLeaves[2].alpha,.5f),"actual source Fade color/alpha and one-half banner CanvasGroup composition");
            require(finished.resultLeaves.Skip(3).All(n=>n.active&&n.alpha>0&&!string.IsNullOrEmpty(n.text))&&player.screenRect.width>0&&player.screenRect.height>0,"actual result-player character/rank/time leaves have visible screen geometry");
            var resultTime=TimeSpan.FromSeconds(finishSeconds[0]);
            string expectedResultTime="<mspace=18>"+resultTime.Hours.ToString("00")+":"+resultTime.Minutes.ToString("00")+":"+resultTime.Seconds.ToString("00")+"."+resultTime.Milliseconds.ToString("000")+"</mspace>";
            require(finished.resultLeaves[3].text==racerNames[0].Substring(0,racerNames[0].Length-" / YOU".Length)&&finished.resultLeaves[4].text==finishPlaces[0].ToString()
                &&finished.resultLeaves[5].text==expectedResultTime,"actual player card maps local roster name/place/finish time");
            require(phase==Phase.Finished&&finished.coveredByResults&&progress[0].Finished&&lapSeconds[0].Count==5&&finished.actual.groupAlpha==1,"single-human finish preserves charged module HUD beneath measured Results overlays");
            Time.timeScale=0;
            motor.stats=JsonUtility.FromJson<NativeKartSettings>(JsonUtility.ToJson(original));motor.stats._moduleKind=NativeVehicleModuleKind.None;
            yield return MinimapSettle();var none=hud.ReadOriginalVehicleModuleBar();require(none!=null&&none.family=="None"&&none.nodeCount==0&&!none.canvasEnabled,"no-module selection removes independent bar tree");motor.stats=original;
            File.WriteAllText(Path.Combine(reportDirectory,"vehicle-module-hud-verification.json"),JsonUtility.ToJson(new VehicleBarReport{passed=passed,visualParityAccepted=false,sourceFamilyCount=source.familyCount,checks=checks.ToArray(),states=captures.ToArray()},true));
            Debug.Log("NATIVE_VEHICLE_MODULE_HUD_RESULT "+passed);
            Application.Quit(passed && errorCount == 0 ? 0 : 2);
        }
    }
}
