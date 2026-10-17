using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        const string CustomPage = "selection/Canvas/Customization/";
        const string CustomRows = CustomPage + "OptionsBox/Scroll View/Viewport/Content/";
        float customScroll;
        int customLastFocus = -1;
        public int CustomControlCount => Navigation.Count;
        public bool CustomAuthoredReady => authoredUi != null;
        public int CustomRenderedFrame { get; private set; } = -1;
        internal Action<EventType, EventType, Vector2, int> CustomScrollbarPointerObserved;
        internal Action CustomBeforeLayoutVerification;
        public void CloseCustomDropdown() => authoredUi?.ClearCustomDropdown();
        public AudioSource CustomUiAudioSource => menuAudio;
        public AudioClip CustomUiSound(int index) => index == 0 ? menuMoveSound : index == 1 ? menuConfirmSound : index == 2 ? menuCancelSound : throw new ArgumentOutOfRangeException(nameof(index));
        public void PlayCustomizationSound(int index) => PlaySound(CustomUiSound(index));
        public void ResetCustomizationFocus()
        {
            customScroll = 0; customLastFocus = -1;
            Navigation.Select(0);
            authoredUi?.ReleaseSettingsPointer();
            PlayCustomizationSound(0);
        }
        string[] CustomOptions(int row) => Array.ConvertAll(authoredUi.CustomizationOptionKeys(NativeCustomRaceRules.Rows[row]), key => NativeLocalization.Get(key));
        public NativeMenuAction CustomModeMenu(float width, Func<int, int> valueAt, Action<int, int> setValue,
            bool dropdownOpen, int dropdownOption, Action<int> selectOption, Action closeDropdown, Action<int> hoverOption, Func<int, int> minimumAt = null)
        {
            SetMenuPage(NativeMenuPage.Custom); activeFont = menuFont;
            if (Event.current.type == EventType.Repaint) CustomRenderedFrame = Time.frameCount;
            authoredUi.Show("selection", "Customization", "/SubOptionBox", "/PreviewDescription", "/Tabs", "/ScreenOptions", "/PreviewBox", "/Template");
            authoredUi.SetActive("selection/Canvas/bg", authoredUi.Definition("selection/Canvas/bg").active); authoredUi.SetActive("selection/Canvas/testbg", false);
            foreach (string row in new[] { "-BOTS", "characters", "vehicles", "availability", "skin" }) authoredUi.SetActive(CustomRows + row, false);
            CustomBeforeLayoutVerification?.Invoke();
            Rect viewport = authoredUi.RectAt(CustomPage + "OptionsBox/Scroll View/Viewport");
            if (!dropdownOpen && !SuppressPointerInput && Event.current.type == EventType.ScrollWheel && viewport.Contains(Event.current.mousePosition))
            { customScroll += Event.current.delta.y * authoredUi.CustomizationScrollSensitivity; Event.current.Use(); }
            authoredUi.CustomizationLayout(customScroll);
            var scrollbarEvent = Event.current.type;
            var scrollbarPosition = Event.current.mousePosition;
            customScroll = authoredUi.SettingsScrollbarPointer(CustomPage + "OptionsBox/Scroll View", customScroll, !dropdownOpen && !SuppressPointerInput);
            CustomScrollbarPointerObserved?.Invoke(scrollbarEvent, Event.current.type, scrollbarPosition, GUIUtility.hotControl);
            authoredUi.CustomizationLayout(customScroll);
            if (customLastFocus != SelectedMenuIndex)
            {
                customScroll = authoredUi.CustomizationScrollTo(NativeCustomRaceRules.Rows[SelectedMenuIndex], customScroll);
                authoredUi.CustomizationLayout(customScroll); customLastFocus = SelectedMenuIndex;
            }
            NativeMenuAction action = NativeMenuAction.None;
            for (int i = 0; i < NativeCustomRaceRules.Rows.Length; i++)
            {
                string row = NativeCustomRaceRules.Rows[i];
                authoredUi.CustomizationFocus(row, i == SelectedMenuIndex && !dropdownOpen);
                if (i == 3 || i == 4) authoredUi.SetText(CustomRows + row + "/Value/Dropdown/Label", CustomOptions(i)[valueAt(i)]);
                else if (i > 0) authoredUi.CustomizationSlider(row, authoredUi.CustomizationFormattedValue(row, valueAt(i)),
                    Mathf.InverseLerp((minimumAt != null ? minimumAt(i) : NativeCustomRaceRules.Minimum(i)), NativeCustomRaceRules.Maximum(i), valueAt(i)));
                Rect rect = authoredUi.RectAt(CustomRows + row);
                if (!rect.Overlaps(viewport) || dropdownOpen || SuppressPointerInput) continue;
                Rect visible = Rect.MinMaxRect(Mathf.Max(rect.x, viewport.x), Mathf.Max(rect.y, viewport.y), Mathf.Min(rect.xMax, viewport.xMax), Mathf.Min(rect.yMax, viewport.yMax));
                HoverMenu(i, visible);
                if (i > 0 && i != 3 && i != 4)
                {
                    Rect slider = authoredUi.RectAt(CustomRows + row + "/Value/Slider");
                    float normalized = Mathf.InverseLerp((minimumAt != null ? minimumAt(i) : NativeCustomRaceRules.Minimum(i)), NativeCustomRaceRules.Maximum(i), valueAt(i));
                    float changed = GUI.HorizontalSlider(slider, normalized, 0, 1, GUIStyle.none, GUIStyle.none);
                    if (Mathf.Abs(changed - normalized) > .00001f)
                    {
                        Navigation.Select(i);
                        setValue(i, Mathf.RoundToInt(Mathf.Lerp((minimumAt != null ? minimumAt(i) : NativeCustomRaceRules.Minimum(i)), NativeCustomRaceRules.Maximum(i), changed)));
                    }
                    if (PointerButton(visible)) Navigation.Select(i);
                }
                else if (PointerButton(visible)) { Navigation.Select(i); action = ConfirmMenu(MenuPage); }
            }
            if (dropdownOpen)
            {
                var options = authoredUi.CustomizationDropdown(NativeCustomRaceRules.Rows[SelectedMenuIndex], CustomOptions(SelectedMenuIndex), dropdownOption, valueAt(SelectedMenuIndex));
                Rect popup = authoredUi.RectAt(CustomRows + NativeCustomRaceRules.Rows[SelectedMenuIndex] + "/Value/Dropdown/Template");
                if (!SuppressPointerInput && Event.current.type == EventType.MouseUp && Event.current.button == 0 && !popup.Contains(Event.current.mousePosition))
                { closeDropdown(); Event.current.Use(); }
                if (!SuppressPointerInput) for (int i = 0; i < options.Length; i++)
                {
                    if ((Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDown) && options[i].Contains(Event.current.mousePosition)) hoverOption(i);
                    if (PointerButton(options[i])) { selectOption(i); break; }
                }
            }
            return action;
        }
    }
}
