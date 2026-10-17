using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public bool AdditionalPlayersCanJoin { get; set; }
        readonly Dictionary<string, Sprite> originalExclusiveIcons = new Dictionary<string, Sprite>();
        void ShowOriginalSinglePlayerBoxes()
        {
            authoredUi.Show("selection", "CharSelection", "/P2Select", "/P3Select", "/P4Select", "/P1Box/JoinText", "/P1Box/Profile", "/P1Box/blocked");
            authoredUi.SetActive("selection/Canvas/bg", authoredUi.Definition("selection/Canvas/bg").active);
            authoredUi.SetActive("selection/Canvas/testbg", false);
            authoredUi.SetActive(CharacterPanel + "P1Box/Joined", true);
            authoredUi.SetText(CharacterPanel + "Prompts/XText (1)", NativeLocalization.Get("PROMPT_PROFILE", "Profile"));
            for (int player = 2; player <= 4; player++)
            {
                string box = CharacterPanel + "P" + player + "Box/";
                authoredUi.SetActive(box.TrimEnd('/'), true);
                authoredUi.SetActive(box + "blocked", !AdditionalPlayersCanJoin);
                authoredUi.SetActive(box + "Joined", false);
                authoredUi.SetActive(box + "JoinText", AdditionalPlayersCanJoin);
                authoredUi.SetActive(box + "Profile", false);
                authoredUi.SetActive(box + "p", AdditionalPlayersCanJoin);
            }
        }
        void BindOriginalExclusiveItem(string characterId)
        {
            if (!originalExclusiveIcons.TryGetValue(characterId, out var icon))
            {
                icon = NativeContent.Load<Sprite>("NativeSelectionPreview/" + characterId + "-exclusive");
                originalExclusiveIcons[characterId] = icon;
            }
            authoredUi.SetActive(VehiclePlayerBox + "ItemBox", icon);
            if (icon) authoredUi.SetSprite(VehiclePlayerBox + "ItemBox/ExclusiveItem", icon);
        }
    }
}
