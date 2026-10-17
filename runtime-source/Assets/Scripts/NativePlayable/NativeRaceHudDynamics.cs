using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public double CurrentLapSeconds;
        public float OriginalBaseTopSpeed;
        public bool OriginalWrongWay;
        float originalLapDeadline=-1, originalNextBlink;
        float originalTimeAlpha=1;
        bool originalBlinking;
        string originalTime="<mspace=24>00:00.000</mspace>", originalDifference="";
        Color originalDifferenceColor=Color.white;
        int originalDynamicFrame=-1;
        static string OriginalHudTime(double seconds) => OriginalTimeText(seconds, 24);
        public NativeAuthoredUi.DynamicHudSample[] ReadOriginalDynamicHud()=>authoredUi.ReadOriginalDynamicHud();
        public bool RacePrefabVisible => authoredUi!=null && authoredUi.VisibleGraphic("HUD/Canvas/Time")
            && authoredUi.VisibleGraphic("HUD/Canvas/lapsbg/Laps") && (!ShowRaceRank || authoredUi.VisibleGraphic("HUD/Canvas/rankbg"));
        public void NotifyOriginalLap()
        { originalLapDeadline=Time.time+5;originalNextBlink=Time.time+.2f;originalBlinking=true;authoredUi?.NotifyOriginalLap(); }
        void DrawRestoredRaceDynamics(NativeKartMotor motor)
        {
            if(originalDynamicFrame!=Time.frameCount)
            {
                originalDynamicFrame=Time.frameCount;
                if(originalBlinking && Time.time>=originalNextBlink)
                {
                    int ticks=1+Mathf.FloorToInt((Time.time-originalNextBlink)/.2f);
                    if((ticks&1)==1)originalTimeAlpha=originalTimeAlpha>.5f?.5f:1;
                    originalNextBlink+=ticks*.2f;
                }
                if(!RaceFinished)
                {
                    if(Time.time<originalLapDeadline && RaceLapSeconds.Count>0)
                    {
                        originalTime=OriginalHudTime(RaceLapSeconds[RaceLapSeconds.Count-1]);
                        if(RaceLapSeconds.Count>=2)
                        {
                            double diff=RaceLapSeconds[RaceLapSeconds.Count-1]-RaceLapSeconds[RaceLapSeconds.Count-2];
                            originalDifference=(diff<0?"-":"+")+OriginalHudTime(diff);originalDifferenceColor=diff<0?Color.green:Color.red;
                        }
                    }
                    else {originalTime=OriginalHudTime(CurrentLapSeconds);originalDifference="";originalTimeAlpha=1;originalBlinking=false;}
                }
            }
            // The progress counter advances past the final lap; the HUD stays at total/total.
            int displayedLap = Mathf.Clamp(CurrentLap, 1, Mathf.Max(1, RaceLapCount));
            authoredUi.DrawOriginalRaceDynamics(motor,OriginalBaseTopSpeed,displayedLap,RaceLapCount,RaceFinished,
                NativeUserSettings.GetInt("speedometerEnabled")==1,originalTime,originalTimeAlpha,originalDifference,originalDifferenceColor,OriginalWrongWay);
        }
        public void ResetOriginalRaceDynamics()
        {originalLapDeadline=-1;originalBlinking=false;originalTimeAlpha=1;originalTime="<mspace=24>00:00.000</mspace>";originalDifference="";originalDynamicFrame=-1;authoredUi?.ResetOriginalDynamics();}
    }
}
