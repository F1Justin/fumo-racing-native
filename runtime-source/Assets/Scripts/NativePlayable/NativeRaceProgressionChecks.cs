using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ProgressionSettingsLiveReport
        {
            public bool passed;
            public string track, phase, savePath, presentationPolicy;
            public string[] checks;
            public NativeSceneSettings.EffectReport[] effects;
            public int screenWidth, screenHeight;
            public bool fullscreen;
            public string language;
            public NativeSaveData save;
        }
        void FinishProgressionSettingsReport(string filename, List<NativeSceneSettings.EffectReport> effects, string probePhase = "live")
        {
            ModeCheck(errorCount == 0, "runtime errors=" + errorCount);
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory, filename), JsonUtility.ToJson(new ProgressionSettingsLiveReport {
                passed = modePassed, track = trackId, phase = probePhase, savePath = NativeSaveStore.FilePath,
                presentationPolicy = automatedSettingsPresentation ? "automated-background-vsync-off" : "user-settings",
                checks = modeChecks.ToArray(), effects = effects.ToArray(), screenWidth = Screen.width, screenHeight = Screen.height,
                fullscreen = Screen.fullScreen, language = NativeLocalization.LanguageName, save = NativeSaveStore.Data }, true));
            Debug.Log("NATIVE_SETTINGS_LIVE_RESULT " + filename + " passed=" + modePassed);
            Application.Quit(modePassed ? 0 : 2);
        }
        IEnumerator CheckSettingsWithBackgroundPresentation(IEnumerator verification)
        {
            // Automated background windows verify saved settings and synchronous application;
            // frame presentation uses VSync off because Metal completion can stall in this environment.
            while (verification.MoveNext())
            {
                QualitySettings.vSyncCount = 0;
                var nested = verification.Current as IEnumerator;
                if (nested != null) yield return CheckSettingsWithBackgroundPresentation(nested);
                else yield return verification.Current;
            }
            QualitySettings.vSyncCount = 0;
        }
        IEnumerator CheckSettingsLive()
        {
            var effects = new List<NativeSceneSettings.EffectReport>();
            string savePath = NativeSaveStore.FilePath;
            NativeSettingsVerification.Result rules;
            try { rules = NativeSettingsVerification.Run(Path.Combine(reportDirectory, "settings-rules-isolated")); }
            finally
            {
                NativeSaveStore.ResetForVerification(); NativeSaveStore.Initialize(new[] { "--save-directory", Path.GetDirectoryName(savePath) });
                NativeInputBindings.Initialize(); NativeLocalization.ResetForVerification();
            }
            ModeCheck(rules.passed, "isolated settings rules and input profiles: " + rules.error);
            ModeCheck(NativeSaveStore.FilePath == savePath, "rule fixture restored live save directory");
            ModeCheck(NativeSettingsVerification.RunScene(this, reportDirectory), "actual engine quality, lighting, postprocessing consumers");
            yield return CheckSettingsUiLive();
            ModeCheck(SettingsUiVerificationPassed, "real settings and profile UI activation, edit, confirm, cancel and delete");
            OpenSettings();
            ModeCheck(SettingsOpen && settingsRows.Count == 4, "real settings root has category rows");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-root.png");
            foreach (var definition in NativeUserSettings.Definitions)
            {
                bool saved = definition.floating ? NativeUserSettings.TrySetFloat(definition.key, definition.minimum) : NativeUserSettings.TrySetInt(definition.key, (int)definition.minimum);
                var low = NativeUserSettings.ApplyScene(this); effects.Add(low);
                bool lowRead = Math.Abs(NativeUserSettings.GetFloat(definition.key) - definition.minimum) < .00001;
                saved &= definition.floating ? NativeUserSettings.TrySetFloat(definition.key, definition.maximum) : NativeUserSettings.TrySetInt(definition.key, (int)definition.maximum);
                var high = NativeUserSettings.ApplyScene(this); effects.Add(high);
                bool highRead = Math.Abs(NativeUserSettings.GetFloat(definition.key) - definition.maximum) < .00001;
                ModeCheck(saved && lowRead && highRead && low != null && high != null, "persisted range endpoints and scene application: " + definition.key);
            }
            ModeCheck(NativeUserSettings.TrySetInt("video_grass", 0), "grass on setting saved");
            var grassOn = NativeUserSettings.ApplyScene(this); effects.Add(grassOn);
            var grass = gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .Where(renderer => renderer.sharedMaterials.Any(material => material && material.shader && material.shader.name == "Fumo/NativeGrass")).ToArray();
            bool on = grass.Length > 0 && grass.All(renderer => renderer.enabled);
            ModeCheck(NativeUserSettings.TrySetInt("video_grass", 1), "grass off setting saved");
            var grassOff = NativeUserSettings.ApplyScene(this); effects.Add(grassOff);
            ModeCheck(on && grass.All(renderer => !renderer.enabled), "actual native grass renderers toggle on and off");
            NativeUserSettings.TrySetInt("video_particlequality", 0); NativeUserSettings.TrySetInt("video_particlelighting", 0); NativeUserSettings.ApplyScene(this);
            var particles = gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ParticleSystem>(true)).Where(system => system.GetComponent<ParticleSystemRenderer>() != null).ToArray();
            var originalCounts = particles.Select(system => system.main.maxParticles).ToArray();
            NativeUserSettings.TrySetInt("video_particlequality", 2); NativeUserSettings.TrySetInt("video_particlelighting", 1);
            var particleLow = NativeUserSettings.ApplyScene(this); effects.Add(particleLow);
            ModeCheck(particles.Length > 0 && particles.Select((system, index) => system.main.maxParticles == Math.Max(1, Mathf.RoundToInt(originalCounts[index] * .25f))).All(value => value) && particleLow.litParticles == 0, "actual particle budgets reduce to quarter and disable lighting");
            foreach (string key in new[] { "video_bloom", "video_ao", "video_cc", "video_ppquality" }) ModeCheck(NativeUserSettings.TrySetInt(key, 0), key + " enabled save");
            var postOn = NativeUserSettings.ApplyScene(this); effects.Add(postOn);
            foreach (string key in new[] { "video_bloom", "video_ao", "video_cc", "video_ppquality" }) ModeCheck(NativeUserSettings.TrySetInt(key, 1), key + " disabled save");
            var postOff = NativeUserSettings.ApplyScene(this); effects.Add(postOff);
            ModeCheck(NativeContent.Load<Shader>("NativeSettings/post") != null && raceCamera.GetComponent<NativeSceneSettings>() != null && postOn.bloom && postOn.ao && postOn.cc && postOn.highPostQuality && !postOff.bloom && !postOff.ao && !postOff.cc && !postOff.highPostQuality, "real camera post consumer receives all enabled and disabled flags");
            ModeCheck(NativeUserSettings.TrySetInt("fov", 95) && NativeUserSettings.TrySetInt("vsync", 1), "FOV and vsync saved");
            effects.Add(NativeUserSettings.ApplyScene(this));
            ModeCheck(Math.Abs(raceCamera.fieldOfView - 95) < .01f && NativeUserSettings.GetInt("vsync") == 1 && QualitySettings.vSyncCount == 1, "saved VSync request and synchronous camera FOV and engine application");
            ModeCheck(NativeUserSettings.TrySetInt("vsync", 0), "vsync zero saved"); NativeUserSettings.ApplyScene(this);
            ModeCheck(QualitySettings.vSyncCount == 0, "actual engine vsync disabled");
            InitializeSettingsResolutionOptions();int retainedResolutionIndex=settingsResolutionIndex;
            var retainedResolutionLabels=(string[])settingsResolutionLabels.Clone();
            string retainedResolutionLabel=retainedResolutionLabels.Length==0?"":retainedResolutionLabels[retainedResolutionIndex];
            ModeCheck(NativeUserSettings.TrySetDisplay(960, 640, false, 60), "window resolution save requested");
            yield return WaitForDisplay(960, 640, false);
            SettingsGo(SettingsPage.Video); BuildSettingsRows();
            var resolutionRow=settingsRows.Single(row=>row.control!=null && row.control.node=="Resolution").control;
            ModeCheck(Screen.width==960 && Screen.height==640 && !Screen.fullScreen,"actual requested window dimensions");
            ModeCheck(resolutionRow.option==retainedResolutionIndex && resolutionRow.value==retainedResolutionLabel && resolutionRow.options.SequenceEqual(retainedResolutionLabels),"resolution source Awake cache survives external display change");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-window-960x640.png");
            ModeCheck(NativeUserSettings.TrySetDisplay(960, 640, true), "fullscreen save requested"); yield return WaitForDisplay(960, 640, true);
            BuildSettingsRows(); ModeCheck(Screen.fullScreen, "actual fullscreen transition");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-fullscreen.png");
            ModeCheck(NativeUserSettings.TrySetDisplay(960, 640, false, 60), "window restored"); yield return WaitForDisplay(960, 640, false);
            ModeCheck(!Screen.fullScreen, "actual return from fullscreen preserves settings UI");
            ModeCheck(NativeUserSettings.TrySetInt("lang_game", 0), "English saved"); SettingsGo(SettingsPage.Root);
            string english = SettingsPageTitle(SettingsPage.Root); yield return new WaitForSecondsRealtime(.2f); yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-english.png");
            ModeCheck(NativeUserSettings.TrySetInt("lang_game", 1), "Spanish saved"); BuildSettingsRows();
            string spanish = SettingsPageTitle(SettingsPage.Root);
            ModeCheck(english != spanish && NativeLocalization.Language == 1, "settings root displays original Spanish phrase");
            yield return new WaitForSecondsRealtime(.2f); yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-spanish.png");
            string customPath = Path.Combine(reportDirectory, "live-customlang.csv"); File.WriteAllText(customPath, "MENU_OPTIONS,Opciones personalizadas\nVIDEO_TEXTURE_FULL,Custom,comma\n");
            ModeCheck(NativeLocalization.ReloadCustom(customPath) && NativeUserSettings.TrySetInt("lang_game", 2), "Custom CSV loaded and selected"); BuildSettingsRows();
            ModeCheck(SettingsPageTitle(SettingsPage.Root) == "Opciones personalizadas" && NativeLocalization.Get("VIDEO_TEXTURE_FULL") == "Custom,comma", "live settings custom phrase and first-comma parsing");
            yield return new WaitForSecondsRealtime(.2f); yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-custom.png");
            ModeCheck(NativeSettingsVerification.PrepareCustomFontProbe(this, reportDirectory), "Custom Chinese source-font glyphs rendered through fallback");
            yield return new WaitForSecondsRealtime(.2f); yield return new WaitForEndOfFrame();
            ModeCheck(FindObjectsOfType<TMPro.TMP_Text>().Any(text => text.gameObject.activeInHierarchy && text.text.Contains("中文設定")), "Custom Chinese phrase reaches original Video category");
            CaptureTestScreenshot("settings-custom-chinese.png"); yield return null;
            NativeUserSettings.TrySetInt("lang_game", 0); BuildSettingsRows(); SettingsBack();
            ModeCheck(!SettingsOpen && hud.MenuPage == settingsReturnPage, "settings exits to original menu page");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-returned.png");
            FinishProgressionSettingsReport("settings-live-report.json", effects);
        }
        IEnumerator WaitForDisplay(int width, int height, bool fullscreen)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            do { yield return null; }
            while (Time.realtimeSinceStartup < deadline && (Screen.fullScreen != fullscreen || (!fullscreen && (Screen.width != width || Screen.height != height))));
            // AppKit completes the full-screen animation after Unity updates Screen.fullScreen.
            yield return new WaitForSecondsRealtime(Application.platform == RuntimePlatform.OSXPlayer ? 1.2f : .2f);
        }
        IEnumerator CheckSettingsRestartProbe(string[] args)
        {
            string probePhase = Argument(args, "--settings-restart-phase") ?? "read";
            var effects = new List<NativeSceneSettings.EffectReport>();
            if (probePhase == "write")
            {
                string csv=Path.Combine(Path.GetDirectoryName(NativeSaveStore.FilePath),"customlang.csv");File.WriteAllText(csv,"MENU_OPTIONS,Restart custom settings\nVIDEO_TEXTURE_FULL,Restart,comma\n");NativeLocalization.ResetForVerification();
                ModeCheck(NativeUserSettings.TrySetInt("qualityLevel",0),"probe quality preset persisted");
                ModeCheck(NativeUserSettings.TrySetInt("fov", 93) && NativeUserSettings.TrySetInt("video_grass", 1) && NativeUserSettings.TrySetInt("vsync", 0) && NativeUserSettings.TrySetInt("lang_game", 2) && NativeUserSettings.TrySetFloat("mainVolume", .42f), "probe settings persisted");
                string profileId = NativeInputBindings.CreateProfile("Restart probe keyboard", "<Keyboard>");
                ModeCheck(profileId.Length > 0 && NativeInputBindings.BeginEdit(profileId) && NativeInputBindings.SetDraftOverride(NativeInputBindings.ActionId(NativeInputBindings.Action.Accelerate), "<Keyboard>/a") && NativeInputBindings.ConfirmEdit() && NativeInputBindings.SelectProfile(profileId), "probe named input profile override and selection saved");
                ModeCheck(NativeUserSettings.WriteValue("native.probe.profileId", profileId, "string") && NativeUserSettings.WriteValue("native.probe.marker", "settings-restart-v2", "string") && NativeUserSettings.TrySetDisplay(960, 640, false, 60), "probe stable profile identity and display persisted");
            }
            else
            {
                ModeCheck(probePhase == "read", "read probe phase selected");
                NativeInputBindings.Initialize();
                string profileId = NativeUserSettings.ReadValue("native.probe.profileId");
                var profile = NativeInputBindings.Profiles.FirstOrDefault(value => value.profileId == profileId);
                ModeCheck(NativeUserSettings.ReadValue("native.probe.marker") == "settings-restart-v2" && NativeUserSettings.GetInt("fov") == 93 && NativeUserSettings.GetInt("video_grass") == 1 && NativeUserSettings.GetInt("vsync") == 0 && NativeUserSettings.GetInt("lang_game") == 2 && Math.Abs(NativeUserSettings.GetFloat("mainVolume") - .42f) < .00001, "separate process loaded exact saved settings");
                ModeCheck(profile != null && profile.name == "Restart probe keyboard" && profile.devicePath == "<Keyboard>" && profile.overrides.Any(value => value.actionId == NativeInputBindings.ActionId(NativeInputBindings.Action.Accelerate) && value.path == "<Keyboard>/a") && NativeInputBindings.SelectedProfile() == profileId, "separate process loaded exact profile override and selection");
            }
            effects.Add(NativeUserSettings.ApplyScene(this)); yield return new WaitForSecondsRealtime(1);
            ModeCheck(NativeUserSettings.ReadValue("native.screen.refreshRate")=="60" && NativeUserSettings.RequestedDisplayRefreshRate==60,"separate process saved and reapplied refresh-rate request");
            ModeCheck(Math.Abs(raceCamera.fieldOfView - 93) < .01f && QualitySettings.vSyncCount == 0 && Screen.width == 960 && Screen.height == 640 && !Screen.fullScreen && NativeLocalization.Language == 2, "loaded values apply to actual camera engine display and language");
            ModeCheck(NativeLocalization.Get("MENU_OPTIONS")=="Restart custom settings" && NativeLocalization.Get("VIDEO_TEXTURE_FULL")=="Restart,comma", "separate-process Custom CSV automatically loaded from writable save directory");
            OpenSettings(); SettingsGo(SettingsPage.Video);
            var hardwareResolutions=Screen.resolutions;
            int sourceResolutionIndex=Array.FindIndex(hardwareResolutions,r=>r.width==Screen.width && r.height==Screen.height && Mathf.Abs(r.refreshRate-Screen.currentResolution.refreshRate)<=2);
            if(sourceResolutionIndex<0)sourceResolutionIndex=0;
            var sourceResolutionLabels=hardwareResolutions.Select(r=>r.ToString()).ToArray();
            var restartResolution=settingsRows.Single(row=>row.control!=null && row.control.node=="Resolution").control;
            ModeCheck(restartResolution.options.SequenceEqual(sourceResolutionLabels) && restartResolution.option==sourceResolutionIndex && restartResolution.value==(sourceResolutionLabels.Length==0?"":sourceResolutionLabels[sourceResolutionIndex]),"restart Resolution source Awake hardware label and chosen index");
            yield return new WaitForEndOfFrame(); CaptureTestScreenshot("settings-restart-" + probePhase + ".png");
            FinishProgressionSettingsReport("settings-restart-" + probePhase + "-report.json", effects, probePhase);
        }
        [Serializable] sealed class UfoTriggerFrame
        {
            public string phase;
            public int step, playerTriggerEntries, triggerPickups, pickups, boxColor, lastPickupColor, collected, ordinaryGives, ordinaryUses;
            public bool spawned, ordinaryHeld;
            public string preview, ordinaryItem;
            public Vector3 position, colliderCenter;
        }
        IEnumerator CheckUfoLive()
        {
            selectedMode = NativeSessionMode.CustomRace; customLaps = 1; customRacers = 4; customItemType = 1;
            Begin(); while (phase != Phase.Racing) yield return null;
            var manager = GetComponent<NativeUfoManager>();
            ModeCheck(manager && manager.ItemType == 1 && racers.Length == 4 && RaceLaps == 1 && raceConfig.itemType == "ufo", "actual Custom UFO one lap four racer configuration");
            if (!manager || manager.Boxes.Length == 0) { FinishModeReport("ufo-live-report.json"); yield break; }
            foreach (var motor in racers) { motor.driving = false; motor.enabled = false; motor.GetComponent<NativeKartOriginalAI>().enabled = false; }
            racers[0].driving = true; manager.SetWorking(true);
            yield return NativeUfoVerification.StartVerification(reportDirectory, manager);
            ModeCheck(NativeUfoVerification.Passed, "original UFO recipe and live inventory verification");
            var player = manager.Players[0]; var box = manager.Boxes[0]; var controller = racers[0].GetComponent<CharacterController>();
            var collider = box.GetComponent<BoxCollider>(); Vector3 center = collider.bounds.center;
            Vector3 outside = center + Vector3.right * (collider.bounds.extents.x + controller.radius + 4);
            var competingColliders = manager.Boxes.SelectMany(value => value.GetComponents<Collider>())
                .Concat(FindObjectsOfType<NativeKartItemBox>(true).SelectMany(value => value.GetComponents<Collider>())).Where(value => value != collider).Distinct().ToArray();
            var enabledStates = competingColliders.Select(value => value.enabled).ToArray();
            var frames = new List<UfoTriggerFrame>();
            System.Action<string, int> sample = (label, step) => frames.Add(new UfoTriggerFrame {
                phase = label, step = step, playerTriggerEntries = box.PlayerTriggerEntries, triggerPickups = box.TriggerPickups,
                pickups = box.PickupCount, boxColor = box.colorIndex, lastPickupColor = box.LastPickupColor, spawned = box.Spawned,
                collected = player.State.count, preview = player.State.previewItemId, ordinaryGives = player.inventory.GiveCount,
                ordinaryUses = player.inventory.UseCount, ordinaryHeld = player.inventory.HasItem, ordinaryItem = player.inventory.CurrentId,
                position = racers[0].transform.position, colliderCenter = collider.bounds.center });
            try
            {
                // Flush prior trigger pairs outside the target before starting an independently measured entry.
                foreach (var competing in competingColliders) competing.enabled = false;
                collider.enabled = false;
                racers[0].ResetAt(outside, Quaternion.identity, false); racers[0].driving = true; Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                player.inventory.ResetItems(); player.ResetState(); box.ResetBox(0); collider.enabled = true; Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); sample("colored-outside", -1);
                ModeCheck(box.PlayerTriggerEntries == 0 && box.PickupCount == 0 && player.State.count == 0 && !player.inventory.HasItem && !collider.bounds.Intersects(controller.bounds), "colored fixture begins outside with cleared contacts and empty inventories");
                for (int step = 0; step < 10; step++) { controller.Move((center - outside) / 10); yield return new WaitForFixedUpdate(); sample("colored-entry", step); }
                ModeCheck(box.PlayerTriggerEntries == 1 && box.TriggerPickups == 1 && box.PickupCount == 1 && box.LastPickupColor == 0 && player.State.count == 1 && player.State.previewItemId == "YinYang" && !player.inventory.HasItem, "actual CharacterController enters colored trigger and collects recipe color");
                yield return new WaitForEndOfFrame(); CaptureTestScreenshot("ufo-colored-collected.png");
                int useCount = player.inventory.UseCount; UsePlayerItem(); yield return null; sample("first-use", 0);
                ModeCheck(player.inventory.HasItem && player.inventory.ItemDecided && player.inventory.CurrentId == "YinYang" && player.State.count == 0 && player.inventory.UseCount == useCount, "first item-use handler gives collected recipe without releasing item");
                yield return new WaitForEndOfFrame(); CaptureTestScreenshot("ufo-recipe-held.png");
                UsePlayerItem(); yield return null; yield return new WaitForFixedUpdate(); sample("second-use", 0);
                ModeCheck(player.inventory.UseCount == useCount + 1, "second item-use handler releases original inventory item");
                yield return new WaitForEndOfFrame(); CaptureTestScreenshot("ufo-recipe-released.png");
                collider.enabled = false; controller.Move(outside - racers[0].transform.position); Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                player.inventory.ResetItems(); player.ResetState(); box.ResetBox(3); collider.enabled = true; Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); sample("gray-outside", -1);
                ModeCheck(box.PlayerTriggerEntries == 0 && box.PickupCount == 0 && player.State.count == 0 && !player.inventory.HasItem && !collider.bounds.Intersects(controller.bounds), "gray fixture begins outside with cleared contacts and empty inventories");
                for (int step = 0; step < 10; step++) { controller.Move((center - outside) / 10); yield return new WaitForFixedUpdate(); sample("gray-entry", step); }
                ModeCheck(box.PlayerTriggerEntries == 1 && box.TriggerPickups == 1 && box.PickupCount == 1 && box.LastPickupColor == 3 && player.State.count == 0 && player.inventory.HasItem, "actual gray trigger gives ordinary random item without a recipe color");
                yield return new WaitForEndOfFrame(); CaptureTestScreenshot("ufo-gray-random.png");
            }
            finally
            {
                for (int i = 0; i < competingColliders.Length; i++) if (competingColliders[i]) competingColliders[i].enabled = enabledStates[i];
                collider.enabled = true;
                File.WriteAllText(Path.Combine(reportDirectory, "ufo-trigger-evidence.json"), JsonUtility.ToJson(new UfoTriggerEvidence { frames = frames.ToArray() }, true));
            }
            FinishModeReport("ufo-live-report.json");
        }
        [Serializable] sealed class UfoTriggerEvidence { public UfoTriggerFrame[] frames; }
    }
}
