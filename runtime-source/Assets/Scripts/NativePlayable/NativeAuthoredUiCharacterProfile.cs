using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        static string CharacterProfileBox(int human) => "selection/Canvas/CharSelection/P" + (human + 1) + "Box/";
        public void CharacterProfile(int human, bool open, string name, bool compatible)
        {
            string box = CharacterProfileBox(human);
            SetActive(box + "Joined", !open);
            SetActive(box + "Profile", open);
            if (!open) return;
            string profile = box + "Profile/";
            // Native page hiding visits descendants; original Config only hides the parent.
            foreach (var pair in transforms.Where(pair => pair.Key.StartsWith(profile, StringComparison.Ordinal)))
                pair.Value.gameObject.SetActive(definitions[pair.Key].active);
            SetText(box + "Profile/profileselection/profilename", name);
            transforms[box + "Profile/profileselection/profilename"].GetComponent<TMP_Text>().color = compatible ? Color.white : Color.red;
        }
        [Serializable] public sealed class CharacterProfileControlSample
        {
            public string path, text, sprite, shader;
            public bool activeSelf, activeInHierarchy, graphicEnabled, cull;
            public float inheritedAlpha, canvasAlpha;
            public Color color;
            public Rect screenRect, boxRect;
            public int textCharacters, textVertices;
        }
        static Rect ProfileScreenRect(RectTransform target)
        {
            var corners = new Vector3[4]; target.GetWorldCorners(corners);
            var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(null,point)).ToArray();
            float left=points.Min(point=>point.x),right=points.Max(point=>point.x),bottom=points.Min(point=>point.y),top=points.Max(point=>point.y);
            return new Rect(left,Screen.height-top,right-left,top-bottom);
        }
        public CharacterProfileControlSample[] ReadCharacterProfileControls(int human)
        {
            string box=CharacterProfileBox(human);
            return new[] { "profilename","arrowleft","arrowright" }.Select(leaf =>
            {
                string path=box+"Profile/profileselection/"+leaf;
                var node=transforms[path]; var graphic=node.GetComponent<Graphic>(); var text=node.GetComponent<TMP_Text>(); var image=node.GetComponent<Image>();
                return new CharacterProfileControlSample { path=path,text=text ? text.text : "",sprite=image && image.sprite ? image.sprite.name : "",
                    shader=graphic && graphic.material ? graphic.material.shader.name : "", activeSelf=node.gameObject.activeSelf,activeInHierarchy=node.gameObject.activeInHierarchy,
                    graphicEnabled=graphic && graphic.enabled,cull=graphic && graphic.canvasRenderer.cull,inheritedAlpha=graphic ? graphic.canvasRenderer.GetInheritedAlpha() : 0,
                    canvasAlpha=graphic ? graphic.canvasRenderer.GetAlpha() : 0,color=graphic ? graphic.color : Color.clear,screenRect=ProfileScreenRect(node),boxRect=ProfileScreenRect(transforms[box.TrimEnd('/')]),
                    textCharacters=text ? text.textInfo.characterCount : 0,textVertices=text ? text.textInfo.meshInfo.Sum(mesh=>mesh.vertexCount) : 0 };
            }).ToArray();
        }
        public string[] CharacterProfileControls(int human) => transforms.Keys.Where(key => key == CharacterProfileBox(human) + "Profile"
            || key.StartsWith(CharacterProfileBox(human) + "Profile/", StringComparison.Ordinal)).OrderBy(key => key).ToArray();
        public bool[] CharacterProfileOtherBoxVisibility(int human)
        {
            string box = CharacterProfileBox(human);
            return new[] { transforms[box + "blocked"].gameObject.activeSelf, transforms[box + "JoinText"].gameObject.activeSelf, transforms[box + "p"].gameObject.activeSelf };
        }
        public bool CharacterProfileActive(int human) => transforms[CharacterProfileBox(human) + "Profile"].gameObject.activeInHierarchy;
        public Color CharacterProfileNameColor(int human) => transforms[CharacterProfileBox(human) + "Profile/profileselection/profilename"].GetComponent<TMP_Text>().color;
    }
    public sealed partial class NativeRaceHud
    {
        public void DrawCharacterProfile(NativeCharacterProfileState state)
        {
            var profiles = NativeInputBindings.Profiles;
            bool valid = state.CurrentIndex == -1 || (state.CurrentIndex >= 0 && state.CurrentIndex < profiles.Length);
            string id = state.CurrentIndex == -1 || !valid ? "" : profiles[state.CurrentIndex].profileId;
            string name = state.CurrentIndex == -1 ? "Default" : valid ? profiles[state.CurrentIndex].name : "";
            authoredUi.CharacterProfile(state.HumanIndex,state.Open,name,valid && (state.CurrentIndex == -1 || NativeInputBindings.CanSelectProfile(id,state.HumanIndex)));
        }
        public void CloseCharacterProfile(NativeCharacterProfileState state) => authoredUi.CharacterProfile(state.HumanIndex,false,"",true);
        public NativeAuthoredUi.CharacterProfileControlSample[] ReadCharacterProfileControls(int human) => authoredUi.ReadCharacterProfileControls(human);
        public string[] CharacterProfileControls(int human) => authoredUi.CharacterProfileControls(human);
        public bool[] CharacterProfileOtherBoxVisibility(int human) => authoredUi.CharacterProfileOtherBoxVisibility(human);
        public bool CharacterProfileActive(int human) => authoredUi.CharacterProfileActive(human);
        public Color CharacterProfileNameColor(int human) => authoredUi.CharacterProfileNameColor(human);
        public void CharacterProfileSound(NativeCharacterProfileResult result)
        {
            if (result == NativeCharacterProfileResult.Opened || result == NativeCharacterProfileResult.Moved) PlayOriginalSelectionSound(NativePlayerBoxSoundData.Move);
            else if (result == NativeCharacterProfileResult.Applied) PlayOriginalSelectionSound(NativePlayerBoxSoundData.PlayerBoxSelect);
            else if (result == NativeCharacterProfileResult.Rejected || result == NativeCharacterProfileResult.Cancelled) PlayOriginalSelectionSound(NativePlayerBoxSoundData.PlayerBoxCancel);
        }
    }
}
