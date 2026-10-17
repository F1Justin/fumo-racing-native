using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Fumo.NativePlayable
{
    public static partial class NativeInputBindings
    {
        public enum Action { Horizontal, Vertical, Accelerate, Brake, Jump, Use, Ok, Cancel, Navigate, LookBehind, ChangeView, Pause, DeleteProfile }
        static readonly InputActionAsset[] assets=new InputActionAsset[4];
        static readonly string[] selected=new string[4];
        static readonly int[] navigateStartedFrame = { -1, -1, -1, -1 };
        static readonly Vector2[] navigateStartedVector = new Vector2[4];
        static NativeSaveData.InputProfile draft;
        static InputActionRebindingExtensions.RebindingOperation rebinding;
        static string editingId;
        static readonly Dictionary<string,int> partCounts=new Dictionary<string,int>();
        static InputActionAsset bindingDisplayTemplate;
        static InputActionAsset rebindingAsset;
        public static string LastError {get;private set;}="";
        public static bool IsEditing => draft!=null;
        public static bool IsRebinding => rebinding!=null;
        public static event System.Action Changed;
        public static NativeSaveData.InputProfile Draft => draft;
        public static NativeSaveData.InputProfile[] Profiles => NativeSaveStore.Data.inputProfiles;
        public static void Initialize()
        {
            InvalidateSharedMenu(); InitializePrompts();
            InputSystem.onDeviceChange-=DeviceChanged; InputSystem.onDeviceChange+=DeviceChanged;
            for(int player=0;player<4;player++) { if (!ActualLocalOwner(player)) selected[player]=NativeUserSettings.ReadValue("native.inputProfile."+player); Rebuild(player); }
            if(sharedMenuInput)MenuActionAsset();
        }
        static void DeviceChanged(InputDevice device,InputDeviceChange change)
        {
            if(change==InputDeviceChange.Added || change==InputDeviceChange.Removed || change==InputDeviceChange.Disconnected || change==InputDeviceChange.Reconnected)
            {
                NativeTelemetry.Event("input.device.changed", detail: change + ";layout=" + (device == null ? "unknown" : device.layout));
                for(int player=0;player<4;player++) if (!ActualLocalOwner(player)) Rebuild(player);
            }
        }
        public static void Tick() { }
        public static string SelectedProfile(int player=0) => ValidPlayer(player)?selected[player]??"":"";
        public static bool UsingFallback(int player=0) {var p=Find(SelectedProfile(player)); return p==null || CompatibleDevice(p.devicePath,player)==null;}
        public static string ActionId(Action action) {var a=Get(action,0);return a==null?"":a.id.ToString();}
        static NativeSaveData.InputProfile Find(string id) {foreach(var p in Profiles) if(p.profileId==id) return p;return null;}
        static InputDevice CompatibleDevice(string path,int player=0)
        {
            int index=0;foreach(var device in InputSystem.devices)
            {
                if(!device.enabled || !InputControlPath.Matches(path,device))continue;
                if(device is Gamepad && path.StartsWith("<",StringComparison.Ordinal) && index++!=player)continue;
                return device;
            }
            return null;
        }
        static bool ValidPlayer(int player) => player>=0 && player<assets.Length;
        static InputAction Get(Action action,int player)
        {
            if(!ValidPlayer(player)) return null;
            if (player == 0 && sharedMenuInput) return MenuActionAsset().FindAction("Normal/" + action, false);
            if(assets[player]==null) Rebuild(player);
            return assets[player]==null?null:assets[player].FindAction("Normal/"+action,false);
        }
        public static float GetAxis(Action action,int player=0) {var a=Get(action,player);return a==null?0:a.ReadValue<float>();}
        public static Vector2 GetVector(Action action=Action.Navigate,int player=0) {var a=Get(action,player);return a==null?Vector2.zero:a.ReadValue<Vector2>();}
        public static bool GetNavigateStarted(out Vector2 value, int player=0)
        {
            if (player == 0 && sharedMenuInput) { value = menuNavigateVector; return menuNavigateFrame == Time.frameCount; }
            value = ValidPlayer(player) ? navigateStartedVector[player] : Vector2.zero;
            return ValidPlayer(player) && navigateStartedFrame[player] == Time.frameCount;
        }
        public static InputDevice ActualConfirmDevice(int player=0)
        {
            var action=Get(Action.Ok,player);
            if(action!=null && action.WasPressedThisFrame() && action.activeControl!=null)return action.activeControl.device;
            return Keyboard.current!=null && Keyboard.current.enterKey.wasPressedThisFrame ? Keyboard.current : null;
        }
        public static bool GetButtonDown(Action action,int player=0) {var a=Get(action,player);return a!=null && !CharacterProfileReplay(action,player) && a.WasPressedThisFrame();}
        public static bool GetGamepadButtonDown(Action action,int player=0)
        { var a=Get(action,player);return a?.activeControl?.device is Gamepad && GetButtonDown(action,player); }
        public static bool GetButtonHeld(Action action,int player=0) {var a=Get(action,player);return a!=null && a.ReadValue<float>()>0.5f;}
        static void Rebuild(int player, InputDevice forcedDevice = null)
        {
            if (TryRebuildLocalOwner(player, forcedDevice)) return;
            if(rebinding!=null) CancelRebind();
            if(assets[player]!=null) {assets[player].Disable(); UnityEngine.Object.Destroy(assets[player]);assets[player]=null;}
            var resource=NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");
            if(resource==null) {LastError="Missing original Controls resource";return;}
            var asset=InputActionAsset.FromJson(resource.text); EnsureGamepadBindings(asset);assets[player]=asset;
            navigateStartedFrame[player] = -1;
            var navigate = asset.FindAction("Normal/Navigate", false);
            if (navigate != null) navigate.started += context =>
            {
                navigateStartedVector[player] = context.ReadValue<Vector2>();
                navigateStartedFrame[player] = Time.frameCount;
            };
            var profile=Find(selected[player]); InputDevice device=forcedDevice ?? (profile==null?null:CompatibleDevice(profile.devicePath,player));
            if(device!=null) {asset.devices=new[]{device}; if(profile!=null) foreach(var pair in profile.overrides) ApplyOverride(asset,pair.actionId,pair.path,device);}
            else
            {
                var devices=new List<InputDevice>(); if(player==0 && Keyboard.current!=null) devices.Add(Keyboard.current);
                int padIndex=0;foreach(var pad in Gamepad.all)if(pad.enabled && padIndex++==player){devices.Add(pad);break;} asset.devices=devices.ToArray();
            }
            TrackCharacterProfileActions(asset,player);
            asset.FindActionMap("Normal",true).Enable();
        }
        static void EnsureGamepadBindings(InputActionAsset asset)
        {
            if (asset.FindAction("Normal/DeleteProfile", false) == null)
            {
                var delete = asset.FindActionMap("Normal", true).AddAction("DeleteProfile", InputActionType.Button);
                delete.AddBinding("<Keyboard>/space", groups: "KeyboardControls");
                delete.AddBinding("<Gamepad>/buttonNorth", groups: "GamePadControls");
            }
            foreach(var item in new[]{new[]{"Horizontal","<Gamepad>/leftStick/x"},new[]{"Vertical","<Gamepad>/leftStick/y"},new[]{"Navigate","<Gamepad>/leftStick"}})
            {
                var action=asset.FindAction("Normal/"+item[0],false);if(action==null)continue;bool present=false;
                // An analog stick binding starts before the original digital direction threshold.
                foreach(var binding in action.bindings)if(!binding.isComposite && (item[0]=="Navigate" || !binding.isPartOfComposite) && binding.path.StartsWith("<Gamepad>/",StringComparison.Ordinal))present=true;
                if(!present)action.AddBinding(item[1],groups:"GamePadControls");
            }
        }
        static bool ForDraftDevice(string path)
        {
            var device=draft==null?null:CompatibleDevice(draft.devicePath);if(device!=null)return BoundToDevice(path,device);
            return draft!=null && path.StartsWith(draft.devicePath+"/",StringComparison.Ordinal);
        }
        public static int DraftPartCount(Action action)
        {
            if(draft==null)return 1;string key=draft.devicePath+"|"+action;int cached;if(partCounts.TryGetValue(key,out cached))return cached;var resource=NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");if(resource==null)return 1;
            var asset=InputActionAsset.FromJson(resource.text);EnsureGamepadBindings(asset);var input=asset.FindAction("Normal/"+action,false);int count=PartCount(input);UnityEngine.Object.Destroy(asset);partCounts[key]=count;return count;
        }
        public static string DraftBindingDisplay(Action action,int part=-1)
        {
            if(draft==null)return "";
            string path="";
            foreach(var pair in draft.overrides)if(pair.actionId==ActionId(action)){var parts=pair.path.Split('#');int index=Math.Max(0,part);if(index<parts.Length)path=parts[index];}
            if(string.IsNullOrEmpty(path))
            {
                var resource=NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");if(!resource)return "";
                if(!bindingDisplayTemplate){bindingDisplayTemplate=InputActionAsset.FromJson(resource.text);EnsureGamepadBindings(bindingDisplayTemplate);}
                var input=bindingDisplayTemplate.FindAction("Normal/"+action,false);
                if(input!=null)foreach(var binding in input.bindings)
                {
                    if(binding.isComposite || !ForDraftDevice(binding.path))continue;
                    int index=binding.name=="negative"?0:binding.name=="positive"?1:binding.name=="up"?0:binding.name=="down"?1:binding.name=="left"?2:binding.name=="right"?3:-1;
                    if((part<0 && !binding.isPartOfComposite) || (part>=0 && index==part)){path=binding.effectivePath;break;}
                }
            }
            return string.IsNullOrEmpty(path)?"":InputControlPath.ToHumanReadableString(path,out _,out _,InputControlPath.HumanReadableStringOptions.OmitDevice);
        }
        static int PartCount(InputAction input)
        {
            if(input==null)return 1;foreach(var binding in input.bindings)if(!binding.isComposite && !binding.isPartOfComposite && ForDraftDevice(binding.path))return 1;
            int count=0;foreach(var binding in input.bindings)if(binding.isPartOfComposite && ForDraftDevice(binding.path))count++;
            return count==0?(input.name=="Navigate"?4:input.name=="Horizontal" || input.name=="Vertical"?2:1):(input.expectedControlType=="Vector2" || input.expectedControlType=="Dpad")?4:2;
        }
        static bool BoundToDevice(string path,InputDevice device)
        { int slash=path.IndexOf('/'); string layout=slash<0?path:path.Substring(0,slash); return InputControlPath.Matches(layout,device); }
        static bool ApplyOverride(InputActionAsset asset,string actionId,string path,InputDevice device)
        {
            var action=asset.FindAction(actionId,false); if(action==null) return false;
            string[] parts=path.Split('#'); bool any=false;
            bool matching=false;foreach(var binding in action.bindings)if(!binding.isComposite && BoundToDevice(binding.path,device))matching=true;
            if(!matching)
            {
                if(parts.Length==1)action.AddBinding(path);
                else if(parts.Length==2)action.AddCompositeBinding("1DAxis").With("negative",parts[0]).With("positive",parts[1]);
                else if(parts.Length==4)action.AddCompositeBinding("2DVector").With("up",parts[0]).With("down",parts[1]).With("left",parts[2]).With("right",parts[3]);
                else return false;
                return true;
            }
            if(parts.Length==1)
            {
                bool direct=false;foreach(var binding in action.bindings)if(!binding.isComposite && !binding.isPartOfComposite && BoundToDevice(binding.path,device))direct=true;
                if(direct){for(int i=0;i<action.bindings.Count;i++)if(!action.bindings[i].isComposite && BoundToDevice(action.bindings[i].path,device))action.ApplyBindingOverride(i,new InputBinding{overridePath=""});action.AddBinding(path);return true;}
            }
            for(int i=0;i<action.bindings.Count;i++)
            {
                var binding=action.bindings[i]; if(binding.isComposite) continue;
                if(!BoundToDevice(binding.path,device)) continue;
                if(binding.isPartOfComposite)
                {
                    int part=binding.name=="negative"?0:binding.name=="positive"?1:binding.name=="up"?0:binding.name=="down"?1:binding.name=="left"?2:binding.name=="right"?3:-1;
                    if(part>=0 && part<parts.Length && !string.IsNullOrEmpty(parts[part])) {action.ApplyBindingOverride(i,parts[part]);any=true;}
                }
                else if(parts.Length==1 && !string.IsNullOrEmpty(path)) {action.ApplyBindingOverride(i,path);any=true;}
            }
            return any;
        }
        public static bool SelectProfile(string profileId,int player=0)
        {
            string telemetryOperation = NativeTelemetry.Begin("input.profile.select", "slot=" + player);
            bool telemetrySucceeded = false;
            try
            {
                if(!ValidPlayer(player) || (!string.IsNullOrEmpty(profileId) && Find(profileId)==null)) return Fail("Unknown input profile");
                if(!NativeUserSettings.WriteValue("native.inputProfile."+player,profileId??"","string")) return Fail(NativeUserSettings.LastError);
                selected[player]=profileId??"";InvalidateSharedMenu();Rebuild(player);Changed?.Invoke();return telemetrySucceeded = true;
            }
            finally { NativeTelemetry.End(telemetryOperation, "input.profile.select", telemetrySucceeded ? "completed" : "failed", telemetrySucceeded ? null : "profile-select-failed"); }
        }
        public static string CreateProfile(string name,string devicePath)
        {
            int index=0;foreach(var p in Profiles) index=Math.Max(index,p.index+1);
            var candidate=NativeUserSettings.CopyData(); var profiles=new List<NativeSaveData.InputProfile>(candidate.inputProfiles);
            var profile=new NativeSaveData.InputProfile{index=index,profileId="input-"+Guid.NewGuid().ToString("N"),name=name,devicePath=devicePath,overrides=new NativeSaveData.InputOverride[0]}; profiles.Add(profile);candidate.inputProfiles=profiles.ToArray();
            if(!NativeSaveStore.ReplaceData(candidate)) {Fail(NativeSaveStore.LastError);return "";} Changed?.Invoke();return profile.profileId;
        }
        public static bool BeginEdit(string profileId)
        {
            var source=Find(profileId);if(source==null) return Fail("Unknown input profile");CancelEdit();draft=JsonUtility.FromJson<NativeSaveData.InputProfile>(JsonUtility.ToJson(source));editingId=profileId;return true;
        }
        public static bool SetDraftName(string name) {if(draft==null)return Fail("No input edit");draft.name=name;return true;}
        public static bool SetDraftOverride(string actionId,string path)
        {
            Guid parsed;if(draft==null || !Guid.TryParse(actionId,out parsed) || string.IsNullOrEmpty(path))return Fail("Invalid input override");
            var resource=NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");if(resource==null)return Fail("Missing Controls resource");
            var check=InputActionAsset.FromJson(resource.text);EnsureGamepadBindings(check);var action=check.FindAction(actionId,false);bool known=action!=null;int expected=PartCount(action);UnityEngine.Object.Destroy(check);
            if(!known)return Fail("Unknown original action GUID");
            var parts=path.Split('#');
            if(parts.Length!=expected)return Fail("Invalid composite override parts");
            var device=CompatibleDevice(draft.devicePath);
            foreach(var part in parts)if(part.Length>0 && (device!=null?!BoundToDevice(part,device):!part.StartsWith(draft.devicePath+"/",StringComparison.Ordinal)))return Fail("Override device mismatch");
            var entries=new List<NativeSaveData.InputOverride>(draft.overrides);entries.RemoveAll(x=>x.actionId==actionId);entries.Add(new NativeSaveData.InputOverride{actionId=actionId,path=path});draft.overrides=entries.ToArray();return true;
        }
        public static bool ConfirmEdit()
        {
            if(draft==null)return Fail("No input edit"); CancelRebind();var candidate=NativeUserSettings.CopyData();bool found=false;
            for(int i=0;i<candidate.inputProfiles.Length;i++) if(candidate.inputProfiles[i].profileId==editingId) {candidate.inputProfiles[i]=draft;found=true;}
            if(!found)return Fail("Input profile removed");if(!NativeSaveStore.ReplaceData(candidate))return Fail(NativeSaveStore.LastError);
            draft=null;editingId=null;InvalidateSharedMenu();for(int player=0;player<4;player++)Rebuild(player);Changed?.Invoke();return true;
        }
        public static void CancelEdit() {CancelRebind();draft=null;editingId=null;}
        public static bool DeleteProfile(string profileId)
        {
            if(Find(profileId)==null)return Fail("Unknown input profile");var candidate=NativeUserSettings.CopyData();var profiles=new List<NativeSaveData.InputProfile>(candidate.inputProfiles);profiles.RemoveAll(x=>x.profileId==profileId);candidate.inputProfiles=profiles.ToArray();
            for(int player=0;player<4;player++) if(selected[player]==profileId)NativeUserSettings.Put(candidate,"native.inputProfile."+player,"","string");
            if(!NativeSaveStore.ReplaceData(candidate))return Fail(NativeSaveStore.LastError);if(editingId==profileId)CancelEdit();
            InvalidateSharedMenu();
            for(int player=0;player<4;player++){if(selected[player]==profileId)selected[player]="";Rebuild(player);}Changed?.Invoke();return true;
        }
        public static string RebindExpectedControlType {get;private set;}
        public static string LastCapturedPath {get;private set;}
        public static bool BeginRebind(Action action,int compositePart=-1)
        {
            if(draft==null)return Fail("No input edit");var device=CompatibleDevice(draft.devicePath);if(device==null)return Fail("Profile device absent");CancelRebind();
            var resource=NativeContent.Load<TextAsset>("NativeSettings/controls-runtime");if(!resource)return Fail("Missing original Controls resource");
            rebindingAsset=InputActionAsset.FromJson(resource.text);EnsureGamepadBindings(rebindingAsset);
            var input=rebindingAsset.FindAction("Normal/"+action,false);if(input==null){UnityEngine.Object.Destroy(rebindingAsset);rebindingAsset=null;return Fail("Unknown input action");}int bindingIndex=-1;
            for(int i=0;i<input.bindings.Count;i++) {var b=input.bindings[i];if(!b.isComposite && BoundToDevice(b.path,device)){if(compositePart<0 && !b.isPartOfComposite){bindingIndex=i;break;}if(compositePart>=0 && b.isPartOfComposite){int n=b.name=="negative"?0:b.name=="positive"?1:b.name=="up"?0:b.name=="down"?1:b.name=="left"?2:b.name=="right"?3:-1;if(n==compositePart){bindingIndex=i;break;}}}}
            if(bindingIndex<0)
            {
                int first=input.bindings.Count;
                if(compositePart<0){input.AddBinding("",groups:device.layout);bindingIndex=first;}
                else if(action==Action.Navigate){input.AddCompositeBinding("2DVector").With("up","",groups:device.layout).With("down","",groups:device.layout).With("left","",groups:device.layout).With("right","",groups:device.layout);bindingIndex=first+1+compositePart;}
                else {input.AddCompositeBinding("1DAxis").With("negative","",groups:device.layout).With("positive","",groups:device.layout);bindingIndex=first+1+compositePart;}
            }
            input.Disable();string guid=input.id.ToString(); string previousOverride=input.bindings[bindingIndex].overridePath;
            RebindExpectedControlType=compositePart>=0?"Button":input.expectedControlType=="Dpad"?"Vector2":string.IsNullOrEmpty(input.expectedControlType)?"Button":input.expectedControlType;LastCapturedPath="";
            string telemetryOperation = NativeTelemetry.Begin("input.rebind", "action=" + action + ";part=" + compositePart);
            rebinding=input.PerformInteractiveRebinding(bindingIndex).WithControlsHavingToMatchPath(draft.devicePath+"/*").WithExpectedControlType(RebindExpectedControlType).WithCancelingThrough("<Keyboard>/escape").WithTimeout(10);
            rebinding.OnComplete(op=>{string path=input.bindings[bindingIndex].overridePath;LastCapturedPath=path; if(compositePart>=0){string old="";foreach(var pair in draft.overrides)if(pair.actionId==guid)old=pair.path;int count=action==Action.Navigate?4:2;var parts=new string[count];var saved=old.Split('#');for(int i=0;i<count;i++)parts[i]=i<saved.Length?saved[i]:"";parts[compositePart]=path;path=string.Join("#",parts);}bool captured=SetDraftOverride(guid,path);input.ApplyBindingOverride(bindingIndex,new InputBinding{overridePath=previousOverride});FinishRebind(input);NativeTelemetry.End(telemetryOperation,"input.rebind",captured?"completed":"failed",captured?null:"override-rejected");});
            rebinding.OnCancel(op=>{FinishRebind(input);NativeTelemetry.End(telemetryOperation,"input.rebind","cancelled","cancel-or-timeout");});rebinding.Start();return true;
        }
        static void FinishRebind(InputAction action) {var old=rebinding;rebinding=null;old?.Dispose();if(rebindingAsset){UnityEngine.Object.Destroy(rebindingAsset);rebindingAsset=null;}}
        public static void CancelRebind(){if(rebinding!=null)rebinding.Cancel();}
        public static InputActionAsset RaceActionAsset(int player)
        { if (!ValidPlayer(player)) return null; if (!assets[player]) Rebuild(player); return assets[player]; }
        static bool Fail(string error){LastError=error;return false;}
    }
}
