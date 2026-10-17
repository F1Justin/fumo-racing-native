using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class MainPromptObservation
        {
            public string fixture, sourcePath, domainConclusion;
            public int frame, graphicInstanceId, actualSpriteInstanceId, actualTextureInstanceId;
            public bool aliasPresent, productionStateConsistent, spritePointerMatchesCatalog, textBranchMatches, conditionalIconAbsence;
            public NativeMainPromptSource.State state, productionRead;
            public NativeUiCanvas.SourceGraphicSample iconAlias, textAlias;
        }
        [Serializable] sealed class MainPromptReport
        {
            public string evidenceCategory = "injected-native-input-and-real-main-render";
            public bool passed, originalDenominatorComplete = false, originalPixelsReviewed = false, hardwareInputReviewed = false;
            public bool customProfileDomainPassed = false;
            public MainPromptObservation[] frames;
            public string[] checks;
        }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
        IEnumerator CheckMainPromptSourceLive(Keyboard keyboard, Action<bool,string> check)
        {
            var report = new MainPromptReport(); var checks = new List<string>(); var frames = new List<MainPromptObservation>();
            Action<bool,string> require = (ok, text) => { checks.Add((ok ? "PASS " : "FAIL ") + text); check(ok, text); };
            var save = NativeUserSettings.CopyData(); string selected = NativeInputBindings.SelectedProfile();
            int selection = hud.SelectedMenuIndex; var disabled = Gamepad.all.Where(pad => pad.enabled).ToArray();
            Mouse mouse = null; Gamepad gamepad = null;
            try
            {
                foreach (var pad in disabled) InputSystem.DisableDevice(pad);
                mouse = InputSystem.AddDevice<Mouse>(); gamepad = InputSystem.AddDevice<Gamepad>();
                require(NativeInputBindings.SelectProfile(""), "Main prompt fixture applies standard default controls");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow));
                yield return null; yield return null; InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null; yield return new WaitForEndOfFrame();
                require(hud.MenuPage == NativeMenuPage.Main && NativeInputBindings.AppliedProfileDevice() == keyboard, "Main prompt actual keyboard Navigate.started device");
                foreach (var action in new[] { NativeInputBindings.Action.Ok, NativeInputBindings.Action.Cancel })
                {
                    var observation = CaptureMainPromptObservation("standard-keyboard-plus-mouse", action); frames.Add(observation);
                    string expected = action == NativeInputBindings.Action.Ok ? "keyboard.z" : "keyboard.x";
                    require(observation.state != null && observation.state.closed && observation.state.scheme == "KeyboardControls"
                        && observation.state.dictionaryKey == expected && observation.state.spriteFound, "Main prompt original first Z/X binding " + action);
                    require(observation.productionStateConsistent && observation.aliasPresent && observation.spritePointerMatchesCatalog
                        && observation.textBranchMatches, "Main prompt keyboard production reader and actual catalog Sprite writer " + action);
                }
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadUp));
                yield return null; yield return null; InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null; yield return new WaitForEndOfFrame();
                require(hud.MenuPage == NativeMenuPage.Main && NativeInputBindings.AppliedProfileDevice() == gamepad, "Main prompt actual standard gamepad Navigate.started device");
                foreach (var action in new[] { NativeInputBindings.Action.Ok, NativeInputBindings.Action.Cancel })
                {
                    var observation = CaptureMainPromptObservation("standard-gamepad", action); frames.Add(observation);
                    require(observation.state != null && observation.state.closed && observation.state.scheme == "GamePadControls"
                        && observation.productionStateConsistent && observation.textBranchMatches
                        && (observation.state.spriteFound ? observation.aliasPresent && observation.spritePointerMatchesCatalog : observation.conditionalIconAbsence),
                        "Main prompt gamepad source catalog hit or legal missing Sprite render branch " + action);
                }
                // Named profiles resolve their effective bindings through the same glyph catalog.
                string profile = NativeInputBindings.CreateProfile("Main prompt source fixture", "<Keyboard>");
                require(!string.IsNullOrEmpty(profile) && NativeInputBindings.SelectProfile(profile), "Main prompt fixture selects nonempty production profile");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow));
                yield return null; yield return null; InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null; yield return new WaitForEndOfFrame();
                foreach (var action in new[] { NativeInputBindings.Action.Ok, NativeInputBindings.Action.Cancel })
                {
                    var observation = CaptureMainPromptObservation("custom-profile-effective-binding", action); frames.Add(observation);
                    observation.domainConclusion = "PASS effective named profile bindings resolved";
                    require(observation.state != null && observation.state.closed && observation.state.spriteFound
                        && observation.spritePointerMatchesCatalog,
                        "Main prompt named profile effective glyph resolved " + action);
                }
            }
            finally
            {
                NativeSaveStore.ReplaceData(save); NativeInputBindings.SelectProfile(selected); hud.SelectMenuIndex(selection);
                if (gamepad != null) InputSystem.RemoveDevice(gamepad); if (mouse != null) InputSystem.RemoveDevice(mouse);
                foreach (var pad in disabled) if (pad.added) InputSystem.EnableDevice(pad);
                // Restore the actual keyboard device through the same input callback used by Main.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow));
            }
            yield return null; yield return null; InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; hud.SelectMenuIndex(selection); yield return new WaitForEndOfFrame();
            PersistMainAliasObservation();
            report.frames = frames.ToArray(); report.checks = checks.ToArray(); report.passed = checks.All(text => text.StartsWith("PASS "));
            File.WriteAllText(Path.Combine(reportDirectory, "main-prompt-source-observation.json"), JsonUtility.ToJson(report, true));
        }
        MainPromptObservation CaptureMainPromptObservation(string fixture, NativeInputBindings.Action action)
        {
            bool confirm = action == NativeInputBindings.Action.Ok;
            string path = "menu2/Canvas/MainMenu/Prompts/" + (confirm ? "AText" : "BText");
            var state = confirm ? hud.MainConfirmPromptState : hud.MainCancelPromptState;
            var read = NativeMainPromptSource.Read(action); var writer = hud.MainWriterCanvas;
            Graphic icon, text; bool exists = writer.TryGetSourceGraphic(path + "/icon", out icon);
            bool textExists = writer.TryGetSourceGraphic(path, out text);
            var image = icon as Image; var label = text as TMP_Text;
            var catalog = NativeContent.Load<NativePromptSpriteCatalog>("NativeMenu/Prompts/Catalog");
            Sprite expected = state != null && catalog ? catalog.Find(state.dictionaryKey) : null;
            string caption = NativeLocalization.TranslateText(confirm ? "Select" : "Exit");
            string expectedText = state != null && state.closed && !state.spriteFound ? "<color=red>" + state.displayBinding + "</color> " + caption : caption;
            AppendMainPromptAliasObservation(fixture, path); AppendMainPromptAliasObservation(fixture, path + "/icon");
            return new MainPromptObservation {
                fixture = fixture, sourcePath = path, frame = Time.frameCount, state = state, productionRead = read,
                domainConclusion = state != null && state.closed ? "production-source-algorithm-observed" : "UNKNOWN",
                aliasPresent = exists, graphicInstanceId = exists ? icon.GetInstanceID() : 0,
                actualSpriteInstanceId = image && image.sprite ? image.sprite.GetInstanceID() : 0,
                actualTextureInstanceId = image && image.sprite && image.sprite.texture ? image.sprite.texture.GetInstanceID() : 0,
                spritePointerMatchesCatalog = state != null && state.closed && expected && image && image.sprite == expected
                    && state.spriteInstanceId == expected.GetInstanceID() && state.textureInstanceId == expected.texture.GetInstanceID(),
                productionStateConsistent = state != null && state.productionFrame == Time.frameCount && state.closed == read.closed
                    && state.dictionaryKey == read.dictionaryKey && state.scheme == read.scheme && state.unknownReason == read.unknownReason,
                textBranchMatches = textExists && label && label.text == expectedText,
                conditionalIconAbsence = state != null && state.closed && !state.spriteFound && !exists,
                iconAlias = writer.SourceSample(path + "/icon"), textAlias = writer.SourceSample(path)
            };
        }
#endif
    }
}
