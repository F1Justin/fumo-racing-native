using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
using UnityEngine.InputSystem.LowLevel;
#endif
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class NetworkOwnerRejoinReport
        {
            public string scope="NativeInjected device removal/addition and real confirm -> PIM join -> same-race router rebind; physical hardware and rendering pending",error;
            public bool passed,physicalHumanAccepted=false,renderEvidenceAvailable=false,actualConfirmObserved,actualPimJoined,oldRouterUnbound;
            public int raceIdBefore,raceIdAfter,startCountBefore,startCountAfter,roundBefore,roundAfter,seatBefore,seatAfter,oldOwnerId,newOwnerId,oldActionsId,newActionsId,confirmDeviceId;
            public uint oldUserId,newUserId;
            public string[] oldPairedDevices,newPairedDevices;
            public double clockBefore,clockAfter;
            public int routerOwnerId,routerActionsId; public uint routerUserId;
        }
        bool networkOwnerRejoinDiagnosticStarted;
        void BeginNetworkOwnerRejoinDiagnostic()
        {
            if(networkOwnerRejoinDiagnosticStarted||!Environment.GetCommandLineArgs().Contains("--net-owner-rejoin-diagnostic"))return;
            networkOwnerRejoinDiagnosticStarted=true;StartCoroutine(GuardNetworkOwnerRejoinDiagnostic());
        }
        IEnumerator GuardNetworkOwnerRejoinDiagnostic()
        {
            var report=new NetworkOwnerRejoinReport();var iterator=RunNetworkOwnerRejoinDiagnostic(report);Exception error=null;
            while(true){bool next=false;object value=null;try{next=iterator.MoveNext();if(next)value=iterator.Current;}catch(Exception ex){error=ex;}if(error!=null||!next)break;yield return value;}
            (iterator as IDisposable)?.Dispose();report.error=error?.ToString();report.passed=error==null;
            string directory=Argument(Environment.GetCommandLineArgs(),"--net-report-dir")??Application.persistentDataPath;Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"network-owner-rejoin-diagnostic.json"),JsonUtility.ToJson(report,true));
            Debug.Log("NATIVE_NETWORK_OWNER_REJOIN_DIAGNOSTIC "+(report.passed?"PASS":"FAIL"));
        }
        IEnumerator RunNetworkOwnerRejoinDiagnostic(NetworkOwnerRejoinReport report)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            float deadline=Time.realtimeSinceStartup+45;
            while(phase!=Phase.Racing||Net.Loading||!NetworkSourceStarted||!raceClock.Running||raceClock.Elapsed<=0){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Rejoin fixture racing timeout");yield return null;}
            var old=NetworkActualOwner();
            if(!Net.DiagnosticOwnerCreated||!old||!old.user.valid)throw new InvalidOperationException("Rejoin fixture requires actual diagnostic owner");
            var paired=old.user.pairedDevices.ToArray();
            report.oldPairedDevices=paired.Select(device=>device.deviceId+":"+device.layout+":native="+device.native).ToArray();
            var injected=paired.OfType<Keyboard>().Where(device=>!device.native).ToArray();
            if(injected.Length!=1||paired.Any(device=>device!=injected[0]&&!(device is Mouse && device.native)))throw new InvalidOperationException("Rejoin fixture requires one injected keyboard with only original native Mouse companion");
            report.raceIdBefore=sourceRaceState.RaceId;report.roundBefore=Net.Round;report.seatBefore=Net.LocalSeat;report.startCountBefore=NetworkSourceStartCount;report.clockBefore=raceClock.Elapsed;
            report.oldOwnerId=old.GetInstanceID();report.oldUserId=old.user.id;report.oldActionsId=old.actions.GetInstanceID();
            InputSystem.RemoveDevice(injected[0]);yield return null;yield return null;yield return null;
            report.oldRouterUnbound=!sourceHumanInput.BoundPlayerInput(0)&&!sourceHumanInput.BoundActionAsset(0);
            if(NetworkActualOwner()||!report.oldRouterUnbound)throw new InvalidOperationException("Lost actual owner/router was not invalidated");
            var replacement=InputSystem.AddDevice<Keyboard>();var service=NativeLocalSelectionOwners.Instance;PlayerInput joined=null;
            Action<PlayerInput> onJoin=player=>{if(service.Paired(player,replacement))joined=player;};service.Joined+=onJoin;
            try
            {
                networkRejoinConfirmDeviceId=-1;
                InputSystem.QueueStateEvent(replacement,new KeyboardState(Key.Enter));yield return null;yield return null;
                InputSystem.QueueStateEvent(replacement,new KeyboardState());yield return null;
                var current=NetworkActualOwner();
                report.actualConfirmObserved=networkRejoinConfirmDeviceId==replacement.deviceId;report.confirmDeviceId=networkRejoinConfirmDeviceId;
                report.actualPimJoined=joined&&joined==current;
                if(!current||!report.actualConfirmObserved||!report.actualPimJoined)throw new InvalidOperationException("Real confirm did not create the actual PIM replacement owner");
                report.newPairedDevices=current.user.pairedDevices.Select(device=>device.deviceId+":"+device.layout+":native="+device.native).ToArray();
                report.newOwnerId=current.GetInstanceID();report.newUserId=current.user.id;report.newActionsId=current.actions.GetInstanceID();
                report.routerOwnerId=sourceHumanInput.BoundPlayerInput(0)?sourceHumanInput.BoundPlayerInput(0).GetInstanceID():0;
                report.routerActionsId=sourceHumanInput.BoundActionAsset(0)?sourceHumanInput.BoundActionAsset(0).GetInstanceID():0;report.routerUserId=sourceHumanInput.BoundStableUserId(0);
                report.raceIdAfter=sourceRaceState.RaceId;report.roundAfter=Net.Round;report.seatAfter=Net.LocalSeat;report.startCountAfter=NetworkSourceStartCount;report.clockAfter=raceClock.Elapsed;
                if(report.newOwnerId==report.oldOwnerId||report.newUserId==report.oldUserId||report.newActionsId==report.oldActionsId||report.routerOwnerId!=report.newOwnerId||report.routerActionsId!=report.newActionsId||report.routerUserId!=report.newUserId)throw new InvalidOperationException("Same-race router retains the old actual owner/actions/User");
                if(report.raceIdBefore!=report.raceIdAfter||report.roundBefore!=report.roundAfter||report.seatBefore!=report.seatAfter||report.startCountBefore!=report.startCountAfter||report.clockAfter<report.clockBefore)throw new InvalidOperationException("Owner rebind reset race/round/seat/source clock");
            }
            finally{service.Joined-=onJoin;}
#else
            throw new InvalidOperationException("Native injected rejoin diagnostic requires Player Input System APIs");
#endif
        }
    }
}
