using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        public static string[] RequiredMenuFieldPaths(string page)
        {
            switch(page)
            {
                case "MainMenu": return new[] { "menu2/Canvas/MainMenu/Logo", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/ExtrasButton", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/ExtrasButton/Text", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/OnlineButton", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/OnlineButton/Text", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/OptionsButton", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/LowerOptionsMask/LowerOptionsPIvot2/OptionsButton/Text", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorOutline", "menu2/Canvas/MainMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot/Selector", "menu2/Canvas/MainMenu/Prompts/AText", "menu2/Canvas/MainMenu/Prompts/AText/icon", "menu2/Canvas/MainMenu/Prompts/BText", "menu2/Canvas/MainMenu/Prompts/BText/icon", "menu2/Canvas/MainMenu/WheelRoot/Wheel", "menu2/Canvas/MainMenu/WheelRoot/WheelOptions/RaceButton", "menu2/Canvas/MainMenu/WheelRoot/WheelOptions/RaceButton/Text", "menu2/Canvas/MainMenu/WheelRoot/WheelOptions/SoloButton", "menu2/Canvas/MainMenu/WheelRoot/WheelOptions/SoloButton/Text" };
                case "ExtrasMenu": return new[] { "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/MusicRoomButton", "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/CreditsButton/Text", "menu2/Canvas/ExtrasMenu/WheelRoot/Wheel", "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot/Selector", "menu2/Canvas/ExtrasMenu/WheelRoot", "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorOutline", "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/MusicRoomButton/Text", "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG", "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/CreditsButton", "menu2/Canvas/ExtrasMenu/Prompts/BText", "menu2/Canvas/ExtrasMenu/Prompts/AText/icon", "menu2/Canvas/ExtrasMenu/Prompts/BText/icon", "menu2/Canvas/ExtrasMenu/Prompts/AText" };
                case "OptionsMenu": return new[] { "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/LanguageButton", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/VideoButton/Text", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot/Selector", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorOutline", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/InputButton/Text", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/AudioButton/Text", "menu2/Canvas/OptionsMenu/WheelRoot", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/VideoButton", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/AudioButton", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/LanguageButton/Text", "menu2/Canvas/OptionsMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/InputButton", "menu2/Canvas/OptionsMenu/WheelRoot/Wheel", "menu2/Canvas/OptionsMenu/Prompts/AText/icon", "menu2/Canvas/OptionsMenu/Prompts/BText", "menu2/Canvas/OptionsMenu/Prompts/AText", "menu2/Canvas/OptionsMenu/Prompts/BText/icon" };
                default: throw new ArgumentException("Unknown menu source state: " + page);
            }
        }
        public RequiredLeafSample[] ReadRequiredMenuFields(string page) => RequiredMenuFieldPaths(page).Select(ReadRequiredLeaf).ToArray();
        // A fixture-only scoped mutation restores the exact previous field value.
        public IDisposable MutateRequiredLeaf(string path, string mutation)
        {
            var node = transforms[path]; var graphic = node.GetComponent<Graphic>();
            if (mutation == "inactive") { bool old = node.gameObject.activeSelf; node.gameObject.SetActive(false); return new LeafMutationRestore(() => node.gameObject.SetActive(old)); }
            if (mutation == "disabled") { bool old = graphic.enabled; graphic.enabled = false; return new LeafMutationRestore(() => graphic.enabled = old); }
            if (mutation == "canvas-disabled") { var canvas = graphic.canvas; bool old = canvas.enabled; canvas.enabled = false; return new LeafMutationRestore(() => canvas.enabled = old); }
            if (mutation == "alpha-zero") { var old = graphic.color; graphic.color = new Color(old.r, old.g, old.b, 0); return new LeafMutationRestore(() => graphic.color = old); }
            throw new ArgumentException("Unsupported fixture mutation: " + mutation);
        }
        sealed class LeafMutationRestore : IDisposable
        {
            Action restore;
            public LeafMutationRestore(Action restore) { this.restore = restore; }
            public void Dispose() { if (restore == null) return; restore(); restore = null; }
        }
    }
    public sealed partial class NativeRace
    {
        [Serializable] sealed class RequiredMenuFieldReport
        {
            public string page, evidenceCategory = "injected-runtime-field-fixture";
            public bool visualParityAccepted = false, dynamicDenominatorComplete = false, mainAliasComplete = false;
            public string[] unresolvedAliasSourcePaths;
            public NativeAuthoredUi.RequiredLeafSample[] leaves;
            public string[] checks;
            public NativeMainPromptSource.State confirmPrompt, cancelPrompt;
            public int promptObservationFrame;
        }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
        IEnumerator CaptureRequiredMenuFieldVerification(string page)
        {
            yield return new WaitForEndOfFrame();
            var ui = hud.OriginalSpriteUi; var leaves = ui.ReadRequiredMenuFields(page);
            var checks = new List<string>();
            bool main = page == "MainMenu";
            var confirmPrompt = page == "ExtrasMenu" ? hud.ExtrasConfirmPromptState : page == "OptionsMenu" ? hud.OptionsConfirmPromptState : null;
            var cancelPrompt = page == "ExtrasMenu" ? hud.ExtrasCancelPromptState : page == "OptionsMenu" ? hud.OptionsCancelPromptState : null;
            var enforced = main ? Array.Empty<NativeAuthoredUi.RequiredLeafSample>() : leaves;
            var unresolved = main ? leaves.Select(leaf => leaf.path).ToArray() : new string[0];
            if (main)
                checks.Add((!ui.VisibleGraphic("menu2/Canvas/MainMenu/discordserverbutton") && !ui.VisibleGraphic("menu2/Canvas/MainMenu/tooltip") ? "PASS " : "FAIL ") + "user-removed Discord button and tooltip remain hidden");
            if (page == "ExtrasMenu" || page == "OptionsMenu")
                checks.Add((leaves.Length == (page == "ExtrasMenu" ? 13 : 17) ? "PASS " : "FAIL ") + "unchanged full source candidate count " + page + "=" + leaves.Length);
            foreach (var leaf in enforced)
            {
                bool extrasRoot = page == "ExtrasMenu" && leaf.path == "menu2/Canvas/ExtrasMenu/WheelRoot";
                bool optionsRoot = page == "OptionsMenu" && leaf.path == "menu2/Canvas/OptionsMenu/WheelRoot";
                if (extrasRoot || optionsRoot)
                {
                    long expectedId = extrasRoot ? 1422 : 1393;
                    if (leaf.failure == "missing-node")
                    {
                        checks.Add("FAIL source structural disabled WheelRoot " + leaf.path + " expectedId=" + expectedId + " source-definition-unavailable missing-node");
                        continue;
                    }
                    var sourceNode = ui.Definition(leaf.path);
                    string expectedPath = extrasRoot ? "Canvas/ExtrasMenu/WheelRoot" : "Canvas/OptionsMenu/WheelRoot";
                    bool sourceDisabled = sourceNode.id == expectedId && sourceNode.scene == "menu2" && sourceNode.path == expectedPath
                        && sourceNode.image && !sourceNode.imageEnabled && sourceNode.spriteId == 0
                        && string.IsNullOrEmpty(sourceNode.spriteGuid) && !sourceNode.sprite
                        && sourceNode.mask && !sourceNode.maskEnabled && !sourceNode.showMask;
                    bool graphicPresentDisabled = leaf.failure != "missing-node" && leaf.failure != "missing-graphic"
                        && leaf.contentKind == "image-geometry" && !leaf.enabled;
                    checks.Add((sourceDisabled && graphicPresentDisabled ? "PASS " : "FAIL ")
                        + "source structural disabled WheelRoot " + leaf.path + " id=" + sourceNode.id + " expectedId=" + expectedId
                        + " scene=" + sourceNode.scene + " path=" + sourceNode.path
                        + " image=" + sourceNode.image + " imageEnabled=" + sourceNode.imageEnabled
                        + " spriteId=" + sourceNode.spriteId + " spriteGuid=" + sourceNode.spriteGuid + " sourceSprite=" + (bool)sourceNode.sprite
                        + " mask=" + sourceNode.mask + " maskEnabled=" + sourceNode.maskEnabled + " showMask=" + sourceNode.showMask
                        + " nativeGraphicKind=" + leaf.contentKind + " nativeGraphicEnabled=" + leaf.enabled + " sampleFailure=" + leaf.failure);
                }
                else if ((page == "ExtrasMenu" || page == "OptionsMenu")
                    && (leaf.path == "menu2/Canvas/" + page + "/Prompts/AText/icon" || leaf.path == "menu2/Canvas/" + page + "/Prompts/BText/icon")
                    && (leaf.path.EndsWith("AText/icon") ? confirmPrompt : cancelPrompt) is NativeMainPromptSource.State prompt
                    && prompt.closed && !prompt.spriteFound)
                {
                    string textPath = leaf.path.Substring(0, leaf.path.Length - "/icon".Length);
                    var textLeaf = leaves.FirstOrDefault(candidate => candidate.path == textPath);
                    bool textPresent = textLeaf != null && textLeaf.failure != "missing-node" && textLeaf.failure != "missing-graphic" && textLeaf.contentKind == "text-mesh";
                    if (leaf.failure == "missing-node" || !textPresent)
                    {
                        checks.Add("FAIL source TIPromptUI missing-sprite conditional node unavailable " + leaf.path + " sourceText=" + textPath + " textPresent=" + textPresent + " iconFailure=" + leaf.failure);
                        continue;
                    }
                    var iconSource = ui.Definition(leaf.path);
                    var textSource = ui.Definition(textPath);
                    long expectedId = page == "ExtrasMenu" ? leaf.path.EndsWith("AText/icon") ? 1693 : 1714 : leaf.path.EndsWith("AText/icon") ? 1689 : 1711;
                    long expectedTextId = page == "ExtrasMenu" ? leaf.path.EndsWith("AText/icon") ? 1715 : 1691 : leaf.path.EndsWith("AText/icon") ? 1701 : 1697;
                    bool exactSource = textSource.id == expectedTextId && textSource.hasText && textSource.scene == "menu2"
                        && textSource.scene + "/" + textSource.path == textPath && prompt.sourcePath == textPath && iconSource.id == expectedId && iconSource.scene == "menu2" && iconSource.scene + "/" + iconSource.path == leaf.path
                        && iconSource.image && iconSource.imageEnabled;
                    bool actualHidden = leaf.failure != "missing-node" && leaf.failure != "missing-graphic"
                        && leaf.contentKind == "image-geometry" && leaf.enabled && !leaf.active && prompt.productionFrame == Time.frameCount;
                    checks.Add((exactSource && actualHidden ? "PASS " : "FAIL ") + "source TIPromptUI missing-sprite conditional icon hidden " + leaf.path
                        + " sourceId=" + iconSource.id + " expectedId=" + expectedId + " sourceTextId=" + textSource.id + " expectedTextId=" + expectedTextId + " stateFrame=" + prompt.productionFrame
                        + " sampleFrame=" + Time.frameCount + " scheme=" + prompt.scheme + " key=" + prompt.dictionaryKey
                        + " spriteFound=" + prompt.spriteFound + " active=" + leaf.active + " enabled=" + leaf.enabled + " sampleFailure=" + leaf.failure);
                }
                else checks.Add((leaf.fieldEligible ? "PASS " : "FAIL ") + "required leaf field eligibility " + leaf.path);
            }
            var eligible = enforced.FirstOrDefault(leaf => leaf.fieldEligible);
            string target = eligible != null ? eligible.path : null;
            if (target == null && !main) checks.Add("FAIL mutation target absent");
            if (target != null) foreach (string mutation in new[] { "inactive", "disabled", "canvas-disabled", "alpha-zero" })
            {
                // Assert immediately so the production next-frame Show reset cannot erase the mutation.
                using (ui.MutateRequiredLeaf(target, mutation))
                    checks.Add((!ui.ReadRequiredLeaf(target).fieldEligible ? "PASS " : "FAIL ") + "required-leaf mutation " + mutation + " " + target);
            }
            File.WriteAllText(Path.Combine(reportDirectory, "required-leaves-" + page + ".json"), JsonUtility.ToJson(new RequiredMenuFieldReport { page = page, leaves = leaves, confirmPrompt = confirmPrompt, cancelPrompt = cancelPrompt, promptObservationFrame = Time.frameCount, unresolvedAliasSourcePaths = unresolved, checks = checks.ToArray() }, true));
            requiredMenuFieldChecks.AddRange(checks);
        }
        readonly List<string> requiredMenuFieldChecks = new List<string>();
#endif
    }
}
