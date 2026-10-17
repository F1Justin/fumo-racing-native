using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class GridSpawnCase
        {
            public int count,expectedSlot,actualSlot;
            public string mode,screenshot,error;
            public Vector3 actualHumanPosition,assignedHumanPosition,sourcePoint;
            public bool passed;
        }
        [Serializable] sealed class GridSpawnReport
        {
            public bool passed,accepted=false;
            public string scope="Injected single actual input owner; production config/Begin/ResetAt and rendered starting grid; physical receipt remains separate";
            public List<GridSpawnCase> cases=new List<GridSpawnCase>();
        }
        internal IEnumerator CheckGridSpawnLive()
        {
            Directory.CreateDirectory(reportDirectory);
            verificationRun=true;selfTest=false;
            var report=new GridSpawnReport{passed=true};
            foreach(int count in new[]{10,40,1})
            {
                var row=new GridSpawnCase{count=count,mode=count==1?"time-trials":"custom-race"};
                report.cases.Add(row);
                try
                {
                    ResetRace(false);
                    selectedMode=count==1?NativeSessionMode.TimeTrials:NativeSessionMode.CustomRace;
                    customRacers=count;customLaps=2;
                    if(!CreateSourceDiagnosticOwnerBeforeBegin())throw new InvalidOperationException("Actual diagnostic owner missing");
                    Begin();
                    row.expectedSlot=Math.Min(count,sourceGridPositions.Length)-1;
                    row.actualSlot=NativeLocalRosterOrder.GridSlot(raceConfig,0,sourceGridPositions.Length);
                    row.sourcePoint=sourceGridPositions[row.expectedSlot];
                    row.assignedHumanPosition=starts[0];row.actualHumanPosition=racers[0].transform.position;
                    row.passed=racers.Length==count && row.actualSlot==row.expectedSlot
                        && Vector3.Distance(row.assignedHumanPosition,row.sourcePoint)<.001f
                        && Vector3.Distance(row.actualHumanPosition,row.sourcePoint)<.001f;
                    row.screenshot="single-human-grid-"+count+".png";
                }
                catch(Exception error){row.error=error.ToString();row.passed=false;}
                report.passed &= row.passed;
                if(!row.passed)break;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory,row.screenshot));
                yield return null;
            }
            File.WriteAllText(Path.Combine(reportDirectory,"single-human-grid-report.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.passed?0:2);
        }
    }
}
