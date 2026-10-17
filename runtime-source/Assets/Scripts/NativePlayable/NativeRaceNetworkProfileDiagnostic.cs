using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        [Serializable] sealed class ProfileInputReport
        {
            public bool passed;
            public string scope = "Rendered authored TMP profile, injected keyboard/gamepad and IME events; native OS candidate window requires separate inspection", error;
            public List<string> checks = new List<string>();
        }
        bool profileDiagnosticStarted;
        void BeginProfileInputDiagnostic()
        {
            if (profileDiagnosticStarted || !Environment.GetCommandLineArgs().Contains("--profile-input-diagnostic")) return;
            if (string.IsNullOrEmpty(Arg(Environment.GetCommandLineArgs(), "--save-directory")))
                throw new InvalidOperationException("Profile diagnostic requires an isolated --save-directory");
            profileDiagnosticStarted = true;
            StartCoroutine(GuardProfileInputDiagnostic());
        }
        static void ProfileRequire(ProfileInputReport report, bool condition, string check)
        {
            report.checks.Add((condition ? "PASS " : "FAIL ") + check);
            if (!condition) throw new InvalidOperationException(check);
        }
        IEnumerator GuardProfileInputDiagnostic()
        {
            var report = new ProfileInputReport();
            var stack = new Stack<IEnumerator>(); stack.Push(RunProfileInputDiagnostic(report));
            Exception failure = null;
            while (stack.Count > 0)
            {
                object value = null; bool next = false;
                try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (!next) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (value is IEnumerator nested) { stack.Push(nested); continue; }
                yield return value;
            }
            foreach (var iterator in stack) (iterator as IDisposable)?.Dispose();
            report.passed = failure == null; report.error = failure?.ToString();
            Directory.CreateDirectory(reportDir);
            File.WriteAllText(Path.Combine(reportDir, "profile-input-diagnostic.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_PROFILE_INPUT_DIAGNOSTIC " + (report.passed ? "PASS" : "FAIL") + " " + report.error);
        }
        IEnumerator DiagnosticOpenProfile(string draft)
        {
            if (!Race.SettingsOpen) Race.OpenSettings();
            float deadline = Time.realtimeSinceStartup + 5;
            while (OnlineHud.MenuTransitionActive)
            { if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Settings transition did not settle"); yield return null; }
            Race.ReopenIdentityProfileFromSettings();
            yield return null; yield return new WaitForEndOfFrame();
            if (!onlineFields.TryGetValue("player-name", out var field) || !field)
                throw new InvalidOperationException("Authored player-name field not present");
            field.Select(); field.ActivateInputField();
            yield return null; yield return null;
            field.text = draft;
            field.selectionStringAnchorPosition = field.selectionStringFocusPosition = draft.Length;
            yield return null;
        }
        static IEnumerator DiagnosticKey(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        static IEnumerator DiagnosticPad(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button)); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
        }
        static IEnumerator DiagnosticEmptyImeSpace(Keyboard keyboard)
        {
            var empty = IMECompositionEvent.Create(keyboard.deviceId, "", InputState.currentTime);
            InputSystem.QueueEvent(ref empty);
            InputSystem.QueueTextEvent(keyboard, ' ');
            yield return DiagnosticKey(keyboard, Key.Space);
        }
        IEnumerator RunProfileInputDiagnostic(ProfileInputReport report)
        {
            yield return new WaitForSecondsRealtime(2);
            var keyboard = InputSystem.AddDevice<Keyboard>(); var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                keyboard.MakeCurrent();
                foreach (var good in new[] { "Player-01", "灵梦_123", "e\u0301", new string('a', 32), "\U00020000" })
                    ProfileRequire(report, NativePlayerNamePolicy.TryNormalize(good, out _, out _), "accept valid ID " + good);
                foreach (var bad in new[] { "", "a b", "a\u00a0b", "a\u200bb", "a\u202eb", "a\U0001f600", "<b>A</b>", "_..", "\u0301a", "a\ud800", new string('a', 33) })
                    ProfileRequire(report, !NativePlayerNamePolicy.TryNormalize(bad, out _, out _), "reject invalid ID " + bad.Replace("\ud800", "[surrogate]"));
                ProfileRequire(report, NativePlayerNamePolicy.TryNormalize("e\u0301", out var normalized, out _) && normalized == "é", "NFC normalization");
                yield return DiagnosticOpenProfile("Modifier-Test");
                string beforeShortcuts = NativeUserSettings.ReadValue(OnlineNameSetting);
                foreach (var modifier in new[] { Key.LeftCtrl, Key.LeftShift, Key.LeftAlt, Key.LeftMeta })
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(modifier, Key.Space)); yield return null; yield return null;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                    ProfileRequire(report, profilesPage && OnlineEditing && NativeUserSettings.ReadValue(OnlineNameSetting) == beforeShortcuts, modifier + " + Space never saves or exits");
                }
                yield return DiagnosticOpenProfile("Empty-Ime-Space");
                yield return DiagnosticEmptyImeSpace(keyboard);
                ProfileRequire(report, !profilesPage && NativeUserSettings.ReadValue(OnlineNameSetting) == "Empty-Ime-Space", "empty IME event plus bare Space saves and exits");
                yield return DiagnosticOpenProfile("Ctrl-Then-Space");
                string beforeCtrlSpace = NativeUserSettings.ReadValue(OnlineNameSetting);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.Space)); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                bool ctrlSpaceKeptEditing = profilesPage && OnlineEditing && NativeUserSettings.ReadValue(OnlineNameSetting) == beforeCtrlSpace;
                yield return DiagnosticEmptyImeSpace(keyboard);
                ProfileRequire(report, ctrlSpaceKeptEditing && !profilesPage && NativeUserSettings.ReadValue(OnlineNameSetting) == "Ctrl-Then-Space", "Ctrl+Space switching then empty IME plus bare Space saves only on bare Space");
                foreach (var key in new[] { Key.Enter, Key.Space, Key.NumpadEnter })
                {
                    string name = "Key-" + key;
                    yield return DiagnosticOpenProfile(name);
                    yield return DiagnosticKey(keyboard, key);
                    ProfileRequire(report, !profilesPage && NativeUserSettings.ReadValue(OnlineNameSetting) == name, key + " saves and exits once (saved=" + NativeUserSettings.ReadValue(OnlineNameSetting) + ", notice=" + profileNotice + ", transition=" + OnlineHud.MenuTransitionActive + ")");
                }
                string saved = NativeUserSettings.ReadValue(OnlineNameSetting);
                yield return DiagnosticOpenProfile("Unsaved");
                yield return DiagnosticKey(keyboard, Key.Escape);
                ProfileRequire(report, profilesPage && !OnlineEditing && profileNameDraft == saved && NativeUserSettings.ReadValue(OnlineNameSetting) == saved, "Escape cancels draft without saving or leaving profile");
                yield return DiagnosticKey(keyboard, Key.Escape);
                ProfileRequire(report, !profilesPage, "Escape outside text returns");
                yield return DiagnosticOpenProfile("Invalid Paste");
                yield return DiagnosticKey(keyboard, Key.Enter);
                ProfileRequire(report, profilesPage && OnlineEditing && profileNameDraft == "Invalid Paste" && !string.IsNullOrEmpty(profileNotice) && NativeUserSettings.ReadValue(OnlineNameSetting) == saved, "invalid paste remains visible, explains error and is not saved");
                foreach (var key in new[] { Key.Space, Key.Enter, Key.Escape })
                {
                    yield return DiagnosticOpenProfile("IME-Base");
                    var composition = IMECompositionEvent.Create(keyboard.deviceId, "ni", InputState.currentTime);
                    InputSystem.QueueEvent(ref composition); yield return null; yield return null;
                    yield return DiagnosticKey(keyboard, key);
                    ProfileRequire(report, profilesPage && OnlineEditing && NativeUserSettings.ReadValue(OnlineNameSetting) == saved, "composition consumes " + key);
                    var end = IMECompositionEvent.Create(keyboard.deviceId, "", InputState.currentTime);
                    InputSystem.QueueEvent(ref end); InputSystem.QueueTextEvent(keyboard, '你');
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
                    ProfileRequire(report, profilesPage && OnlineEditing, "commit frame and held " + key + " stay in editor");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
                    ProfileRequire(report, !NativeTextInputBridge.BlocksMenuInput, "IME guard releases after " + key + " release");
                }
                string afterCompositionDraft = profileNameDraft;
                yield return DiagnosticEmptyImeSpace(keyboard);
                ProfileRequire(report, !profilesPage && NativeUserSettings.ReadValue(OnlineNameSetting) == afterCompositionDraft, "after composition commit protection releases, empty IME plus new bare Space saves and exits");
                foreach (var button in new[] { GamepadButton.South, GamepadButton.West })
                {
                    string name = "Pad-" + button;
                    yield return DiagnosticOpenProfile(name);
                    yield return DiagnosticPad(pad, button);
                    ProfileRequire(report, !profilesPage && NativeUserSettings.ReadValue(OnlineNameSetting) == name, button + " saves and exits");
                }
                yield return DiagnosticOpenProfile("灵梦-01");
                yield return DiagnosticKey(keyboard, Key.Enter);
                ProfileRequire(report, !profilesPage && NativeUserSettings.ReadValue(OnlineNameSetting) == "灵梦-01", "Chinese ID passes actual font check and persists");
                yield return DiagnosticOpenProfile("灵梦-01");
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(reportDir, "profile-input.png")); yield return new WaitForEndOfFrame();
            }
            finally { InputSystem.RemoveDevice(pad); InputSystem.RemoveDevice(keyboard); }
        }
    }
}
