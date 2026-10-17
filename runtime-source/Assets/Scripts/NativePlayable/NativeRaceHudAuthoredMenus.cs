using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public void DisposeAuthoredUi() { ClearMenuTransition(); DisposeOnlineAuthoring(); authoredUi?.Dispose(); authoredUi = null; DisposeRaceMarkers(); }
        public int MusicSelection { get; set; }
        public string OnlineAddress { get; set; } = "127.0.0.1";
        Rect AuthoredRect(string scene, string path) => authoredUi.RectAt(scene + "/Canvas/" + path);
        NativeMenuAction OriginalPromptPointer(string prefix, NativeMenuAction action)
        {
            if (SuppressPointerInput) return action;
            if (PointerButton(authoredUi.RectAt(prefix + "Prompts/BText"))) return CancelMenu(MenuPage);
            var pointer = Mouse.current != null && Mouse.current.enabled ? Mouse.current : null;
            if (PointerButton(authoredUi.RectAt(prefix + "Prompts/AText")))
            {
                originalMenuPointerDevice = pointer; originalMenuPointerFrame = pointer != null ? Time.frameCount : -1;
                return ConfirmMenu(MenuPage);
            }
            return action;
        }
        public NativeMenuAction ExtrasMenu(float width)
        {
            SetMenuPage(NativeMenuPage.Extras); activeFont = menuFont;
            authoredUi.Show("menu2", "ExtrasMenu");
            NativeMenuAction action = NativeMenuAction.None;
            var names = new[] { "CreditsButton", "MusicRoomButton" };
            BindExtrasWheelCaptions();
            var rects = new Rect[names.Length];
            var amounts = new float[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string key = "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/" + names[i];
                float amount = amounts[i] = ButtonAmount(i, SelectedMenuIndex == i);
                authoredUi.SetActive(key, true);
                if (!authoredUi.ApplyMenuButton(key, amount, SelectedMenuIndex == i)) continue;
                var rect = rects[i] = authoredUi.RectAt(key);
                HoverMenu(i, rect);
                if (!SuppressPointerInput && PointerButton(rect)) { Navigation.Select(i); action = ConfirmMenu(MenuPage); }
            }
            const string selectorPivot = "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot";
            authoredUi.Offset(selectorPivot, Vector2.zero);
            float target = SelectorPosition(rects[SelectedMenuIndex].center.y + MenuSelectorOffset);
            authoredUi.Offset(selectorPivot, new Vector2(0, target - authoredUi.RectAt(selectorPivot).center.y));
            LocalizeOriginalPrompts("menu2/Canvas/ExtrasMenu/");
            if (Event.current.type == EventType.Repaint)
                MenuFocus = new MenuFocusSnapshot { page = "Extras", index = SelectedMenuIndex, selectorVisible = true,
                    selectorY = authoredUi.RectAt(selectorPivot).center.y, buttonAmounts = amounts, buttonRects = rects };
            return OriginalPromptPointer("menu2/Canvas/ExtrasMenu/", action);
        }
        void BindExtrasWheelCaptions()
        {
            const string root = "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/";
            authoredUi.BindLocalizedText(root + "CreditsButton/Text", "EXTRAS_CREDITS", "Credits");
            authoredUi.BindLocalizedText(root + "MusicRoomButton/Text", "EXTRAS_MUSICROOM", "Music Room");
        }
        public bool OriginalMenuVisualsBound(string page, out string detail)
        {
            bool prompts = authoredUi.OriginalPromptLayoutBound(page, out detail);
            if (page == "ExtrasMenu")
            {
                const string root = "menu2/Canvas/ExtrasMenu/LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/";
                bool buttons = authoredUi.VisibleGraphic(root + "CreditsButton") && authoredUi.VisibleGraphic(root + "CreditsButton/Text")
                    && authoredUi.VisibleGraphic(root + "MusicRoomButton") && authoredUi.VisibleGraphic(root + "MusicRoomButton/Text");
                detail += "; extras buttons visible=" + buttons;
                return prompts && buttons;
            }
            if (page == "MusicRoom")
            {
                bool disc = authoredUi.OriginalMusicDiscBound(NativeMusicRoomData.At(MusicSelection).cover, out string discDetail);
                detail += "; " + discDetail;
                return prompts && disc;
            }
            return prompts;
        }
        void LocalizeOriginalPrompts(string prefix)
        {
            if (SourceSchemeOriginalPrompts(prefix)) return;
            foreach (var node in authoredUi.Nodes(prefix))
            {
                string phrase = string.IsNullOrEmpty(node.phrase) ? node.text : node.phrase;
                string fallback;
                switch (phrase)
                {
                    case "PROMPT_SELECT": fallback = "Select"; break;
                    case "PROMPT_GOBACK": fallback = "Go Back"; break;
                    case "PROMPT_PAUSE": fallback = "Pause"; break;
                    default: continue;
                }
                string key = node.scene + "/" + node.path;
                string text = NativeLocalization.Get(phrase, fallback);
                authoredUi.SetText(key, text);
            }
        }
        public NativeMenuAction CreditsMenu(float width)
        {
            SetMenuPage(NativeMenuPage.Credits); authoredUi.Show("menu2", "Credits");
            LocalizeOriginalPrompts("menu2/Canvas/Credits/");
            return !SuppressPointerInput && PointerButton(authoredUi.RectAt("menu2/Canvas/Credits/Prompts/BText"))
                ? NativeMenuAction.BackToExtras : NativeMenuAction.None;
        }
        public NativeMenuAction MusicRoomMenu(float width)
        {
            SetMenuPage(NativeMenuPage.MusicRoom); activeFont = menuFont;
            return OriginalMusicRoom();
        }
        public NativeMenuAction OnlineMenu(float width)
        {
            SetMenuPage(NativeMenuPage.Online); activeFont = menuFont; Shade(width, .82f);
            Label(new Rect(width / 2 - 300, 100, 600, 70), NativeLocalization.Get("MENU_ONLINE", "Online"), 54, Color.white, TextAnchor.MiddleCenter);
            Label(new Rect(width / 2 - 360, 230, 720, 100), "Online play is unavailable in this build.", 28, Color.white, TextAnchor.MiddleCenter);
            return MenuTextButton(0, new Rect(width / 2 - 220, 430, 440, 65), NativeLocalization.Get("PROMPT_GOBACK", "Go Back"))
                ? NativeMenuAction.BackToMain : NativeMenuAction.None;
        }
        const string CharacterPanel = "selection/Canvas/CharSelection/";
        public NativeMenuAction AuthoredCharacters(float width, NativeVehicleRoster roster, int[] choices)
        {
            SetMenuPage(NativeMenuPage.Characters); activeFont = menuFont;
            ShowOriginalSinglePlayerBoxes();
            int character = choices[SelectedMenuIndex];
            authoredUi.SetText(CharacterPanel + "P1Box/Joined/charname", NativeLocalization.Get("CHAR_" + roster.CharacterIdAt(character).ToUpperInvariant(), roster.CharacterNameAt(character)));
            authoredUi.SetActive(CharacterPanel + "P1Box/Joined/charname", true);
            authoredUi.SetActive(CharacterPanel + "P1Box/Joined/vehicleselection", false);
            authoredUi.SetActive(CharacterPanel + "P1Box/Joined/prompt", false);
            BindOriginalExclusiveItem(roster.CharacterIdAt(character));
            return OriginalPromptPointer(CharacterPanel, RenderOriginalCharacterGrid(roster, choices[SelectedMenuIndex], false));
        }
        public int VehicleSelection { get; set; }
        NativeMenuAction RenderOriginalCharacterGrid(NativeVehicleRoster roster, int selectedCharacter, bool vehicleState)
        {
            NativeMenuAction action = NativeMenuAction.None;
            int[] choices = roster.SelectableCharacters();
            authoredUi.PrepareCharacterNavigation(choices.Select(roster.CharacterIdAt));
            var slots = authoredUi.Nodes(CharacterPanel + "CharacterGrid/").Where(n => !string.IsNullOrEmpty(n.characterId)).OrderBy(n => n.selectionOrder).ToArray();
            string selectedKey = null;
            foreach (var slot in slots)
            {
                string key = "selection/" + slot.path;
                int character = roster.CharacterIndexOf(slot.characterId), index = Array.IndexOf(choices, character);
                if (index < 0 || !authoredUi.CharacterSlotActive(slot.characterId)) continue;
                authoredUi.SetSprite(key + "/Image", roster.CharacterIconAt(character));
                if (character == selectedCharacter) selectedKey = key;
            }
            foreach (var slot in slots)
            {
                int character = roster.CharacterIndexOf(slot.characterId), index = Array.IndexOf(choices, character);
                if (index < 0 || vehicleState || !authoredUi.CharacterSlotActive(slot.characterId)) continue;
                Rect rect = authoredUi.RectAt("selection/" + slot.path);
                HoverMenu(index, rect);
                if (!SuppressPointerInput && PointerButton(rect)) { Navigation.Select(index); NotifyOriginalCharacterFocusSelected(); action = ConfirmMenu(MenuPage); }
            }
            // GridLayoutGroup resolves positions after all original slots have been ordered.
            Canvas.ForceUpdateCanvases();
            if (selectedKey != null) authoredUi.PlaceAt(CharacterPanel + "P1Select", selectedKey);
            authoredUi.SetActive(CharacterPanel + "P1Select", true);
            authoredUi.SetSprite(CharacterPanel + "P1Select/Image", roster.CharacterIconAt(selectedCharacter));
            authoredUi.SetActive(CharacterPanel + "P1Select/highlight", !vehicleState);
            authoredUi.SetActive(CharacterPanel + "P1Select/selected", vehicleState);
            return action;
        }
        public void NavigateOriginalChoice(NativeMenuPage page, Vector2 input, NativeVehicleRoster roster, int[] choices)
        {
            if (page == NativeMenuPage.Vehicles || page == NativeMenuPage.Skins)
            {
                MoveOriginalPlayerBox(NativePlayerBoxNavigation.Step(input));
                return;
            }
            if (page == NativeMenuPage.Tracks)
            {
                int index = SelectedMenuIndex;
                int target = Mathf.Abs(input.y) > .5f ? index + (input.y > 0 ? -2 : 2)
                    : index + (input.x > 0 ? 1 : -1);
                if (target >= 0 && target < 4 && (Mathf.Abs(input.y) > .5f || index / 2 == target / 2)
                    && Navigation.Select(target)) { HoverOriginalCircuitBanner(target); PlaySelectionSound(page); }
                return;
            }
            if (page == NativeMenuPage.Characters)
            {
                if (authoredUi == null || choices.Length == 0) return;
                authoredUi.PrepareCharacterNavigation(choices.Select(roster.CharacterIdAt));
                int characterDirection = NativePlayerBoxNavigation.Direction(input);
                string neighbor = authoredUi.CharacterNeighbor(roster.CharacterIdAt(choices[SelectedMenuIndex]), characterDirection);
                int target = neighbor == null ? -1 : Array.IndexOf(choices, roster.CharacterIndexOf(neighbor));
                if (target >= 0)
                {
                    bool changed = Navigation.Select(target);
                    NotifyOriginalCharacterFocusSelected();
                    if (changed) PlayOriginalSelectionMoveSound(false);
                }
                return;
            }
            var nodes = authoredUiData.nodes.Where(n => n.scene == "selection").ToArray();
            NativeAuthoredUiNode current = page == NativeMenuPage.Characters
                ? nodes.Single(n => n.characterId == roster.CharacterIdAt(choices[SelectedMenuIndex]))
                : nodes.Single(n => n.path == "Canvas/CupSelection/CircuitGrid4x4/" + (SelectedMenuIndex + 1));
            int direction = Mathf.Abs(input.y) > .5f ? input.y > 0 ? 0 : 2 : input.x > 0 ? 1 : 3;
            for (int step = 0; step < 24; step++)
            {
                if (current.navigation == null || current.navigation.Length <= direction || string.IsNullOrEmpty(current.navigation[direction])) return;
                current = nodes.Single(n => n.path == current.navigation[direction]);
                int index = page == NativeMenuPage.Characters ? Array.IndexOf(choices, roster.CharacterIndexOf(current.characterId))
                    : int.Parse(current.path.Substring(current.path.LastIndexOf('/') + 1)) - 1;
                if (index < 0) continue;
                if (Navigation.Select(index)) PlaySelectionSound(page);
                return;
            }
        }
        public bool OriginalCharacterSelectionBound(string characterId, out string detail) => authoredUi.OriginalCharacterSelectionBound(characterId, Navigation.CharacterCount, out detail);
        public NativeAuthoredUi.CharacterNavigationSample[] CharacterNavigationSnapshot() => authoredUi.CharacterNavigationSnapshot();
        public Rect PreviewRect => AuthoredRect("selection", "CharSelection/P1Box/Joined/rt");
        public void PreviewPicture(Rect rect, Texture texture) { if (texture) authoredUi.RawTexture(CharacterPanel + "P1Box/Joined/rt", texture); }
        public NativeMenuAction AuthoredVehicles(float width, NativeVehicleRoster roster) => DrawOriginalVehicleSelection(width, roster);
        public Rect VehiclePreviewRect => AuthoredRect("selection", "CharSelection/P1Box/Joined/rt");
        Sprite[] originalTrackBanners;
        Image originalTrackBannerImage;
        int originalTrackBannerSelection = -1;
        public Sprite TrackBannerAt(int index)
        {
            if (originalTrackBanners == null || originalTrackBanners.Length != NativeTrackCatalog.Count)
                originalTrackBanners = Enumerable.Range(0, NativeTrackCatalog.Count)
                    .Select(i => NativeContent.Load<Sprite>("NativeSelectionPreview/" + NativeTrackCatalog.IdAt(i) + "-banner")).ToArray();
            return originalTrackBanners[Mathf.Clamp(index, 0, originalTrackBanners.Length - 1)];
        }
        public bool OriginalTrackBannersBound(out string detail)
        {
            if (MenuPage != NativeMenuPage.Tracks) { detail = "Track menu is not active"; return false; }
            bool loaded = Enumerable.Range(0, NativeTrackCatalog.Count).All(i => TrackBannerAt(i) && TrackBannerAt(i).texture);
            bool bound = originalTrackBannerImage && originalTrackBannerImage.sprite == TrackBannerAt(SelectedMenuIndex);
            detail = "Original circuit banners loaded=" + loaded + "; selected=" + NativeTrackCatalog.IdAt(SelectedMenuIndex) + "; authored Image bound=" + bound;
            return loaded && bound;
        }
        void UpdateOriginalTrackBanner() => TickOriginalCircuitBannerMotion();
        public NativeMenuAction AuthoredTracks(float width) => DrawOriginalCircuitSelection(width);
    }
}
