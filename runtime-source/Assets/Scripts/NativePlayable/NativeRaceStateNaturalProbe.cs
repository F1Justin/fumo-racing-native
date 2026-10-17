using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Fumo.NativePlayable
{
    public sealed class NativeRaceStateNaturalProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool accepted=false,naturalObserved;
            public string identity="passive real NativeRace state; no input or finish injection";
            public List<string> events=new List<string>();
            public List<string> gatepending=new List<string>{"real-device pause/unpause owner order","natural all-human finish","hidden/repeat ready","mode restart/quit destination","cross-domain scaled timers","source pixel pairing"};
        }
        Report report=new Report();NativeRace race;NativeRaceStateFlow state;string path;int flushFrame;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--race-state-natural-probe")<0)return;
            if(FindObjectOfType<NativeRaceStateNaturalProbe>())return;
            var go=new GameObject("RaceStateNaturalProbe");DontDestroyOnLoad(go);go.AddComponent<NativeRaceStateNaturalProbe>();
        }
        void Awake(){path=Path.Combine(Application.persistentDataPath,"race-state-natural-probe.json");SceneManager.sceneLoaded+=Loaded;Record("probe-installed");}
        void Loaded(Scene scene,LoadSceneMode mode){Record("scene-loaded:"+scene.name+":"+mode);}
        void Update()
        {
            var found=FindObjectOfType<NativeRace>();var current=found?found.SourceRaceState:null;
            if(current!=state){Unbind();race=found;state=current;if(state!=null){Bind();Record("new-race-binding");}}
            if(state!=null){report.naturalObserved=true;Record("frame");}
            if(Time.frameCount-flushFrame>=60){Flush();flushFrame=Time.frameCount;}
        }
        void Bind()
        {
            state.Paused+=Paused;state.UnPaused+=UnPaused;state.ReadyChanged+=Ready;
            state.RaceEnded+=Ended;state.RaceFinish+=Finish;state.ShowScoreboardRequested+=Scoreboard;
            state.AllReadyFinishRequested+=AllReady;state.ModeRestartRequested+=Restart;state.ModeQuitRequested+=Quit;
        }
        void Unbind()
        {
            if(state==null)return;state.Paused-=Paused;state.UnPaused-=UnPaused;state.ReadyChanged-=Ready;
            state.RaceEnded-=Ended;state.RaceFinish-=Finish;state.ShowScoreboardRequested-=Scoreboard;
            state.AllReadyFinishRequested-=AllReady;state.ModeRestartRequested-=Restart;state.ModeQuitRequested-=Quit;
        }
        void Paused(int user)=>Record("onPaused:"+user);
        void UnPaused(int user)=>Record("onUnPaused:"+user);
        void Ready(int slot)=>Record("readyChanged:"+slot);
        void Ended()=>Record("raceEnded");void Finish()=>Record("raceFinish");
        void Scoreboard()=>Record("scoreboardRequested");void AllReady()=>Record("allReadyFinishRequested");
        void Restart()=>Record("modeRestartRequested");void Quit()=>Record("modeQuitRequested");
        void Record(string label)
        {
            string line=Time.frameCount+"|"+Time.realtimeSinceStartup+"|"+label+"|scene="+SceneManager.GetActiveScene().name+"|time="+Time.time+"|scale="+Time.timeScale;
            if(state!=null)
            {
                line+="|phase="+(race?race.SourceNativePhase:"destroyed")+"|raceId="+state.RaceId+"|inRace="+state.InRace+"|ended="+state.EndedRace+"|owner="+state.PausedBy;
                for(int i=0;i<4;i++){var h=state.Humans[i];line+="|slot="+i+",inputUser="+h.InputUserIndex+",joined="+h.Joined+",racer="+h.RacerIndex+",finished="+h.Finished+",card="+h.CardActive+",actualSelf="+(race&&race.SourceActualCardActive(i))+",ready="+h.Ready+",selection="+h.Selected;}
            }
            report.events.Add(line);
        }
        void Flush(){if(!string.IsNullOrEmpty(path))File.WriteAllText(path,JsonUtility.ToJson(report,true));}
        void OnApplicationQuit(){Record("application-quit");Flush();}
        void OnDestroy(){Unbind();SceneManager.sceneLoaded-=Loaded;Flush();}
    }
}
