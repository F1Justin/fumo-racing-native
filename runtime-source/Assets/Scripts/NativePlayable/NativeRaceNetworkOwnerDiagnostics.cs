using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeNetworkOwnerDiagnostics
    {
        public string scope="actual owner and production consumer bindings; input hardware origin and pixels remain unverified", inputMode, failure, controlScheme, capturedControlScheme;
        public uint expectedStableInputUser;
        public string expectedControlScheme;public int[] expectedPairedDeviceIds=new int[0];public bool diagnosticOwnerCreated;
        public bool consumerBindingsPassed, minimapConsumerObserved, inRaceContext, passed, overallAccepted=false, ownerExists, inputUserValid, capturedMatchesCurrent, cameraMatchesLocal, itemInventoryMatchesLocal;
        public bool insanityTargetsOnlyLocal, sourceHumansOnlyLocal, localViewsAbsent;
        public bool sourceRouterMatchesActualOwner;
        public int sourceRouterPlayerInputId, sourceRouterActionAssetId, actualOwnerActionAssetId;
        public uint sourceRouterStableUserId;
        public bool renderEvidenceAvailable, actualOnGUIObserved, moduleHudFieldConsumerMatchesLocal, moduleHudRuntimeDrawObserved;
        public bool raceHudExpected,raceHudPrefabVisible;
        public int moduleHudMotorId, moduleHudCameraId, moduleHudSeat=-1, moduleHudDrawFrame=-1;
        public int sampleFrame, capturedInputFrame, capturedOwnerId, currentOwnerId, currentInputUserIndex=-1, playerIndex=-1, localSeat=-1, cameraSeat=-1, cameraTargetId, itemInventoryId, itemInventorySeat=-1;
        public uint capturedInputUser, currentInputUser;
        public int minimapConsumerFrame=-1, minimapConsumerPrimarySeat=-1, localViewCount, raceId;
        public Device[] capturedPairedDevices=new Device[0];
        public Device[] fullPairedDevices=new Device[0];
        public Human[] sourceHumans=new Human[0];
        public Actor[] actors=new Actor[0];
        public int[] insanityTargetSeats=new int[0], minimapConsumerSeats=new int[0];
        public string[] sourceSeatIds=new string[0];
        [Serializable] public sealed class Device {public int id;public string layout,name;public bool native;}
        [Serializable] public sealed class Human {public int slot, inputUserIndex,racerIndex;public bool joined,valid;}
        [Serializable] public sealed class Actor {public string kind,item,ownerSeatId;public int id,ownerSeat,ownerMotorId,replicaInstanceId;}
    }
    public sealed partial class NativeRace
    {
        int networkObservedMinimapFrame=-1, networkObservedModuleDrawFrame=-1;
        void ObserveNetworkModuleHudDraw()
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                networkObservedModuleDrawFrame=Time.frameCount;
        }
        NativeKartMotor[] networkObservedMinimapOrder;
        void ObserveNetworkHudConsumerOrder(NativeKartMotor[] actualConsumerOrder)
        {networkObservedMinimapFrame=Time.frameCount;networkObservedMinimapOrder=(NativeKartMotor[])actualConsumerOrder.Clone();}
        internal NativeNetworkOwnerDiagnostics ReadNetworkOwnerDiagnostics(PlayerInput captured,uint capturedUser,string capturedScheme,NativeNetworkOwnerDiagnostics.Device[] capturedDevices,int capturedFrame,bool injectedInput,bool injectedAutopilot,bool inRace)
        {
            var current=NetworkActualOwner();int local=Net?Net.LocalSeat:-1;
            var d=new NativeNetworkOwnerDiagnostics{expectedStableInputUser=Net?Net.StableLocalOwnerId:0,expectedControlScheme=Net?Net.StableLocalOwnerScheme:null,expectedPairedDeviceIds=Net?(int[])Net.StableLocalOwnerDevices.Clone():new int[0],diagnosticOwnerCreated=Net&&Net.DiagnosticOwnerCreated,inRaceContext=inRace,sampleFrame=Time.frameCount,capturedInputFrame=capturedFrame,
                inputMode=injectedInput?"injected-verification-input":injectedAutopilot?"injected-autopilot":"actual-owner-action-sampling",
                ownerExists=current,inputUserValid=current&&current.user.valid,currentOwnerId=current?current.GetInstanceID():0,
                capturedOwnerId=captured?captured.GetInstanceID():0,currentInputUser=current&&current.user.valid?current.user.id:0,
                currentInputUserIndex=current&&current.user.valid?current.user.index:-1,
                capturedInputUser=capturedUser,capturedControlScheme=capturedScheme,capturedPairedDevices=capturedDevices,playerIndex=current?current.playerIndex:-1,
                controlScheme=current?current.currentControlScheme:null,localSeat=local,cameraSeat=NetworkCameraSeat,
                cameraTargetId=cameraRig&&cameraRig.target?cameraRig.target.GetInstanceID():0,itemInventoryId=itemHud&&itemHud.inventory?itemHud.inventory.GetInstanceID():0,
                itemInventorySeat=itemHud&&itemHud.inventory?Array.FindIndex(racers,m=>m&&m.GetComponent<NativeKartItemInventory>()==itemHud.inventory):-1,
                minimapConsumerFrame=networkObservedMinimapFrame,localViewCount=localRaceViews.Count,raceId=sourceRaceState==null?-1:sourceRaceState.RaceId};
            d.renderEvidenceAvailable=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
            d.moduleHudMotorId=hud.OriginalModulePresentedMotorId;d.moduleHudCameraId=hud.OriginalModulePresentedCameraId;
            d.moduleHudSeat=Array.FindIndex(racers,m=>m&&m.GetInstanceID()==d.moduleHudMotorId);
            d.moduleHudDrawFrame=networkObservedModuleDrawFrame;
            d.moduleHudFieldConsumerMatchesLocal=local>=0&&d.moduleHudSeat==local&&raceCamera&&d.moduleHudCameraId==raceCamera.GetInstanceID();
            d.moduleHudRuntimeDrawObserved=d.renderEvidenceAvailable&&d.moduleHudDrawFrame>=0;
            d.actualOnGUIObserved=d.renderEvidenceAvailable&&networkObservedMinimapFrame>=0;
            if(current&&current.user.valid)d.fullPairedDevices=current.user.pairedDevices.Select(device=>new NativeNetworkOwnerDiagnostics.Device{id=device.deviceId,layout=device.layout,name=device.name,native=device.native}).ToArray();
            d.capturedMatchesCurrent=captured&&current&&captured.user.valid&&current.user.valid&&capturedUser==current.user.id
                &&capturedScheme==current.currentControlScheme
                &&capturedDevices!=null&&capturedDevices.OrderBy(device=>device.id).Select(device=>device.id+"|"+device.layout+"|"+device.name+"|"+device.native)
                    .SequenceEqual(d.fullPairedDevices.OrderBy(device=>device.id).Select(device=>device.id+"|"+device.layout+"|"+device.name+"|"+device.native));
            var routerOwner=sourceHumanInput?sourceHumanInput.BoundPlayerInput(0):null;
            var routerActions=sourceHumanInput?sourceHumanInput.BoundActionAsset(0):null;
            d.sourceRouterPlayerInputId=routerOwner?routerOwner.GetInstanceID():0;
            d.sourceRouterActionAssetId=routerActions?routerActions.GetInstanceID():0;
            d.actualOwnerActionAssetId=current&&current.actions?current.actions.GetInstanceID():0;
            d.sourceRouterStableUserId=sourceHumanInput?sourceHumanInput.BoundStableUserId(0):0;
            d.sourceRouterMatchesActualOwner=current&&routerOwner==current&&routerActions==current.actions&&current.user.valid&&d.sourceRouterStableUserId==current.user.id;
            d.localViewsAbsent=d.localViewCount==0;d.cameraMatchesLocal=local>=0&&d.cameraSeat==local;d.itemInventoryMatchesLocal=local>=0&&d.itemInventorySeat==local;
            if(networkObservedMinimapOrder!=null)d.minimapConsumerSeats=networkObservedMinimapOrder.Select(m=>Array.IndexOf(racers,m)).ToArray();
            if(d.minimapConsumerSeats.Length>0)d.minimapConsumerPrimarySeat=d.minimapConsumerSeats[0];
            if(sourceRaceState!=null)d.sourceHumans=sourceRaceState.Humans.Select((h,i)=>new NativeNetworkOwnerDiagnostics.Human{slot=i,inputUserIndex=h.InputUserIndex,racerIndex=h.RacerIndex,joined=h.Joined,valid=h.RacerValid}).ToArray();
            var joined=d.sourceHumans.Where(h=>h.joined).ToArray();d.sourceHumansOnlyLocal=joined.Length==1&&joined[0].slot==0&&joined[0].racerIndex==local&&joined[0].valid&&current&&current.user.valid&&joined[0].inputUserIndex==current.user.index;
            var router=itemWorld?itemWorld.GetComponent<NativeInsanityHudRouter>():null;
            if(router)d.insanityTargetSeats=router.humanPlayers.Select(m=>Array.IndexOf(racers,m)).ToArray();
            d.insanityTargetsOnlyLocal=router&&router.Initialized&&d.insanityTargetSeats.Length==1&&d.insanityTargetSeats[0]==local;
            if(raceConfig!=null)d.sourceSeatIds=raceConfig.roster.Select(seat=>seat.seatId).ToArray();
            Func<int,string> seatId=seat=>seat>=0&&seat<d.sourceSeatIds.Length?d.sourceSeatIds[seat]:null;
            var live=FindObjectsOfType<NativeItemActor>().Where(a=>!a.Finished).Select(a=>{int seat=Array.IndexOf(racers,a.Owner);return new NativeNetworkOwnerDiagnostics.Actor{kind="actual-production-actor",id=a.GetInstanceID(),item=a.ItemId,ownerSeat=seat,ownerMotorId=a.Owner?a.Owner.GetInstanceID():0,ownerSeatId=seatId(seat)};});
            var wire=Net&&Net.Latest!=null&&Net.Latest.actors!=null?Net.Latest.actors.Select(a=>{GameObject replica;networkActors.TryGetValue(a.id,out replica);return new NativeNetworkOwnerDiagnostics.Actor{kind="received-snapshot-actor",id=a.id,item=a.item,ownerSeat=a.owner,ownerMotorId=a.owner>=0&&a.owner<racers.Length&&racers[a.owner]?racers[a.owner].GetInstanceID():0,ownerSeatId=seatId(a.owner),replicaInstanceId=replica?replica.GetInstanceID():0};}):Enumerable.Empty<NativeNetworkOwnerDiagnostics.Actor>();
            d.actors=live.Concat(wire).ToArray();
            d.minimapConsumerObserved=d.actualOnGUIObserved;
            d.consumerBindingsPassed=d.ownerExists&&d.inputUserValid&&d.fullPairedDevices.Length>0&&d.capturedMatchesCurrent&&d.cameraMatchesLocal
                &&d.itemInventoryMatchesLocal&&d.sourceRouterMatchesActualOwner&&d.sourceHumansOnlyLocal&&d.localViewsAbsent&&d.insanityTargetsOnlyLocal&&d.moduleHudFieldConsumerMatchesLocal;
            d.raceHudExpected=inRace && Net && !Net.Loading && !Net.requestedSpectator && !Net.OnlineRaceMenuVisible;
            d.raceHudPrefabVisible=hud!=null && hud.RacePrefabVisible;
            if(!d.ownerExists||!d.inputUserValid||d.fullPairedDevices.Length==0)d.failure="Actual valid paired owner missing";
            else if(inRace&&!d.consumerBindingsPassed)d.failure="Actual network race consumer owner mapping failed";
            else if(inRace&&d.renderEvidenceAvailable&&(!d.moduleHudRuntimeDrawObserved||d.minimapConsumerFrame<0||d.minimapConsumerPrimarySeat!=local))d.failure="Actual OnGUI HUD/minimap consumer receipt missing or primary differs from LocalSeat";
            else if(inRace&&!d.renderEvidenceAvailable)d.failure="Pending actual rendered OnGUI module HUD/minimap consumer observation (NullGraphics)";
            else if(d.raceHudExpected && d.actualOnGUIObserved && !d.raceHudPrefabVisible)d.failure="Shared race HUD prefab was hidden after the Online page rendered";
            d.passed=d.failure==null&&(!inRace||(d.actualOnGUIObserved&&d.moduleHudRuntimeDrawObserved&&d.minimapConsumerPrimarySeat==local));return d;
        }
    }
}
