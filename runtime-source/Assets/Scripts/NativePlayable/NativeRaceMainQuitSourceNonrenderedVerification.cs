using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        bool quitSourceNonrenderedCheck,quitSourceExitObserver;
        float quitSourceObserverStarted;
        string quitSourceObserverAction="";
        [Serializable] sealed class QuitSourceStateReport
        {
            public string scope="nonrendered: 6 injected model assertions and 15 production Cancel/clock/route adapter assertions; panel graph excluded";
            public bool passed,completed,visualAcceptance=false,rendered=false;
            public int runtimeCount,expectedRuntimeCount=21,failures,engineErrors;
            public string[] checks;public string exception="";public float wallSeconds;
        }
        [Serializable] sealed class QuitSourceExitReport
        {
            public string scope="pre-Application.Quit production observer; external process exit code/termination required";
            public bool passed,productionExitReached,visualAcceptance=false,rendered=false;
            public float listenerVolume,holdingTime,wallSeconds;
            public int humansPressing,engineErrors;
            public string actionIdentity,exception="";
        }
        void ObserveQuitSourceExit()
        {
            if(!quitSourceExitObserver)return;
            WriteQuitSourceExitReport(true,"");
        }
        void WriteQuitSourceExitReport(bool reached,string failure)
        {
            var report=new QuitSourceExitReport{productionExitReached=reached,listenerVolume=AudioListener.volume,
                holdingTime=mainQuitSource==null?0:mainQuitSource.HoldingTime,
                humansPressing=mainQuitSource==null?0:mainQuitSource.HumansPressing,
                engineErrors=errorCount,actionIdentity=quitSourceObserverAction,exception=failure,
                wallSeconds=Time.realtimeSinceStartup-quitSourceObserverStarted};
            report.passed=reached && failure.Length==0 && report.listenerVolume==0 && report.holdingTime>=1
                && report.humansPressing==1 && report.actionIdentity.Length>0 && errorCount==0;
            string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"main-quit-source-exit-observer.json"),json);
            Debug.Log("MAIN_QUIT_SOURCE_EXIT_OBSERVER_RESULT "+json);
        }
        void ValidateQuitSourceSeeds()
        {
            if(hud==null || hud.authoredUiData==null)throw new InvalidOperationException("missing authored menu2 seed data");
            foreach(string path in new[]{"Canvas/MainMenu/WheelRoot/Wheel","Canvas/ExtrasMenu/WheelRoot/Wheel","Canvas/OptionsMenu/WheelRoot/Wheel","Canvas/RaceMenu/Wheel"})
                if(hud.authoredUiData.nodes.Count(n=>n.scene=="menu2" && n.path==path)!=1)
                    throw new InvalidOperationException("missing/duplicate authored wheel seed: "+path);
        }
#if ENABLE_INPUT_SYSTEM
        IEnumerator CheckQuitSourceExitObserver()
        {
            quitSourceObserverStarted=Time.realtimeSinceStartup;
            Keyboard keyboard=null;string failure="";
            try
            {
                keyboard=InputSystem.AddDevice<Keyboard>();
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                ValidateQuitSourceSeeds();
                phase=Phase.Menu;Time.timeScale=1;settingsOpen=false;hud.SuppressPointerInput=true;
                hud.SetMenuPage(NativeMenuPage.Main);InitializeMainQuitSource();FixedUpdate();
                var action=mainQuitAction;if(action==null)throw new InvalidOperationException("missing production applied Normal Cancel action");
                action.actionMap.asset.devices=new InputDevice[]{keyboard};
                int direct=-1;
                for(int i=0;i<action.bindings.Count;i++)
                {
                    if(!action.bindings[i].isComposite)action.ApplyBindingOverride(i,new InputBinding{overridePath=""});
                    if(direct<0 && !action.bindings[i].isComposite && !action.bindings[i].isPartOfComposite)direct=i;
                }
                if(direct<0)throw new InvalidOperationException("missing direct source Cancel binding");
                action.ApplyBindingOverride(direct,"<Keyboard>/q");quitSourceObserverAction=action.actionMap.name+"/"+action.name+"/"+action.id;
            }
            catch(Exception e){failure=e.ToString();}
            if(failure.Length>0){WriteQuitSourceExitReport(false,failure);Application.Quit(2);yield break;}
            yield return null;yield return null;
            try{InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));}
            catch(Exception e){failure=e.ToString();}
            if(failure.Length>0){WriteQuitSourceExitReport(false,failure);Application.Quit(2);yield break;}
            while(Time.realtimeSinceStartup-quitSourceObserverStarted<5)yield return null;
            WriteQuitSourceExitReport(false,"timeout: real production Cancel hold did not terminate process");Application.Quit(2);
        }
        IEnumerator CheckQuitSourceNonrendered()
        {
            modeChecks.Clear();modePassed=true;float start=Time.realtimeSinceStartup;
            var priorBackground=InputSystem.settings.backgroundBehavior;
            Keyboard keyboard=null;string failure="";
            try
            {
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                keyboard=InputSystem.AddDevice<Keyboard>();ValidateQuitSourceSeeds();hud.SuppressPointerInput=true;
                phase=Phase.Menu;Time.timeScale=1;settingsOpen=false;hud.SetMenuPage(NativeMenuPage.Main);InitializeMainQuitSource();
            }
            catch(Exception e){failure=e.ToString();}
            if(failure.Length>0)
            {
                try{InputSystem.settings.backgroundBehavior=priorBackground;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);}
                catch(Exception e){failure+="\ncleanup: "+e;}
                FinishQuitSourceStateReport(false,failure,start);yield break;
            }
            yield return null;yield return null;
            var stack=new Stack<IEnumerator>();stack.Push(CheckMainQuitSourceNonrenderedCases(keyboard));
            while(stack.Count>0)
            {
                if(Time.realtimeSinceStartup-start>30){failure="timeout after 30 seconds";break;}
                bool moved=false;object value=null;
                try{moved=stack.Peek().MoveNext();if(moved)value=stack.Peek().Current;}
                catch(Exception e){failure=e.ToString();break;}
                if(!moved){try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception e){failure="cleanup: "+e;break;}continue;}
                if(value is IEnumerator nested && !(value is CustomYieldInstruction)){stack.Push(nested);continue;}
                yield return value;
            }
            bool completed=stack.Count==0 && failure.Length==0;
            while(stack.Count>0){try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception e){failure+="\ncleanup: "+e;}}
            try{InputSystem.settings.backgroundBehavior=priorBackground;InputSystem.RemoveDevice(keyboard);}
            catch(Exception e){failure+="\ncleanup: "+e;}
            FinishQuitSourceStateReport(completed,failure,start);
        }
