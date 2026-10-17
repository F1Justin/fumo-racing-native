using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.UI;
using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class SettingsFocusField
        {
            public string rowKey, target, expectedSpriteGuid, actualSpriteName, textLeaf, actualRowName;
            public int actualRowInstanceId, actualImageInstanceId;
            public long sourceComponentId;
            public bool focused, spriteMatchesSource, textMatchesSource, active;
            public Color actualTextColor;
            public Rect rect;
        }
        [Serializable] sealed class SettingsFocusBinding
        {
            public string rowKey, target, selectedSpriteGuid, defaultSpriteGuid;
            public long sourceComponentId;
        }
        [Serializable] sealed class SettingsFocusBindings { public SettingsFocusBinding[] rows; }
        [Serializable] sealed class SettingsPopupGeometry
        {
            public string rowKey;
            public int optionCount;
            public float sourceItemHeight, sourceEdgeOffset, expectedContentHeight, actualContentHeight, actualTemplateHeight;
            public Rect templatePixels, firstItemPixels, lastItemPixels;
        }
        [Serializable] sealed class SettingsPointerReport
        {
            public bool passed, scriptedState = true, visualParityAccepted = false;
            public string error, cleanupError, inputRoute = "Event.current in production OnGUI/DrawSettings; IMGUI hotControl";
            public string[] checks;
            public List<SettingsFocusField> focusFields = new List<SettingsFocusField>();
            public int sourceFocusBindingCount;
            public List<SettingsPopupGeometry> popupGeometry = new List<SettingsPopupGeometry>();
            public float languageRowY, languageHeaderHeight, emptyProfileHeight, editProfileHeight;
            public float videoAfterDrag, videoAfterTrackClick, videoAfterHeldRepeat, editAfterDrag;
            public int framebufferWidth, framebufferHeight;
        }
        Event settingsPointerEvent;
        bool settingsPointerProbeActive;
        public bool SettingsPointerVerificationPassed { get; private set; }

        // The normal OnGUI renderer consumes this event through its production controls.
        void ApplySettingsPointerVerificationEvent()
        {
            if (!settingsPointerProbeActive || settingsPointerEvent == null || Event.current.type != EventType.Repaint) return;
            Event.current = settingsPointerEvent;
            settingsPointerEvent = null;
        }
        IEnumerator SettingsPointerSend(EventType type, Vector2 position)
        {
            settingsPointerEvent = new Event { type = type, button = 0, mousePosition = position };
            int remaining = 120;
            while (settingsPointerEvent != null && remaining-- > 0) yield return new WaitForEndOfFrame();
            if (settingsPointerEvent != null) throw new InvalidOperationException("production-OnGUI-event-dispatch-timeout");
            Canvas.ForceUpdateCanvases();
        }
        RectTransform SettingsPointerNode(string scene, string path)
        {
            var root = GameObject.Find("Original authored menus");
            var node = root ? root.transform.Find(scene + " authored Canvas/" + path) as RectTransform : null;
            if (!node) throw new InvalidOperationException("Missing authored node " + scene + "/" + path);
            return node;
        }
        Rect SettingsPointerRect(RectTransform node)
        {
            var corners = new Vector3[4]; node.GetWorldCorners(corners);
            float scale = Screen.height / 600f;
            return new Rect(corners[0].x / scale, (Screen.height - corners[1].y) / scale,
                (corners[2].x - corners[0].x) / scale, (corners[1].y - corners[0].y) / scale);
        }
        IEnumerator SettingsPointerDrag(string scene, string scrollView, bool bottom)
        {
            var handle = SettingsPointerNode(scene, scrollView + "/Scrollbar Vertical/Sliding Area/Handle");
            var slide = SettingsPointerNode(scene, scrollView + "/Scrollbar Vertical/Sliding Area");
            Rect h = SettingsPointerRect(handle), s = SettingsPointerRect(slide);
            Vector2 start = h.center;
            Vector2 end = new Vector2(start.x, bottom ? s.yMax - h.height * .5f : s.y + h.height * .5f);
            yield return SettingsPointerSend(EventType.MouseDown, start);
            yield return SettingsPointerSend(EventType.MouseDrag, end);
            yield return SettingsPointerSend(EventType.MouseUp, end);
        }
        internal IEnumerator CheckSettingsPointerLive()
        {
            var report = new SettingsPointerReport(); var checks = new List<string>();
            int previousVsync = QualitySettings.vSyncCount; QualitySettings.vSyncCount = 0;
            var savedProfiles = NativeSaveStore.Data.inputProfiles;
            var savedSettings = NativeSaveStore.Data.settings;
            bool previousPointer = hud.SuppressPointerInput;
            settingsPointerProbeActive = true; hud.SuppressPointerInput = false;
            var pending = new Stack<IEnumerator>(); pending.Push(CheckSettingsPointerCore(report, checks));
            try
            {
                while (true)
                {
                    bool next; object instruction = null;
                    try { next = pending.Peek().MoveNext(); if (next) instruction = pending.Peek().Current; }
                    catch (Exception failure) { report.error = failure.ToString(); break; }
                    if (!next)
                    {
                        pending.Pop();
                        if (pending.Count == 0) { report.passed = true; break; }
                        continue;
                    }
                    if (instruction is IEnumerator nested && !(instruction is CustomYieldInstruction)) { pending.Push(nested); continue; }
                    yield return instruction;
                }
            }
            finally
            {
                settingsPointerEvent = null; settingsPointerProbeActive = false;
                SettingsPointerCleanup(() => { if (hud != null && hud.SettingsAuthoredReady) hud.CloseSettingsDropdown(); }, report);
                SettingsPointerCleanup(NativeInputBindings.CancelEdit, report);
                settingsOpen = false; settingsNameEditing = false;
                SettingsPointerCleanup(DisposeLocalSelection, report); localSelectionVerification=false;
                NativeSaveStore.Data.inputProfiles = savedProfiles; NativeSaveStore.Data.settings = savedSettings;
                SettingsPointerCleanup(NativeInputBindings.Initialize, report);
                if (hud != null) hud.SuppressPointerInput = previousPointer;
                QualitySettings.vSyncCount = previousVsync;
                SettingsPointerCleanup(() => { if (hud != null) hud.SetMenuPage(NativeMenuPage.Main); }, report);
                report.checks = checks.ToArray(); report.framebufferWidth = Screen.width; report.framebufferHeight = Screen.height;
                SettingsPointerVerificationPassed = report.passed;
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "settings-pointer-report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("SETTINGS_POINTER_RESULT passed=" + report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
        void CheckSettingsFocusLeaf(SettingsPointerReport report, List<string> checks, string scene, string rowPath, bool focused, Transform actualRow = null)
        {
            string rowKey = scene + "/Canvas/" + rowPath;
            var source = JsonUtility.FromJson<SettingsFocusBindings>(NativeContent.Load<TextAsset>("NativeMenu/settings-focus-source").text);
            report.sourceFocusBindingCount = source.rows.Length;
            var binding = source.rows.Single(entry => entry.rowKey == rowKey);
            var data = NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");
            if (!actualRow) actualRow = SettingsPointerNode(scene, rowPath);
            var image = actualRow.Find(binding.target).GetComponent<Image>();
            string guid = focused ? binding.selectedSpriteGuid : binding.defaultSpriteGuid;
            Sprite expected = guid == "be8a4e5ff4bb7014e9321941f176decc" ? NativeContent.Load<Sprite>("NativeMenu/SettingsSelectedDropdown") : data.nodes.First(node => node.spriteGuid == guid).sprite;
            string leaf = binding.target == "Item Background" ? "Item Label" : "Name/Property";
            var text = actualRow.Find(leaf).GetComponent<TMP_Text>();
            var sourceText = data.nodes.Single(node => node.scene == scene && node.path == "Canvas/" + rowPath + "/" + leaf);
            var field = new SettingsFocusField { rowKey = rowKey, target = binding.target,
                expectedSpriteGuid = guid, actualSpriteName = image.sprite ? image.sprite.name : "", sourceComponentId = binding.sourceComponentId,
                actualRowName = actualRow.name, actualRowInstanceId = actualRow.GetInstanceID(), actualImageInstanceId = image.GetInstanceID(),
                focused = focused, spriteMatchesSource = expected && image.sprite == expected, textLeaf = leaf,
                textMatchesSource = text.color == sourceText.fontColor, actualTextColor = text.color,
                active = image.gameObject.activeInHierarchy, rect = SettingsPointerRect(image.rectTransform) };
            report.focusFields.Add(field);
            SettingsUiRequire(field.spriteMatchesSource && field.textMatchesSource, rowKey + "-focus-source-target-sprite-and-text", checks);
            if (focused) SettingsUiRequire(field.active && field.rect.width > 10 && field.rect.height > 10 && image.color.a > 0,
                rowKey + "-focus-leaf-visible", checks);
        }
        void CheckSettingsArrowLeaf(SettingsPointerReport report, List<string> checks, string scene, string rowPath)
        {
            var data = NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");
            string path = rowPath + "/Value/Dropdown/Arrow";
            var node = SettingsPointerNode(scene, path); var image = node.GetComponent<Image>();
            var source = data.nodes.Single(entry => entry.scene == scene && entry.path == "Canvas/" + path);
            var field = new SettingsFocusField { rowKey = scene + "/Canvas/" + rowPath, target = "Value/Dropdown/Arrow",
                expectedSpriteGuid = source.spriteGuid, actualSpriteName = image.sprite ? image.sprite.name : "",
                actualRowName = node.name, actualRowInstanceId = node.GetInstanceID(), actualImageInstanceId = image.GetInstanceID(),
                spriteMatchesSource = source.sprite && image.sprite == source.sprite, active = node.gameObject.activeInHierarchy,
                rect = SettingsPointerRect(node) };
            report.focusFields.Add(field);
            SettingsUiRequire(field.active && field.spriteMatchesSource && image.color == source.color && field.rect.width > 2 && field.rect.height > 2,
                field.rowKey + "-actual-arrow-leaf-source-sprite-color-and-rect", checks);
        }
        Transform[] CheckSettingsPopupGeometry(SettingsPointerReport report, List<string> checks, string scene, string rowPath)
        {
            string templatePath = rowPath + "/Value/Dropdown/Template";
            var template = SettingsPointerNode(scene, templatePath);
            var content = SettingsPointerNode(scene, templatePath + "/Viewport/Content");
            var items = content.Cast<Transform>().Where(child => child.name == "Native option" && child.gameObject.activeInHierarchy).ToArray();
            int count = settingsRows[settingsIndex].control.options.Length;
            SettingsUiRequire(items.Length == count && !content.Find("Item").gameObject.activeSelf,
                rowPath.StartsWith("LanguageOptions/", StringComparison.Ordinal) ? "language-popup-visible-production-clones-and-inactive-source-template" : scene + "/" + rowPath + "-visible-production-clones-and-inactive-source-template", checks);
            var data = NativeContent.Load<NativeAuthoredUiData>("NativeMenu/AuthoredUi");
            var sourceItem = data.nodes.Single(node => node.scene == scene && node.path == "Canvas/" + templatePath + "/Viewport/Content/Item");
            var sourceContent = data.nodes.Single(node => node.scene == scene && node.path == "Canvas/" + templatePath + "/Viewport/Content");
            // TMP_Dropdown.Show uses itemSize * count plus the source item/content edge offsets.
            float offsetMin = -sourceItem.size.y * sourceItem.pivot.y + sourceContent.size.y * sourceContent.pivot.y + sourceItem.position.y;
            float offsetMax = sourceItem.size.y * (1 - sourceItem.pivot.y) - sourceContent.size.y * (1 - sourceContent.pivot.y) + sourceItem.position.y;
            float expected = sourceItem.size.y * count + offsetMin - offsetMax;
            float pixels = Screen.height / 600f;
            Func<RectTransform, Rect> pixelRect = node => { Rect r = SettingsPointerRect(node); return new Rect(r.x * pixels, r.y * pixels, r.width * pixels, r.height * pixels); };
            var geometry = new SettingsPopupGeometry { rowKey = scene + "/Canvas/" + rowPath, optionCount = count,
                sourceItemHeight = sourceItem.size.y, sourceEdgeOffset = offsetMin - offsetMax,
                expectedContentHeight = expected, actualContentHeight = content.rect.height, actualTemplateHeight = template.rect.height,
                templatePixels = pixelRect(template), firstItemPixels = pixelRect((RectTransform)items[0]), lastItemPixels = pixelRect((RectTransform)items[count - 1]) };
            report.popupGeometry.Add(geometry);
            SettingsUiRequire(Mathf.Abs(content.rect.height - expected) < .01f && Mathf.Abs(template.rect.height - expected) < .01f,
                geometry.rowKey + "-source-popup-preferred-height-without-added-padding", checks);
            SettingsUiRequire(Mathf.Abs(geometry.templatePixels.yMax - geometry.lastItemPixels.yMax) < .1f,
                geometry.rowKey + "-popup-bottom-aligns-last-item-edge", checks);
            return items;
        }
        static void SettingsPointerCleanup(Action action, SettingsPointerReport report)
        {
            try { action(); }
            catch (Exception failure)
            {
                report.cleanupError = (report.cleanupError ?? "") + failure + "\n";
                report.passed = false;
            }
        }
        IEnumerator CheckSettingsPointerCore(SettingsPointerReport report, List<string> checks)
        {
            phase = Phase.Menu; autopilot = false;
            yield return null;
            int readyFrames = 120;
            while ((hud == null || !hud.SettingsAuthoredReady) && readyFrames-- > 0) yield return null;
            SettingsUiRequire(hud != null && hud.SettingsAuthoredReady, "first-production-OnGUI-created-authored-settings", checks);
            Screen.SetResolution(1280, 800, false);
            Directory.CreateDirectory(reportDirectory);
            NativeSaveStore.Data.inputProfiles = new NativeSaveData.InputProfile[0];
            OpenSettings(); yield return WaitForSettingsRender();
            settingsIndex=1;
            var rootConfirm=SettingsPointerRect(SettingsPointerNode("menu2", "OptionsMenu/Prompts/AText"));
            yield return SettingsPointerSend(EventType.MouseDown,rootConfirm.center);
            yield return SettingsPointerSend(EventType.MouseUp,rootConfirm.center);
            SettingsUiRequire(settingsPage==SettingsPage.Audio,"root-select-prompt-click-opens-selected-page",checks);
            yield return WaitForSettingsRender();
            var settingsBack=new Vector2(802.5f,557);
            yield return SettingsPointerSend(EventType.MouseDown,settingsBack);
            yield return SettingsPointerSend(EventType.MouseUp,settingsBack);
            SettingsUiRequire(settingsPage==SettingsPage.Root,"audio-back-prompt-click-returns-to-root",checks);
            yield return WaitForSettingsRender();
            var rootBack=SettingsPointerRect(SettingsPointerNode("menu2", "OptionsMenu/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,rootBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,rootBack.center);
            SettingsUiRequire(!settingsOpen,"root-back-prompt-click-closes-settings",checks);
            OpenSettings(); SettingsGo(SettingsPage.Language);
            yield return WaitForSettingsRender();
            var header = SettingsPointerNode("menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/-CATEGORY");
            var language = SettingsPointerNode("menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language");
            report.languageHeaderHeight = header.rect.height; report.languageRowY = language.anchoredPosition.y;
            SettingsUiRequire(header.gameObject.activeInHierarchy && Mathf.Abs(header.rect.height - 56) < .01f && Mathf.Abs(language.anchoredPosition.y + 56) < .01f,
                "language-original-blank-category-and-row-offset", checks);
            CheckSettingsFocusLeaf(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language", true);
            CheckSettingsArrowLeaf(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language");
            yield return CaptureSettingsUi("settings-pointer-language.png");
            Rect row = SettingsPointerRect(language);
            yield return SettingsPointerSend(EventType.MouseDown, row.center);
            yield return SettingsPointerSend(EventType.MouseUp, row.center);
            SettingsUiRequire(settingsDropdownOpen, "language-dropdown-opens-through-pointer", checks);
            yield return WaitForSettingsRender();
            CheckSettingsFocusLeaf(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language", false);
            var popupRows = CheckSettingsPopupGeometry(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language");
            CheckSettingsFocusLeaf(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language/Value/Dropdown/Template/Viewport/Content/Item", true, popupRows[settingsDropdownOption]);
            CheckSettingsFocusLeaf(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language/Value/Dropdown/Template/Viewport/Content/Item", false, popupRows[settingsDropdownOption == 0 ? 1 : 0]);
            yield return CaptureSettingsUi("settings-pointer-language-dropdown.png");
            yield return SettingsPointerSend(EventType.MouseDown, new Vector2(5, 5));
            yield return SettingsPointerSend(EventType.MouseUp, new Vector2(5, 5));
            SettingsUiRequire(!settingsDropdownOpen, "language-dropdown-outside-pointer-closes", checks);
            yield return WaitForSettingsRender();
            CheckSettingsFocusLeaf(report, checks, "menu2", "LanguageOptions/VideoBox/Scroll View/Viewport/Content/Language", true);
            yield return CaptureSettingsUi("settings-pointer-language-restored.png");

            SettingsGo(SettingsPage.Video); yield return WaitForSettingsRender();
            const string video = "VideoOptions/VideoBox/Scroll View";
            CheckSettingsFocusLeaf(report, checks, "menu2", video + "/Viewport/Content/Mode", true);
            CheckSettingsFocusLeaf(report, checks, "menu2", video + "/Viewport/Content/Resolution", false);
            CheckSettingsArrowLeaf(report, checks, "menu2", video + "/Viewport/Content/Mode");
            yield return CaptureSettingsUi("settings-pointer-video-focus.png");
            var content = SettingsPointerNode("menu2", video + "/Viewport/Content");
            var viewport = SettingsPointerNode("menu2", video + "/Viewport");
            yield return SettingsPointerDrag("menu2", video, true);
            report.videoAfterDrag = content.anchoredPosition.y;
            SettingsUiRequire(Mathf.Abs(report.videoAfterDrag - (content.rect.height - viewport.rect.height)) < .1f,
                "video-production-handle-drag-reaches-bottom", checks);
            yield return CaptureSettingsUi("settings-pointer-video-bottom.png");
            yield return SettingsPointerDrag("menu2", video, false);
            SettingsUiRequire(Mathf.Abs(content.anchoredPosition.y) < .1f, "video-production-handle-drag-reaches-top", checks);
            Rect bar = SettingsPointerRect(SettingsPointerNode("menu2", video + "/Scrollbar Vertical"));
            Vector2 track = new Vector2(bar.center.x, bar.yMax - 2);
            yield return SettingsPointerSend(EventType.MouseDown, track);
            report.videoAfterTrackClick = content.anchoredPosition.y;
            SettingsUiRequire(report.videoAfterTrackClick > .1f, "video-track-pointer-down-scrolls", checks);
            for (int i = 0; i < 5; i++) yield return null;
            yield return new WaitForEndOfFrame();
            report.videoAfterHeldRepeat = content.anchoredPosition.y;
            SettingsUiRequire(report.videoAfterHeldRepeat > report.videoAfterTrackClick + .1f, "video-held-track-click-repeats", checks);
            yield return SettingsPointerSend(EventType.MouseUp, track);
            float releasedScroll = content.anchoredPosition.y;
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            SettingsUiRequire(Mathf.Abs(content.anchoredPosition.y - releasedScroll) < .1f, "video-track-repeat-stops-after-pointer-up", checks);
            yield return SettingsPointerDrag("menu2", video, false);
            var mode = SettingsPointerNode("menu2", video + "/Viewport/Content/Mode");
            row = SettingsPointerRect(mode);
            yield return SettingsPointerSend(EventType.MouseDown, row.center);
            yield return SettingsPointerSend(EventType.MouseUp, row.center);
            SettingsUiRequire(settingsDropdownOpen, "video-dropdown-opens-through-pointer", checks);
            yield return WaitForSettingsRender();
            CheckSettingsFocusLeaf(report, checks, "menu2", video + "/Viewport/Content/Mode", false);
            popupRows = CheckSettingsPopupGeometry(report, checks, "menu2", video + "/Viewport/Content/Mode");
            CheckSettingsFocusLeaf(report, checks, "menu2", video + "/Viewport/Content/Mode/Value/Dropdown/Template/Viewport/Content/Item", true, popupRows[settingsDropdownOption]);
            CheckSettingsFocusLeaf(report, checks, "menu2", video + "/Viewport/Content/Mode/Value/Dropdown/Template/Viewport/Content/Item", false, popupRows[settingsDropdownOption == 0 ? 1 : 0]);
            yield return CaptureSettingsUi("settings-pointer-video-dropdown.png");
            yield return SettingsPointerSend(EventType.MouseDown, new Vector2(5, 5));
            yield return SettingsPointerSend(EventType.MouseUp, new Vector2(5, 5));
            SettingsUiRequire(!settingsDropdownOpen, "video-dropdown-outside-pointer-closes", checks);
            yield return WaitForSettingsRender();
            CheckSettingsFocusLeaf(report, checks, "menu2", video + "/Viewport/Content/Mode", true);

            SettingsGo(SettingsPage.Audio); yield return WaitForSettingsRender();
            CheckSettingsFocusLeaf(report, checks, "menu2", "AudioOptions/VideoBox/Scroll View/Viewport/Content/MasterVolumeSlider", true);
            CheckSettingsFocusLeaf(report, checks, "menu2", "AudioOptions/VideoBox/Scroll View/Viewport/Content/SFXVolumeSlider", false);
            yield return CaptureSettingsUi("settings-pointer-audio-focus.png");
            SettingsGo(SettingsPage.Profiles); yield return WaitForSettingsRender();
            const string profiles = "Profiles/ProfilesBox/Scroll View";
            content = SettingsPointerNode("profiles", profiles + "/Viewport/Content");
            report.emptyProfileHeight = content.rect.height;
            SettingsUiRequire(settingsRows.Count == 1 && Mathf.Abs(content.rect.height - 56) < .1f,
                "empty-profiles-preferred-single-row-height", checks);
            var profileClone = content.Cast<Transform>().Single(child => child.gameObject.activeSelf);
            CheckSettingsFocusLeaf(report, checks, "profiles", profiles + "/Viewport/Content/CreateNewProfile", true, profileClone);
            yield return CaptureSettingsUi("settings-pointer-empty-profiles.png");
            NativeSaveStore.Data.inputProfiles = new[] { new NativeSaveData.InputProfile {
                index = 0, profileId = "settings-pointer-keyboard", name = "Pointer verification", devicePath = "<Keyboard>", overrides = new NativeSaveData.InputOverride[0] } };
            EditSettingsProfile("settings-pointer-keyboard"); yield return WaitForSettingsRender();
            const string edit = "Rebindings/ProfilesBox/Scroll View";
            content = SettingsPointerNode("profiles", edit + "/Viewport/Content");
            viewport = SettingsPointerNode("profiles", edit + "/Viewport");
            report.editProfileHeight = content.rect.height;
            CheckSettingsFocusLeaf(report, checks, "profiles", edit + "/Viewport/Content/HorizontalLeft", true);
            CheckSettingsFocusLeaf(report, checks, "profiles", edit + "/Viewport/Content/HorizontalRight", false);
            yield return CaptureSettingsUi("settings-pointer-rebindings-focus.png");
            SettingsUiRequire(settingsRows.Count == 18 && Mathf.Abs(content.rect.height - 19 * 56) < .1f,
                "rebindings-header-plus-eighteen-preferred-rows", checks);
            yield return SettingsPointerDrag("profiles", edit, true);
            report.editAfterDrag = content.anchoredPosition.y;
            SettingsUiRequire(Mathf.Abs(report.editAfterDrag - (content.rect.height - viewport.rect.height)) < .1f,
                "rebindings-production-drag-reaches-final-row-without-trailing-viewport", checks);
            yield return CaptureSettingsUi("settings-pointer-rebindings-bottom.png");
            NativeInputBindings.CancelEdit(); settingsEditingId = "settings-pointer-keyboard";
            SettingsGo(SettingsPage.DeleteProfile); yield return WaitForSettingsRender();
            CheckSettingsFocusLeaf(report, checks, "profiles", "Profiles/ConfirmDeletionPanel/False", true);
            CheckSettingsFocusLeaf(report, checks, "profiles", "Profiles/ConfirmDeletionPanel/True", false);
            yield return CaptureSettingsUi("settings-pointer-delete-focus.png");
            var profileBack=SettingsPointerRect(SettingsPointerNode("profiles","Profiles/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,profileBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,profileBack.center);
            SettingsUiRequire(settingsPage==SettingsPage.Profiles,"delete-profile-back-prompt-returns-to-list",checks);
            yield return WaitForSettingsRender();
            profileBack=SettingsPointerRect(SettingsPointerNode("profiles","Profiles/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,profileBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,profileBack.center);
            SettingsUiRequire(!settingsOpen,"profiles-back-prompt-closes-settings",checks);
            hud.SetMenuPage(NativeMenuPage.RaceModes); yield return WaitForSettingsRender();
            var menuBack=SettingsPointerRect(SettingsPointerNode("menu2","RaceMenu/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,menuBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,menuBack.center);
            SettingsUiRequire(hud.MenuPage==NativeMenuPage.Main,"race-mode-back-prompt-returns-to-main",checks);
            hud.SetMenuPage(NativeMenuPage.Extras); yield return WaitForSettingsRender();
            menuBack=SettingsPointerRect(SettingsPointerNode("menu2","ExtrasMenu/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,menuBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,menuBack.center);
            SettingsUiRequire(hud.MenuPage==NativeMenuPage.Main,"extras-back-prompt-returns-to-main",checks);
            localSelectionVerification=true; selectedMode=NativeSessionMode.TimeTrials;
            hud.SetMenuPage(NativeMenuPage.Characters); BeginLocalSelection(); yield return WaitForSettingsRender();
            var selectionConfirm=SettingsPointerRect(SettingsPointerNode("selection","CharSelection/Prompts/AText"));
            var selectionBack=SettingsPointerRect(SettingsPointerNode("selection","CharSelection/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,selectionConfirm.center);
            yield return SettingsPointerSend(EventType.MouseUp,selectionConfirm.center);
            SettingsUiRequire(localSelection.ReadBox(0).Selection==NativeLocalSelectionState.Stage.Vehicle,"local-character-pointer-confirm-enters-vehicle",checks);
            yield return CaptureSettingsUi("pointer-local-vehicle.png");
            yield return SettingsPointerSend(EventType.MouseDown,selectionConfirm.center);
            yield return SettingsPointerSend(EventType.MouseUp,selectionConfirm.center);
            SettingsUiRequire(localSelection.ReadBox(0).Selection==NativeLocalSelectionState.Stage.Skin,"local-vehicle-pointer-confirm-enters-skin",checks);
            yield return SettingsPointerSend(EventType.MouseDown,selectionBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,selectionBack.center);
            SettingsUiRequire(localSelection.ReadBox(0).Selection==NativeLocalSelectionState.Stage.Vehicle,"local-skin-pointer-back-enters-vehicle",checks);
            yield return SettingsPointerSend(EventType.MouseDown,selectionBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,selectionBack.center);
            SettingsUiRequire(localSelection.ReadBox(0).Selection==NativeLocalSelectionState.Stage.Character,"local-vehicle-pointer-back-enters-character",checks);
            yield return SettingsPointerSend(EventType.MouseDown,selectionBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,selectionBack.center);
            SettingsUiRequire(!localSelectionActive && hud.MenuPage==NativeMenuPage.Main,"local-character-pointer-back-returns-to-main",checks);
            DisposeLocalSelection(); localSelectionVerification=false;
            hud.SetMenuPage(NativeMenuPage.Tracks); yield return WaitForSettingsRender();
            menuBack=SettingsPointerRect(SettingsPointerNode("selection","CupSelection/Prompts/BText"));
            yield return SettingsPointerSend(EventType.MouseDown,menuBack.center);
            yield return SettingsPointerSend(EventType.MouseUp,menuBack.center);
            SettingsUiRequire(hud.MenuPage==NativeMenuPage.Cups,"track-pointer-back-returns-to-cup",checks);
            SettingsUiRequire(report.sourceFocusBindingCount == 66, "all-source-focus-bindings-loaded", checks);
            SettingsUiRequire(Screen.width == 1280 && Screen.height == 800, "same-source-framebuffer-1280x800", checks);
        }
    }
}
