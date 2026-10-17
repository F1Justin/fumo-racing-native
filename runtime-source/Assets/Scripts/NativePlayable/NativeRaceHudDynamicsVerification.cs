using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class HudFinisherReceipt
        {
            public string expectedSeat;public int expectedPlace,racerCount;public double expectedTime;
            public string[] pendingSeats;public int[] pendingPlaces,sourceOrder;public double[] pendingTimes;
        }
        [Serializable] sealed class HudDynamicState
        {
            public string id,screenshot,phase,scope,mode,sessionMode,resultPath;
            public VehicleBarOverlayLeaf[] resultLeaves;public bool resultPlayerCardVisible,resultsDelayElapsed;public int capturedFrame;public float time;public double raceElapsed,lapTimestamp,currentLapElapsed;
            public bool coveredByResults,startCaptionVisible,requiredRaceLeavesVisible;public int raceLaps,currentLap;public int[] completedLaps;public bool[] finished;
            public double[] lapRecords;public HudDynamicLeaf[] requiredLeaves;
            public NativeAuthoredUi.DynamicHudSample[] nodes;
        }
        [Serializable] sealed class HudDynamicLeaf {public string path;public Rect screenRect;public bool visible;public int canvasRenderMode,canvasSortingOrder;public string[] foregroundResultsLeaves;public float resultBackgroundSurvival;}
        [Serializable] sealed class HudDynamicReport
        {
            public bool passed,visualParityAccepted;public string[] checks;public HudDynamicState[] states;
            public NativeResultsAvatarSet.RenderObservation[] resultAvatarRendering;
            public string renderBinding="Every capture follows two complete render frames; scaled state is frozen at capture. Twelve Racing, two pre-delay Finished lifecycle and one real delayed QuickRace card.";
            public string evidenceScope="Five-lap production HUD with injected clock/motor/lap samples and actual CrossCircuitGate delivery. Racing screenshots verify visible race HUD; final single-human Results screenshots verify lifecycle fields under the Results overlay. Original paired rendered acceptance remains separate.";
        }
        static NativeAuthoredUi.DynamicHudSample DynamicNode(HudDynamicState state,string path)=>state.nodes.Single(n=>n.path=="HUD/Canvas/"+path);
        void DeliverHudFixtureLap(NativeKartMotor motor,double seconds)
        {
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble-lapSeconds[0].Sum()-seconds);
            TickSourceTimers();
            for(int gate=0;gate<gates.Length;gate++)CrossCircuitGate(motor,gate,true);
            CrossCircuitGate(motor,0,true);
        }
        HudDynamicState CaptureDynamicHud(string id)
        {
            string screenshot="hud-dynamics-"+id+".png";
            var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(reportDirectory,screenshot),texture.EncodeToPNG());Destroy(texture);
            var authored=GameObject.Find("Original authored menus");
            var resultLeaves=new[]{"Fade","playersresult/ResultPlayerUI","playersresult/ResultPlayerUI/banner","playersresult/ResultPlayerUI/character","playersresult/ResultPlayerUI/rank","playersresult/ResultPlayerUI/Time"}.Select(ReadVehicleBarResultLeaf).ToArray();
            var labels=UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);

            bool start=labels.Any(t=>t.gameObject.activeInHierarchy&&t.text=="START"&&t.color.a>0);
            var nodes=hud.ReadOriginalDynamicHud();
            var leaves=new[]{"TimeText","Time","lapsbg/LapsTxt","lapsbg/Laps","SpeedGroup/SpeedText","SpeedGroup/Speed"}.Select(path=>
            {
                var sample=nodes.Single(n=>n.path=="HUD/Canvas/"+path);
                var target=authored?authored.transform.Find("HUD authored Canvas/"+path) as RectTransform:null;
                var canvas=target?target.GetComponentInParent<Canvas>(true):null;
                var corners=new Vector3[4];if(target)target.GetWorldCorners(corners);
                var screen=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null,c)).ToArray();
                var rect=Rect.MinMaxRect(screen.Min(c=>c.x),screen.Min(c=>c.y),screen.Max(c=>c.x),screen.Max(c=>c.y));
                var foreground=resultLeaves.Take(3).Where(n=>n.active&&n.alpha>0&&n.renderMode==(int)RenderMode.ScreenSpaceOverlay&&canvas&&n.sortingOrder>canvas.sortingOrder&&VehicleBarInside(rect.center,n.corners)).ToArray();
                float survival=1;foreach(var layer in foreground)survival*=1-layer.alpha;
                bool visible=target&&sample.active&&sample.alpha>0&&sample.color.a>0&&rect.width>0&&rect.height>0
                    &&rect.xMax>0&&rect.yMax>0&&rect.xMin<Screen.width&&rect.yMin<Screen.height;
                return new HudDynamicLeaf{path="HUD/Canvas/"+path,screenRect=rect,visible=visible,canvasRenderMode=canvas?(int)canvas.renderMode:-1,canvasSortingOrder=canvas?canvas.sortingOrder:0,
                    foregroundResultsLeaves=foreground.Select(n=>n.path).ToArray(),resultBackgroundSurvival=survival};
            }).ToArray();
            return new HudDynamicState{id=id,screenshot=screenshot,capturedFrame=Time.frameCount,time=Time.time,raceElapsed=PreciseElapsed,phase=phase.ToString(),
                coveredByResults=leaves.Any(n=>n.foregroundResultsLeaves.Length>0),mode=raceConfig?.mode.ToString(),sessionMode=NativeGameSession.Current?.config.mode.ToString(),
                resultPath=raceConfig!=null&&raceConfig.mode==NativeSessionMode.NativeRace?"legacy ResultsMenu":"FullRaceResults",resultLeaves=resultLeaves,
                resultPlayerCardVisible=resultLeaves[1].active&&resultLeaves[1].alpha>0,resultsDelayElapsed=hud.ResultsVisible,startCaptionVisible=start,raceLaps=RaceLaps,currentLap=hud.CurrentLap,
                completedLaps=progress.Select(p=>p.CompletedLaps).ToArray(),finished=progress.Select(p=>p.Finished).ToArray(),
                lapRecords=lapSeconds[0].ToArray(),lapTimestamp=lapTimestamps[0],currentLapElapsed=PreciseElapsed-lapTimestamps[0],
                requiredLeaves=leaves,requiredRaceLeavesVisible=leaves.All(l=>l.visible),
                scope=phase==Phase.Racing?"Visible racing HUD; injected motor/clock, production lap delivery":hud.ResultsVisible?"QuickRace source player card after three seconds; actual overlay geometry/alpha, underlying HUD lifecycle fields":"QuickRace before source three-second delay; final HUD lifecycle fields, actual player card absent",nodes=nodes};
        }
        static void SetMotorHudField(NativeKartMotor motor,string name,object value)
        {typeof(NativeKartMotor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(motor,value);}
        [Serializable] sealed class ExactEndpointRow
        {
            public string path; public float elapsed, nearSeed, skippedSetter, endpoint, endFrame;
            public int seedBits, skippedBits, endpointBits, endFrameBits;
        }
        [Serializable] sealed class ExactEndpointReport { public bool passed; public ExactEndpointRow[] samples; }
        IEnumerator CheckDeterministicOriginalLapEndpoint(Action<bool,string> require)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ui = (NativeAuthoredUi)typeof(NativeRaceHud).GetField("authoredUi", flags).GetValue(hud);
            var nodes = (Dictionary<string,RectTransform>)typeof(NativeAuthoredUi).GetField("transforms", flags).GetValue(ui);
            var field = typeof(TMP_Text).GetField("m_fontColor", flags);
            var clip = typeof(NativeAuthoredUi).GetMethod("DynamicClip", flags);
            var samples = new List<ExactEndpointRow>();
            foreach (string path in new[] { "HUD/Canvas/lapsbg/LapsTxt", "HUD/Canvas/lapsbg/Laps" })
            {
                var text = nodes[path].GetComponent<TMP_Text>(); var saved = text.color;
                try
                {
                    foreach (float elapsed in new[] { .5f, 5.75f })
                    {
                        var seeded = Color.white; seeded.b = BitConverter.ToSingle(BitConverter.GetBytes(1065353214),0);
                        field.SetValue(text,seeded);
                        var row = new ExactEndpointRow { path=path, elapsed=elapsed, nearSeed=text.color.b,
                            seedBits=BitConverter.ToInt32(BitConverter.GetBytes(text.color.b),0) };
                        text.color = Color.white;
                        row.skippedSetter=text.color.b; row.skippedBits=BitConverter.ToInt32(BitConverter.GetBytes(text.color.b),0);
                        require(row.seedBits==1065353214 && row.skippedBits==1065353214,"exact seeded TMP near-end value demonstrates ordinary setter skip");
                        clip.Invoke(ui,new object[] { "HUD/Canvas/lapsbg", "lapped", elapsed, null });
                        row.endpoint=text.color.b; row.endpointBits=BitConverter.ToInt32(BitConverter.GetBytes(text.color.b),0);
                        yield return new WaitForEndOfFrame();
                        row.endFrame=text.color.b; row.endFrameBits=BitConverter.ToInt32(BitConverter.GetBytes(text.color.b),0);
                        samples.Add(row);
                        require(row.endpoint==1f && row.endFrame==1f,"production nonloop endpoint and next EndOfFrame are exact source 1");
                    }
                }
                finally { field.SetValue(text,saved); text.havePropertiesChanged=true; text.SetVerticesDirty(); }
            }
            File.WriteAllText(Path.Combine(reportDirectory,"lap-endpoint-deterministic.json"),JsonUtility.ToJson(new ExactEndpointReport {
                passed=samples.Count==4 && samples.All(x=>x.seedBits==1065353214 && x.skippedBits==1065353214 && x.endpoint==1f && x.endFrame==1f),samples=samples.ToArray() },true));
        }
        internal IEnumerator CheckOriginalHudDynamicsLive()
        {
            QualitySettings.vSyncCount=0;
            Screen.SetResolution(1280,800,false);
            Directory.CreateDirectory(reportDirectory);
            yield return null;
            var checks=new List<string>();var states=new List<HudDynamicState>();bool passed=true;
            Action<bool,string> require=(ok,name)=>{passed&=ok;checks.Add((ok?"PASS ":"FAIL ")+name);Debug.Log("NATIVE_HUD_DYNAMIC_CHECK "+ok+" "+name);};
            require(PrepareHudFixtureQuickRace(vehicleRoster.VehicleIdAt(selectedVehicleIndex))&&raceConfig.mode==NativeSessionMode.QuickRace
                &&raceConfig.roster.Count==racers.Length&&raceConfig.roster.Count(seat=>seat.isHuman)==1,"coherent production QuickRace session/config/roster excludes legacy ResultsMenu");
            Begin();
            Time.timeScale=1;
            double startDeadline=Time.realtimeSinceStartupAsDouble+10;
            while(phase!=Phase.Racing&&Time.realtimeSinceStartupAsDouble<startDeadline)yield return null;
            bool sourceReady=phase==Phase.Racing&&sourceRaceState!=null&&sourceRaceState.InRace&&sourceRanking!=null&&sourceRanking.Started
                &&sourceLapTimers!=null&&sourceLapTimers.Length==racers.Length&&sourceHumanDriving!=null&&sourceResultAppend!=null;
            require(sourceReady,"real Begin/countdown initialized production source state before HUD injection");
            if(!sourceReady)
            {
                File.WriteAllText(Path.Combine(reportDirectory,"hud-dynamics-verification.json"),JsonUtility.ToJson(new HudDynamicReport{passed=false,visualParityAccepted=false,checks=checks.ToArray(),states=states.ToArray()},true));
                Application.Quit(2);yield break;
            }
            SetVisualCapture("race",true);
            hud.RaceLapCount=5;
            for(int i=0;i<racers.Length;i++)
            {
                progress[i]=new NativeCircuitProgress(gates.Select(g=>g.position).ToArray(),5);
                lapSeconds[i].Clear();lapTimestamps[i]=0;finishSeconds[i]=0;finishTimes[i]=0;
                racers[i].gameObject.SetActive(true);
            }
            raceClock.StartAt(Time.timeSinceLevelLoadAsDouble-2);
            NativeUserSettings.TrySetInt("speedometerEnabled",1);
            foreach(var racer in racers){racer.enabled=false;var ai=racer.GetComponent<NativeKartOriginalAI>();if(ai)ai.enabled=false;}
            var motor=racers[0];var originalStats=motor.stats;
            SetMotorHudField(motor,"velocity",Vector3.forward*motor.OriginalHudBaseTopSpeed*.7f);
            SetMotorHudField(motor,"<Grounded>k__BackingField",true);
            yield return MinimapSettle();
            var speed=CaptureDynamicHud("speed-max");states.Add(speed);
            require(Mathf.Abs(DynamicNode(speed,"SpeedGroup/speedbarFill").fill-.7f)<.0001f,"actual speed fill uses original base denominator");
            require(DynamicNode(speed,"SpeedGroup/max").active&&DynamicNode(speed,"SpeedGroup/max").alpha==1,"actual MAX node appears at source .688 threshold");
            var changed=JsonUtility.FromJson<NativeKartSettings>(JsonUtility.ToJson(originalStats));changed._topSpeed*=2;motor.stats=changed;
            yield return MinimapSettle();
            var basePreserved=CaptureDynamicHud("base-preserved");states.Add(basePreserved);
            require(Mathf.Abs(DynamicNode(basePreserved,"SpeedGroup/speedbarFill").fill-.7f)<.0001f,"modified live stats retain original base speed denominator");motor.stats=originalStats;
            SetMotorHudField(motor,"velocity",new Vector3(0,motor.OriginalHudBaseTopSpeed*.6f,motor.OriginalHudBaseTopSpeed*.3f));
            yield return MinimapSettle();var grounded=CaptureDynamicHud("grounded");states.Add(grounded);
            require(Mathf.Abs(DynamicNode(grounded,"SpeedGroup/speedbarFill").fill-.3f)<.0001f,"grounded source speed excludes vertical velocity");
            SetMotorHudField(motor,"<Grounded>k__BackingField",false);
            yield return MinimapSettle();var airborne=CaptureDynamicHud("airborne");states.Add(airborne);
            require(Mathf.Abs(DynamicNode(airborne,"SpeedGroup/speedbarFill").fill-Mathf.Sqrt(.45f))<.0001f,"airborne source speed includes vertical velocity");
            require(DynamicNode(airborne,"SpeedGroup/max").alpha==0,"MAX hidden below source threshold");
            int countBefore=lapSeconds[0].Count;
            DeliverHudFixtureLap(motor,20.553);
            require(lapSeconds[0].Count>countBefore,"production CrossCircuitGate emits completed-lap HUD event");
            Time.timeScale=1;
            yield return new WaitForSeconds(.23f);Time.timeScale=0;yield return MinimapSettle();
            var first=CaptureDynamicHud("first-lap-blink");states.Add(first);
            require(DynamicNode(first,"Time").text=="<mspace=24>00:20.553</mspace>"&&DynamicNode(first,"Time").alpha==.5f&&DynamicNode(first,"DifferenceTime").text=="","first-lap source window blink and no delta");
            require(DynamicNode(first,"lapsbg/Laps").active&&DynamicNode(first,"lapsbg/LapsTxt").active,"lap animation retains required leaves through every render pass");
            DeliverHudFixtureLap(motor,18.600);yield return MinimapSettle();
            var faster=CaptureDynamicHud("faster-lap");states.Add(faster);
            require(DynamicNode(faster,"DifferenceTime").text=="-<mspace=24>00:01.953</mspace>"&&DynamicNode(faster,"DifferenceTime").color==Color.green,"faster source lap delta sign and green");
            hud.BeginOriginalLapEndpointTrace();
            DeliverHudFixtureLap(motor,19.351);yield return MinimapSettle();
            var slower=CaptureDynamicHud("slower-lap");states.Add(slower);
            require(DynamicNode(slower,"DifferenceTime").text=="+<mspace=24>00:00.751</mspace>"&&DynamicNode(slower,"DifferenceTime").color==Color.red,"slower source lap delta sign and red");
            Time.timeScale=1;yield return new WaitForSeconds(.04f);Time.timeScale=0;yield return MinimapSettle();
            var lapped=CaptureDynamicHud("lap-animation");states.Add(lapped);
            hud.WriteOriginalLapEndpointTrace(Path.Combine(reportDirectory,"lap-endpoint-interior.json"), "interior-after-MinimapSettle");
            require(DynamicNode(lapped,"lapsbg").scale.x>1&&DynamicNode(lapped,"lapsbg/Laps").color.b<1,"production lap event runs source scale and leaf color curves");
            SetMotorHudField(motor,"<IsWheelie>k__BackingField",true);SetMotorHudField(motor,"<WheelieBalance>k__BackingField",.75f);
            yield return MinimapSettle();var balance=CaptureDynamicHud("wheelie");states.Add(balance);
            require(DynamicNode(balance,"Balancebar").alpha==1&&DynamicNode(balance,"Balancebar/Dot").active,"wheelie producer activates bar and actual dot");
            var dot=DynamicNode(balance,"Balancebar/Dot");var bar=DynamicNode(balance,"Balancebar");
            float expected=(bar.size.y-dot.size.y*.5f)*.25f+dot.size.y*.5f;
            require(Mathf.Abs(dot.localPosition.y-expected)<.01f,"wheelie dot source height/half-size formula");
            SetMotorHudField(motor,"<IsWheelie>k__BackingField",false);
            if(progress[0].CurrentTrigger<0)CrossCircuitGate(motor,0,true);
            int current=progress[0].CurrentTrigger;motor.transform.rotation=Quaternion.LookRotation(-gates[current].forward);
            Time.timeScale=1;yield return new WaitForSeconds(.55f);Time.timeScale=0;yield return MinimapSettle();
            var wrong=CaptureDynamicHud("wrong-way");states.Add(wrong);
            require(DynamicNode(wrong,"WrongWay").active&&DynamicNode(wrong,"WrongWay").alpha==1,"actual current-trigger wrong-way after source .5s delay");
            motor.transform.rotation=gates[current].rotation;yield return MinimapSettle();
            var aligned=CaptureDynamicHud("aligned");states.Add(aligned);
            require(DynamicNode(aligned,"WrongWay").alpha==0&&DynamicNode(aligned,"Balancebar").alpha==0,"alignment and wheelie-end hide respective groups");
            Time.timeScale=1;yield return new WaitForSeconds(5.1f);Time.timeScale=0;yield return MinimapSettle();
            hud.WriteOriginalLapEndpointTrace(Path.Combine(reportDirectory,"lap-endpoint-expired.json"), "expired-after-MinimapSettle");
            yield return new WaitForEndOfFrame();
            hud.WriteOriginalLapEndpointTrace(Path.Combine(reportDirectory,"lap-endpoint-endframe.json"), "expired-next-EndOfFrame");
            yield return CheckDeterministicOriginalLapEndpoint(require);
            var expired=CaptureDynamicHud("lap-window-expired");states.Add(expired);
            require(DynamicNode(expired,"Time").alpha==1&&DynamicNode(expired,"DifferenceTime").text=="","source five-second window restores current lap and clears delta");
            require(DynamicNode(expired,"lapsbg").scale==Vector3.one&&DynamicNode(expired,"lapsbg/Laps").color.b==1,"completed source lap clip restores source scale/color");
            require(states.All(s=>s.phase=="Racing"&&!s.coveredByResults&&!s.startCaptionVisible&&s.raceLaps==5&&s.requiredRaceLeavesVisible),"all nonfinal screenshots have actual visible leaf rects in five-lap Racing without START/Results");
            require(progress.Skip(1).All(p=>!p.Finished),"CPU racers remain unfinished before local finisher lifecycle sample");
            DeliverHudFixtureLap(motor,18.900);yield return MinimapSettle();
            require(progress[0].CompletedLaps==4&&lapSeconds[0].Count==4&&hud.CurrentLap==5&&phase==Phase.Racing,"four completed source laps enter final lap5/5 before ending");
            DeliverHudFixtureLap(motor,20.100);
            require(itemHud.LocalRaceFinished&&itemHud.panel.GetComponent<CanvasGroup>().alpha==0,"actual final CrossCircuitGate local callback freezes source item panel immediately");
            yield return null;
            float resultsDeadline=Time.realtimeSinceStartup+2;
            while(hud.MenuTransitionActive&&Time.realtimeSinceStartup<resultsDeadline)yield return null;
            yield return MinimapSettle();var finished=CaptureDynamicHud("local-finished");states.Add(finished);
            require(finished.phase=="Finished"&&!finished.coveredByResults&&!finished.resultPlayerCardVisible&&!finished.resultsDelayElapsed,"real QuickRace finish waits three scaled seconds before result-player overlay");
            require(DynamicNode(finished,"lapsbg/Laps").text=="5/5", "final gate keeps the rendered lap counter at5/5");
            var finalRows=NativeGameSession.Current.pending;
            File.WriteAllText(Path.Combine(reportDirectory,"hud-finisher-receipt.json"),JsonUtility.ToJson(new HudFinisherReceipt{
                expectedSeat=raceConfig.roster[0].seatId,expectedPlace=finishPlaces[0],expectedTime=finishSeconds[0],racerCount=raceConfig.racerCount,
                pendingSeats=finalRows.Select(r=>r.seatId).ToArray(),pendingPlaces=finalRows.Select(r=>r.place).ToArray(),
                pendingTimes=finalRows.Select(r=>r.time).ToArray(),sourceOrder=SourceResultOrderSnapshot()},true));
            require(finalRows.Count==raceConfig.racerCount&&finalRows.Select(r=>r.seatId).Distinct().Count()==raceConfig.racerCount&&NativeGameSession.Current.pending[0].seatId==raceConfig.roster[0].seatId&&NativeGameSession.Current.pending[0].place==finishPlaces[0]
                &&Math.Abs(NativeGameSession.Current.pending[0].time-finishSeconds[0])<.0001,"real finisher records matching QuickRace seat/place/time");
            require(itemHud.panel.GetComponent<CanvasGroup>().alpha==0,"local finisher source item panel freeze persists through Results");
            string finishedTime=DynamicNode(finished,"Time").text;
            Time.timeScale=1;yield return new WaitForSeconds(.23f);Time.timeScale=0;yield return MinimapSettle();
            var frozen=CaptureDynamicHud("local-finished-later");states.Add(frozen);
            require(DynamicNode(frozen,"Time").text==finishedTime,"local finisher freezes Time text while independent source animations continue");
            require(DynamicNode(frozen,"Time").alpha!=DynamicNode(finished,"Time").alpha,"final-lap InvokeRepeating continues .2-second group blink after text freezes");
            require(frozen.currentLap==6&&frozen.lapRecords.Length==5&&frozen.phase=="Finished"&&!frozen.coveredByResults&&!frozen.resultPlayerCardVisible,"internal progress advances after lap five while independent blink runs before the delayed card");
            require(DynamicNode(frozen,"lapsbg/Laps").text=="5/5", "rendered lap counter remains5/5 while waiting for results");
            Time.timeScale=1;yield return new WaitForSeconds(3.15f);Time.timeScale=0;yield return MinimapSettle();
            var card=CaptureDynamicHud("local-finished-player-card");states.Add(card);
            require(card.phase=="Finished"&&card.mode=="QuickRace"&&card.sessionMode=="QuickRace"&&card.resultPath=="FullRaceResults"&&card.resultsDelayElapsed&&card.resultPlayerCardVisible&&!hud.ResultsScoreboardVisible,"after source delay real QuickRace player card renders");
            require(card.resultLeaves[0].active&&card.resultLeaves[0].renderMode==(int)RenderMode.ScreenSpaceOverlay&&card.resultLeaves[0].sortingOrder==200
                &&card.requiredLeaves.All(n=>n.foregroundResultsLeaves.Contains("Fade")&&n.resultBackgroundSurvival<1),"actual Overlay200 source Fade polygon and alpha cover every sampled HUD leaf center");
            var resultSource=NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi").nodes.Single(n=>n.scene=="GlobalHUD"&&n.path=="Canvas/Results/Fade");
            require(BarColor(card.resultLeaves[0].color,resultSource.color)&&BarNear(card.resultLeaves[0].alpha,resultSource.color.a)&&BarNear(card.resultLeaves[2].alpha,.5f),"source actual result Fade color/alpha and one-half banner group survive real branch");
            var resultTime=TimeSpan.FromSeconds(finishSeconds[0]);
            string resultTimeText="<mspace=18>"+resultTime.Hours.ToString("00")+":"+resultTime.Minutes.ToString("00")+":"+resultTime.Seconds.ToString("00")+"."+resultTime.Milliseconds.ToString("000")+"</mspace>";
            require(card.resultLeaves[3].active&&card.resultLeaves[3].text==racerNames[0].Substring(0,racerNames[0].Length-" / YOU".Length)
                &&card.resultLeaves[4].active&&card.resultLeaves[4].text==finishPlaces[0].ToString()&&card.resultLeaves[5].active&&card.resultLeaves[5].text==resultTimeText,"actual player-card leaves map production local result data");
            require(DynamicNode(card,"Time").text==finishedTime&&itemHud.LocalRaceFinished&&itemHud.panel.GetComponent<CanvasGroup>().alpha==0,"real card overlay retains final frozen Time/item lifecycle");
            require(states.All(s=>s.currentLap==s.completedLaps[0]+1&&s.completedLaps[0]==s.lapRecords.Length
                &&Math.Abs(s.lapRecords.Sum()-s.lapTimestamp)<.000001&&s.raceElapsed>=s.lapTimestamp),"all PNG states bind coherent progress, source lap indices, lap array count, timestamps and elapsed");
            require(states.All(s=>s.mode=="QuickRace"&&s.sessionMode=="QuickRace"&&s.resultPath=="FullRaceResults"),"all fixture captures bind source QuickRace result branch");
            require(hud.OriginalMinimapBound(racers.Length,out string mapDetail),"exact source minimap sprite binding "+mapDetail);
            File.WriteAllText(Path.Combine(reportDirectory,"hud-dynamics-verification.json"),JsonUtility.ToJson(new HudDynamicReport{passed=passed,visualParityAccepted=false,checks=checks.ToArray(),states=states.ToArray(),resultAvatarRendering=sourceResultsAvatars.RenderingObservations},true));
            Debug.Log("NATIVE_HUD_DYNAMIC_VERIFICATION "+passed);Application.Quit(passed?0:2);
        }
    }
}

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public void CheckOriginalTimeBoundaryFields(NativeKartMotor motor, System.Action<bool, string> require)
        {
            // Expected strings are independent constants from the original IL2CPP formatter and Interval bodies.
            var seconds = new[] { 59.9996, 60, 3599.9996, 3600, 3661.234, -59.9996, -60, -3599.9996, -3600, -3661.234 };
            var expected = new[] { "01:00.000", "01:00.000", "00:00.000", "00:00.000", "01:01.234",
                "01:00.000", "01:00.000", "00:00.000", "00:00.000", "01:01.234" };
            double previousSeconds = CurrentLapSeconds;
            bool previousFinished = RaceFinished, previousBlinking = originalBlinking;
            float previousDeadline = originalLapDeadline, previousNextBlink = originalNextBlink, previousAlpha = originalTimeAlpha;
            string previousTime = originalTime, previousDifference = originalDifference;
            Color previousDifferenceColor = originalDifferenceColor;
            int previousFrame = originalDynamicFrame;
            try
            {
                for (int i = 0; i < seconds.Length; i++)
                {
                    string total = "<mspace=24>" + expected[i] + "</mspace>";
                    string split = "<mspace=16>" + expected[i] + "</mspace>";
                    require(TimeText(seconds[i]) == expected[i], "original IL2CPP time boundary core " + i);
                    foreach (string panel in new[] { "TimePanel", "TimePanelCUP" })
                    {
                        BindOriginalRecordPanel(panel, seconds[i], new[] { seconds[i], seconds[i], seconds[i], seconds[i], seconds[i] }, "kart", "reimu");
                        var actual = authoredUi.ReadSelectionRecordPanel(panel);
                        require(actual.total == total && actual.splits.All(value => value == split), "actual " + panel + " original raw time boundary " + i);
                    }
                    CurrentLapSeconds = seconds[i];
                    RaceFinished = false;
                    originalLapDeadline = -1;
                    originalBlinking = false;
                    originalDynamicFrame = -1;
                    DrawRestoredRaceDynamics(motor);
                    var hudTime = authoredUi.ReadOriginalDynamicHud().Single(node => node.path == "HUD/Canvas/Time");
                    require(hudTime.text == total, "actual HUD original raw time boundary " + i);
                }
            }
            finally
            {
                CurrentLapSeconds = previousSeconds;
                RaceFinished = previousFinished;
                originalBlinking = previousBlinking;
                originalLapDeadline = previousDeadline;
                originalNextBlink = previousNextBlink;
                originalTimeAlpha = previousAlpha;
                originalTime = previousTime;
                originalDifference = previousDifference;
                originalDifferenceColor = previousDifferenceColor;
                originalDynamicFrame = previousFrame;
                DrawOriginalRecordPanels();
                authoredUi.DrawOriginalRaceDynamics(motor, OriginalBaseTopSpeed, CurrentLap, RaceLapCount, RaceFinished,
                    NativeUserSettings.GetInt("speedometerEnabled") == 1, originalTime, originalTimeAlpha, originalDifference, originalDifferenceColor, OriginalWrongWay);
            }
        }
    }
}

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public void BeginOriginalLapEndpointTrace() => authoredUi.BeginLapEndpointTrace();
        public void WriteOriginalLapEndpointTrace(string path, string stage) => authoredUi.WriteLapEndpointTrace(path, stage);
    }
}
