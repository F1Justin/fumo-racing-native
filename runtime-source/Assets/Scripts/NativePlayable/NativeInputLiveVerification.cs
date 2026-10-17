using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public static class NativeInputLiveVerification
    {
#if ENABLE_INPUT_SYSTEM
        [Serializable] public sealed class Result {public bool passed;public string[] checks;public string error;public float timeoutSeconds;public bool keyboardRemoved,gamepadRemoved;public bool rebindActive;public string expectedControlType,lastCapturedPath,draftSnapshot;}
        public static Result LastResult {get;private set;}
        public static IEnumerator Run(NativeRace race,string directory)
        {
            LastResult=new Result();var checks=new List<string>();string previousPath=NativeSaveStore.FilePath;bool previousEnabled=race!=null && race.enabled;
            Keyboard keyboard=null;Gamepad gamepad=null,secondGamepad=null;var disabled=new List<InputDevice>();var previousBackground=InputSystem.settings.backgroundBehavior;
            bool previousSharedMenu = NativeInputBindings.SharedMenuInputActive;
            if(race!=null)race.enabled=false;
            NativeInputBindings.SetSharedMenuInput(false);
            try
            {
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                foreach(var device in InputSystem.devices)if(device.enabled && (device is Keyboard || device is Gamepad))disabled.Add(device);
                Require(NativeSaveStore.Initialize(new[]{"--save-directory",directory}),"isolated-store",checks);
                var clean=NativeUserSettings.CopyData();clean.inputProfiles=new NativeSaveData.InputProfile[0];var settings=new List<NativeSaveData.Setting>(clean.settings);settings.RemoveAll(x=>x.key.StartsWith("native.inputProfile.",StringComparison.Ordinal));clean.settings=settings.ToArray();Require(NativeSaveStore.ReplaceData(clean),"clean-input-store",checks);
                foreach(var device in disabled)InputSystem.DisableDevice(device);
                keyboard=InputSystem.AddDevice<Keyboard>("native-settings-test-keyboard");gamepad=InputSystem.AddDevice<Gamepad>("native-settings-test-gamepad");NativeInputBindings.Initialize();
                string keyboardId=NativeInputBindings.CreateProfile("Verification keyboard","<Keyboard>");Require(keyboardId.Length>0 && NativeInputBindings.SelectProfile(keyboardId),"keyboard-profile",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftArrow,Key.UpArrow,Key.Z));yield return null;
                Require(NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal)<-.9f && NativeInputBindings.GetAxis(NativeInputBindings.Action.Vertical)>.9f && NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate),"keyboard-queued-state",checks);
                Require(NativeInputBindings.GetVector().y>.6f && NativeInputBindings.GetVector().x<-.6f,"keyboard-menu-navigation",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Require(!NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate),"keyboard-release",checks);
                string accelerate=NativeInputBindings.ActionId(NativeInputBindings.Action.Accelerate);
                Require(NativeInputBindings.BeginEdit(keyboardId) && NativeInputBindings.SetDraftOverride(accelerate,"<Keyboard>/a") && NativeInputBindings.ConfirmEdit(),"keyboard-confirm-rebind",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return null;
                Require(NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Accelerate) && NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate),"new-key-down-and-held",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Require(NativeInputBindings.BeginEdit(keyboardId) && NativeInputBindings.BeginRebind(NativeInputBindings.Action.Brake),"interactive-keyboard-start",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.B));yield return null;yield return new WaitForSecondsRealtime(0.2f);
                Require(!NativeInputBindings.IsRebinding && NativeInputBindings.Draft.overrides.Length>=2,"interactive-keyboard-capture",checks);NativeInputBindings.CancelEdit();
                Require(NativeInputBindings.Profiles[0].overrides.Length==1,"interactive-edit-cancel-preserves",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Require(NativeInputBindings.BeginEdit(keyboardId) && NativeInputBindings.BeginRebind(NativeInputBindings.Action.Brake),"escape-rebind-start",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;
                Require(!NativeInputBindings.IsRebinding && NativeInputBindings.Draft.overrides.Length==1,"escape-cancels",checks);NativeInputBindings.CancelEdit();InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Require(NativeInputBindings.BeginEdit(keyboardId) && NativeInputBindings.BeginRebind(NativeInputBindings.Action.Brake),"timeout-rebind-start",checks);
                float started=Time.realtimeSinceStartup;yield return new WaitForSecondsRealtime(10.25f);LastResult.timeoutSeconds=Time.realtimeSinceStartup-started;
                Require(!NativeInputBindings.IsRebinding && NativeInputBindings.Draft.overrides.Length==1,"ten-second-timeout-preserves",checks);NativeInputBindings.CancelEdit();
                string gamepadId=NativeInputBindings.CreateProfile("Verification gamepad","<Gamepad>");Require(gamepadId.Length>0 && NativeInputBindings.SelectProfile(gamepadId),"gamepad-profile",checks);
                InputSystem.QueueStateEvent(gamepad,new GamepadState{leftStick=new Vector2(.9f,.7f),rightTrigger=1}.WithButton(GamepadButton.South));yield return null;
                Require(NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal)>.5f && NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate) && NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok),"gamepad-queued-stick-trigger-button",checks);
                InputSystem.QueueStateEvent(gamepad,new GamepadState());yield return null;
                Require(NativeInputBindings.BeginEdit(gamepadId) && NativeInputBindings.DraftPartCount(NativeInputBindings.Action.Horizontal)==1 && NativeInputBindings.DraftPartCount(NativeInputBindings.Action.Navigate)==1,"gamepad-axis-vector-shape",checks);
                Require(NativeInputBindings.BeginRebind(NativeInputBindings.Action.Horizontal),"gamepad-axis-rebind-start",checks);InputSystem.QueueStateEvent(gamepad,new GamepadState{rightStick=new Vector2(.9f,0)});yield return null;yield return new WaitForSecondsRealtime(.2f);
                Require(!NativeInputBindings.IsRebinding && NativeInputBindings.ConfirmEdit(),"gamepad-axis-rebind-confirm",checks);InputSystem.QueueStateEvent(gamepad,new GamepadState());yield return null;InputSystem.QueueStateEvent(gamepad,new GamepadState{rightStick=new Vector2(.9f,0)});yield return null;Require(NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal)>.5f,"gamepad-rebound-rightstick-axis",checks);
                InputSystem.QueueStateEvent(gamepad,new GamepadState());yield return null;Require(NativeInputBindings.BeginEdit(gamepadId) && NativeInputBindings.BeginRebind(NativeInputBindings.Action.Navigate),"gamepad-vector-rebind-start",checks);InputSystem.QueueStateEvent(gamepad,new GamepadState{rightStick=new Vector2(.8f,.7f)});yield return null;yield return new WaitForSecondsRealtime(.2f);
                Require(!NativeInputBindings.IsRebinding && NativeInputBindings.ConfirmEdit(),"gamepad-vector-rebind-confirm",checks);InputSystem.QueueStateEvent(gamepad,new GamepadState());yield return null;InputSystem.QueueStateEvent(gamepad,new GamepadState{rightStick=new Vector2(.8f,.7f)});yield return null;Require(NativeInputBindings.GetVector().x>.5f && NativeInputBindings.GetVector().y>.5f,"gamepad-rebound-rightstick-vector",checks);
                secondGamepad=InputSystem.AddDevice<Gamepad>("native-settings-test-gamepad-2");
                Require(NativeInputBindings.SelectProfile(gamepadId,0) && NativeInputBindings.SelectProfile(gamepadId,1),"two-gamepad-profile-selection",checks);
                InputSystem.QueueStateEvent(gamepad,new GamepadState{rightStick=new Vector2(.9f,.6f),rightTrigger=1});InputSystem.QueueStateEvent(secondGamepad,new GamepadState{rightStick=new Vector2(-.9f,-.6f)});yield return null;
                Require(NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal,0)>.5f && NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal,1)<-.5f && NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate,0) && !NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate,1),"two-gamepad-profile-device-isolation",checks);
                InputSystem.RemoveDevice(secondGamepad);secondGamepad=null;yield return null;
                Require(NativeInputBindings.UsingFallback(1) && Mathf.Abs(NativeInputBindings.GetAxis(NativeInputBindings.Action.Horizontal,1))<.001f && NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate,0),"second-gamepad-disconnect-isolated-fallback",checks);
                InputSystem.RemoveDevice(gamepad);LastResult.gamepadRemoved=true;gamepad=null;yield return null;
                Require(NativeInputBindings.UsingFallback(),"disconnected-device-default-fallback",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));yield return null;
                Require(NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate),"fallback-keyboard-live",checks);
                NativeSaveStore.ResetForVerification();Require(NativeSaveStore.Initialize(new[]{"--save-directory",directory}),"restart-store",checks);NativeInputBindings.Initialize();Require(NativeInputBindings.Profiles.Length==2 && NativeInputBindings.Profiles[0].overrides[0].path=="<Keyboard>/a","restart-binding-persisted",checks);
                Require(race!=null && race.hud!=null && race.racers.Length>0,"race-consumer-present",checks);
                Require(NativeInputBindings.SelectProfile(keyboardId),"race-keyboard-profile",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                race.enabled=true;race.ResetInputVerificationMenu();yield return null;
                Require(race.hud.MenuPage==NativeMenuPage.Main,"live-main-menu",checks);
                int initialIndex=race.hud.SelectedMenuIndex;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.DownArrow));yield return null;
                Require(race.hud.SelectedMenuIndex!=initialIndex,"live-menu-profile-navigation",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.UpArrow));yield return null;
                Require(race.hud.SelectedMenuIndex==initialIndex,"live-menu-navigation-return",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));yield return null;
                Require(race.hud.MenuPage!=NativeMenuPage.Main,"live-menu-profile-confirm",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                race.EnsureInputVerificationOwner(keyboard);
                race.BeginInputVerificationRace();
                float deadline=Time.realtimeSinceStartup+8;
                while(race.DiagnosticPhase==1 && Time.realtimeSinceStartup<deadline)yield return null;
                Require(race.DiagnosticPhase==2,"live-race-countdown",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A,Key.LeftArrow));yield return null;
                var motor=race.racers[0];
                Require(Mathf.Abs(motor.throttle-.5f)<.001f && Mathf.Abs(motor.steering+1)<.001f && !motor.braking,"live-race-profile-throttle-steering",checks);
                yield return new WaitForSecondsRealtime(.25f);
                Require(motor.Speed>0,"live-race-profile-movement",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));yield return null;
                Require(Mathf.Abs(motor.throttle)<.001f,"live-race-old-accelerate-inactive",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Require(NativeUserSettings.TrySetInt("speedometerEnabled",0),"live-speedometer-save-off",checks);yield return new WaitForEndOfFrame();
                Require(!VisibleText("KM/H"),"live-speedometer-hidden",checks);
                Require(NativeUserSettings.TrySetInt("speedometerEnabled",1),"live-speedometer-save-on",checks);yield return new WaitForEndOfFrame();
                Require(VisibleText("KM/H"),"live-speedometer-visible",checks);
                deadline=Time.realtimeSinceStartup+2;while(!motor.Grounded && Time.realtimeSinceStartup<deadline)yield return null;
                Require(motor.Grounded && NativeUserSettings.TrySetInt("alternativeJumping",0),"live-altjump-grounded-off",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.UpArrow,Key.X));
                bool jumped=false;deadline=Time.realtimeSinceStartup+.15f;
                do {yield return null;jumped|=motor.JumpedThisStep || motor.VerticalSpeed>1;}while(Time.realtimeSinceStartup<deadline);
                Require(!jumped,"live-altjump-disabled-brake-up",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Require(NativeUserSettings.TrySetInt("alternativeJumping",1),"live-altjump-save-on",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.UpArrow,Key.X));jumped=false;deadline=Time.realtimeSinceStartup+.15f;
                do {yield return null;jumped|=motor.JumpedThisStep || motor.VerticalSpeed>1;}while(Time.realtimeSinceStartup<deadline);
                Require(jumped,"live-altjump-enabled-brake-up",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                race.ResetInputVerificationMenu();
                Require(race.hud.MenuPage==NativeMenuPage.Main,"live-race-reset-menu",checks);
                Require(NativeInputBindings.BeginEdit(keyboardId) && NativeInputBindings.SetDraftName("Cancelled name"),"live-profile-rename-draft",checks);NativeInputBindings.CancelEdit();
                Require(NativeInputBindings.Profiles[0].name=="Verification keyboard","live-profile-rename-cancel",checks);
                Require(NativeInputBindings.BeginEdit(keyboardId) && NativeInputBindings.SetDraftName("Confirmed keyboard") && NativeInputBindings.ConfirmEdit(),"live-profile-rename-confirm",checks);
                Require(NativeInputBindings.DeleteProfile(keyboardId) && NativeInputBindings.SelectedProfile()=="" && NativeInputBindings.UsingFallback(),"live-delete-selected-default",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));yield return null;
                Require(NativeInputBindings.GetButtonHeld(NativeInputBindings.Action.Accelerate),"live-delete-default-key-restored",checks);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                race.ResetInputVerificationMenu();
                LastResult.passed=true;
            }
            finally
            {
                NativeInputBindings.CancelEdit();if(secondGamepad!=null)InputSystem.RemoveDevice(secondGamepad);if(gamepad!=null){InputSystem.RemoveDevice(gamepad);LastResult.gamepadRemoved=true;}if(keyboard!=null){InputSystem.RemoveDevice(keyboard);LastResult.keyboardRemoved=true;}
                foreach(var device in disabled)if(device.added)InputSystem.EnableDevice(device);
                InputSystem.settings.backgroundBehavior=previousBackground;
                if(!string.IsNullOrEmpty(previousPath)){NativeSaveStore.ResetForVerification();NativeSaveStore.Initialize(new[]{"--save-directory",Path.GetDirectoryName(previousPath)});NativeInputBindings.Initialize();}
                NativeInputBindings.SetSharedMenuInput(previousSharedMenu);
                if(race!=null)race.enabled=previousEnabled;LastResult.checks=checks.ToArray();Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"input-live-verification.json"),JsonUtility.ToJson(LastResult,true));
            }
        }
        static bool VisibleText(string text)
        {
            foreach(var label in UnityEngine.Object.FindObjectsOfType<TMP_Text>())if(label.gameObject.activeInHierarchy && label.text==text)return true;
            return false;
        }
        static void Require(bool value,string name,List<string> checks)
        {
            if(!value){LastResult.rebindActive=NativeInputBindings.IsRebinding;LastResult.expectedControlType=NativeInputBindings.RebindExpectedControlType;LastResult.lastCapturedPath=NativeInputBindings.LastCapturedPath;LastResult.draftSnapshot=NativeInputBindings.Draft==null?"":JsonUtility.ToJson(NativeInputBindings.Draft);LastResult.error=name+": "+NativeInputBindings.LastError+NativeSaveStore.LastError;throw new InvalidOperationException(LastResult.error);}checks.Add(name);
        }
#else
        [Serializable] public sealed class Result {public bool passed;public string[] checks=new string[0];public string error;}
        public static Result LastResult {get;private set;}
        public static IEnumerator Run(NativeRace race,string directory)
        {LastResult=new Result{passed=false,error="Input live verification requires the real InputSystem package"};throw new InvalidOperationException(LastResult.error);}
#endif
    }
}
