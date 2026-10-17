using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class OfudaSourceSample
        {public string stage;public int frame,currentLeader,historyLeader;public double gameTime,next,firstNextBefore,firstNextAfter;public float firstTime,forgetTime;public bool eligible;}
        [Serializable] sealed class OfudaSourceReport {public bool accepted;public string scope;public OfudaSourceSample[] samples;}
        internal IEnumerator CheckOfudaSourceEligibilityLive(Action<bool,string> check)
        {
            var entry=GetComponent<NativeItemCatalog>().Require("Ofuda");
            bool savedVisual=visualTest,savedAuto=autopilot;var savedMode=selectedMode;
            var positions=racers.Select(x=>x.transform.position).ToArray();var enabled=racers.Select(x=>x.enabled).ToArray();
            var samples=new List<OfudaSourceSample>();
            Func<string,OfudaSourceSample> sample=stage=>{
                var row=new OfudaSourceSample {stage=stage,frame=Time.frameCount,gameTime=SourceGameTime,firstTime=SourceFirstRacerTime,
                    forgetTime=sourceRanking.ForgetTime,currentLeader=sourceRanking.RacerAtRank(0),historyLeader=Array.IndexOf(racers,SourceMostFirstRacer()),firstNextBefore=NextOfudaTime};
                row.eligible=NativeItemSelection.Eligible(entry,entry.minRank,"Reimu",0,SourceGameTime,NextOfudaTime,0,SourceFirstRacerTime,SourceOfudaEligibilityNext);
                row.firstNextAfter=NextOfudaTime;row.next=row.firstNextAfter;
                samples.Add(row);return row;
            };
            try
            {
                visualTest=false;autopilot=false;selectedMode=NativeSessionMode.QuickRace;selectedTrackId=trackId;
                var config=new NativeSessionConfig {mode=NativeSessionMode.QuickRace,modeId="quick-race",laps=5,racerCount=racers.Length,itemType="none",trackIds=new List<string>{trackId}};
                for(int i=0;i<racers.Length;i++)config.roster.Add(new NativeSessionSeat {seatId="ofuda-seat-"+i,characterId=vehicleRoster.CharacterIdAt(selectedCharacterIndex),vehicleId="kart",skinIndex=0,isHuman=i==0});
                sessionId=NativeGameSession.Create(config);Begin();
                for(int i=0;i<racers.Length;i++){racers[i].enabled=false;racers[i].transform.position=gates[0].position+Vector3.right*(100f+100f*i);}
                Physics.SyncTransforms();
                double deadline=Time.realtimeSinceStartupAsDouble+8;
                while(phase!=Phase.Racing&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                check(phase==Phase.Racing&&raceClock.Running&&sourceRanking.Started,"Ofuda real Begin/countdown/ranking timer producer");
                yield return null;yield return null;
                var early=sample("early-current-source");
                check(early.currentLeader==0&&!early.eligible&&early.next==entry.ofudaCooldown,"Ofuda real early eligibility/lazy per-race cooldown");
                check(early.historyLeader==0&&early.forgetTime<4f,"fresh real countdown has current historical leader before cumulative forget threshold");
                var a=racers[0].transform.position;racers[0].transform.position=racers[1].transform.position;racers[1].transform.position=a;Physics.SyncTransforms();
                yield return null;yield return null;
                var switched=sample("early-switch-before-cumulative-forget");
                check(switched.currentLeader==1&&switched.historyLeader==0&&switched.forgetTime<4f&&!switched.eligible,"early real sorting changes current leader while original history persists below cumulative threshold");
                deadline=Time.realtimeSinceStartupAsDouble+5;
                while(sourceRanking.ForgetTime<4f&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                yield return null;
                var forgotten=sample("actual-cumulative-threshold-forgotten");
                check(forgotten.currentLeader==1&&forgotten.historyLeader==1&&forgotten.forgetTime>=4f,"actual cumulative forget threshold clears history and returns current leader");
                deadline=Time.realtimeSinceStartupAsDouble+Math.Max(entry.ofudaCooldown,entry.firstRacerTimeCondition)+8;
                while((SourceGameTime<entry.ofudaCooldown||SourceFirstRacerTime<entry.firstRacerTimeCondition)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                var ready=sample("steady-after-cumulative-forget-eligible");
                check(ready.currentLeader==1&&ready.historyLeader==1&&ready.forgetTime>=4f&&ready.eligible,"steady qualification after original cumulative history clearing");
                Time.timeScale=0;var paused=sample("paused-source");yield return new WaitForSecondsRealtime(.25f);var pausedAfter=sample("paused-source-after");
                check(paused.gameTime==pausedAfter.gameTime&&paused.firstTime==pausedAfter.firstTime,"Ofuda actual source time and first-time pause freeze");Time.timeScale=1;
                var inventory=racers[0].GetComponent<NativeKartItemInventory>();inventory.ResetItems();
                check(inventory.TryGive("Ofuda"),"controlled actual inventory grant executes Ofuda create cooldown producer");
                var created=sample("actual-inventory-create");check(created.next>created.gameTime&&!created.eligible,"real Ofuda creation advances production cooldown");inventory.ResetItems();
            }
            finally
            {
                Time.timeScale=1;ResetRace(false);visualTest=savedVisual;autopilot=savedAuto;selectedMode=savedMode;
                for(int i=0;i<racers.Length&&i<positions.Length;i++){racers[i].transform.position=positions[i];racers[i].enabled=enabled[i];}
                File.WriteAllText(Path.Combine(reportDirectory,"ofuda-source-eligibility-report.json"),JsonUtility.ToJson(new OfudaSourceReport {accepted=false,
                    scope="Injected racer positions with real Begin/countdown/time/ranking/history and production Eligible callback; controlled actual inventory grant. Random item acquisition, natural racing and original paired visuals remain separate.",samples=samples.ToArray()},true));
            }
        }
    }
}
