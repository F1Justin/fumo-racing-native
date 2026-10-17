using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed class NativeQuitUiActivationProbe : MonoBehaviour
    {
        public int enables, disables;
        void OnEnable() => enables++;
        void OnDisable() => disables++;
    }
    public sealed partial class NativeRace
    {
#if ENABLE_INPUT_SYSTEM
        [Serializable] sealed class QuitHoldUiFrame
        {
            public int frame;
            public float seconds, fill;
            public bool visible, keyboardEnabled, parentActive, panelActive, actionEnabled;
            public int activeAsset, subscribedAsset, humansPressing;
        }
        [Serializable] sealed class QuitHoldUiClip
        {
            public string route, screenshot;
            public int enables, disables;
            public float maximumFrameGap;
            public List<QuitHoldUiFrame> frames = new List<QuitHoldUiFrame>();
        }
        [Serializable] sealed class QuitHoldUiReport
        {
            public bool passed, physicalInputAccepted = false;
            public string input = "InputSystem Keyboard X events through production Cancel callbacks and rendered authored Images", error;
            public List<string> checks = new List<string>();
            public List<QuitHoldUiClip> clips = new List<QuitHoldUiClip>();
        }
        void QuitHoldRequire(QuitHoldUiReport report, bool value, string name)
        {
            report.checks.Add((value ? "PASS " : "FAIL ") + name);
            if (!value) throw new InvalidOperationException(name);
        }
        IEnumerator CheckQuitHoldUi()
        {
            var report = new QuitHoldUiReport();
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            NativeInputBindings.Initialize(); phase = Phase.Menu; autopilot = false;
            Directory.CreateDirectory(reportDirectory);
            var stack = new Stack<IEnumerator>(); stack.Push(CheckQuitHoldUiCore(report, keyboard));
            while (stack.Count > 0)
            {
                bool next; object value = null;
                try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                catch (Exception failure) { report.error = failure.ToString(); break; }
                if (!next) { stack.Pop(); if (stack.Count == 0) report.passed = true; continue; }
                if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return value;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int i = 0; i < 3; i++) yield return null;
            File.WriteAllText(Path.Combine(reportDirectory, "quit-hold-ui.json"), JsonUtility.ToJson(report, true));
            Debug.Log("QUIT_HOLD_UI_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
        IEnumerator CheckQuitHoldUiCore(QuitHoldUiReport report, Keyboard keyboard)
        {
            yield return null; yield return new WaitForEndOfFrame();
            foreach (int language in new[] { 0, 3 })
            {
                NativeUserSettings.TrySetInt("lang_game", language);
                hud.SetMenuPage(NativeMenuPage.Main);
                yield return WaitForSettingsRender();
                for (int i = 0; i < 15; i++) yield return null;
                yield return ObserveQuitHoldUi(report, keyboard, "menu2", "MainMenu", "main-" + language);
                localSelectionVerification = true; selectedMode = NativeSessionMode.TimeTrials;
                hud.SetMenuPage(NativeMenuPage.Characters); BeginLocalSelection();
                yield return WaitForSettingsRender();
                for (int i = 0; i < 15; i++) yield return null;
                yield return ObserveQuitHoldUi(report, keyboard, "selection", "CharSelection", "character-" + language);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.X));
                float deadline = Time.realtimeSinceStartup + 2;
                while (localSelectionActive && Time.realtimeSinceStartup < deadline) yield return null;
                QuitHoldRequire(report, !localSelectionActive && hud.MenuPage == NativeMenuPage.Main, "character-" + language + "-full-hold-returns-to-main");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                localSelectionVerification = false;
            }
        }
        IEnumerator ObserveQuitHoldUi(QuitHoldUiReport report, Keyboard keyboard, string scene, string page, string route)
        {
            var menus = GameObject.Find("Original authored menus");
            var panel = menus.transform.Find(scene + " authored Canvas/" + page + "/quitting");
            var bar = panel.Find("quitbar").GetComponent<Image>();
            var probe = bar.gameObject.AddComponent<NativeQuitUiActivationProbe>();
            var clip = new QuitHoldUiClip { route = route, screenshot = route + ".png" }; report.clips.Add(clip);
            QuitHoldRequire(report, !panel.gameObject.activeInHierarchy, route + "-panel-hidden-before-hold");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.X)); yield return null;
            float start = Time.realtimeSinceStartup;
            bool captured = false;
            while (Time.realtimeSinceStartup - start < .28f)
            {
                yield return new WaitForEndOfFrame();
                clip.frames.Add(new QuitHoldUiFrame { frame = Time.frameCount, seconds = Time.realtimeSinceStartup,
                    fill = bar.fillAmount, visible = bar.isActiveAndEnabled && bar.canvasRenderer.GetAlpha() > 0,
                    keyboardEnabled = keyboard.enabled, parentActive = hud.MainQuitSourceParentActive,
                    panelActive = mainQuitSource != null && mainQuitSource.PanelActive, humansPressing = mainQuitSource?.HumansPressing ?? -1,
                    activeAsset = NativeInputBindings.ActiveActionAsset(0).GetInstanceID(),
                    subscribedAsset = mainQuitAction?.actionMap.asset.GetInstanceID() ?? 0, actionEnabled = mainQuitAction != null && mainQuitAction.enabled });
                if (!captured && Time.realtimeSinceStartup - start > .14f)
                { ScreenCapture.CaptureScreenshot(Path.Combine(reportDirectory, clip.screenshot)); captured = true; }
            }
            clip.enables = probe.enables; clip.disables = probe.disables;
            bool monotonic = true, visible = true;
            for (int i = 0; i < clip.frames.Count; i++)
            {
                visible &= clip.frames[i].visible;
                if (i == 0) continue;
                monotonic &= clip.frames[i].fill > clip.frames[i - 1].fill;
                clip.maximumFrameGap = Mathf.Max(clip.maximumFrameGap, clip.frames[i].seconds - clip.frames[i - 1].seconds);
            }
            QuitHoldRequire(report, clip.frames.Count >= 4 && visible, route + "-bar-visible-on-every-rendered-frame");
            QuitHoldRequire(report, monotonic, route + "-fill-advances-on-every-rendered-frame");
            QuitHoldRequire(report, clip.enables == 1 && clip.disables == 0, route + "-bar-stays-active-without-rebuilding-page");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return new WaitForEndOfFrame();
            QuitHoldRequire(report, !panel.gameObject.activeInHierarchy, route + "-release-hides-panel");
            QuitHoldRequire(report, scene == "menu2" ? mainQuitSource.HoldingTime == 0 : localQuittingHeld == 0, route + "-release-resets-progress");
            Destroy(probe);
        }
        IEnumerator CheckMainQuitSourceLive(Keyboard keyboard)
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
                yield return new WaitForEndOfFrame();
                ModeCheck(hud.MainQuitSourcePanelMatches(mainQuitSource.PanelActive,mainQuitSource.FillAmount),"quit original panel and Image fill match live production state");
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
            yield return CheckMenu2SourceWheelsLive();
        }
#endif
    }
}
