using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        const string OnlineNameSetting = "native.onlinePlayerName";
        string profileNameDraft, profileNotice = "";
        string profileNameBeforeEdit;
        int profilePresetPage;
        Action profileSettingsReturn;
        public bool ProfileFromSettings => profileSettingsReturn != null;

        public void OpenProfileFromSettings(Action onClose)
        {
            profileSettingsReturn = onClose;
            OpenOnlineProfile();
        }
        void OpenOnlineProfile()
        {
            SyncProfilePlayerName();
            string saved = NativeUserSettings.ReadValue(OnlineNameSetting);
            profileNameDraft = string.IsNullOrEmpty(saved) ? PlayerName : saved;
            profileNameBeforeEdit = null; profileNotice = ""; profilePresetPage = 0;
            if (!NativePlayerNamePolicy.TryNormalize(profileNameDraft, out _, out var error)) profileNotice = error;
            profilesPage = true; settingsPage = false; onlinePage = null; onlineFocus = "field-player-name";
            onlineHeldDirection = Vector2.zero;
        }
        void CloseOnlineProfile()
        {
            profilesPage = false; profileNameDraft = null; profileNameBeforeEdit = null; profileNotice = ""; onlineFocus = "profiles";
            if (onlineFields.TryGetValue("player-name", out var field) && field) field.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
            OnlineHud.NetworkVisible(false);
            var callback = profileSettingsReturn; profileSettingsReturn = null; callback?.Invoke();
        }
        void SaveOnlineProfileName()
        {
            if (!NativePlayerNamePolicy.TryNormalize(profileNameDraft, out var name, out var error))
            { ProfileNameFailure(error); return; }
            if (onlineFields.TryGetValue("player-name", out var input) && input && input.textComponent && input.textComponent.font
                && !input.textComponent.font.HasCharacters(name, out uint[] missing, true, true))
            { ProfileNameFailure("Player ID contains characters unavailable in the game fonts."); return; }
            if (!NativeUserSettings.WriteValue(OnlineNameSetting, name, "string")) { profileNotice = NativeUserSettings.LastError; return; }
            playerNameOverride = null; SyncProfilePlayerName();
            CloseOnlineProfile();
        }
        void ProfileNameFailure(string error)
        {
            profileNotice = error; onlineFocus = "field-player-name";
            if (onlineFields.TryGetValue("player-name", out var field) && field)
            { field.Select(); field.ActivateInputField(); }
        }
        void ChangeProfilePlayerId(string value)
        {
            // Preserve what was typed/pasted and the IME caret; validate the complete name on submit.
            profileNameDraft = value;
            if (!NativeTextInputBridge.BlocksMenuInput)
                profileNotice = NativePlayerNamePolicy.TryNormalize(value, out _, out var error) ? "" : error;
        }
        bool HandleProfileNameInput(bool escape)
        {
            if (!profilesPage) return false;
            bool editing = OnlineEditing;
            if (editing && NativeTextInputBridge.BlocksMenuInput)
            { onlineInputConsumedFrame = Time.frameCount; return true; }
            // Input-source switching shortcuts belong to the OS, including when the field has just lost focus.
            if (!OnlineKeyboardUnmodified && (OnlineKeyboardSpacePressed || OnlineKeyboardEnterPressed))
            { onlineInputConsumedFrame = Time.frameCount; return true; }
            bool save = OnlineKeyboardSpace
                || NativeInputBindings.GetGamepadButtonDown(NativeInputBindings.Action.Use)
                || (editing && (OnlineKeyboardSubmit || NativeInputBindings.GetGamepadButtonDown(NativeInputBindings.Action.Ok)));
            if (save)
            {
                onlineInputConsumedFrame = Time.frameCount;
                SaveOnlineProfileName(); return true;
            }
            if (!editing) return false;
            onlineInputConsumedFrame = Time.frameCount;
            if (escape || NativeInputBindings.GetGamepadButtonDown(NativeInputBindings.Action.Cancel))
            {
                ChangeProfilePlayerId(profileNameBeforeEdit ?? PlayerName);
                if (onlineFields.TryGetValue("player-name", out var input) && input) input.SetTextWithoutNotify(profileNameDraft);
                EndOnlineTextEdit();
            }
            else if (Input.GetKeyDown(KeyCode.Tab) || (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame))
            { EndOnlineTextEdit(); MoveOnlineFocus(new Vector2(0, -1)); }
            // Z/X remain literal text; keyboard Space is reserved for save and exit.
            return true;
        }
        static bool IsOnlineWheelKey(string key) => key == "host-direct" || key == "join-direct" || key == "community"
            || key == "profiles" || key == "online-settings" || key == "settings-video" || key == "settings-audio" || key == "settings-language";
        void RenderOnlineEntryWheel()
        {
            var actions = new List<(string key, string caption, Action action)>();
            if (ProfileFromSettings)
            {
                actions.Add(("settings-video", "Video Settings", () => LeaveProfileForSettings("Video")));
                actions.Add(("settings-audio", "Audio Settings", () => LeaveProfileForSettings("Audio")));
                actions.Add(("profiles", "Profile", OpenOnlineProfile));
                actions.Add(("settings-language", "Language", () => LeaveProfileForSettings("Language")));
            }
            else
            {
                actions.Add(("host-direct", "Host Game", () => { profilesPage = false; BeginConnectionStep(OnlineConnectionStep.HostChoice, "host-direct"); }));
                actions.Add(("join-direct", "Join Game", () => { profilesPage = false; BeginConnectionStep(OnlineConnectionStep.JoinBrowser, "join-direct"); }));
                actions.Add(("profiles", "Profile", OpenOnlineProfile));
            }
            if (onlineFocus == null) onlineFocus = actions[0].key;
            int selected = actions.FindIndex(a => a.key == onlineFocus);
            if (selected < 0) selected = profilesPage ? actions.FindIndex(a => a.key == "profiles") : 0;
            OnlineHud.ConfigureNetworkWheel(actions.Select(a => a.caption).ToArray());
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i]; bool pressed;
                var rect = OnlineHud.NetworkWheelButton(i, selected == i, true, out pressed);
                var button = new OnlineControl { key = action.key, rect = rect, enabled = true, action = action.action };
                onlineButtons.Add(button); if (pressed) ActivateOnlineButton(button);
            }
            OnlineHud.NetworkWheelForeground(selected);
            Rect back;
            if (OnlineHud.NetworkWheelPrompts(out back)) { if (profilesPage) CloseOnlineProfile(); else CloseMenu(); }
        }
        void OpenProfileControlPresets()
        {
            bool online = !ProfileFromSettings;
            CloseOnlineProfile();
            if (online) CloseMenu();
            Race.OpenProfileControlPresets(() =>
            {
                if (online) { Race.CloseProfileControlPresets(); OpenMenu(); OpenOnlineProfile(); }
                else Race.ReopenIdentityProfileFromSettings();
            });
        }
        void LeaveProfileForSettings(string page)
        { CloseOnlineProfile(); Race.ProfileSettingsNavigate(page); }
        void RenderOnlineProfileIdentity()
        {
            OnlineHud.NetworkLabel("OnlineProfiles.Label1", "Profile");
            OnlineHud.NetworkPanel("profile-name-panel");
            OnlineHud.NetworkLabel("profile-name-title", "Your player ID");
            OnlineHud.NetworkLabel("profile-name-help", "No spaces. Shown in rooms and race results.");
            EnsureOnlineFields(); onlineInputRoot.SetActive(true);
            OnlineField("player-name", profileNameDraft ?? PlayerName, ChangeProfilePlayerId);
            var profileView = OnlineHud.OnlinePageView.GetComponent<NativeOnlineProfileView>();
            if (profileView) profileView.Present(onlineFocus == "field-player-name");
            OnlineHud.NetworkLabel("profile-controls-title", "Input preset");
            var presets = new[] { new NativeSaveData.InputProfile { profileId = "", name = "Default controls" } }.Concat(NativeInputBindings.Profiles).ToArray();
            profilePresetPage = Mathf.Clamp(profilePresetPage, 0, (presets.Length - 1) / 3);
            var visible = presets.Skip(profilePresetPage * 3).Take(3).ToArray();
            for (int i = 0; i < visible.Length; i++)
            {
                var preset = visible[i];
                OnlineHud.BeginOnlineRow("Profiles", i);
                string id = preset.profileId ?? "";
                OnlineButton("profile-" + id, preset.name, () =>
                {
                    if (!NativeInputBindings.SelectProfile(id)) profileNotice = NativeInputBindings.LastError;
                    else { SyncProfilePlayerName(); profileNotice = ""; }
                }, viewKey: "profile");
                if (id == NativeInputBindings.SelectedProfile()) OnlineHud.NetworkFill("profile-selected");
                OnlineHud.EndOnlineRow();
            }
            if (presets.Length > 3)
            {
                OnlineButton("profiles-prev", "<", () => profilePresetPage--, profilePresetPage > 0);
                OnlineButton("profiles-next", ">", () => profilePresetPage++, (profilePresetPage + 1) * 3 < presets.Length);
                OnlineHud.NetworkLabel("profile-controls-page", (profilePresetPage + 1) + " / " + ((presets.Length + 2) / 3), false);
            }
            OnlineButton("profile-manage-controls", "Configure controls", OpenProfileControlPresets);
            OnlineHud.NetworkLabel("profile-notice", profileNotice);
            OnlineHud.NetworkLabel("profiles-back", OnlineEditing ? "Cancel" : "Back");
            OnlineHud.NetworkLabel("profile-save", "Save and Exit");
        }
    }
    public sealed partial class NativeRace
    {
        internal void ProfileSettingsNavigate(string page) => SettingsGo((SettingsPage)Enum.Parse(typeof(SettingsPage), page));
        Action settingsIdentityReturn;
        internal void CloseProfileControlPresets() { settingsOpen = false; heldMenuDirection = 0; }
        internal void ReopenIdentityProfileFromSettings() => OpenIdentityProfileFromSettings();
        internal void OpenProfileControlPresets(Action onBack)
        {
            if (!settingsOpen) OpenSettings();
            settingsIdentityReturn = onBack; SettingsGo(SettingsPage.Profiles);
        }
        void OpenIdentityProfileFromSettings()
        {
            if (Net) Net.OpenProfileFromSettings(() => { SettingsGo(SettingsPage.Root); settingsConfirmAwaitingRelease = true; });
        }
    }
}
