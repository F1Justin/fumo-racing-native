using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public NativeMenuAction DrawLocalSelection(NativeLocalSelectionState state, NativeVehicleRoster roster, NativeCharacterProfileState[] profiles)
        {
            var first = state.ReadBox(0);
            if (first == null) throw new InvalidOperationException("Local character selection requires a joined owner");
            AdditionalPlayersCanJoin = state.JoiningEnabled;
            ShowOriginalSinglePlayerBoxes();
            bool previousPointer = SuppressPointerInput;
            SuppressPointerInput = previousPointer || (profiles != null && profiles[0] != null && profiles[0].Open);
            Navigation.Select(Array.IndexOf(roster.SelectableCharacters(),first.Character));
            NativeMenuAction pointerAction;
            try { pointerAction=RenderOriginalCharacterGrid(roster, first.Character, first.Selection != NativeLocalSelectionState.Stage.Character); }
            finally { SuppressPointerInput = previousPointer; }
            var nodes = authoredUi.Nodes(CharacterPanel + "CharacterGrid/").Where(node => !string.IsNullOrEmpty(node.characterId)).ToArray();
            for (int slot = 0; slot < 4; slot++)
            {
                var box = state.ReadBox(slot); string root = CharacterPanel + "P" + (slot + 1) + "Box/";
                string joined = root + "Joined/", selector = CharacterPanel + "P" + (slot + 1) + "Select";
                bool present = box != null, profileOpen = profiles != null && profiles[slot] != null && profiles[slot].Open;
                authoredUi.SetActive(root.TrimEnd('/'), true);
                authoredUi.SetActive(root + "JoinText", !present && state.JoiningEnabled);
                authoredUi.SetActive(root + "blocked", !present && !state.JoiningEnabled);
                authoredUi.SetActive(root + "p", present || state.JoiningEnabled);
                authoredUi.SetActive(root + "Joined", present && !profileOpen);
                authoredUi.SetActive(root + "Profile", present && profileOpen);
                authoredUi.SetActive(selector, present);
                if (!present) continue;
                string characterId = roster.CharacterIdAt(box.Character);
                var node = nodes.Single(candidate => candidate.characterId == characterId);
                authoredUi.PlaceAt(selector, "selection/" + node.path);
                authoredUi.SetSprite(selector + "/Image", roster.CharacterIconAt(box.Character));
                bool character = box.Selection == NativeLocalSelectionState.Stage.Character;
                authoredUi.SetActive(selector + "/highlight", character);
                authoredUi.SetActive(selector + "/selected", !character);
                authoredUi.SetText(joined + "charname", NativeLocalization.Get("CHAR_" + characterId.ToUpperInvariant(), roster.CharacterNameAt(box.Character)));
                authoredUi.SetActive(joined + "charname", character || box.Selection == NativeLocalSelectionState.Stage.Skin || box.Selection == NativeLocalSelectionState.Stage.Ready);
                authoredUi.SetActive(joined + "vehicleselection", !character);
                authoredUi.SetActive(joined + "prompt", !character);
                if (!originalExclusiveIcons.TryGetValue(characterId, out var icon)) { icon = NativeContent.Load<Sprite>("NativeSelectionPreview/" + characterId + "-exclusive"); originalExclusiveIcons[characterId] = icon; }
                authoredUi.SetActive(joined + "ItemBox", icon); if (icon) authoredUi.SetSprite(joined + "ItemBox/ExclusiveItem", icon);
                string vehicle = joined + "vehicleselection/", id = roster.VehicleIdAt(box.Vehicle), phrase = "VEHICLE_" + id.ToUpperInvariant();
                bool skin = box.Selection == NativeLocalSelectionState.Stage.Skin || box.Selection == NativeLocalSelectionState.Stage.Ready;
                authoredUi.SetText(vehicle + "vehiclename", skin ? NativeLocalization.Get(roster.SkinPhraseAt(box.Character, box.Skin)) : NativeLocalization.Get(phrase));
                authoredUi.SetText(vehicle + "vehicledesc", NativeLocalization.Get(phrase + "_DESCRIPTION"));
                authoredUi.SetActive(vehicle + "vehicledesc", !skin);
                var grades = OriginalVehicleGrades(id);
                authoredUi.SetText(vehicle + "spd/spdvalue", grades[0]); authoredUi.SetText(vehicle + "spd/accelvalue", grades[1]); authoredUi.SetText(vehicle + "spd/driftvalue", grades[2]);
                authoredUi.SetText(joined + "prompt", NativeLocalization.Get(box.Selection == NativeLocalSelectionState.Stage.Ready ? "SELECTION_READY" : skin ? "SELECTION_SKIN" : "SELECTION_VEHICLE"));
                if(slot==0 && !profileOpen && !SuppressPointerInput && box.Selection!=NativeLocalSelectionState.Stage.Character && box.Selection!=NativeLocalSelectionState.Stage.Ready)
                    foreach(int step in new[]{-1,1})if(PointerButton(authoredUi.RectAt(vehicle+(step>0?"arrowright":"arrowleft"))))LocalSelectionPointerMove?.Invoke(step);
                if (profileOpen) DrawCharacterProfile(profiles[slot]);
            }
            if (!SuppressPointerInput && (profiles == null || profiles[0] == null || !profiles[0].Open))
            {
                if (PointerButton(authoredUi.RectAt(CharacterPanel + "Prompts/BText"))) pointerAction = NativeMenuAction.BackToCharacters;
                if (PointerButton(authoredUi.RectAt(CharacterPanel + "Prompts/AText"))) { PlayLocalSelectionSound(first.Selection == NativeLocalSelectionState.Stage.Character ? 5 : 8); pointerAction = NativeMenuAction.OpenVehicles; }
            }
            return pointerAction;
        }
        public event Action<int> LocalSelectionPointerMove;
        public void LocalPreviewTexture(string sourceRawImagePath,Texture texture) => authoredUi.RawTexture("selection/"+sourceRawImagePath,texture);
        public int LocalCharacterNeighbor(NativeVehicleRoster roster,int character,int direction)
        {
            authoredUi.PrepareCharacterNavigation(roster.SelectableCharacters().Select(roster.CharacterIdAt));
            string id=authoredUi.CharacterNeighbor(roster.CharacterIdAt(character),direction);
            return string.IsNullOrEmpty(id)?-1:roster.CharacterIndexOf(id);
        }
        public void PulseLocalSelectionArrow(int slot, int step) => authoredUi.PulsePlayerBoxArrow(CharacterPanel + "P" + (slot + 1) + "Box/Joined/vehicleselection/" + (step > 0 ? "arrowright" : "arrowleft"));
        public void PlayLocalSelectionSound(int index)
        {
            AudioClip clip = index == 0 ? NativePlayerBoxSoundData.PlayerBoxCancel : index == 4 ? NativePlayerBoxSoundData.CharacterMove
                : index == 5 ? NativePlayerBoxSoundData.CharacterSelect : index == 7 ? NativePlayerBoxSoundData.Move : index == 8 ? NativePlayerBoxSoundData.PlayerBoxSelect : null;
            if (!clip) throw new ArgumentOutOfRangeException(nameof(index)); PlayOriginalSelectionSound(clip);
        }
    }
}
