using System;
using System.Collections;
using System.Collections.Generic;
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
        [Serializable] sealed class NetworkParityReport
        {
            public string scope="Injected Gamepad state through actual PlayerInput actions and relay; shared prefab visibility and recovery ownership; physical hardware/pixel acceptance separate",error;
            public bool passed,physicalHumanAccepted=false;
            public int seat,networkRecoveryEvents;
            public float distanceBefore,distanceAfter,speedAfter;
            public List<string> checks=new List<string>();
        }
        bool networkParityDiagnosticStarted;
        void BeginNetworkParityDiagnostic()
        {
            if(networkParityDiagnosticStarted||!Environment.GetCommandLineArgs().Contains("--net-parity-diagnostic"))return;
            networkParityDiagnosticStarted=true;StartCoroutine(GuardNetworkParityDiagnostic());
        }
        void ParityRequire(NetworkParityReport report,bool condition,string check)
        { report.checks.Add((condition?"PASS ":"FAIL ")+check);if(!condition)throw new InvalidOperationException(check); }
        IEnumerator GuardNetworkParityDiagnostic()
        {
            var report=new NetworkParityReport();var iterator=RunNetworkParityDiagnostic(report);Exception error=null;
            while(true)
            {
                bool next=false;object value=null;
                try{next=iterator.MoveNext();if(next)value=iterator.Current;}catch(Exception ex){error=ex;}
                if(error!=null||!next)break;yield return value;
            }
            (iterator as IDisposable)?.Dispose();report.error=error?.ToString();report.passed=error==null;
            string directory=Argument(Environment.GetCommandLineArgs(),"--net-report-dir")??Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"network-parity-diagnostic.json"),JsonUtility.ToJson(report,true));
            Debug.Log("NATIVE_NETWORK_PARITY_DIAGNOSTIC "+(report.passed?"PASS":"FAIL"));
        }
        IEnumerator RunNetworkParityDiagnostic(NetworkParityReport report)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            float deadline=Time.realtimeSinceStartup+45;
            while(phase!=Phase.Racing||Net.Loading||!NetworkSourceStarted)
            {if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Race startup timeout");yield return null;}
            var owner=NetworkActualOwner();var pad=owner?owner.user.pairedDevices.OfType<Gamepad>().FirstOrDefault():null;
            var keyboard=owner?owner.user.pairedDevices.OfType<Keyboard>().FirstOrDefault(k=>!k.native):null;
            ParityRequire(report,Net.DiagnosticOwnerCreated && (pad!=null && !pad.native || keyboard!=null),"explicit injected device owns production actions");
            report.seat=Net.LocalSeat;
            yield return new WaitForEndOfFrame();
            ParityRequire(report,hud.RacePrefabVisible,"shared time/laps/rank prefab stays visible after complete OnGUI");
            var recovery=racers[Net.LocalRacerIndex].GetComponent<NativeKartRecovery>();
            for(int i=0;i<racers.Length;i++)
                ParityRequire(report,racers[i].GetComponent<NativeKartRecovery>().FadeEnabled==(i==Net.LocalRacerIndex),"recovery fade belongs only to local racer "+i);
            foreach(var button in pad!=null?new[]{GamepadButton.South,GamepadButton.East,GamepadButton.West,GamepadButton.North}:new GamepadButton[0])
            {
                InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(button));yield return null;yield return null;
                ParityRequire(report,!Net.OnlineRaceMenuVisible,button+" never opens pause");
                InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;yield return null;
            }
            if(keyboard!=null)
                foreach(var key in new[]{Key.X,Key.C,Key.Space})
                {
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
                    ParityRequire(report,!Net.OnlineRaceMenuVisible,key+" (including original X brake) never opens pause");
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
                }
            if(pad!=null)InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.Start));
            else InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;yield return null;
            ParityRequire(report,Net.OnlineRaceMenuVisible,"dedicated Start opens pause");
            if(pad!=null)InputSystem.QueueStateEvent(pad,new GamepadState());else InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            if(pad!=null)InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.Start));else InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;yield return null;
            ParityRequire(report,!Net.OnlineRaceMenuVisible,"dedicated Start resumes without restarting race");
            if(pad!=null)InputSystem.QueueStateEvent(pad,new GamepadState());else InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            report.distanceBefore=Net.Latest.karts.First(k=>k.seat==Net.LocalRacerIndex).distance;
            if(pad!=null)InputSystem.QueueStateEvent(pad,new GamepadState{rightTrigger=1});else InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));
            yield return new WaitForSecondsRealtime(3);
            var state=Net.Latest.karts.First(k=>k.seat==Net.LocalRacerIndex);
            report.distanceAfter=state.distance;report.speedAfter=state.speed;
            ParityRequire(report,Net.Inputs.TryGetValue(Net.LocalSeat,out var input)&&input.throttle>.49f&&!input.autopilot,"real action sampler sends trigger throttle without autopilot or verification input");
            ParityRequire(report,report.distanceAfter>report.distanceBefore+.5f&&report.speedAfter>.5f,"authoritative relay snapshot confirms human-input acceleration");
            if(pad!=null)InputSystem.QueueStateEvent(pad,new GamepadState());else InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            string sync=Argument(Environment.GetCommandLineArgs(),"--net-parity-sync-dir");
            ParityRequire(report,!string.IsNullOrEmpty(sync),"explicit cross-process recovery synchronization directory");
            Directory.CreateDirectory(sync);
            string ready=Path.Combine(sync,"client-recovery-ready"),requested=Path.Combine(sync,"host-recovery-requested");
            if(Net.IsHost)
            {
                deadline=Time.realtimeSinceStartup+15;
                while(!File.Exists(ready)){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Client recovery barrier timeout");yield return null;}
                var remote=Enumerable.Range(0,racers.Length).First(i=>i!=Net.LocalRacerIndex);
                var remoteRecovery=racers[remote].GetComponent<NativeKartRecovery>();
                ParityRequire(report,remoteRecovery.Request(),"host accepts remote-racer recovery");
                File.WriteAllText(requested+".tmp",remoteRecovery.State.Requests.ToString());File.Move(requested+".tmp",requested);
                yield return new WaitForSecondsRealtime(.25f);
                ParityRequire(report,recovery.FadeAlpha==0,"remote-racer recovery never blacks out host screen");
            }
            else
            {
                ParityRequire(report,!recovery.Request()&&!recovery.RequestFrozen(null)&&recovery.State.Requests==0,"client fall/grass contacts cannot schedule local teleports or fades");
                int before=recovery.NetworkRecoveryEvents;
                File.WriteAllText(ready,"ready");deadline=Time.realtimeSinceStartup+15;
                while(!File.Exists(requested)){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Host recovery barrier timeout");yield return null;}
                int expected=int.Parse(File.ReadAllText(requested));
                ParityRequire(report,expected>before,"host generated a fresh authoritative recovery event");
                while(recovery.NetworkRecoveryEvents<expected){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Recovery replication timeout");yield return null;}
                ParityRequire(report,recovery.NetworkRecoveryEvents==expected,"client receives this specific authoritative recovery event");
            }
            yield return new WaitForSecondsRealtime(1);
            report.networkRecoveryEvents=recovery.NetworkRecoveryEvents;
            yield return new WaitForEndOfFrame();
            ParityRequire(report,recovery.FadeAlpha==0&&hud.RacePrefabVisible,"recovery fade clears and shared HUD remains visible");
            string directory=Argument(Environment.GetCommandLineArgs(),"--net-report-dir");
            if(!string.IsNullOrEmpty(directory)){ScreenCapture.CaptureScreenshot(Path.Combine(directory,"network-parity.png"));yield return new WaitForEndOfFrame();}
#else
            throw new InvalidOperationException("Diagnostic requires standalone Input System");
#endif
        }
    }
}
