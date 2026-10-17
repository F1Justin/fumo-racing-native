using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeOnlineLobbyView : MonoBehaviour
    {
        public const string ResourcePath = "NativeOnline/OnlineLobby";
        [SerializeField] NativeLobbyLabel roomTitle, playersTitle, invitationLabel, exitLabel;
        [SerializeField] RectTransform playersContainer;
        [SerializeField] NativeLobbyPlayerRow playerRowTemplate;
        [SerializeField] NativeLobbyButton characterButton, copyButton, spectateButton, readyButton;
        [SerializeField] NativeLobbyButton settingsButton;
        [SerializeField] RawImage characterModel;
        [SerializeField] TMP_InputField invitationInput;
        [SerializeField] Image invitationSurface;
        [SerializeField] Sprite invitationNormal, invitationFocused;
        [SerializeField] GameObject invitationDisplay;
        [SerializeField] NativeLobbyMarquee invitationText;
        [SerializeField] GameObject exitControl;
        [SerializeField] Image exitProgress;
        UnityEngine.Events.UnityAction<string> invitationEdited;
        readonly List<NativeLobbyPlayerRow> rows = new List<NativeLobbyPlayerRow>();
        public TMP_InputField InvitationInput => invitationInput;
        public GameObject ExitControl => exitControl;
        public NativeLobbyButton CharacterButton => characterButton;
        public NativeLobbyButton CopyButton => copyButton;
        public NativeLobbyButton SpectateButton => spectateButton;
        public NativeLobbyButton ReadyButton => readyButton;
        public NativeLobbyButton SettingsButton => settingsButton;

        public void Bind(Action<string> click, Action<string> hover, Action<string> edited)
        {
            characterButton.Bind(() => click("character"), () => hover("character"));
            copyButton.Bind(() => click("copy-invite"), () => hover("copy-invite"));
            spectateButton.Bind(() => click("role"), () => hover("role"));
            readyButton.Bind(() => click("ready"), () => hover("ready"));
            if (settingsButton) settingsButton.Bind(() => click("room-settings"), () => hover("room-settings"));
            if (invitationEdited != null) invitationInput.onValueChanged.RemoveListener(invitationEdited);
            invitationEdited = value => edited(value);
            invitationInput.onValueChanged.AddListener(invitationEdited);
            playerRowTemplate.gameObject.SetActive(false);
            for (int seat = rows.Count; seat < 8; seat++)
            {
                var row = Instantiate(playerRowTemplate, playersContainer, false);
                row.name = "Player " + (seat + 1); row.gameObject.SetActive(true); rows.Add(row);
            }
        }
        public void Header(string room)
        {
            roomTitle.Set(room, localize: false); playersTitle.Set("Players");
        }
        public void Row(int seat, string name, string state, Sprite icon, Texture preview, bool local, bool ready)
            => rows[seat].Present(name, state, icon, preview, local, ready, seat == 7);
        public void Model(Texture texture, bool visible)
        { characterButton.gameObject.SetActive(visible); characterModel.texture = texture; }
        public void Invitation(string caption, string endpoint, bool editable, bool focused)
        {
            invitationLabel.Set(caption);
            invitationInput.gameObject.SetActive(editable); invitationDisplay.SetActive(!editable);
            if (editable && !invitationInput.isFocused && invitationInput.text != endpoint)
                invitationInput.SetTextWithoutNotify(endpoint);
            invitationSurface.sprite = invitationInput.isFocused || focused ? invitationFocused : invitationNormal;
            if (!editable) invitationText.Set(endpoint);
        }
        public void Quit(bool held, float amount)
        {
            exitControl.SetActive(held);
            exitLabel.Set(NativeLocalization.Get("SELECTION_QUITTING"), localize: false);
            exitProgress.fillAmount = Mathf.Clamp01(amount);
        }
        public static Rect LogicalRect(RectTransform target)
        {
            var corners = new Vector3[4]; target.GetWorldCorners(corners);
            // Corner order follows local axes, not screen axes. Mirrored/rotated
            // original arrows can otherwise produce a negative width or zero height.
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            float scale = 600f / Screen.height;
            return new Rect(min.x * scale, (Screen.height - max.y) * scale, (max.x - min.x) * scale, (max.y - min.y) * scale);
        }
    }
}
