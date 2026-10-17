using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class LocalSelectionReport
        {
            public bool passed,accepted=false,fourHumanActualAccepted=false,ordinaryAudioAccepted=false;
            public string error,assemblySHA256;
            public string fixtureType="NativeInjectedRenderedLocalSelection";
            public bool renderEvidenceAvailable=true;
            public List<string> pending=new List<string>();
            public string inputScope="NativeInjected device-state events through actual PlayerInputManager/PlayerInput/InputUser/started callbacks; physical four-human acceptance remains open";
            public string audioScope="Actual post-PlayOneShot source/clip boundary with listener0; DSP/audibility remains open";
            public List<string> checks=new List<string>(),audio=new List<string>(),screenshots=new List<string>();
            public NativeAuthoredUi.CharacterNavigationSample[] navigationLoaded;
            public int heldNavigationBefore,heldNavigationExpected,heldNavigationAfter,heldAudioBefore,heldAudioAfter;
            public List<string> profileBindings=new List<string>();
            public List<string> profileReceipts=new List<string>();
            public string profileFixtureScope="NativeInjected actual PIM/InputUser/Use/Navigate/Ok callbacks; no actual Canvas required; physical/render evidence pending";
            public List<LocalPreviewObservation> previews=new List<LocalPreviewObservation>();
        }
        [Serializable] sealed class LocalPreviewObservation
        {
            public int slot,userId,inputUserIndex,cameraId,textureId,pivotId,width,height;
            public string sourceBox,sourceCamera,sourcePivot,sourceRawImage,pixelSHA256;
            public float listenerVolume; public bool muted,verificationPlayback;
        }
        static void LocalRequire(LocalSelectionReport report,bool condition,string name)
        {report.checks.Add((condition?"PASS ":"FAIL ")+name);if(!condition)throw new InvalidOperationException(name);}
        internal IEnumerator CheckLocalSelectionLive(bool nonrendered=false)
        {
            var report=new LocalSelectionReport();var saved=NativeUserSettings.CopyData();
            if(nonrendered){report.fixtureType="NativeInjectedNonrenderedLocalSelection";report.renderEvidenceAvailable=false;report.pending.Add("Screenshots, visible leaves, RT pixels and original/native rendering pair pending");}
            float listener=AudioListener.volume;bool playback=NativeAudioRuntime.UiVerificationPlayback,pointer=hud.SuppressPointerInput;
            var inputs=new List<InputDevice>();var stack=new Stack<IEnumerator>();
            Action<AudioSource,AudioClip> observe=(source,clip)=>{if(source==hud.OriginalSelectionMoveSource)report.audio.Add(source.GetInstanceID()+":"+clip.name+":listener="+AudioListener.volume+":muted="+NativeAudioRuntime.Muted+":playback="+NativeAudioRuntime.UiVerificationPlayback);};
            NativeUserSettings.Changed+=SilenceCharacterProfileVerification;NativeAudioRuntime.UiOneShotPlayed+=observe;NativeAudioRuntime.UiVerificationPlayback=true;AudioListener.volume=0;hud.SuppressPointerInput=true;
            stack.Push(LocalSelectionCore(report,inputs,nonrendered));
            try
            {
                while(stack.Count>0)
                {
                    bool next;object value;
                    try{next=stack.Peek().MoveNext();value=next?stack.Peek().Current:null;}
                    catch(Exception exception){report.error=exception.ToString();break;}
                    if(!next){stack.Pop();if(stack.Count==0)report.passed=true;continue;}
                    if(value is IEnumerator nested && !(value is CustomYieldInstruction)){stack.Push(nested);continue;}
                    yield return value;
                }
            }
            finally
            {
                foreach(var routine in stack)(routine as IDisposable)?.Dispose();
                DisposeLocalSelection();if(localOwners)Destroy(localOwners.gameObject);
                localSelectionActive=false;NativeInputBindings.SetActualLocalOwners(new PlayerInput[0]);
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
                foreach(var input in inputs)if(input.added)InputSystem.RemoveDevice(input);
#endif
                NativeAudioRuntime.UiOneShotPlayed-=observe;NativeAudioRuntime.UiVerificationPlayback=playback;
                NativeSaveStore.ReplaceData(saved);NativeInputBindings.Initialize();NativeUserSettings.Changed-=SilenceCharacterProfileVerification;AudioListener.volume=listener;hud.SuppressPointerInput=pointer;
                Directory.CreateDirectory(reportDirectory);File.WriteAllText(Path.Combine(reportDirectory,"local-selection-report.json"),JsonUtility.ToJson(report,true));
            }
            Application.Quit(report.passed?0:2);
        }
        IEnumerator LocalSelectionCore(LocalSelectionReport report,List<InputDevice> inputs,bool nonrendered)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            report.assemblySHA256=ProfileSha256(File.ReadAllBytes(typeof(NativeRace).Assembly.Location));
            phase=Phase.Menu;verificationRun=true;localSelectionVerification=true;selectedMode=NativeSessionMode.GrandPrix;
            var keyboard=InputSystem.AddDevice<Keyboard>();inputs.Add(keyboard);keyboard.MakeCurrent();
            var pads=Enumerable.Range(0,3).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();inputs.AddRange(pads);
            var sourceControls=InputActionAsset.FromJson(NativeContent.Load<TextAsset>("NativeSettings/controls-runtime").text);
            string accelerateId=sourceControls.FindAction("Normal/Accelerate",true).id.ToString();
            Destroy(sourceControls);
            var fixture=NativeUserSettings.CopyData();fixture.inputProfiles=new[] {
                new NativeSaveData.InputProfile{profileId="local-keyboard",index=0,name="Keyboard",devicePath="<Keyboard>",overrides=new[]{new NativeSaveData.InputOverride{actionId=accelerateId,path="<Keyboard>/a"}}},
                new NativeSaveData.InputProfile{profileId="local-gamepad",index=1,name="Gamepad",devicePath="<Gamepad>",overrides=new NativeSaveData.InputOverride[0]}};
            LocalRequire(report,NativeSaveStore.ReplaceData(fixture),"isolated original profile fixture: "+NativeSaveStore.LastError);
            NativeInputBindings.Initialize();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter));InputSystem.Update();
            hud.SetMenuPage(NativeMenuPage.Characters);BeginLocalSelection();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
            while(hud.MenuTransitionActive)yield return null;
            LocalRequire(report,localSelection.JoinedCount==1&&localOwners.AtSlot(0).user.pairedDevices.Contains(keyboard),"P1 actual PlayerInput/user pair");
            var p1=localOwners.AtSlot(0);uint p1User=p1.user.id;var p1Devices=p1.user.pairedDevices.Select(device=>device.deviceId).ToArray();string p1Scheme=p1.currentControlScheme;
            var template=(InputActionAsset)typeof(NativeLocalSelectionOwners).GetField("template",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(localOwners);
            LocalRequire(report,template&&localOwners.IsTemplate(template),"actual source owner-service template identity");
            string templateJson=template.ToJson();string templateSHA=ProfileSha256(System.Text.Encoding.UTF8.GetBytes(templateJson));
            localOwners.StartedObserved+=(owner,action,deviceId)=>report.profileReceipts.Add("user="+owner.user.id+":device="+deviceId+":action="+action+":profileOpen="+localProfiles[0].Open+":stage="+localSelection.ReadBox(0).Selection);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));InputSystem.Update();yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
            LocalRequire(report,localProfiles[0].Open&&localProfiles[0].CurrentIndex==-1,"real Use.started opens original Default profile popup");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.RightArrow));InputSystem.Update();yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
            LocalRequire(report,localProfiles[0].CurrentIndex==0,"real Navigate.started selects fixture Keyboard pending");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));InputSystem.Update();yield return null;
            for(int frame=0;frame<8;frame++)yield return null;
            LocalRequire(report,!localProfiles[0].Open&&localProfiles[0].SelectedIndex==0&&localSelection.ReadBox(0).Selection==NativeLocalSelectionState.Stage.Character,"real Ok.started applies profile with no held-submit replay");
            LocalRequire(report,p1.user.id==p1User&&p1.user.pairedDevices.Select(device=>device.deviceId).SequenceEqual(p1Devices)&&p1.currentControlScheme==p1Scheme,"profile rebuild preserves exact InputUser devices and scheme");
            LocalRequire(report,template.ToJson()==templateJson&&localOwners.IsTemplate(template),"profile apply preserves source template bytes and identity "+templateSHA);
            foreach(var binding in p1.actions.FindAction("Normal/Accelerate",true).bindings)
                report.profileBindings.Add("action="+accelerateId+":id="+binding.id+":path="+binding.path+":override="+binding.overridePath+":effective="+binding.effectivePath+":groups="+binding.groups);
            LocalRequire(report,p1.actions.FindAction("Normal/Accelerate",true).bindings.Any(binding=>binding.effectivePath=="<Keyboard>/a"),"P1 actual action contains fixture Accelerate override");
            LocalRequire(report,report.profileReceipts.Any(value=>value.Contains(":action=Use:"))&&report.profileReceipts.Any(value=>value.Contains(":action=Navigate:"))&&report.profileReceipts.Any(value=>value.Contains(":action=Ok:")),"actual producer receipts include Use Navigate Ok");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
            for(int slot=1;slot<4;slot++)
            {
                InputSystem.QueueStateEvent(pads[slot-1],new GamepadState().WithButton(GamepadButton.South));InputSystem.Update();yield return null;
                InputSystem.QueueStateEvent(pads[slot-1],new GamepadState());InputSystem.Update();yield return null;
                LocalRequire(report,localSelection.JoinedCount==slot+1&&localOwners.AtSlot(slot).user.pairedDevices.Contains(pads[slot-1]),"P"+(slot+1)+" actual PIM action join/user pair");
                LocalRequire(report,localSelection.ReadBox(slot).Selection==NativeLocalSelectionState.Stage.Character,"join physical press does not replay submit");
            }
            LocalRequire(report,localOwners.Players.Select(owner=>owner.actions.GetInstanceID()).Distinct().Count()==4,"four actual owner actions are independent");
            LocalRequire(report,template.ToJson()==templateJson,"P2-P4 joins preserve exact source template fingerprint");
            LocalRequire(report,localOwners.Players.Skip(1).All(owner=>!owner.actions.bindings.Any(binding=>binding.effectivePath=="<Keyboard>/a")),"P2-P4 source actions do not inherit P1 Accelerate override");
            LocalRequire(report,p1.user.id==p1User&&p1.user.pairedDevices.Select(device=>device.deviceId).SequenceEqual(p1Devices)&&p1.currentControlScheme==p1Scheme,"later joins preserve P1 stable identity exact devices and scheme");
            LocalRequire(report,localOwners.Players.SelectMany(owner=>owner.user.pairedDevices).Select(device=>device.deviceId).Distinct().Count()==localOwners.Players.Sum(owner=>owner.user.pairedDevices.Count),"exclusive actual device ownership");
            yield return null;if(!nonrendered)yield return LocalCapture(report,"characters4");
            if(nonrendered)
            {
                // OnGUI normally runs this source Loaded hierarchy chain before input navigation.
                typeof(NativeRaceHud).GetMethod("ShowOriginalSinglePlayerBoxes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(hud,null);
            }
            var before=localSelection.ReadBox(1);int p1Character=localSelection.ReadBox(0).Character;int audio=report.audio.Count;
            int expectedCharacter=hud.LocalCharacterNeighbor(vehicleRoster,before.Character,1);
            report.navigationLoaded=hud.CharacterNavigationSnapshot();report.heldNavigationBefore=before.Character;report.heldNavigationExpected=expectedCharacter;report.heldAudioBefore=audio;
            LocalRequire(report,expectedCharacter>=0&&expectedCharacter!=before.Character,"source loaded P2 right neighbor exists before held input");
            if(Environment.GetCommandLineArgs().Contains("--stick-regression-check"))
            {
                LocalRequire(report,NativeInputBindings.SelectProfile("local-gamepad",1),"P2 gamepad profile uses rebuilt production actions");
                foreach(float amount in new[]{.1f,.25f})
                {
                    InputSystem.QueueStateEvent(pads[0],new GamepadState{leftStick=new Vector2(amount,0)});InputSystem.Update();yield return null;
                    LocalRequire(report,localSelection.ReadBox(1).Character==before.Character,"subthreshold stick does not consume source direction "+amount);
                }
                InputSystem.QueueStateEvent(pads[0],new GamepadState{leftStick=new Vector2(.6f,0)});InputSystem.Update();yield return null;
                LocalRequire(report,localSelection.ReadBox(1).Character==expectedCharacter,"gradual stick reaches source threshold after profile rebuild");
                if(!nonrendered)yield return LocalCapture(report,"stick-threshold-selection");
            }
            InputSystem.QueueStateEvent(pads[0],new GamepadState{leftStick=Vector2.right});InputSystem.Update();yield return null;
            for(int frame=0;frame<8;frame++)yield return null;
            report.heldNavigationAfter=localSelection.ReadBox(1).Character;report.heldAudioAfter=report.audio.Count;
            LocalRequire(report,report.heldNavigationAfter==expectedCharacter&&report.audio.Count==audio+1,"Navigate.started moves owner once during hold");
            LocalRequire(report,localSelection.ReadBox(0).Character==p1Character,"P1 state retains its independent owner selection");
            InputSystem.QueueStateEvent(pads[0],new GamepadState());InputSystem.Update();yield return null;
            for(int slot=0;slot<4;slot++)yield return LocalPress(keyboard,pads,slot,false);
            LocalRequire(report,Enumerable.Range(0,4).All(slot=>localSelection.ReadBox(slot).Selection==NativeLocalSelectionState.Stage.Vehicle),"four vehicle stages");
            if(!nonrendered)yield return LocalCapture(report,"vehicles4");
            for(int slot=0;slot<4;slot++)yield return LocalPress(keyboard,pads,slot,false);
            LocalRequire(report,Enumerable.Range(0,4).All(slot=>localSelection.ReadBox(slot).Selection==NativeLocalSelectionState.Stage.Skin),"four skin stages");
            if(!nonrendered)yield return LocalCapture(report,"skins4");
            for(int slot=0;slot<3;slot++)yield return LocalPress(keyboard,pads,slot,false);
            LocalRequire(report,localSelectionActive&&!localSelection.AllReady,"three ready wait for fourth owner");
            yield return LocalPress(keyboard,pads,3,false);yield return null;
            LocalRequire(report,!localSelectionActive&&hud.MenuPage==NativeMenuPage.Cups,"all-ready source consumer once returns exact next module");
            var config=BuildModeConfig();LocalRequire(report,config.roster.Count(seat=>seat.isHuman)==4,"mode roster contains four committed humans");
            LocalRequire(report,NativeLocalRosterOrder.Read(config).SequenceEqual(new[]{4,5,6,7,3,2,1,0}),"GP source array reverses humans at tail");
            EnsureSession();var original=NativeGameSession.Current.sessionId;
            LocalRequire(report,LocalSessionRosterMatches(NativeGameSession.Current.config),"session matches actual owner/choice roster");
            if(nonrendered)
            {
                Begin();yield return null;
                LocalRequire(report,Enumerable.Range(0,4).All(slot=>SourceActualLocalOwner(slot)!=null),"production Begin retains four stable actual race owners");
            }
            var player=localOwners.AtSlot(1);Destroy(player.gameObject);yield return null;yield return null;
            LocalRequire(report,localOwners.Players.Length==3,"real PlayerInput destruction routes actual Left");
            LocalRequire(report,!LocalSessionRosterMatches(config),"left owner invalidates prior session roster");
            phase=Phase.Menu;OpenModeCharacters();
            LocalRequire(report,localSelectionActive&&localSelection.JoinedCount==3,"production selection reentry retains three actual owners after Left");
            var survivor=localOwners.AtSlot(0);uint survivorId=survivor.user.id;
            int[] survivorDevices=survivor.user.pairedDevices.Select(device=>device.deviceId).ToArray();
            var choice=localSelection.ReadBox(0);int survivorCharacter=choice.Character,survivorVehicle=choice.Vehicle,survivorSkin=choice.Skin;
            var lostOwners=localOwners.Players.Skip(1).ToDictionary(owner=>owner,owner=>owner.user.id);
            var lostIds=lostOwners.Values.ToArray();var leftIds=new List<uint>();
            Action<PlayerInput> observeLeft=owner=>{if(lostOwners.TryGetValue(owner,out uint stableId))leftIds.Add(stableId);};localOwners.Left+=observeLeft;
            int lossErrors=errorCount;
            try
            {
                InputSystem.RemoveDevice(pads[1]);InputSystem.RemoveDevice(pads[2]);
                float lossDeadline=Time.realtimeSinceStartup+3;
                while((leftIds.Count<2||localSelection.JoinedCount!=1)&&Time.realtimeSinceStartup<lossDeadline)yield return null;
                yield return null;yield return null;
                LocalRequire(report,leftIds.Count==2&&leftIds.OrderBy(id=>id).SequenceEqual(lostIds.OrderBy(id=>id)),"two same-frame actual device losses retain both real Left callbacks");
                LocalRequire(report,localOwners.Players.Length==1&&localSelection.JoinedCount==1&&localSelection.ReadBox(0).InputUserId==checked((int)survivorId),"live PlayerInput identity prunes all missing boxes before exact order guard");
                LocalRequire(report,survivor.user.valid&&survivor.user.id==survivorId&&survivor.user.pairedDevices.Select(device=>device.deviceId).SequenceEqual(survivorDevices)&&localSelection.ReadBox(0).Character==survivorCharacter&&localSelection.ReadBox(0).Vehicle==survivorVehicle&&localSelection.ReadBox(0).Skin==survivorSkin,"same-frame losses preserve surviving owner devices and saved selections");
                LocalRequire(report,errorCount==lossErrors,"same-frame device-loss callbacks produce zero scoped engine errors");
            }
            finally{localOwners.Left-=observeLeft;}


#else
            throw new InvalidOperationException("NativeInjected local selection diagnostics require the Player InputSystem build");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
#endif
        }
        internal IEnumerator PrepareInjectedLocalHumansForRace(int count,List<InputDevice> inputs, NativeSessionMode mode = NativeSessionMode.GrandPrix)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            if(count<1||count>4)throw new ArgumentOutOfRangeException(nameof(count));
            if(NativeLocalSelectionOwners.Instance&&NativeLocalSelectionOwners.Instance.Players.Length>0)throw new InvalidOperationException("Injected diagnostic requires an isolated owner service");
            verificationRun=true;localSelectionVerification=true;phase=Phase.Menu;selectedMode=mode;
            var keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();inputs.Add(keyboard);
            var pads=Enumerable.Range(0,count-1).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();inputs.AddRange(pads);
            NativeInputBindings.Initialize();NativeInputBindings.SetSharedMenuInput(true);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Enter));InputSystem.Update();
            hud.SetMenuPage(NativeMenuPage.Characters);BeginLocalSelection();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
            while(hud.MenuTransitionActive)yield return null;
            for(int slot=1;slot<count;slot++)
            {
                InputSystem.QueueStateEvent(pads[slot-1],new GamepadState().WithButton(GamepadButton.South));InputSystem.Update();yield return null;
                InputSystem.QueueStateEvent(pads[slot-1],new GamepadState());InputSystem.Update();yield return null;
            }
            if(localOwners.Players.Length!=count)throw new InvalidOperationException("Actual PIM diagnostic join count differs");
            for(int stage=0;stage<3;stage++)for(int slot=0;slot<count;slot++)yield return LocalPress(keyboard,pads,slot,false);
            if(localSelectionActive||!localSelection.AllReady)throw new InvalidOperationException("Actual diagnostic selection/ready consumer did not complete");
            Debug.Log("NATIVE_LOCAL_RACE_SETUP scope=NativeInjected device-state through actual PIM/User/menu callbacks; physicalHumanAccepted=false count="+count);
            Begin();

