using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed class NativePhysicalInputAcceptance : MonoBehaviour
    {
        [Serializable] sealed class Sample
        {
            public int frame, deviceId, owners;
            public string device, transport, action, page, phase, appliedDevice;
            public bool native, focused;
            public double seconds;
        }
        [Serializable] sealed class Report
        {
            public string scope = "Ordinary game; native hardware state observation; this observer never injects input";
            public bool inputInjected = false;
            public int nativeSonyEvents;
            public List<Sample> samples = new List<Sample>();
        }
        readonly Report report = new Report();
        string directory;
        float nextWrite;
        string previousState;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--physical-input-acceptance") || FindObjectOfType<NativePhysicalInputAcceptance>()) return;
            int index = Array.IndexOf(args, "--report-dir");
            if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException("Physical observer requires its own report directory");
            var observer = new GameObject("Physical input acceptance observer").AddComponent<NativePhysicalInputAcceptance>();
            observer.directory = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(observer.directory);
            DontDestroyOnLoad(observer); InputSystem.onEvent += observer.OnInput;
        }
        static bool Sony(Gamepad pad) => pad != null && pad.native &&
            ((pad.description.product ?? "").IndexOf("DUALSHOCK", StringComparison.OrdinalIgnoreCase) >= 0 || (pad.description.manufacturer ?? "").IndexOf("Sony", StringComparison.OrdinalIgnoreCase) >= 0);
        void OnInput(InputEventPtr input, InputDevice device)
        {
            if (device is Gamepad pad && Sony(pad) && (input.IsA<StateEvent>() || input.IsA<DeltaStateEvent>())) report.nativeSonyEvents++;
        }
        void Record(InputDevice device, string action)
        {
            var race = FindObjectOfType<NativeRace>(); var service = NativeLocalSelectionOwners.Instance;
            report.samples.Add(new Sample { frame = Time.frameCount, seconds = Time.realtimeSinceStartupAsDouble,
                deviceId = device.deviceId, device = device.description.product, transport = device.description.interfaceName,
                native = device.native, focused = Application.isFocused, action = action,
                page = race ? race.hud.MenuPage.ToString() : "", phase = race ? race.SourceNativePhase : "",
                appliedDevice = NativeInputBindings.AppliedDeviceIdentity(), owners = service ? service.Players.Length : 0 });
        }
        void Update()
        {
            var race = FindObjectOfType<NativeRace>();
            string state = race ? race.hud.MenuPage + ":" + race.SourceNativePhase + ":" + NativeInputBindings.AppliedDeviceIdentity() : "loading";
            if (state != previousState && Keyboard.current != null) { previousState = state; Record(Keyboard.current, "State:" + state); }
            foreach (var pad in InputSystem.devices.OfType<Gamepad>().Where(Sony))
            {
                if (pad.buttonSouth.wasPressedThisFrame) Record(pad, "Cross");
                if (pad.buttonEast.wasPressedThisFrame) Record(pad, "Circle");
                if (pad.startButton.wasPressedThisFrame) Record(pad, "Options");
                if (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame) Record(pad, "Dpad");
                if (pad.rightTrigger.wasPressedThisFrame) Record(pad, "Throttle");
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.native)
            {
                if (keyboard.xKey.wasPressedThisFrame) Record(keyboard, "X");
                if (keyboard.zKey.wasPressedThisFrame) Record(keyboard, "Z");
                if (keyboard.enterKey.wasPressedThisFrame) Record(keyboard, "Enter");
            }
            if (Time.realtimeSinceStartup >= nextWrite)
            {
                nextWrite = Time.realtimeSinceStartup + .25f;
                File.WriteAllText(Path.Combine(directory, "physical-input.json"), JsonUtility.ToJson(report, true));
                if (File.Exists(Path.Combine(directory, "stop"))) Application.Quit();
            }
        }
        void OnDestroy() { InputSystem.onEvent -= OnInput; }
    }
}