#else
        IEnumerator CheckQuitSourceExitObserver()
        {
            quitSourceObserverStarted=Time.realtimeSinceStartup;
            WriteQuitSourceExitReport(false,"ENABLE_INPUT_SYSTEM is absent; real production Cancel injection is unavailable");
            Application.Quit(2);yield break;
        }
        IEnumerator CheckQuitSourceNonrendered()
        {
            modeChecks.Clear();modePassed=false;
            FinishQuitSourceStateReport(false,"ENABLE_INPUT_SYSTEM is absent; 21 real source assertions were not executed",Time.realtimeSinceStartup);
            yield break;
        }
#endif
        void FinishQuitSourceStateReport(bool completed,string failure,float start)
        {
            int failed=modeChecks.Count(c=>c.StartsWith("FAIL ",StringComparison.Ordinal));
            var report=new QuitSourceStateReport{completed=completed,runtimeCount=modeChecks.Count,failures=failed,
                engineErrors=errorCount,checks=modeChecks.ToArray(),exception=failure,wallSeconds=Time.realtimeSinceStartup-start};
            report.passed=completed && failure.Length==0 && modeChecks.Count==21 && failed==0 && modePassed && errorCount==0;
            string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"main-quit-source-state-nonrendered.json"),json);
            Debug.Log("MAIN_QUIT_SOURCE_STATE_NONRENDERED_RESULT "+json);Application.Quit(report.passed?0:2);
        }
