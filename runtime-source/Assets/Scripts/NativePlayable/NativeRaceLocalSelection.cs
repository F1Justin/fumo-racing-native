using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        NativeLocalSelectionOwners localOwners;
        NativeLocalSelectionState localSelection;
        readonly Dictionary<PlayerInput,int> localOwnerIds = new Dictionary<PlayerInput,int>();
        NativeCharacterProfileState[] localProfiles = new NativeCharacterProfileState[4];
        bool localSelectionActive, localSelectionVerification, localSelectionLoaded, localSelectionFirstLoad;
        int localQuittingPresses;
        float localQuittingHeld;
        bool LocalSelectionEnabled => !verificationRun || localSelectionVerification;
        int localSelectionOpenedFrame = -1;
        bool BeginLocalSelection()
        {
            if (!LocalSelectionEnabled) return false;
            var initialDevice = NativeInputBindings.AppliedProfileDevice();
            NativeInputBindings.SetSharedMenuInput(false);
            if (!localOwners)
            {
                localOwners = NativeLocalSelectionOwners.Create();
                localOwners.Joined += LocalOwnerJoined; localOwners.Left += LocalOwnerLeft; localOwners.ActionTriggered += LocalOwnerAction;
            }
            hud.LocalSelectionPointerMove-=LocalPointerMove;hud.LocalSelectionPointerMove+=LocalPointerMove;
            localOwners.MarkDiagnosticOwner(false);
            localSelection = new NativeLocalSelectionState((id,devices) => localOwners.Players.Any(player => checked((int)player.user.id)==id && player.user.pairedDevices.Select(device=>device.deviceId).OrderBy(value=>value).SequenceEqual(devices.OrderBy(value=>value))),
                character => selectableCharacters.Contains(character), character => vehicleRoster.VehicleCount, vehicleRoster.SkinCountAt,
                (character,direction) => hud.LocalCharacterNeighbor(vehicleRoster,character,direction));
            localSelectionFirstLoad=!localSelectionLoaded; localSelectionLoaded=true;
            localSelection.Loaded(selectedMode == NativeSessionMode.TimeTrials, localSelectionFirstLoad);
            localSelection.Changed += LocalSelectionChanged; localSelection.Arrow += hud.PulseLocalSelectionArrow;
            localSelection.SoundRequested += (slot,sound) => hud.PlayLocalSelectionSound(sound);
            localSelectionActive = true; localSelectionOpenedFrame = Time.frameCount; localQuittingPresses = 0; localQuittingHeld = 0;
            foreach (var player in localOwners.Players.Take(localSelection.MaxHumans)) AttachLocalOwner(player,false);
            SynchronizeLocalOwners();
            var device = initialDevice;
            if (device == null && Keyboard.current != null && Keyboard.current.added && Keyboard.current.enabled) device = Keyboard.current;
            if (!localOwners.Begin(selectedMode == NativeSessionMode.TimeTrials,device))
            { localSelectionActive = false; localOwners.End(); throw new InvalidOperationException("Local selection requires an actual initial device within the source mode limit"); }
            SynchronizeLocalOwners(); return true;
        }
        void LocalOwnerJoined(PlayerInput player) => AttachLocalOwner(player,true);
        void AttachLocalOwner(PlayerInput player,bool synchronize)
        {
            if (!localSelectionActive || !player.user.valid || localSelection.JoinedOwner(checked((int)player.user.id))>=0) return;
            int slot = localSelection.JoinedCount;
            var retained = localOwners.SavedChoice(player);
            int[] sourceStarts = {vehicleRoster.CharacterIndexOf("reimu"),vehicleRoster.CharacterIndexOf("marisa"),vehicleRoster.CharacterIndexOf("rumia"),vehicleRoster.CharacterIndexOf("cirno")};
            int character = retained == null || localSelectionFirstLoad ? sourceStarts[slot] : retained.Character;
            if (!localSelection.AttachLoadedOwner(checked((int)player.user.id),player.user.pairedDevices.Select(device=>device.deviceId).ToArray(),character,retained?.Vehicle??0,localSelectionFirstLoad?0:retained?.Skin??0))
                throw new InvalidOperationException("Actual joined owner failed original local mode/ownership guards");
            localOwnerIds[player] = checked((int)player.user.id);
            if(synchronize)SynchronizeLocalOwners();
        }
        void LocalOwnerLeft(PlayerInput player)
        {
            if (localSelection != null && localOwnerIds.TryGetValue(player,out int id)) localSelection.ActualPlayerLeft(id);
            localOwnerIds.Remove(player);
            if (localSelectionActive)
            {
                SynchronizeLocalOwners();
                if (localSelection.JoinedCount == 0)
                {
                    localSelectionActive = false;
                    localSelection.Ended(); localOwners.End();
                    hud.SetMenuPage(NativeMenuPage.Main);
                    heldMenuDirection = 0;
                }
            }
            else if(raceConfig!=null && phase!=Phase.Menu) {SynchronizeRaceLocalOwners();if(sourceHumanInput)sourceHumanInput.SynchronizeUsers();}
        }
        void SynchronizeLocalOwners()
        {
            var players = localOwners.Players;
            if (localSelectionActive)
            {
                var order=players.Take(localSelection.MaxHumans).Select(player=>checked((int)player.user.id)).ToArray();
                // Several device losses can invalidate owners before the first deferred Left callback.
                for(int slot=0;slot<4;slot++)
                {
                    var box=localSelection.ReadBox(slot);
                    if(box!=null&&!order.Contains(box.InputUserId))localSelection.ActualPlayerLeft(box.InputUserId);
                }
                localSelection.ApplyActualPlayerOrder(order);
            }
            NativeInputBindings.SetActualLocalOwners(players);
            localProfiles = new NativeCharacterProfileState[4];
            for (int slot=0;slot<players.Length;slot++)
            {
                localProfiles[slot]=characterProfiles[slot];
            }
        }
        void LocalSelectionChanged(int slot)
        {
            var box=localSelection.ReadBox(slot); var owner=box==null?null:localOwners.Players.FirstOrDefault(player=>checked((int)player.user.id)==box.InputUserId);
            if (owner && box!=null) localOwners.SaveChoice(owner,box);
            var first=localSelection.ReadBox(0); if(first==null)return;
            selectedCharacterIndex=first.Character;selectedVehicleIndex=first.Vehicle;selectedSkinIndex=first.Skin;
            hud.VehicleCharacterSelection=first.Character;hud.VehicleSelection=first.Vehicle;
        }
        void LocalOwnerAction(PlayerInput owner,InputAction.CallbackContext context)
        {
            // The result/menu callback can open selection during the same input dispatch.
            if (!localSelectionActive || Time.frameCount == localSelectionOpenedFrame || phase!=Phase.Menu || hud.MenuTransitionActive || !owner.user.valid || !localOwners.Paired(owner,context.control.device)) return;
            int id=checked((int)owner.user.id),slot=localSelection.JoinedOwner(id); if(slot<0)return;
            NativeInputBindings.RecordLocalOwnerDevice(slot,context.control.device);
            var box=localSelection.ReadBox(slot); var profile=localProfiles[slot]; string action=context.action.name;
            if (action=="Cancel" && context.canceled && box.Selection==NativeLocalSelectionState.Stage.Character && !profile.Open) {localQuittingPresses=Mathf.Clamp(localQuittingPresses-1,0,3);if(localQuittingPresses==0)localQuittingHeld=0;return;}
            if (!context.started) return;
            if (Enum.TryParse(action,out NativeInputBindings.Action input) && NativeInputBindings.LocalOwnerReplay(input,slot,context.control)) return;
            NativeCharacterProfileResult result=NativeCharacterProfileResult.Ignored;
            if(action=="Use") result=profile.Config(true,box.Selection==NativeLocalSelectionState.Stage.Ready,NativeInputBindings.Profiles.Length,box.Selection==NativeLocalSelectionState.Stage.Vehicle?NativeMenuPage.Vehicles:box.Selection==NativeLocalSelectionState.Stage.Skin?NativeMenuPage.Skins:NativeMenuPage.Characters,box.Selection==NativeLocalSelectionState.Stage.Vehicle?box.Vehicle:box.Selection==NativeLocalSelectionState.Stage.Skin?box.Skin:Array.IndexOf(selectableCharacters,box.Character),index=>ApplyCharacterProfile(slot,index));
            else if(profile.Open)
            {
                if(action=="Navigate") result=profile.Navigate(NativePlayerBoxNavigation.Direction(context.ReadValue<Vector2>()),NativeInputBindings.Profiles.Length);
                else if(action=="Ok") result=profile.Confirm(index=>ApplyCharacterProfile(slot,index));
                else if(action=="Cancel") result=profile.Cancel();
            }
            else if(action=="Navigate") localSelection.MoveStarted(id,NativePlayerBoxNavigation.Direction(context.ReadValue<Vector2>()));
            else if(action=="Ok") localSelection.SubmitStarted(id);
            else if(action=="Cancel")
            {
                if(box.Selection==NativeLocalSelectionState.Stage.Character) {localQuittingPresses=Mathf.Clamp(localQuittingPresses+1,0,3);hud.PlayLocalCharacterCancel();}
                else localSelection.CancelPlayerBoxStarted(id);
            }
            hud.CharacterProfileSound(result);
            if(result==NativeCharacterProfileResult.Applied||result==NativeCharacterProfileResult.Cancelled)hud.CloseCharacterProfile(profile);
        }
        bool HandleLocalSelectionInput()
        {
            if(!localSelectionActive)return false;
            if(localQuittingPresses>0)
            {
                localQuittingHeld+=Time.deltaTime;
                if(localQuittingHeld>=.65f){localSelectionActive=false;localSelection.Ended();localOwners.End();hud.SetMenuPage(NativeMenuPage.Main);}
            }
            if(localSelectionActive && localSelection.UpdateAllReady())
            {localSelectionActive=false;localOwners.End();OpenModeCup();}
            return true;
        }
        void LocalPointerMove(int step)
        {var first=localSelection?.ReadBox(0);if(localSelectionActive&&first!=null)localSelection.MoveStarted(first.InputUserId,step>0?1:3);}
        bool DrawLocalSelection()
        {
            if(!localSelectionActive)return false;
            var pointerAction=hud.DrawLocalSelection(localSelection,vehicleRoster,localProfiles);
            if(pointerAction==NativeMenuAction.OpenVehicles){var first=localSelection.ReadBox(0);if(first!=null)localSelection.SubmitStarted(first.InputUserId,false);}
            if(pointerAction==NativeMenuAction.BackToCharacters)
            {
                var first=localSelection.ReadBox(0);
                if(first!=null && first.Selection!=NativeLocalSelectionState.Stage.Character) localSelection.CancelPlayerBoxStarted(first.InputUserId);
                else {localSelectionActive=false;localSelection.Ended();localOwners.End();hud.SetMenuPage(NativeMenuPage.Main);}
            }
            hud.LocalSelectionQuit(localQuittingPresses>0,localQuittingHeld/.65f); return true;
        }
        bool sourceInjectedDiagnosticOwner;
        public PlayerInput SourceActualLocalOwner(int humanSlot)
        {
            if (Net && Net.Open) return humanSlot==0 ? NetworkActualOwner() : null;
            var service=NativeLocalSelectionOwners.Instance;
            if(!service || raceConfig==null)return null;
            var humans=raceConfig.roster.Where(seat=>seat.isHuman).ToArray();
            if(humanSlot<0||humanSlot>=humans.Length)return null;
            string seat=humans[humanSlot].seatId;
            if(seat!=null&&seat.StartsWith("human-",StringComparison.Ordinal)&&uint.TryParse(seat.Substring(6),out uint id))return service.FindOwner(id);
            return sourceInjectedDiagnosticOwner?service.AtSlot(humanSlot):null;
        }
        public int SourcePrimaryRacerIndex
        {
            get
            {
                if (Net && Net.Open) return NetworkActualOwner() && SourceRacerObjectValid(Net.LocalRacerIndex) ? Net.LocalRacerIndex : -1;
                if(raceConfig==null)return -1;
                int first=raceConfig.roster.FindIndex(seat=>seat.isHuman);
                return first>=0&&SourceActualLocalOwner(0)&&SourceRacerObjectValid(first)?first:-1;
            }
        }
        void SynchronizeRaceLocalOwners()
        {
            if (Net && Net.Open) { NativeInputBindings.SetActualLocalOwners(new[]{NetworkActualOwner(),null,null,null}); return; }
            if(!NativeLocalSelectionOwners.Instance || raceConfig==null)return;
            var owners=new PlayerInput[4];int count=raceConfig.roster.Count(seat=>seat.isHuman);
            for(int slot=0;slot<count;slot++)owners[slot]=SourceActualLocalOwner(slot);
            NativeInputBindings.SetActualLocalOwners(owners);
        }
        void EnsureInjectedDiagnosticOwner(InputDevice fixtureDevice = null)
        {
            if(!verificationRun||localSelectionVerification)return;
            if(NativeLocalSelectionOwners.Instance){sourceInjectedDiagnosticOwner=NativeLocalSelectionOwners.Instance.DiagnosticOwner;return;}
            var device=fixtureDevice??NativeInputBindings.AppliedProfileDevice()??(InputDevice)Keyboard.current??Gamepad.current;
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            if(device==null || !device.enabled)
            {
                var keyboard=InputSystem.AddDevice<Keyboard>("source-diagnostic-keyboard");
                InputSystem.EnableDevice(keyboard);keyboard.MakeCurrent();device=keyboard;
            }
#endif
            if(device==null)throw new InvalidOperationException("Injected diagnostic owner requires an actual InputSystem device");
            var service=NativeLocalSelectionOwners.Create();
            if(!service.Begin(false,device))throw new InvalidOperationException("Injected diagnostic PlayerInput join failed device="+device.deviceId+" layout="+device.layout+" enabled="+device.enabled+" added="+device.added);
            service.End();service.MarkDiagnosticOwner(true);sourceInjectedDiagnosticOwner=true;NativeInputBindings.SetActualLocalOwners(service.Players);
            Debug.Log("NATIVE_SOURCE_OWNER_FIXTURE scope=NativeInjected diagnostic data; actual PlayerInput/InputUser; physical-human acceptance=false device="+device.deviceId);
        }
        bool LocalSessionRosterMatches(NativeSessionConfig config)
        {
            var service=NativeLocalSelectionOwners.Instance;
            if(!LocalSelectionEnabled||!service||service.Players.Length==0)return true;
            var players=service.Players.Take(config.mode==NativeSessionMode.TimeTrials?1:4).ToArray();var seats=config.roster.Where(seat=>seat.isHuman).ToArray();
            if(seats.Length!=players.Length)return false;
            for(int slot=0;slot<players.Length;slot++)
            {
                var choice=service.SavedChoice(players[slot]);if(choice==null)return false;
                if(seats[slot].seatId!="human-"+players[slot].user.id||seats[slot].characterId!=vehicleRoster.CharacterIdAt(choice.Character)
                    ||seats[slot].vehicleId!=vehicleRoster.VehicleIdAt(choice.Vehicle)||seats[slot].skinIndex!=choice.Skin)return false;
            }
            return true;
        }
        void DisposeLocalSelection()
        {
            hud.LocalSelectionPointerMove-=LocalPointerMove;
            if(!localOwners)return;
            if(localSelectionActive)localOwners.End();
            localOwners.Joined-=LocalOwnerJoined;localOwners.Left-=LocalOwnerLeft;localOwners.ActionTriggered-=LocalOwnerAction;
        }
    }
}
