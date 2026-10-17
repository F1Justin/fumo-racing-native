using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        const string OriginalCupPage = "selection/Canvas/CupSelection/";
        Sprite[] originalCircuitMinimaps;
        public NativeSessionMode SelectionRecordMode { get; set; }
        NativeSaveData.TimeTrialRecord originalCircuitRecord;
        NativeSaveData.CupRecord originalCupRecord;
        public void LoadOriginalCupRecords()
        {
            originalCircuitRecord = null;
            originalCupRecord = SelectionRecordMode == NativeSessionMode.GrandPrix
                ? NativeSaveStore.Data.cupRecords.FirstOrDefault(r => r.cupId == "moriyacup") : null;
        }
        void BindOriginalRecordPanel(string name, double total, double[] splits, string vehicleId, string characterId)
        {
            string panel = OriginalCupPage + name + "/";
            authoredUi.SetText(panel + "TimeTotal", OriginalTimeText(total, 24));
            for (int split = 0; split < (name == "TimePanelCUP" ? 4 : 5); split++)
                authoredUi.SetText(panel + "lap" + (split + 1) + "time", OriginalTimeText(split < splits.Length ? splits[split] : 0, 16));
            authoredUi.SetText(panel + "TimeVehicle", NativeLocalization.Get("VEHICLE_" + vehicleId.ToUpperInvariant()));
            var roster = UnityEngine.Object.FindObjectOfType<NativeVehicleRoster>();
            int character = roster ? roster.CharacterIndexOf(characterId) : -1;
            authoredUi.SetActive(panel + "TimeCharacterIcon", character >= 0);
            if (character >= 0) authoredUi.SetSprite(panel + "TimeCharacterIcon", roster.CharacterIconAt(character));
        }
        void DrawOriginalRecordPanels()
        {
            authoredUi.SetActive(OriginalCupPage + "TimePanel", originalCircuitRecord != null && originalCircuitRecord.bestSeconds > 0);
            authoredUi.SetActive(OriginalCupPage + "TimePanelCUP", originalCupRecord != null && originalCupRecord.bestSeconds > 0);
            if (originalCircuitRecord != null)
                BindOriginalRecordPanel("TimePanel", originalCircuitRecord.bestSeconds, originalCircuitRecord.lapSeconds, originalCircuitRecord.vehicleId, originalCircuitRecord.characterId);
            if (originalCupRecord != null)
                BindOriginalRecordPanel("TimePanelCUP", originalCupRecord.bestSeconds, originalCupRecord.stationSeconds, originalCupRecord.vehicleId, originalCupRecord.characterId);
        }
        public NativeMenuAction AuthoredCup(float width)
        {
            SetMenuPage(NativeMenuPage.Cups); activeFont = menuFont;
            authoredUi.Show("selection", "CupSelection");
            authoredUi.SetActive("selection/Canvas/bg", authoredUi.Definition("selection/Canvas/bg").active);
            authoredUi.SetActive("selection/Canvas/testbg", false);
            authoredUi.SetActive(OriginalCupPage + "Cups", true);
            authoredUi.SetActive(OriginalCupPage + "promptCup", true);
            authoredUi.SetActive(OriginalCupPage + "promptCircuit", false);
            authoredUi.SetActive(OriginalCupPage + "Minimap", false);
            authoredUi.SetActive(OriginalCupPage + "maskbanner", false);
            DrawOriginalRecordPanels();
            authoredUi.SetText(OriginalCupPage + "CupName", NativeLocalization.Get("CUP_MORIYA"));
            for (int i = 0; i < NativeTrackCatalog.Count; i++)
            {
                string card = OriginalCupPage + "CircuitGrid4x4/" + (i + 1);
                authoredUi.SetSprite(card, TrackBannerAt(i));
                authoredUi.SetText(card + "/CircuitName", NativeLocalization.Get(i == 3 ? "CIRCUIT_WINTERLAKE" : "CIRCUIT_" + NativeTrackCatalog.IdAt(i).ToUpperInvariant()));
                authoredUi.SetActive(card + "/selected", false);
            }
            authoredUi.SetActive(OriginalCupPage + "CircuitGrid4x4/Select", false);
            var action = !SuppressPointerInput && PointerButton(authoredUi.RectAt(OriginalCupPage + "Cups/Cup"))
                ? ConfirmMenu(NativeMenuPage.Cups) : NativeMenuAction.None;
            return OriginalPromptPointer(OriginalCupPage, action);
        }
        NativeMenuAction DrawOriginalCircuitSelection(float width)
        {
            SetMenuPage(NativeMenuPage.Tracks); activeFont = menuFont;
            authoredUi.Show("selection", "CupSelection");
            authoredUi.SetActive("selection/Canvas/bg", authoredUi.Definition("selection/Canvas/bg").active);
            authoredUi.SetActive("selection/Canvas/testbg", false);
            authoredUi.SetActive(OriginalCupPage + "Cups", false);
            authoredUi.SetActive(OriginalCupPage + "promptCup", false);
            authoredUi.SetActive(OriginalCupPage + "promptCircuit", true);
            authoredUi.SetActive(OriginalCupPage + "Minimap", true);
            authoredUi.SetActive(OriginalCupPage + "maskbanner/circuitgiantbanner", true);
            authoredUi.SetActive(OriginalCupPage + "maskbanner", true);
            authoredUi.SetText(OriginalCupPage + "CupName", NativeLocalization.Get("CUP_MORIYA"));
            if (originalCircuitMinimaps == null)
                originalCircuitMinimaps = Enumerable.Range(0, NativeTrackCatalog.Count)
                    .Select(i => NativeContent.Load<Sprite>("NativeSelectionPreview/" + NativeTrackCatalog.IdAt(i) + "-minimap")).ToArray();
            NativeMenuAction action = NativeMenuAction.None;
            for (int i = 0; i < NativeTrackCatalog.Count; i++)
            {
                string card = OriginalCupPage + "CircuitGrid4x4/" + (i + 1);
                authoredUi.SetSprite(card, TrackBannerAt(i));
                authoredUi.SetText(card + "/CircuitName", NativeLocalization.Get(i == 3 ? "CIRCUIT_WINTERLAKE" : "CIRCUIT_" + NativeTrackCatalog.IdAt(i).ToUpperInvariant()));
                authoredUi.SetActive(card + "/selected", i == SelectedMenuIndex);
                authoredUi.SetSprite(card + "/selected", authoredUi.Definition(OriginalCupPage + "CircuitGrid4x4/Select").sprite);
                Rect rect = authoredUi.RectAt(card);
                HoverMenu(i, rect);
                if (!SuppressPointerInput && PointerButton(rect))
                {
                    Navigation.Select(i); action = ConfirmMenu(MenuPage);
                }
            }
            authoredUi.SetActive(OriginalCupPage + "CircuitGrid4x4/Select", false);
            authoredUi.SetSprite(OriginalCupPage + "Minimap", originalCircuitMinimaps[SelectedMenuIndex]);
            UpdateOriginalTrackBanner();
            originalCircuitRecord = SelectionRecordMode == NativeSessionMode.TimeTrials
                ? NativeSaveStore.Data.timeTrialRecords.FirstOrDefault(r => r.trackId == NativeTrackCatalog.IdAt(SelectedMenuIndex)) : null;
            DrawOriginalRecordPanels();
            return OriginalPromptPointer(OriginalCupPage, action);
        }
    }
}