#else
            throw new InvalidOperationException("NativeInjected local selection diagnostics require the Player InputSystem build");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
#endif
        }
        IEnumerator LocalPress(Keyboard keyboard,Gamepad[] pads,int slot,bool cancel)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            if(slot==0)InputSystem.QueueStateEvent(keyboard,new KeyboardState(cancel?Key.X:Key.Z));
            else InputSystem.QueueStateEvent(pads[slot-1],new GamepadState().WithButton(cancel?GamepadButton.East:GamepadButton.South));
            InputSystem.Update();yield return null;
            if(slot==0)InputSystem.QueueStateEvent(keyboard,new KeyboardState());else InputSystem.QueueStateEvent(pads[slot-1],new GamepadState());
            InputSystem.Update();yield return null;

#else
            throw new InvalidOperationException("NativeInjected local selection diagnostics require the Player InputSystem build");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
#endif
        }
        IEnumerator LocalCapture(LocalSelectionReport report,string name)
        {
            yield return new WaitForEndOfFrame();string file=Path.Combine(reportDirectory,name+".png");Directory.CreateDirectory(reportDirectory);ScreenCapture.CaptureScreenshot(file);report.screenshots.Add(file);
            for(int slot=0;slot<4;slot++)
            {
                var preview=localPreviews[slot];var owner=localOwners.AtSlot(slot);var row=selectionPreviewDefinition.players.Single(value=>value.humanIndex==slot);
                LocalRequire(report,preview!=null&&preview.Camera.targetTexture==preview.Texture&&preview.Texture.IsCreated(),"P"+(slot+1)+" real camera/RT binding");
                var previous=RenderTexture.active;var pixels=new Texture2D(preview.Texture.width,preview.Texture.height,TextureFormat.RGBA32,false);
                try{RenderTexture.active=preview.Texture;pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0);pixels.Apply();var png=pixels.EncodeToPNG();File.WriteAllBytes(Path.Combine(reportDirectory,name+"-P"+(slot+1)+"-RT.png"),png);
                    report.previews.Add(new LocalPreviewObservation{slot=slot,userId=checked((int)owner.user.id),inputUserIndex=owner.user.index,cameraId=preview.Camera.GetInstanceID(),textureId=preview.Texture.GetInstanceID(),pivotId=preview.Pivot.GetInstanceID(),width=pixels.width,height=pixels.height,sourceBox=row.sourcePlayerBoxIdentity,sourceCamera=row.sourceCameraIdentity,sourcePivot=row.sourcePivotIdentity,sourceRawImage=row.sourceRawImageIdentity,pixelSHA256=ProfileSha256(png),listenerVolume=AudioListener.volume,muted=NativeAudioRuntime.Muted,verificationPlayback=NativeAudioRuntime.UiVerificationPlayback});}
                finally{RenderTexture.active=previous;Destroy(pixels);}
            }
            LocalRequire(report,localPreviews.Select(preview=>preview.Texture.GetInstanceID()).Distinct().Count()==4,"four independently cloned render textures");
        }
    }
}
