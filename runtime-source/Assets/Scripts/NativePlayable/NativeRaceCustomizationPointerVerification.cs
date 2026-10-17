using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class CustomBaselineFocus
        {
            public string rowKey, target, selectedSpriteGuid, defaultSpriteGuid;
            public Color color;
        }
        [Serializable] sealed class CustomBaselinePopup
        {
            public string row, itemLabelChild;
            public float itemHeight, itemWidth, contentHeight, itemPositionY, itemPivotY, contentPivotY;
        }
        [Serializable] sealed class CustomBaselineTextBinding
        { public string kind, path; public int percent; public float defaultValue; public string[] optionKeys, optionEnglish; }
        [Serializable] sealed class CustomBaselineTextRow
        { public string rowKey; public int sourceActive; public CustomBaselineTextBinding[] bindings; }
        [Serializable] sealed class CustomBaseline
        {
            public string sourceSceneSha256, repeatSceneSha256;
            public float repeatDelay, repeatRate, scrollSensitivity;
            public CustomBaselineFocus[] rows;
            public CustomBaselinePopup[] popups;
            public CustomBaselineTextRow[] textBindings;
        }
        [Serializable] struct CustomRectProbe
        {
            public float x, y, width, height;
            public float yMax => y + height;
            public static implicit operator CustomRectProbe(Rect r) => new CustomRectProbe { x = r.x, y = r.y, width = r.width, height = r.height };
        }
        [Serializable] sealed class CustomLeafProbe
        {
            public string path, expectedSpriteGuid, actualSprite;
            public bool active, sourceSpriteMatches, sourceColorMatches;
            public float alpha;
            public CustomRectProbe rect;
        }
        [Serializable] sealed class CustomPopupProbe
        {
            public string row;
            public int count, chosen, highlighted, selectedMenuIndex, dropdownRow;
            public bool templateActiveSelf, templateActiveInHierarchy, contentActiveSelf, contentActiveInHierarchy;
            public string[] actualChildren;
            public float sourceItemHeight, actualHeight, expectedHeight;
            public CustomRectProbe popupRect, firstItemRect, lastItemRect;
        }
        [Serializable] sealed class CustomUiSoundBaseline
        {
            public int index;
            public string name, guid, sha256;
        }
        [Serializable] sealed class CustomUiSounds { public CustomUiSoundBaseline[] audioClips; }
        [Serializable] sealed class CustomUiAudioSample
        {
            public string eventScope, sourceGuid, sourceOggSha256, argumentClipName, audioSourceClipPropertyName, mixer;
            public int sourceInstanceId, argumentInstanceId;
            public bool argumentMatchesBoundClip, sourceEnabled, sourcePlaying;
            public float listenerVolume, sourceVolume;
        }
        [Serializable] sealed class CustomScrollbarEventProbe
        {
            public string incomingType, resultingType;
            public Vector2 position;
            public int hotControl, frame;
        }
        [Serializable] sealed class CustomPointerReport
        {
            public bool passed, scriptedState = true, visualParityAccepted = false, scrollbarOverflowFixture;
            public string error, sourceSceneSha256, repeatSceneSha256;
            public string inputRoute = "Event.current in production OnGUI/CustomModeMenu; IMGUI hotControl";
            public string repeatRoute = "production StepCustomizationAxis with controlled unscaled timestamps";
            public float sourceViewportHeight, fixtureViewportHeight, afterDrag, afterTrack, afterHold;
            public int hotControlAfterRelease, firstOnGuiFrame;
            public bool scrollbarActive, handleCenterInsideBar;
            public float actualFixtureViewportHeight, scrollbarContentHeight;
            public CustomRectProbe scrollbarRect, handleRect, slideRect;
            public List<CustomScrollbarEventProbe> scrollbarEvents = new List<CustomScrollbarEventProbe>();
            public string[] checks;
            public List<string> visibleValueSamples = new List<string>();
            public List<string> sourceTextObligations = new List<string>();
            public List<CustomLeafProbe> leaves = new List<CustomLeafProbe>();
            public List<CustomPopupProbe> popups = new List<CustomPopupProbe>();
            public bool ordinaryAudioOutputAccepted = false;
            public string audioEvidence = "Listener-muted actual PlayUi PlayOneShot callback parameters and AudioSource state; audible output remains open";
            public List<CustomUiAudioSample> audioSamples = new List<CustomUiAudioSample>();
        }
        Event customPointerEvent;
        bool customPointerProbeActive;
        CustomPointerReport customAudioReport;
        string customAudioScope;
        void ObserveCustomUiOneShot(AudioSource source, AudioClip clip)
        {
            var original = JsonUtility.FromJson<CustomUiSounds>(NativeContent.Load<TextAsset>("NativeMenu/customization-audio-source").text).audioClips.Single(c => c.name == clip.name);
            customAudioReport.audioSamples.Add(new CustomUiAudioSample {
                eventScope = customAudioScope, sourceGuid = original.guid, sourceOggSha256 = original.sha256,
                argumentClipName = clip.name, audioSourceClipPropertyName = source.clip ? source.clip.name : "",
                mixer = source.outputAudioMixerGroup ? source.outputAudioMixerGroup.name : "",
                sourceInstanceId = source.GetInstanceID(), argumentInstanceId = clip.GetInstanceID(),
                argumentMatchesBoundClip = source == hud.CustomUiAudioSource && clip == hud.CustomUiSound(original.index),
                sourceEnabled = source.enabled, sourcePlaying = source.isPlaying, listenerVolume = AudioListener.volume, sourceVolume = source.volume });
        }
        void RequireCustomSounds(CustomPointerReport report, List<string> checks, int start, params int[] indices)
        {
            var samples = report.audioSamples.Skip(start).ToArray();
            CustomRequire(samples.Length == indices.Length, customAudioScope + "-actual-PlayOneShot-callback-count", checks);
            for (int i = 0; i < indices.Length; i++) CustomRequire(samples[i].argumentMatchesBoundClip && samples[i].argumentClipName == hud.CustomUiSound(indices[i]).name && samples[i].listenerVolume == 0,
                customAudioScope + "-callback-" + i + "-source-bound-argument-GUID-and-muted-listener", checks);
        }
        void ApplyCustomPointerVerificationEvent()
        {
            if (!customPointerProbeActive || customPointerEvent == null || Event.current.type != EventType.Repaint) return;
            Event.current = customPointerEvent; customPointerEvent = null;
        }
        IEnumerator CustomPointerSend(EventType type, Vector2 position)
        {
            customPointerEvent = new Event { type = type, button = 0, mousePosition = position };
            int frames = 120;
            while (customPointerEvent != null && frames-- > 0) yield return new WaitForEndOfFrame();
            if (customPointerEvent != null) throw new InvalidOperationException("custom-production-OnGUI-dispatch-timeout");
            Canvas.ForceUpdateCanvases();
        }
        IEnumerator WaitForCustomRender()
        {
            int frames = 120;
            while ((hud == null || !hud.CustomAuthoredReady || hud.CustomRenderedFrame < 0) && frames-- > 0) yield return null;
            if (hud == null || !hud.CustomAuthoredReady || hud.CustomRenderedFrame < 0) throw new InvalidOperationException("custom-first-OnGUI-timeout");
            yield return null; yield return null; yield return new WaitForEndOfFrame(); Canvas.ForceUpdateCanvases();
        }
        const string CustomSourcePrefix = "selection/Canvas/";
        static string CustomSourceKey(string instancePath) => CustomSourcePrefix + instancePath;
        static string CustomInstancePath(string sourceKey)
        {
            if (!sourceKey.StartsWith(CustomSourcePrefix, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid Customization source key " + sourceKey);
            return sourceKey.Substring(CustomSourcePrefix.Length);
        }
        const string CustomProbeScroll = "Customization/OptionsBox/Scroll View";
        const string CustomProbeRows = CustomProbeScroll + "/Viewport/Content/";
        CustomLeafProbe CheckCustomFocus(CustomPointerReport report, CustomBaseline source, string rowPath, bool focused, Transform actual = null)
        {
            string key = CustomSourceKey(rowPath);
            var binding = source.rows.Single(row => row.rowKey == key);
            if (!actual) actual = SettingsPointerNode("selection", rowPath);
            var image = actual.Find(binding.target).GetComponent<Image>();
            string guid = focused ? binding.selectedSpriteGuid : binding.defaultSpriteGuid;
            var data = NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");
            Sprite expected = guid == "be8a4e5ff4bb7014e9321941f176decc" ? NativeContent.Load<Sprite>("NativeMenu/SettingsSelectedDropdown") : data.nodes.First(node => node.spriteGuid == guid).sprite;
            var leaf = new CustomLeafProbe { path = key + "/" + binding.target, expectedSpriteGuid = guid,
                actualSprite = image.sprite ? image.sprite.name : "", active = image.gameObject.activeInHierarchy,
                sourceSpriteMatches = image.sprite == expected && expected, sourceColorMatches = image.color == binding.color,
                alpha = image.color.a, rect = SettingsPointerRect(image.rectTransform) };
            report.leaves.Add(leaf); return leaf;
        }
        static void CustomRequire(bool condition, string name, List<string> checks)
        {
            checks.Add((condition ? "PASS " : "FAIL ") + name);
            if (!condition) throw new InvalidOperationException(name);
        }
        IEnumerator CheckCustomizationPointerLive()
        {
            var report = new CustomPointerReport(); var checks = new List<string>();
            bool previousPointer = hud.SuppressPointerInput;
            int vsync = QualitySettings.vSyncCount; QualitySettings.vSyncCount = 0;
            float previousListenerVolume = AudioListener.volume;
            bool previousUiProbe = NativeAudioRuntime.UiVerificationPlayback;
            customAudioReport = report; customAudioScope = "setup-source-input";
            AudioListener.volume = 0; NativeAudioRuntime.UiOneShotPlayed += ObserveCustomUiOneShot;
            var savedRules = customRules.Copy(); int savedLaps = customLaps, savedCount = customRacers, savedType = customItemType;
            RectTransform scroll = null; Vector2 scrollSize = Vector2.zero;
            customPointerProbeActive = true; hud.SuppressPointerInput = false;
            var pending = new Stack<IEnumerator>(); pending.Push(CheckCustomizationPointerCore(report, checks));
            try
            {
                while (pending.Count > 0)
                {
                    bool next; object instruction = null;
                    try { next = pending.Peek().MoveNext(); if (next) instruction = pending.Peek().Current; }
                    catch (Exception failure) { report.error = failure.ToString(); checks.Add("FAIL exception " + failure.Message); break; }
                    if (!next) { pending.Pop(); if (pending.Count == 0) report.passed = true; continue; }
                    if (instruction is IEnumerator nested && !(instruction is CustomYieldInstruction)) { pending.Push(nested); continue; }
                    yield return instruction;
                }
            }
            finally
            {
                var root = GameObject.Find("Original authored menus");
                if (root) scroll = root.transform.Find("selection authored Canvas/" + CustomProbeScroll) as RectTransform;
                if (scroll && customProbeOriginalScrollSize.HasValue) { scrollSize = customProbeOriginalScrollSize.Value; scroll.sizeDelta = scrollSize; }
                customProbeOriginalScrollSize = null;
                NativeAudioRuntime.UiOneShotPlayed -= ObserveCustomUiOneShot; customAudioReport = null;
                NativeAudioRuntime.UiVerificationPlayback = previousUiProbe; AudioListener.volume = previousListenerVolume;
                hud.CustomScrollbarPointerObserved = null; hud.CustomBeforeLayoutVerification = null;
                customPointerEvent = null; customPointerProbeActive = false; CloseCustomDropdown();
                customRules = savedRules; customLaps = savedLaps; customRacers = savedCount; customItemType = savedType;
                customHeldAxis = 0; customRepeatAt = 0;
                hud.SuppressPointerInput = previousPointer; hud.SetMenuPage(NativeMenuPage.Main);
                QualitySettings.vSyncCount = vsync;
                report.checks = checks.ToArray(); Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "customization-pointer-report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("CUSTOMIZATION_POINTER_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
        Vector2? customProbeOriginalScrollSize;
        IEnumerator CheckCustomizationPointerCore(CustomPointerReport report, List<string> checks)
        {
            phase = Phase.Menu; autopilot = false;
            var source = JsonUtility.FromJson<CustomBaseline>(NativeContent.Load<TextAsset>("NativeMenu/customization-source-baseline").text);
            report.sourceSceneSha256 = source.sourceSceneSha256; report.repeatSceneSha256 = source.repeatSceneSha256;
            hud.SetMenuPage(NativeMenuPage.Custom); hud.ResetCustomizationFocus(); CloseCustomDropdown();
            yield return WaitForCustomRender(); report.firstOnGuiFrame = hud.CustomRenderedFrame;
            CustomRequire(hud.SelectedMenuIndex == 0, "source-Loaded-Continue-focus-reset-after-first-OnGUI-plus-two-frames", checks);
            CustomRequire(CustomInputRepeatDelay == source.repeatDelay && CustomInputRepeatRate == source.repeatRate, "source-persistent-InputSystemUIInputModule-repeat-delay-rate", checks);
            foreach (var row in source.textBindings)
            {
                string rowPath = CustomInstancePath(row.rowKey);
                var rowNode = SettingsPointerNode("selection", rowPath);
                report.sourceTextObligations.Add(row.rowKey + " sourceActive=" + row.sourceActive + " dynamicBindings=" + row.bindings.Length);
                if (row.rowKey.Contains("/Template/")) continue;
                CustomRequire(rowNode.gameObject.activeInHierarchy == (row.sourceActive == 1), rowPath + "-source-row-visibility", checks);
                int rowIndex = Array.IndexOf(NativeCustomRaceRules.Rows, rowPath.Substring(rowPath.LastIndexOf('/') + 1));
                foreach (var binding in row.bindings)
                {
                    if (row.sourceActive == 0 || rowIndex < 0) continue;
                    var text = SettingsPointerNode("selection", CustomInstancePath(binding.path)).GetComponent<TMP_Text>();
                    int boundValue = CustomValue(rowIndex);
                    string expected = binding.kind == "slider" ? boundValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + (binding.percent == 1 ? "%" : "") : NativeLocalization.Get(binding.optionKeys[boundValue]);
                    report.visibleValueSamples.Add(binding.path + " expected=" + expected + " actual=" + text.text);
                    CustomRequire(text.gameObject.activeInHierarchy && text.enabled && text.color.a > 0 && text.text == expected,
                        rowPath + "-source-bound-visible-dynamic-value-text", checks);
                    if (binding.kind == "dropdown" && NativeLocalization.Language == 0)
                        CustomRequire(expected == binding.optionEnglish[boundValue], rowPath + "-original-English-dropdown-option", checks);
                }
            }
            yield return CaptureSettingsUi("customization-continue-values.png");
            customHeldAxis = 0; customRepeatAt = 0;
            StepCustomizationAxis(1, 1, 0, 10); int first = hud.SelectedMenuIndex;
            StepCustomizationAxis(1, 1, 0, 10.49f); CustomRequire(hud.SelectedMenuIndex == first, "vertical-repeat-waits-source-half-second", checks);
            StepCustomizationAxis(1, 1, 0, 10.5f); CustomRequire(hud.SelectedMenuIndex != first, "vertical-repeat-at-source-half-second", checks);
            int second = hud.SelectedMenuIndex;
            StepCustomizationAxis(1, 1, 0, 10.59f); CustomRequire(hud.SelectedMenuIndex == second, "vertical-repeat-waits-source-tenth-second", checks);
            StepCustomizationAxis(1, 1, 0, 10.601f); CustomRequire(hud.SelectedMenuIndex != second, "vertical-repeat-at-source-tenth-second", checks);
            hud.SelectMenuIndex(5); int value = CustomValue(5); customHeldAxis = 0;
            StepCustomizationAxis(2, 0, 1, 20); StepCustomizationAxis(2, 0, 1, 20.49f);
            CustomRequire(CustomValue(5) == value + 1, "source-whole-number-slider-first-step-and-delay", checks);
            StepCustomizationAxis(2, 0, 1, 20.5f); StepCustomizationAxis(2, 0, 1, 20.601f);
            CustomRequire(CustomValue(5) == value + 3, "source-whole-number-slider-repeat-step", checks);
            customHeldAxis = 0;
            NativeAudioRuntime.UiVerificationPlayback = true; AudioListener.volume = 0;
            customAudioScope = "Loaded-reset-focus"; int loadedSoundStart = report.audioSamples.Count;
            hud.ResetCustomizationFocus(); RequireCustomSounds(report, checks, loadedSoundStart, 0);
            foreach (int row in new[] { 3, 4 })
            {
                hud.SelectMenuIndex(row); SetCustomValue(row, 0); yield return WaitForCustomRender();
                var main = SettingsPointerNode("selection", CustomProbeRows + NativeCustomRaceRules.Rows[row]);
                Rect hit = SettingsPointerRect(main);
                customAudioScope = NativeCustomRaceRules.Rows[row] + "-open"; int soundStart = report.audioSamples.Count;
                yield return CustomPointerSend(EventType.MouseDown, hit.center); yield return CustomPointerSend(EventType.MouseUp, hit.center);
                CustomRequire(customDropdownOpen, NativeCustomRaceRules.Rows[row] + "-production-pointer-opens", checks);
                RequireCustomSounds(report, checks, soundStart, 1, 0);
                yield return WaitForCustomRender();
                string templatePath = CustomProbeRows + NativeCustomRaceRules.Rows[row] + "/Value/Dropdown/Template";
                var template = SettingsPointerNode("selection", templatePath);
                var content = SettingsPointerNode("selection", templatePath + "/Viewport/Content");
                var items = content.Cast<Transform>().Where(t => t.name == "Native custom option" && t.gameObject.activeInHierarchy).ToArray();
                var geometry = source.popups.Single(p => p.row == NativeCustomRaceRules.Rows[row]); int count = row == 3 ? 3 : 2;
                float offset = geometry.contentHeight - geometry.itemHeight;
                var popup = new CustomPopupProbe { row = geometry.row, count = items.Length, chosen = 0, highlighted = 1,
                    selectedMenuIndex = hud.SelectedMenuIndex, dropdownRow = customDropdownRow,
                    templateActiveSelf = template.gameObject.activeSelf, templateActiveInHierarchy = template.gameObject.activeInHierarchy,
                    contentActiveSelf = content.gameObject.activeSelf, contentActiveInHierarchy = content.gameObject.activeInHierarchy,
                    actualChildren = content.Cast<Transform>().Select(t => t.name + ":self=" + t.gameObject.activeSelf + ":hierarchy=" + t.gameObject.activeInHierarchy).ToArray(),
                    sourceItemHeight = geometry.itemHeight, actualHeight = template.rect.height, expectedHeight = geometry.itemHeight * count + offset,
                    popupRect = SettingsPointerRect(template) };
                report.popups.Add(popup);
                CustomRequire(items.Length == count, geometry.row + "-actual-production-popup-visible-clone-count", checks);
                popup.firstItemRect = SettingsPointerRect((RectTransform)items[0]); popup.lastItemRect = SettingsPointerRect((RectTransform)items[count - 1]);
                CustomRequire(items.Length == count && Mathf.Abs(popup.actualHeight - popup.expectedHeight) < .01f && Mathf.Abs(content.rect.height - popup.expectedHeight) < .01f, geometry.row + "-source-popup-height-offset-no-added-padding", checks);
                CustomRequire(Mathf.Abs(popup.popupRect.yMax - popup.lastItemRect.yMax) < .1f, geometry.row + "-actual-last-leaf-aligns-popup-bottom", checks);
                var mainLeaf = CheckCustomFocus(report, source, CustomProbeRows + geometry.row, false);
                CustomRequire(mainLeaf.sourceSpriteMatches && mainLeaf.sourceColorMatches && mainLeaf.alpha > 0, geometry.row + "-open-popup-main-Name-default-source-sprite-alpha", checks);
                customAudioScope = geometry.row + "-highlight"; soundStart = report.audioSamples.Count;
                customHeldAxis = 0; StepCustomizationAxis(1, 1, 0, 30); yield return WaitForCustomRender();
                RequireCustomSounds(report, checks, soundStart, 0);
                CustomRequire(customDropdownOption == 1 && CustomValue(row) == 0, geometry.row + "-highlight-navigation-preserves-chosen-value", checks);
                for (int i = 0; i < count; i++)
                {
                    var leaf = CheckCustomFocus(report, source, templatePath + "/Viewport/Content/Item", i == 1, items[i]);
                    CustomRequire(leaf.active && leaf.sourceSpriteMatches && leaf.sourceColorMatches && leaf.alpha > 0 && leaf.rect.height > 10, geometry.row + "-actual-option-" + i + "-source-focus-leaf-geometry-alpha", checks);
                    CustomRequire(items[i].Find("Item Checkmark").gameObject.activeSelf == (i == 0), geometry.row + "-option-" + i + "-chosen-checkmark-separated-from-highlight", checks);
                    var text = items[i].Find(geometry.itemLabelChild).GetComponent<TMP_Text>();
                    CustomRequire(text.gameObject.activeInHierarchy && text.color.a > 0 && text.rectTransform.rect.height > 10 && text.text == NativeLocalization.Get(source.textBindings.Single(r => r.rowKey == CustomSourceKey(CustomProbeRows + geometry.row)).bindings.Single(b => b.kind == "dropdown").optionKeys[i]), geometry.row + "-option-" + i + "-visible-label-leaf", checks);
                }
                yield return CaptureSettingsUi("customization-" + geometry.row + "-popup.png");
                customAudioScope = geometry.row + "-outside-close"; soundStart = report.audioSamples.Count;
                yield return CustomPointerSend(EventType.MouseDown, new Vector2(1, 1));
                CustomRequire(customDropdownOpen, geometry.row + "-blocker-does-not-close-on-MouseDown", checks);
                yield return CustomPointerSend(EventType.MouseUp, new Vector2(1, 1));
                CustomRequire(!customDropdownOpen && !template.gameObject.activeSelf, geometry.row + "-outside-MouseUp-closes-source-blocker", checks);
                RequireCustomSounds(report, checks, soundStart, 0);
                yield return WaitForCustomRender();
                var restored = CheckCustomFocus(report, source, CustomProbeRows + geometry.row, true);
                CustomRequire(restored.sourceSpriteMatches && restored.sourceColorMatches, geometry.row + "-closed-popup-restores-row-focus", checks);
                customAudioScope = geometry.row + "-keyboard-cancel"; OpenCustomDropdown(row); yield return WaitForCustomRender(); soundStart = report.audioSamples.Count;
                CloseCustomDropdown(true); RequireCustomSounds(report, checks, soundStart, 2, 0);
                soundStart = report.audioSamples.Count; CloseCustomDropdown(true); RequireCustomSounds(report, checks, soundStart);
                customAudioScope = geometry.row + "-pointer-confirm"; OpenCustomDropdown(row); yield return WaitForCustomRender(); soundStart = report.audioSamples.Count;
                var secondHit = SettingsPointerRect((RectTransform)SettingsPointerNode("selection", templatePath + "/Viewport/Content").Cast<Transform>().First(t => t.name == "Native custom option" && t.gameObject.activeInHierarchy));
                yield return CustomPointerSend(EventType.MouseDown, secondHit.center); yield return CustomPointerSend(EventType.MouseUp, secondHit.center);
                RequireCustomSounds(report, checks, soundStart, 0);
                customAudioScope = geometry.row + "-keyboard-confirm"; OpenCustomDropdown(row); yield return WaitForCustomRender(); soundStart = report.audioSamples.Count;
                ConfirmCustomDropdown(1); RequireCustomSounds(report, checks, soundStart, 0);
            }
            // The source CustomRace rows fit the 950-unit viewport. This explicit overflow fixture exercises the same production Scrollbar controls.
            customAudioScope = "scrollbar-fixture-reset"; hud.ResetCustomizationFocus(); yield return WaitForCustomRender();
            var scroll = SettingsPointerNode("selection", CustomProbeScroll);
            var viewport = SettingsPointerNode("selection", CustomProbeScroll + "/Viewport");
            report.sourceViewportHeight = viewport.rect.height; customProbeOriginalScrollSize = scroll.sizeDelta;
            hud.CustomBeforeLayoutVerification = () => scroll.sizeDelta = new Vector2(scroll.sizeDelta.x, 300);
            report.scrollbarOverflowFixture = true; report.fixtureViewportHeight = 300;
            yield return WaitForCustomRender();
            var contentNode = SettingsPointerNode("selection", CustomProbeScroll + "/Viewport/Content");
            var handle = SettingsPointerNode("selection", CustomProbeScroll + "/Scrollbar Vertical/Sliding Area/Handle");
            var slide = SettingsPointerNode("selection", CustomProbeScroll + "/Scrollbar Vertical/Sliding Area");
            Rect h = SettingsPointerRect(handle), s = SettingsPointerRect(slide);
            var bar = SettingsPointerNode("selection", CustomProbeScroll + "/Scrollbar Vertical");
            Rect barRect = SettingsPointerRect(bar);
            report.scrollbarActive = bar.gameObject.activeInHierarchy; report.handleCenterInsideBar = barRect.Contains(h.center);
            report.actualFixtureViewportHeight = viewport.rect.height; report.scrollbarContentHeight = contentNode.rect.height;
            report.scrollbarRect = barRect; report.handleRect = h; report.slideRect = s;
            CustomRequire(report.scrollbarActive && report.handleCenterInsideBar && contentNode.rect.height > viewport.rect.height,
                "custom-source-overflow-fixture-visible-bar-and-real-handle-hit", checks);
            hud.CustomScrollbarPointerObserved = (incoming, resulting, position, hot) => report.scrollbarEvents.Add(new CustomScrollbarEventProbe {
                incomingType = incoming.ToString(), resultingType = resulting.ToString(), position = position, hotControl = hot, frame = Time.frameCount });
            yield return CustomPointerSend(EventType.MouseDown, h.center);
            var capturedDown = report.scrollbarEvents.Last(e => e.incomingType == EventType.MouseDown.ToString());
            CustomRequire(capturedDown.hotControl != 0 && capturedDown.resultingType == EventType.Used.ToString(), "custom-source-scrollbar-pointer-captures-hotControl", checks);
            var end = new Vector2(h.center.x, s.yMax - h.height * .5f);
            yield return CustomPointerSend(EventType.MouseDrag, end); yield return CustomPointerSend(EventType.MouseUp, end);
            report.afterDrag = contentNode.anchoredPosition.y; report.hotControlAfterRelease = report.scrollbarEvents.Last(e => e.incomingType == EventType.MouseUp.ToString()).hotControl;
            CustomRequire(report.afterDrag > 100 && report.hotControlAfterRelease == 0, "custom-source-scrollbar-real-drag-and-release", checks);
            h = SettingsPointerRect(handle); s = SettingsPointerRect(slide);
            yield return CustomPointerSend(EventType.MouseDown, h.center); end = new Vector2(h.center.x, s.y + h.height * .5f);
            yield return CustomPointerSend(EventType.MouseDrag, end); yield return CustomPointerSend(EventType.MouseUp, end);
            h = SettingsPointerRect(handle); s = SettingsPointerRect(slide); var track = new Vector2(h.center.x, s.yMax - 1);
            yield return CustomPointerSend(EventType.MouseDown, track); report.afterTrack = contentNode.anchoredPosition.y;
            yield return null; yield return null; yield return new WaitForEndOfFrame(); report.afterHold = contentNode.anchoredPosition.y;
            yield return CustomPointerSend(EventType.MouseUp, track);
            CustomRequire(report.afterTrack > 0 && report.afterHold > report.afterTrack && report.scrollbarEvents.Last(e => e.incomingType == EventType.MouseUp.ToString()).hotControl == 0, "custom-source-scrollbar-track-hold-and-release", checks);
            hud.CustomBeforeLayoutVerification = null;
            scroll.sizeDelta = customProbeOriginalScrollSize.Value; customProbeOriginalScrollSize = null;
            customAudioScope = "scrollbar-fixture-reset"; hud.ResetCustomizationFocus(); yield return WaitForCustomRender();
        }
    }
}
