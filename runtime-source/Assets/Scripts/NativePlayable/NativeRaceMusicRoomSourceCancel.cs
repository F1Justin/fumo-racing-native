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
        bool musicCancelScaledCheck, musicRoomSourceCancelPending;
        int musicRoomSourceCancelCallbacks;
        readonly System.Collections.Generic.List<int> musicContinuitySourceIds=new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<int> musicContinuityClipIds=new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<int> musicContinuitySamples=new System.Collections.Generic.List<int>();
        void StartMusicRoomSourceCancel()
        {
            NativeAudioRuntime.StopMusicRoomPreview();
            musicRoomSourceCancelPending=true;
            Invoke(nameof(FinishMusicRoomSourceCancel),.02f);
        }
        void FinishMusicRoomSourceCancel()
        {
            hud.SetMenuPage(NativeMenuPage.Extras);hud.SelectMenuIndex(1);
            NativeAudioRuntime.ResumeMenuAfterMusicRoom();
            musicRoomSourceCancelCallbacks++;musicRoomSourceCancelPending=false;
            CancelInvoke(nameof(FinishMusicRoomSourceCancel));
        }
        [Serializable] sealed class MusicCancelScaledReport
        {
            public string scope="nonrendered production MusicRoom BackToExtras action, Unity scaled Invoke and menu2 state";
            public bool passed,completed,visualAcceptance=false,rendered=false;
            public int runtimeCount,expectedRuntimeCount=9,failures,engineErrors;
            public string[] checks;public string exception="";public float wallSeconds;
            public bool ordinaryDSPRequested,hearingAccepted=false;
            public int[] menuSourceIds,menuClipIds,menuTimeSamples;
        }
        IEnumerator MusicCancelScaledCases()
        {
            if(hud==null || hud.authoredUiData==null)throw new InvalidOperationException("missing authored menu2 seed data");
            foreach(string path in new[]{"Canvas/MainMenu/WheelRoot/Wheel","Canvas/ExtrasMenu/WheelRoot/Wheel","Canvas/OptionsMenu/WheelRoot/Wheel","Canvas/RaceMenu/Wheel"})
                if(hud.authoredUiData.nodes.Count(n=>n.scene=="menu2" && n.path==path)!=1)
                    throw new InvalidOperationException("missing/duplicate authored wheel seed: "+path);
            phase=Phase.Menu;Time.timeScale=1;settingsOpen=false;
            hud.SetMenuPage(NativeMenuPage.Extras);InitializeMainQuitSource();
            while(hud.MenuTransitionActive)yield return null;
            yield return null;yield return null;
            int generation=hud.Menu2SourceSceneGeneration;var owner=mainQuitSource;
            NativeAudioRuntime.PlayMusic("Menu",true);
            yield return new WaitForSecondsRealtime(.12f);
            var menuBefore=NativeAudioRuntime.CaptureMusicRoomAudioSnapshot();
            musicContinuitySourceIds.Add(menuBefore.menu.sourceInstanceId);musicContinuityClipIds.Add(menuBefore.menu.clipInstanceId);musicContinuitySamples.Add(menuBefore.menu.timeSamples);
            ExecuteFullModeAction(NativeMenuAction.MusicRoom);SynchronizeMainQuitSourceActive();
            var paused=NativeAudioRuntime.CaptureMusicRoomAudioSnapshot();
            musicContinuitySourceIds.Add(paused.menu.sourceInstanceId);musicContinuityClipIds.Add(paused.menu.clipInstanceId);musicContinuitySamples.Add(paused.menu.timeSamples);
            yield return new WaitForSecondsRealtime(.06f);
            var stillPaused=NativeAudioRuntime.CaptureMusicRoomAudioSnapshot();
            musicContinuitySourceIds.Add(stillPaused.menu.sourceInstanceId);musicContinuityClipIds.Add(stillPaused.menu.clipInstanceId);musicContinuitySamples.Add(stillPaused.menu.timeSamples);
            ModeCheck(menuBefore.menu.source && menuBefore.menu.clip && menuBefore.menu.isPlaying
                && paused.menuPaused && stillPaused.menuPaused
                && ReferenceEquals(menuBefore.menu.source,paused.menu.source)
                && ReferenceEquals(menuBefore.menu.clip,paused.menu.clip)
                && paused.menu.sourceInstanceId==stillPaused.menu.sourceInstanceId
                && paused.menu.clipInstanceId==stillPaused.menu.clipInstanceId
                && paused.menu.timeSamples==stillPaused.menu.timeSamples
                && paused.preview.source && paused.preview.sourceInstanceId!=paused.menu.sourceInstanceId,
                "MusicRoom independent preview preserves real paused Menu source clip and exact sample position");
            Time.timeScale=0;int initialCallbacks=musicRoomSourceCancelCallbacks;
            ExecuteFullModeAction(NativeMenuAction.BackToExtras);
            ModeCheck(hud.MenuPage==NativeMenuPage.MusicRoom && musicRoomSourceCancelPending
                && !NativeAudioRuntime.CaptureMusicRoomAudioSnapshot().preview.isPlaying,"MusicRoom production Cancel stops audio immediately and keeps source page pending");
            yield return new WaitForSecondsRealtime(.06f);
            ModeCheck(hud.MenuPage==NativeMenuPage.MusicRoom && musicRoomSourceCancelPending
                && musicRoomSourceCancelCallbacks==initialCallbacks,"MusicRoom source .02 scaled Invoke freezes at timeScale zero");
            ExecuteFullModeAction(NativeMenuAction.BackToExtras);
            Time.timeScale=1;float resumed=Time.time;
            while(musicRoomSourceCancelPending)yield return null;
            ModeCheck(Time.time-resumed>=.019f,"MusicRoom Cancel callback waits at least source .02 scaled seconds within float tolerance");
            ModeCheck(hud.MenuPage==NativeMenuPage.Extras && hud.SelectedMenuIndex==1 && NativeAudioRuntime.CurrentMusic=="Menu",
                "MusicRoom original onCancel restores Extras/MusicRoom focus and same-source resume restores Menu BGM");
            ModeCheck(musicRoomSourceCancelCallbacks==initialCallbacks+1,
                "MusicRoom CancelB invokes once and cancels duplicate scheduled callbacks");
            yield return new WaitForSecondsRealtime(.06f);
            var resumedAudio=NativeAudioRuntime.CaptureMusicRoomAudioSnapshot();
            musicContinuitySourceIds.Add(resumedAudio.menu.sourceInstanceId);musicContinuityClipIds.Add(resumedAudio.menu.clipInstanceId);musicContinuitySamples.Add(resumedAudio.menu.timeSamples);
            ModeCheck(!resumedAudio.menuPaused && resumedAudio.menu.isPlaying
                && ReferenceEquals(paused.menu.source,resumedAudio.menu.source)
                && ReferenceEquals(paused.menu.clip,resumedAudio.menu.clip)
                && paused.menu.sourceInstanceId==resumedAudio.menu.sourceInstanceId
                && paused.menu.clipInstanceId==resumedAudio.menu.clipInstanceId
                && resumedAudio.menu.timeSamples>paused.menu.timeSamples
                && resumedAudio.dspTime>paused.dspTime && !resumedAudio.preview.isPlaying,
                "MusicRoom delayed Cancel resumes same real Menu source clip from preserved sample and advances DSP");
            ModeCheck(musicRoomSourceCancelCallbacks==initialCallbacks+1 && !musicRoomSourceCancelPending,
                "MusicRoom duplicate Invoke stays canceled after return");
            ModeCheck(hud.Menu2SourceSceneGeneration==generation && ReferenceEquals(owner,mainQuitSource)
                && mainQuitMenu2Present,"MusicRoom Cancel return retains the same menu2 generation and quit owner");
        }
        IEnumerator CheckMusicCancelScaledNonrendered()
        {
            musicContinuitySourceIds.Clear();musicContinuityClipIds.Clear();musicContinuitySamples.Clear();
            modeChecks.Clear();modePassed=true;float start=Time.realtimeSinceStartup;
            string failure="";var stack=new Stack<IEnumerator>();stack.Push(MusicCancelScaledCases());
            while(stack.Count>0)
            {
                if(Time.realtimeSinceStartup-start>15){failure="timeout after 15 seconds";break;}
                bool moved=false;object value=null;
                try{moved=stack.Peek().MoveNext();if(moved)value=stack.Peek().Current;}
                catch(Exception e){failure=e.ToString();break;}
                if(!moved)
                {
                    try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception e){failure="cleanup: "+e;break;}
                    continue;
                }
                if(value is IEnumerator nested && !(value is CustomYieldInstruction)){stack.Push(nested);continue;}
                yield return value;
            }
            bool completed=stack.Count==0 && failure.Length==0;
            while(stack.Count>0){try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception e){failure+="\ncleanup: "+e;}}
            int failed=modeChecks.Count(c=>c.StartsWith("FAIL ",StringComparison.Ordinal));
            var report=new MusicCancelScaledReport{completed=completed,runtimeCount=modeChecks.Count,
                failures=failed,engineErrors=errorCount,checks=modeChecks.ToArray(),exception=failure,
                wallSeconds=Time.realtimeSinceStartup-start,ordinaryDSPRequested=Environment.GetCommandLineArgs().Contains("--music-ordinary-audio-output"),menuSourceIds=musicContinuitySourceIds.ToArray(),menuClipIds=musicContinuityClipIds.ToArray(),menuTimeSamples=musicContinuitySamples.ToArray()};
            report.passed=completed && failure.Length==0 && report.runtimeCount==9 && failed==0 && modePassed && errorCount==0;
            string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"music-cancel-scaled-nonrendered.json"),json);
            Debug.Log("MUSIC_CANCEL_SCALED_NONRENDERED_RESULT "+json);Application.Quit(report.passed?0:2);
        }
    }
}