#if ENABLE_INPUT_SYSTEM
        IEnumerator CheckMainQuitSourceNonrenderedCases(Keyboard keyboard)
        {
            var state=new NativeMainQuitSourceState();int exits=0;
            for(int i=0;i<5;i++)state.Press();
            ModeCheck(state.HumansPressing==3 && state.Enabled && state.PanelActive,"quit source press count clamps at3");
            state.Update(.4f,()=>exits++);state.Release();
            ModeCheck(state.HumansPressing==2 && state.HoldingTime==.4f && state.PanelActive,"quit nonfinal release preserves hold");
            state.Release();state.Release();state.Release();
            ModeCheck(state.HumansPressing==0 && state.HoldingTime==0 && !state.PanelActive && !state.Enabled && state.FillAmount==.4f,"quit final release hides panel and retains fill");
            state.Press();state.Update(.999f,()=>exits++);ModeCheck(exits==0,"quit before source one-second threshold");
            state.Update(.0011f,()=>exits++);ModeCheck(exits==1 && !state.Enabled && state.PanelActive,"quit source threshold invokes once and disables update");
            state.Update(1,()=>exits++);ModeCheck(exits==1,"quit disabled behaviour does not repeat");
            NativeInputBindings.Initialize();FixedUpdate();
            var action=mainQuitAction;var fixtureAsset=action.actionMap.asset;var originalDevices=fixtureAsset.devices;
            fixtureAsset.devices=new InputDevice[]{keyboard};
            string overrides=fixtureAsset.SaveBindingOverridesAsJson();
            float previousScale=Time.timeScale;
            try
            {
                for(int i=0;i<action.bindings.Count;i++)if(!action.bindings[i].isComposite)action.ApplyBindingOverride(i,new InputBinding{overridePath=""});
                int direct=-1;for(int i=0;i<action.bindings.Count;i++)if(!action.bindings[i].isComposite && !action.bindings[i].isPartOfComposite){direct=i;break;}
                ModeCheck(direct>=0,"quit original Normal Cancel direct binding exists");
                action.ApplyBindingOverride(direct,"<Keyboard>/q");
                Time.timeScale=0;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape,Key.Backspace));yield return null;
                ModeCheck(mainQuitSource.HumansPressing==0 && !mainQuitSource.PanelActive,"quit custom action has no raw Escape Backspace bypass");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));yield return null;
                ModeCheck(mainQuitSource.HumansPressing==1 && mainQuitSource.PanelActive,"quit custom Cancel started reaches production Press");
                int retainedGeneration=hud.Menu2SourceSceneGeneration;var retainedInstance=mainQuitSource;
                float held=mainQuitSource.HoldingTime;yield return new WaitForSecondsRealtime(.12f);
                ModeCheck(mainQuitSource.HoldingTime==held,"quit production hold uses scaled deltaTime");
                Time.timeScale=previousScale;
                hud.SetMenuPage(NativeMenuPage.Extras);SynchronizeMainQuitSourceActive();
                ModeCheck(mainQuitSource.HumansPressing==1 && mainQuitSource.PanelActive,"quit same-scene transition preserves count without extra Release");
                yield return new WaitForSeconds(.04f);
                ModeCheck(mainQuitSource.HoldingTime>held,"quit behaviour updates while source Main parent is still visible during Extras transition");
                Time.timeScale=0;while(hud.MenuTransitionActive)yield return null;
                SynchronizeMainQuitSourceActive();float inactiveHolding=mainQuitSource.HoldingTime;
                ModeCheck(!mainQuitAutoActive && !hud.MainQuitSourceParentActive,"quit source Main parent inactive after same-scene transition");
                Time.timeScale=previousScale;yield return new WaitForSeconds(.04f);
                ModeCheck(mainQuitSource.HoldingTime==inactiveHolding && mainQuitSource.HumansPressing==1 && ReferenceEquals(retainedInstance,mainQuitSource) && hud.Menu2SourceSceneGeneration==retainedGeneration,"quit inactive Main parent pauses retained behaviour without Release or scene rebuild");
                hud.SetMenuPage(NativeMenuPage.Main);SynchronizeMainQuitSourceActive();
                Time.timeScale=0;while(hud.MenuTransitionActive)yield return null;
                Time.timeScale=previousScale;yield return new WaitForSeconds(.06f);
                ModeCheck(mainQuitSource.HoldingTime>held && mainQuitSource.FillAmount>0,"quit production scaled progress reaches source bar state");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                ModeCheck(mainQuitSource.HumansPressing==0 && mainQuitSource.HoldingTime==0 && !mainQuitSource.PanelActive,"quit Cancel canceled resets and hides live source panel");
                var oldInstance=mainQuitSource;oldInstance.Press();oldInstance.Update(.2f,()=>{});
                hud.SetMenuPage(NativeMenuPage.Characters);SynchronizeMainQuitSourceActive();
                ModeCheck(!mainQuitMenu2Present && mainQuitSource==null && mainQuitAction==null && !mainQuitAutoActive,"quit Single selection scene departure discards state and subscription");
                hud.SetMenuPage(NativeMenuPage.Main);SynchronizeMainQuitSourceActive();
                ModeCheck(hud.Menu2SourceSceneGeneration==retainedGeneration+1 && mainQuitMenu2Present && !ReferenceEquals(oldInstance,mainQuitSource) && mainQuitSource.HumansPressing==0 && mainQuitSource.HoldingTime==0 && !mainQuitSource.Enabled && !mainQuitSource.PanelActive && mainQuitSource.FillAmount==1,"quit rebuilt menu2 owns fresh source constructor state");
                int beforeProfilesGeneration=hud.Menu2SourceSceneGeneration;
                OpenSettings();ActivateSettingsRow(2);SynchronizeMainQuitSourceActive();
                ModeCheck(settingsPage==SettingsPage.Profiles && mainQuitSource==null && mainQuitAction==null && !mainQuitMenu2Present,"quit original InputButton profiles Single route drops menu2 owner");
                SettingsBack();SynchronizeMainQuitSourceActive();
                ModeCheck(hud.MenuPage==NativeMenuPage.Main && !settingsOpen && hud.Menu2SourceSceneGeneration==beforeProfilesGeneration+1 && mainQuitMenu2Present && mainQuitSource.HumansPressing==0 && mainQuitSource.HoldingTime==0,"quit profiles return creates common fresh menu2 generation");
                var old=mainQuitAction;NativeInputBindings.Initialize();FixedUpdate();
                ModeCheck(!ReferenceEquals(old,mainQuitAction) && ReferenceEquals(mainQuitAction,MainQuitSourceAction()),"quit FixedUpdate follows replaced applied action");
            }
            finally
            {
                Time.timeScale=previousScale;InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                if(fixtureAsset){fixtureAsset.RemoveAllBindingOverrides();fixtureAsset.LoadBindingOverridesFromJson(overrides);fixtureAsset.devices=originalDevices;}
            }
        }
#endif

    }
}
