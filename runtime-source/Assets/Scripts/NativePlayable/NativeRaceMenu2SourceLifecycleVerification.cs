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
        bool menu2SourceLifecycleCheck;
        [Serializable] sealed class Menu2SourceLifecycleReport
        {
            public string scope="nonrendered injected menu2 UI/state; ordinary Update and scaled/unscaled clocks";
            public bool passed, completed, visualAcceptance=false, rendered=false;
            public int runtimeCount, expectedRuntimeCount=16, failures, engineErrors;
            public string[] checks;
            public string exception="";
            public float wallSeconds;
        }
        IEnumerator CheckMenu2SourceLifecycleNonrendered()
        {
            modeChecks.Clear();modePassed=true;
            float start=Time.realtimeSinceStartup;
            string failure="";bool completed=false;
            try
            {
                if(hud==null || hud.authoredUiData==null)throw new InvalidOperationException("missing serialized authored menu2 seed data");
                foreach(string page in new[]{"MainMenu","ExtrasMenu","OptionsMenu"})
                    if(hud.authoredUiData.nodes.Count(n=>n.scene=="menu2" && n.path=="Canvas/"+page+"/WheelRoot/Wheel")!=1)
                        throw new InvalidOperationException("missing/duplicate authored wheel seed: "+page);
                if(hud.authoredUiData.nodes.Count(n=>n.scene=="menu2" && n.path=="Canvas/RaceMenu/Wheel")!=1)
                    throw new InvalidOperationException("missing/duplicate authored wheel seed: RaceMenu");
                phase=Phase.Menu;Time.timeScale=1;settingsOpen=false;
                hud.SuppressPointerInput=true;hud.SetMenuPage(NativeMenuPage.Main);
                InitializeMainQuitSource();
            }
            catch(Exception e){failure=e.ToString();}
            if(failure.Length>0){FinishMenu2SourceLifecycleReport(false,failure,start);yield break;}
            yield return null;
            yield return null;
            var stack=new Stack<IEnumerator>();stack.Push(CheckMenu2SourceWheelsLive());
            while(stack.Count>0)
            {
                if(Time.realtimeSinceStartup-start>30){failure="timeout: ordinary-frame helper did not complete within 30 seconds";break;}
                bool moved=false;object value=null;
                try{moved=stack.Peek().MoveNext();if(moved)value=stack.Peek().Current;}
                catch(Exception e){failure=e.ToString();break;}
                if(!moved)
                {
                    try{(stack.Pop() as IDisposable)?.Dispose();}
                    catch(Exception e){failure="cleanup: "+e;break;}
                    continue;
                }
                var nested=value as IEnumerator;
                if(nested!=null){stack.Push(nested);continue;}
                yield return value;
            }
            completed=stack.Count==0 && failure.Length==0;
            while(stack.Count>0)
            {
                try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception e){failure+="\ncleanup: "+e;}
            }
            FinishMenu2SourceLifecycleReport(completed,failure,start);
        }
        void FinishMenu2SourceLifecycleReport(bool completed,string failure,float start)
        {
            int failed=modeChecks.Count(c=>c.StartsWith("FAIL ",StringComparison.Ordinal));
            var report=new Menu2SourceLifecycleReport{completed=completed,runtimeCount=modeChecks.Count,
                failures=failed,engineErrors=errorCount,checks=modeChecks.ToArray(),exception=failure,
                wallSeconds=Time.realtimeSinceStartup-start};
            report.passed=completed && failure.Length==0 && report.runtimeCount==report.expectedRuntimeCount && failed==0 && modePassed && errorCount==0;
            string json=JsonUtility.ToJson(report,true);
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"menu2-source-lifecycle-nonrendered.json"),json);
            Debug.Log("MENU2_SOURCE_LIFECYCLE_NONRENDERED_RESULT "+json);
            Application.Quit(report.passed?0:2);
        }
    }
}
