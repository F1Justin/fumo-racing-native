using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        internal bool TryGetControllerMenuGraphic(string path, out Graphic graphic)
        {
            graphic = null;
            return transforms.TryGetValue(path, out var node) && node.gameObject.activeInHierarchy
                && node.TryGetComponent(out graphic) && graphic.enabled;
        }
    }
    public sealed partial class NativeRaceHud
    {
        internal bool TryGetControllerMenuGraphic(string path, out Graphic graphic)
        {
            graphic = null;
            return authoredUi != null && authoredUi.PrefabAuthored
                && authoredUi.TryGetControllerMenuGraphic(path, out graphic);
        }
    }
    public sealed partial class NativeRace
    {
#if ENABLE_INPUT_SYSTEM
        [Serializable] sealed class ControllerMenuReport
        {
            public bool passed, physicalInputAccepted;
            public string input = "Virtual DualShock4GamepadHID and Keyboard state events through production menu handlers", error;
            public string[] connectedDevices;
            public List<string> checks = new List<string>();
        }
        void ControllerMenuRequire(ControllerMenuReport report, bool value, string name)
        {
            report.checks.Add((value ? "PASS " : "FAIL ") + name);
            if (!value) throw new InvalidOperationException(name);
        }
        [StructLayout(LayoutKind.Explicit, Size = 9)]
        struct ControllerMenuDs4State : IInputStateTypeInfo
        {
            public FourCC format => new FourCC('D', '4', 'V', 'S');
            [FieldOffset(0)] public byte leftX;
            [FieldOffset(1)] public byte leftY;
            [FieldOffset(4)] public byte buttons;
        }
        static void ControllerMenuQueue<T>(InputControl<T> control, T value) where T : struct
        {
            if (control.device is DualShock4GamepadHID pad)
            {
                // HID controls reject WriteValueIntoEvent; queue the device's native virtual report instead.
                pad.CopyState(out ControllerMenuDs4State report);
                if (ReferenceEquals(control, pad.leftStick))
                {
                    var direction = (Vector2)(object)value;
                    report.leftX = (byte)Mathf.RoundToInt((direction.x + 1) * 127.5f);
                    report.leftY = (byte)Mathf.RoundToInt((1 - direction.y) * 127.5f);
                }
                else
                {
                    int bit = ReferenceEquals(control, pad.buttonSouth) ? 5 : ReferenceEquals(control, pad.buttonEast) ? 6 : -1;
                    if (bit < 0) throw new ArgumentException("Unsupported DS4 fixture control");
                    report.buttons = (byte)((float)(object)value > .5f ? report.buttons | (1 << bit) : report.buttons & ~(1 << bit));
                }
                InputSystem.QueueStateEvent(pad, report); return;
            }
            using (StateEvent.From(control.device, out var state))
            { control.WriteValueIntoEvent(value, state); InputSystem.QueueEvent(state); }
        }
        IEnumerator ControllerMenuPress(ButtonControl control)
        {
            ControllerMenuQueue(control, 1f); yield return null; yield return null;
            ControllerMenuQueue(control, 0f); yield return null; yield return null;
            float deadline = Time.realtimeSinceStartup + 4;
            while (hud.MenuTransitionActive && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForEndOfFrame();
        }
        IEnumerator ControllerMenuMove(DualShock4GamepadHID pad, Vector2 direction)
        {
            ControllerMenuQueue(pad.leftStick, direction); yield return null; yield return null;
            ControllerMenuQueue(pad.leftStick, Vector2.zero); yield return null; yield return null;
            yield return new WaitForEndOfFrame();
        }
        IEnumerator CheckControllerMenu()
        {
            var report = new ControllerMenuReport { connectedDevices = InputSystem.devices.Select(device => device.deviceId + ":" + device.layout + ":" + device.displayName).ToArray() };
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            var mouse = InputSystem.AddDevice<Mouse>();
            var pad = InputSystem.AddDevice<DualShock4GamepadHID>();
            InputSystem.EnableDevice(pad);
            NativeInputBindings.SelectProfile(""); NativeInputBindings.Initialize();
            phase = Phase.Menu; autopilot = false; selfTest = false;
            hud.SuppressPointerInput = true;
            Directory.CreateDirectory(reportDirectory);
            var stack = new Stack<IEnumerator>(); stack.Push(CheckControllerMenuCore(report, keyboard, pad));
            while (stack.Count > 0)
            {
                bool next; object value = null;
                try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                catch (Exception failure) { report.error = failure.ToString(); break; }
                if (!next) { stack.Pop(); if (stack.Count == 0) report.passed = true; continue; }
                if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return value;
            }
            File.WriteAllText(Path.Combine(reportDirectory, "controller-menu.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CONTROLLER_MENU_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
        void ControllerMenuCheckPrompts(ControllerMenuReport report, string route, string root)
        {
            foreach (var action in new[] { NativeInputBindings.Action.Ok, NativeInputBindings.Action.Cancel })
            {
                var state = NativeMainPromptSource.Read(action);
                string path = root + "/Prompts/" + (action == NativeInputBindings.Action.Ok ? "AText" : "BText");
                Graphic icon, text;
                bool found = hud.TryGetControllerMenuGraphic(path + "/icon", out icon);
                hud.TryGetControllerMenuGraphic(path, out text);
                var image = icon as Image; var label = text as TMP_Text;
                ControllerMenuRequire(report, state.closed && state.spriteFound && state.dictionaryKey == "playstation." + (action == NativeInputBindings.Action.Ok ? "cross" : "circle"), route + "-" + action + "-sony-glyph");
                if (root.EndsWith("MainMenu") && action == NativeInputBindings.Action.Cancel)
                {
                    hud.TryGetControllerMenuGraphic(root + "/Prompts/AText", out var select);
                    var corners = new Vector3[4]; var iconCorners = new Vector3[4];
                    ControllerMenuRequire(report, label && select is TMP_Text selectText
                        && ((int)label.alignment & 0xff) == 1 && ((int)selectText.alignment & 0xff) == 1,
                        route + "-both-prompt-captions-left-aligned");
                    select.rectTransform.GetWorldCorners(corners); icon.rectTransform.GetWorldCorners(iconCorners);
                    ControllerMenuRequire(report, iconCorners[0].x - corners[2].x >= 16f, route + "-exit-icon-separated-from-select");
                    hud.TryGetControllerMenuGraphic(root + "/Prompts/AText/icon", out var selectIcon);
                    var selectLabel = select as TMP_Text;
                    selectLabel.ForceMeshUpdate(); label.ForceMeshUpdate();
                    selectIcon.rectTransform.GetWorldCorners(corners); icon.rectTransform.GetWorldCorners(iconCorners);
                    float selectGap = selectLabel.rectTransform.TransformPoint(selectLabel.textBounds.min).x - corners[2].x;
                    float exitGap = label.rectTransform.TransformPoint(label.textBounds.min).x - iconCorners[2].x;
                    ControllerMenuRequire(report, Mathf.Abs(selectGap - exitGap) <= 3f,
                        route + "-glyph-gap-matches Select=" + selectGap.ToString("F2") + " Exit=" + exitGap.ToString("F2"));
                }
                if (root.EndsWith("MainMenu"))
                    ControllerMenuRequire(report, found && image && image.sprite == state.sprite && label && !label.text.Contains("Cross") && !label.text.Contains("Circle"), route + "-" + action + "-rendered-icon-without-button-name");
            }
        }
        IEnumerator CheckControllerMenuCore(ControllerMenuReport report, Keyboard keyboard, DualShock4GamepadHID pad)
        {
            yield return null;
            foreach (int language in new[] { 0, 3 })
            {
                string route = "language-" + language;
                NativeUserSettings.TrySetInt("lang_game", language);
                hud.SetMenuPage(NativeMenuPage.Main); hud.SelectMenuIndex(0);
                yield return WaitForSettingsRender();
                yield return ControllerMenuMove(pad, Vector2.up);
                ControllerMenuRequire(report, NativeInputBindings.AppliedProfileDevice() == pad, route + "-ds4-input-device");
                ControllerMenuCheckPrompts(report, route, "menu2/Canvas/MainMenu");
                CaptureTestScreenshot(route + "-ds4-main.png");
                hud.SelectMenuIndex(0);
                yield return ControllerMenuPress(pad.buttonSouth);
                ControllerMenuRequire(report, hud.MenuPage == NativeMenuPage.RaceModes, route + "-ds4-confirm-enters-race-modes");
                yield return ControllerMenuPress(keyboard.xKey);
                ControllerMenuRequire(report, hud.MenuPage == NativeMenuPage.Main, route + "-keyboard-x-returns-main");
                ControllerMenuRequire(report, NativeMainPromptSource.Read(NativeInputBindings.Action.Cancel).dictionaryKey == "keyboard.x", route + "-keyboard-hints");
                CaptureTestScreenshot(route + "-keyboard-main.png");
                yield return new WaitForEndOfFrame(); yield return null;
                int index = hud.SelectedMenuIndex;
                yield return ControllerMenuMove(pad, Vector2.down);
                ControllerMenuRequire(report, hud.SelectedMenuIndex != index, route + "-ds4-navigation-after-keyboard-return");
                ControllerMenuCheckPrompts(report, route + "-resumed", "menu2/Canvas/MainMenu");
                localSelectionVerification = true; selectedMode = NativeSessionMode.TimeTrials;
                hud.SetMenuPage(NativeMenuPage.Characters); BeginLocalSelection();
                yield return WaitForSettingsRender();
                var owner = NativeInputBindings.ActualLocalOwner(0); uint user = owner.user.id;
                ControllerMenuRequire(report, owner.user.pairedDevices.Contains(pad) && !owner.user.pairedDevices.Contains(keyboard), route + "-initial-player-retains-confirming-ds4");
                int character = localSelection.ReadBox(0).Character;
                yield return ControllerMenuMove(pad, Vector2.right);
                ControllerMenuRequire(report, localSelection.ReadBox(0).Character != character, route + "-ds4-character-navigation");
                ControllerMenuQueue(pad.buttonEast, 1f);
                float deadline = Time.realtimeSinceStartup + 3;
                while (localSelectionActive && Time.realtimeSinceStartup < deadline) yield return null;
                ControllerMenuQueue(pad.buttonEast, 0f); yield return null; yield return WaitForSettingsRender();
                ControllerMenuRequire(report, !localSelectionActive && hud.MenuPage == NativeMenuPage.Main, route + "-ds4-hold-back-returns-main");
                hud.SelectMenuIndex(0); yield return ControllerMenuPress(pad.buttonSouth);
                yield return ControllerMenuPress(keyboard.xKey);
                ControllerMenuRequire(report, hud.MenuPage == NativeMenuPage.Main, route + "-keyboard-return-with-retained-ds4-owner");
                yield return ControllerMenuMove(pad, Vector2.down);
                ControllerMenuRequire(report, NativeInputBindings.AppliedProfileDevice() == pad && owner.user.id == user && owner.user.pairedDevices.Contains(pad), route + "-ds4-resumes-and-owner-stays-paired");
                OpenSettings(); yield return WaitForSettingsRender();
                yield return ControllerMenuMove(pad, Vector2.down);
                ControllerMenuRequire(report, settingsIndex == 1, route + "-ds4-settings-navigation-after-rebuild");
                CaptureTestScreenshot(route + "-ds4-settings.png");
                yield return ControllerMenuPress(keyboard.xKey);
                ControllerMenuRequire(report, !settingsOpen && hud.MenuPage == NativeMenuPage.Main, route + "-keyboard-settings-return");
                yield return ControllerMenuMove(pad, Vector2.down);
                ControllerMenuCheckPrompts(report, route + "-settings-return", "menu2/Canvas/MainMenu");
                localSelectionVerification = false;
            }
        }
#endif
    }
}
