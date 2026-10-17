using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public bool ChoosingSkin { get; set; }
        public int VehicleCharacterSelection { get; set; } = -1;
        const string VehiclePlayerBox = "selection/Canvas/CharSelection/P1Box/Joined/";
        public void MoveOriginalPlayerBox(int step)
        {
            if (step == 0) return;
            authoredUi.PulsePlayerBoxArrow(VehiclePlayerBox + "vehicleselection/" + (step > 0 ? "arrowright" : "arrowleft"));
            PlayOriginalSelectionMoveSound(true);
            Navigation.Select((SelectedMenuIndex + step + Navigation.Count) % Navigation.Count);
        }
        static string[] OriginalVehicleGrades(string id)
        {
            switch (id.ToLowerInvariant())
            {
                case "kart": return new[] { "A", "B", "B" };
                case "bike": return new[] { "B", "A", "A" };
                case "skate": return new[] { "A", "C", "A" };
                case "broom": return new[] { "C", "A", "B" };
                case "genji": return new[] { "B", "B", "C" };
                case "cleanerbot": return new[] { "B", "A", "F" };
                case "shoppingcart": return new[] { "B", "E", "A+" };
                default: throw new System.InvalidOperationException("Original VehicleStats missing: " + id);
            }
        }
        NativeMenuAction DrawOriginalVehicleSelection(float width, NativeVehicleRoster roster)
        {
            SetMenuPage(ChoosingSkin ? NativeMenuPage.Skins : NativeMenuPage.Vehicles); activeFont = menuFont;
            ShowOriginalSinglePlayerBoxes();
            int character = VehicleCharacterSelection >= 0 ? VehicleCharacterSelection : roster.DefaultCharacterIndex;
            BindOriginalExclusiveItem(roster.CharacterIdAt(character));
            authoredUi.SetActive(VehiclePlayerBox + "charname", false);
            authoredUi.SetActive(VehiclePlayerBox + "vehicleselection", true);
            RenderOriginalCharacterGrid(roster, VehicleCharacterSelection >= 0 ? VehicleCharacterSelection : roster.DefaultCharacterIndex, true);
            authoredUi.SetActive(VehiclePlayerBox.TrimEnd('/'), true);
            string vehicle = VehiclePlayerBox + "vehicleselection/";
            authoredUi.SetActive(vehicle + "vehicledesc", !ChoosingSkin);
            authoredUi.SetActive(vehicle + "spd", true);
            authoredUi.SetActive(VehiclePlayerBox + "prompt", true);
            string id = roster.VehicleIdAt(ChoosingSkin ? VehicleSelection : SelectedMenuIndex);
            string phrase = "VEHICLE_" + id.ToUpperInvariant();
            authoredUi.SetText(vehicle + "vehiclename", ChoosingSkin ? NativeLocalization.Get(roster.SkinPhraseAt(character, SelectedMenuIndex)) : NativeLocalization.Get(phrase));
            authoredUi.SetActive(VehiclePlayerBox + "charname", ChoosingSkin);
            if (ChoosingSkin) authoredUi.SetText(VehiclePlayerBox + "charname", NativeLocalization.Get("CHAR_" + roster.CharacterIdAt(character).ToUpperInvariant(), roster.CharacterNameAt(character)));
            authoredUi.SetText(vehicle + "vehicledesc", NativeLocalization.Get(phrase + "_DESCRIPTION"));
            authoredUi.SetText(VehiclePlayerBox + "prompt", NativeLocalization.Get(ChoosingSkin ? "SELECTION_SKIN" : "SELECTION_VEHICLE"));
            string[] grades = OriginalVehicleGrades(id);
            authoredUi.SetText(vehicle + "spd/spdvalue", grades[0]);
            authoredUi.SetText(vehicle + "spd/accelvalue", grades[1]);
            authoredUi.SetText(vehicle + "spd/driftvalue", grades[2]);
            foreach (int direction in new[] { -1, 1 })
            {
                Rect arrow = authoredUi.RectAt(vehicle + (direction < 0 ? "arrowleft" : "arrowright"));
                if (!SuppressPointerInput && PointerButton(arrow))
                    MoveOriginalPlayerBox(direction);
            }
            return OriginalPromptPointer(CharacterPanel, NativeMenuAction.None);
        }
    }
}
