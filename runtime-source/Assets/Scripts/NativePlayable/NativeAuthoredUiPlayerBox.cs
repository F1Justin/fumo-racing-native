using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public static class NativePlayerBoxNavigation
    {
        public static int Direction(Vector2 input) => input.y >= .5f ? 0 : input.y <= -.5f ? 2
            : input.x <= -.5f ? 3 : input.x >= .5f ? 1 : -1;
        public static int Step(Vector2 input)
        {
            int direction = Direction(input);
            return direction < 0 ? 0 : direction == 1 ? 1 : -1;
        }
    }
    public sealed class NativePlayerBoxArrowTween : MonoBehaviour
    {
        float started;
        bool playing;
        public void Pulse()
        {
            transform.localScale = Vector3.one * 1.5f;
            started = Time.time;
            playing = true;
        }
        void Update()
        {
            if (!playing) return;
            float t = Mathf.Clamp01((Time.time - started) / .2f);
            // Source DOScale uses DOTweenSettings default OutQuad (enum 6).
            transform.localScale = Vector3.one * (1f + .5f * (1f - t) * (1f - t));
            playing = t < 1f;
        }
    }
    public sealed partial class NativeAuthoredUi
    {
        [Serializable] public sealed class SelectionRecordPanelSample
        {
            public string panel, total, vehicle, characterSprite;
            public string[] splits;
            public bool activeSelf, activeInHierarchy;
        }
        public SelectionRecordPanelSample ReadSelectionRecordPanel(string panel)
        {
            string key = "selection/Canvas/CupSelection/" + panel;
            var node = transforms[key];
            var icon = transforms[key + "/TimeCharacterIcon"].GetComponent<Image>();
            return new SelectionRecordPanelSample { panel = panel, activeSelf = node.gameObject.activeSelf,
                activeInHierarchy = node.gameObject.activeInHierarchy,
                total = transforms[key + "/TimeTotal"].GetComponent<TMP_Text>().text,
                vehicle = transforms[key + "/TimeVehicle"].GetComponent<TMP_Text>().text,
                characterSprite = icon.sprite ? icon.sprite.name : "",
                splits = Enumerable.Range(1, panel == "TimePanelCUP" ? 4 : 5)
                    .Select(i => transforms[key + "/lap" + i + "time"].GetComponent<TMP_Text>().text).ToArray() };
        }
        public void PulsePlayerBoxArrow(string key)
        {
            var target = transforms[key];
            var tween = target.GetComponent<NativePlayerBoxArrowTween>();
            if (!tween) tween = target.gameObject.AddComponent<NativePlayerBoxArrowTween>();
            tween.Pulse();
        }
        public Vector3 PlayerBoxArrowScale(string key) => transforms[key].localScale;
        public RawImage PlayerBoxPreviewImage => transforms["selection/Canvas/CharSelection/P1Box/Joined/rt"].GetComponent<RawImage>();
    }
    public sealed partial class NativeRaceHud
    {
        public AudioClip LastOriginalSelectionMoveClip { get; private set; }
        public AudioSource OriginalSelectionMoveSource => menuAudio;
        public int OriginalSelectionMoveCalls { get; private set; }
        void PlayOriginalSelectionMoveSound(bool playerBox)
        {
            PlayOriginalSelectionSound(playerBox ? NativePlayerBoxSoundData.Move : NativePlayerBoxSoundData.CharacterMove);
        }
        void PlayOriginalSelectionSound(AudioClip clip)
        {
            LastOriginalSelectionMoveClip = clip;
            OriginalSelectionMoveCalls++;
            NativeAudioRuntime.PlayUi(menuAudio, LastOriginalSelectionMoveClip);
        }
        public int PlayerBoxChoiceCount => Navigation.Count;
        public Vector3 PlayerBoxArrowScale(bool right) => authoredUi.PlayerBoxArrowScale(VehiclePlayerBox + "vehicleselection/" + (right ? "arrowright" : "arrowleft"));
        public RawImage PlayerBoxPreviewImage => authoredUi.PlayerBoxPreviewImage;
        public NativeAuthoredUi.SelectionRecordPanelSample[] ReadSelectionRecordPanels() => new[] {
            authoredUi.ReadSelectionRecordPanel("TimePanel"), authoredUi.ReadSelectionRecordPanel("TimePanelCUP") };
    }
}
