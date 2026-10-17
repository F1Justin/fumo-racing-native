using System;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        const string CustomContent = "selection/Canvas/Customization/OptionsBox/Scroll View/Viewport/Content";
        public void CustomizationLayout(float scroll)
        {
            var rows = transforms.Where(pair => pair.Key.StartsWith(CustomContent + "/", StringComparison.Ordinal)
                && pair.Key.Substring(CustomContent.Length + 1).IndexOf('/') < 0).OrderBy(pair => definitions[pair.Key].order);
            float y = 0;
            foreach (var row in rows)
            {
                row.Value.SetSiblingIndex(definitions[row.Key].order);
                if (!row.Value.gameObject.activeSelf) continue;
                row.Value.anchorMin = row.Value.anchorMax = new Vector2(0, 1);
                row.Value.anchoredPosition = new Vector2(0, -y);
                y += definitions[row.Key].size.y;
            }
            transforms[CustomContent].sizeDelta = new Vector2(transforms[CustomContent].sizeDelta.x, y);
            var viewport = transforms[CustomContent.Substring(0, CustomContent.LastIndexOf('/'))];
            float viewportHeight = viewport.rect.height;
            bool overflow = y > viewportHeight;
            viewport.sizeDelta = new Vector2(overflow ? definitions[CustomContent.Substring(0, CustomContent.LastIndexOf('/'))].size.x : 0, viewport.sizeDelta.y);
            SetActive("selection/Canvas/Customization/OptionsBox/Scroll View/Scrollbar Vertical", overflow);
            transforms[CustomContent].anchoredPosition = new Vector2(0, Mathf.Clamp(scroll, 0, Mathf.Max(0, y - viewportHeight)));
            string handle = "selection/Canvas/Customization/OptionsBox/Scroll View/Scrollbar Vertical/Sliding Area/Handle";
            if (transforms.TryGetValue(handle, out var h))
            {
                float fraction = Mathf.Clamp01(viewportHeight / Mathf.Max(viewportHeight, y));
                float top = 1 - (y <= viewportHeight ? 0 : Mathf.Clamp01(scroll / (y - viewportHeight))) * (1 - fraction);
                h.anchorMin = new Vector2(0, top - fraction); h.anchorMax = new Vector2(1, top);
                h.anchoredPosition = definitions[handle].position;
                h.sizeDelta = definitions[handle].size;
            }
        }
        public float CustomizationScrollTo(string row, float scroll)
        {
            string viewportKey = CustomContent.Substring(0, CustomContent.LastIndexOf('/'));
            Rect viewport = RectAt(viewportKey), item = RectAt(CustomContent + "/" + row);
            float scale = Screen.height / 600f / transforms[viewportKey].GetComponentInParent<Canvas>().scaleFactor;
            if (item.y < viewport.y) scroll -= (viewport.y - item.y) * scale;
            if (item.yMax > viewport.yMax) scroll += (item.yMax - viewport.yMax) * scale;
            return Mathf.Max(0, scroll);
        }
        [Serializable] sealed class CustomFocusSource
        {
            public string rowKey, target, selectedSpriteGuid, defaultSpriteGuid;
            public Color color;
        }
        [Serializable] sealed class CustomValueSource { public string kind, path; public int percent; public string[] optionKeys; }
        [Serializable] sealed class CustomTextSource { public string rowKey; public CustomValueSource[] bindings; }
        [Serializable] sealed class CustomSourceDocument { public CustomFocusSource[] rows; public CustomTextSource[] textBindings; public float scrollSensitivity; }
        public string CustomizationFormattedValue(string row, int value)
        {
            var binding = CustomSource.textBindings.Single(r => r.rowKey == CustomContent + "/" + row).bindings.Single(b => b.kind == "slider");
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture) + (binding.percent == 1 ? "%" : "");
        }
        public string[] CustomizationOptionKeys(string row) => CustomSource.textBindings.Single(r => r.rowKey == CustomContent + "/" + row).bindings.Single(b => b.kind == "dropdown").optionKeys;
        CustomSourceDocument customSource;
        CustomSourceDocument CustomSource
        {
            get
            {
                if (customSource == null) customSource = JsonUtility.FromJson<CustomSourceDocument>(NativeContent.Load<TextAsset>("NativeMenu/customization-source-baseline").text);
                return customSource;
            }
        }
        void CustomFocus(string key, Transform target, bool selected)
        {
            if (prefabAuthored)
            {
                var visual = target.GetComponent<NativeMenuControlVisual>();
                if (!visual) throw new InvalidOperationException("Custom control needs the one-time Editor migration: " + key);
                visual.Apply(selected); return;
            }
            var source = CustomSource.rows.Single(row => row.rowKey == key);
            var image = target.Find(source.target).GetComponent<Image>();
            string guid = selected ? source.selectedSpriteGuid : source.defaultSpriteGuid;
            image.sprite = guid == "be8a4e5ff4bb7014e9321941f176decc" ? NativeContent.Load<Sprite>("NativeMenu/SettingsSelectedDropdown") : definitions.Values.First(node => node.spriteGuid == guid).sprite;
            image.color = source.color;
        }
        public float CustomizationScrollSensitivity => CustomSource.scrollSensitivity;
        public void CustomizationFocus(string row, bool selected)
        {
            string key = CustomContent + "/" + row;
            CustomFocus(key, transforms[key], selected);
        }
        readonly List<GameObject> customDropdownItems = new List<GameObject>();
        string customDropdownKey;
        public Rect[] CustomizationDropdown(string row, string[] options, int highlighted, int chosen)
        {
            string key = CustomContent + "/" + row + "/Value/Dropdown/Template";
            if (customDropdownKey != key) { ClearCustomDropdown(); customDropdownKey = key; }
            foreach (var pair in transforms)
                if (pair.Key.StartsWith(key + "/", StringComparison.Ordinal)) pair.Value.gameObject.SetActive(definitions[pair.Key].active);
            var template = transforms[key]; template.gameObject.SetActive(true); template.parent.parent.parent.SetAsLastSibling();
            var content = transforms[key + "/Viewport/Content"]; var item = transforms[key + "/Viewport/Content/Item"];
            float height = definitions[key + "/Viewport/Content/Item"].size.y;
            item.gameObject.SetActive(false);
            while (customDropdownItems.Count < options.Length)
            { var clone = UnityEngine.Object.Instantiate(item.gameObject, content); clone.name = "Native custom option"; customDropdownItems.Add(clone); }
            template.sizeDelta = new Vector2(template.sizeDelta.x, height * options.Length);
            var viewport = transforms[key + "/Viewport"];
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            if (transforms.TryGetValue(key + "/Scrollbar", out var bar)) bar.gameObject.SetActive(false);
            content.anchorMin = content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(definitions[key + "/Viewport/Content"].size.x, height * options.Length);
            var rects = new Rect[options.Length];
            for (int i = 0; i < customDropdownItems.Count; i++)
            {
                var go = customDropdownItems[i]; go.SetActive(i < options.Length); if (i >= options.Length) continue;
                var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(0, -i * height);
                rt.sizeDelta = new Vector2(definitions[key + "/Viewport/Content/Item"].size.x, height);
                foreach (var text in go.GetComponentsInChildren<TMP_Text>(true)) { text.text = options[i]; text.color = definitions[key + "/Viewport/Content/Item/Item Label"].fontColor; }
                CustomFocus(key + "/Viewport/Content/Item", go.transform, i == highlighted);
                foreach (var image in go.GetComponentsInChildren<Image>(true))
                {
                    if (image.name.IndexOf("Checkmark", StringComparison.OrdinalIgnoreCase) >= 0) image.gameObject.SetActive(i == chosen);
                }
                Canvas.ForceUpdateCanvases(); var corners = new Vector3[4]; rt.GetWorldCorners(corners); float scale = Screen.height / 600f;
                rects[i] = new Rect(corners[0].x / scale, (Screen.height - corners[1].y) / scale, (corners[2].x - corners[0].x) / scale, (corners[1].y - corners[0].y) / scale);
            }
            return rects;
        }
        public void ClearCustomDropdown()
        {
            ReleaseSettingsPointer();
            foreach (var item in customDropdownItems) UnityEngine.Object.Destroy(item);
            customDropdownItems.Clear();
            if (customDropdownKey != null && transforms.TryGetValue(customDropdownKey, out var template)) template.gameObject.SetActive(false);
            customDropdownKey = null;
        }
        public void CustomizationSlider(string row, string value, float normalized)
        {
            SetText(CustomContent + "/" + row + "/Value/SliderValue", value);
            string key = CustomContent + "/" + row + "/Value/Slider";
            var slider = transforms[key].GetComponent<Slider>();
            if (!slider && prefabAuthored) throw new InvalidOperationException("Missing authored Slider: " + key);
            if (!slider)
            {
                slider = transforms[key].gameObject.AddComponent<Slider>();
                slider.fillRect = transforms[key + "/Fill Area/Fill"];
                slider.handleRect = transforms[key + "/Handle Slide Area/Handle"];
                slider.transition = Selectable.Transition.None;
                slider.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            slider.SetValueWithoutNotify(normalized > .5f ? 0 : 1);
            slider.SetValueWithoutNotify(normalized);
        }
    }
}
