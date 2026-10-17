using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeSettingsControl
    {
        public string node, value;
        public string[] options;
        public int option;
        public float normalized;
        public float minimum=0,maximum=1;
        public bool wholeNumbers;
        public Action<float> setNormalized;
        public Action<int> setOption;
    }

    public sealed partial class NativeAuthoredUi
    {
        [Serializable] sealed class SettingsFocusSourceRow
        {
            public string rowKey, target, selectedSpriteGuid, defaultSpriteGuid;
            public long sourceComponentId;
        }
        [Serializable] sealed class SettingsFocusSourceDocument { public SettingsFocusSourceRow[] rows; }
        Dictionary<string, SettingsFocusSourceRow> settingsFocusSources;
        Sprite settingsDropdownSelectedSprite;
        void ApplySettingsFocus(string rowKey, Transform row, bool focused)
        {
            if (prefabAuthored)
            {
                var visual = row.GetComponent<NativeMenuControlVisual>();
                if (!visual) throw new InvalidOperationException("Settings control needs the one-time Editor migration: " + rowKey);
                visual.Apply(focused); return;
            }
            if (settingsFocusSources == null)
            {
                var source = NativeContent.Load<TextAsset>("NativeMenu/settings-focus-source");
                if (!source) throw new InvalidOperationException("Missing original settings focus source");
                settingsFocusSources = JsonUtility.FromJson<SettingsFocusSourceDocument>(source.text).rows.ToDictionary(entry => entry.rowKey);
            }
            if (!settingsFocusSources.TryGetValue(rowKey, out var binding)) throw new InvalidOperationException("Missing original focus target " + rowKey);
            var target = row.Find(binding.target);
            if (!target || !target.TryGetComponent<Image>(out var image)) throw new InvalidOperationException("Missing original focus Image " + rowKey + "/" + binding.target);
            if (focused && binding.selectedSpriteGuid == "be8a4e5ff4bb7014e9321941f176decc")
            {
                if (!settingsDropdownSelectedSprite) settingsDropdownSelectedSprite = NativeContent.Load<Sprite>("NativeMenu/SettingsSelectedDropdown");
                image.sprite = settingsDropdownSelectedSprite;
            }
            else image.sprite = focused ? definitions.Values.First(node => node.spriteGuid == binding.selectedSpriteGuid).sprite : definitions[rowKey + "/" + binding.target].sprite;
            if (!image.sprite) throw new InvalidOperationException("Missing original selected sprite " + binding.selectedSpriteGuid);
            image.color = definitions[rowKey + "/" + binding.target].color;
            var property = row.Find("Name/Property");
            if (property && property.TryGetComponent<TMP_Text>(out var text)) text.color = definitions[rowKey + "/Name/Property"].fontColor;
        }
        readonly Dictionary<string, KeyValuePair<string, RectTransform>[]> settingsChildren = new Dictionary<string, KeyValuePair<string, RectTransform>[]>();
        readonly Dictionary<Slider, int> settingsSliderGenerations = new Dictionary<Slider, int>();
        string settingsLayoutPage;
        int settingsLayoutGeneration = -1, settingsLayoutWidth, settingsLayoutHeight;
        float settingsLayoutScroll = float.NaN;
        KeyValuePair<string, RectTransform>[] SettingsChildren(string parent)
        {
            if (!settingsChildren.TryGetValue(parent, out var children))
            {
                children = transforms.Where(p => p.Key.StartsWith(parent + "/", StringComparison.Ordinal) &&
                    p.Key.Substring(parent.Length + 1).IndexOf('/') < 0).OrderBy(p => definitions[p.Key].order).ToArray();
                settingsChildren.Add(parent, children);
            }
            return children;
        }
        public void ShowSettingsPage(string page)
        {
            pageRequested = true;
            if (visiblePage != "menu2/Canvas/" + page)
                Show("menu2", page, "/ScreenOptions", "/Tabs", "/PreviewDescription", "/PreviewBox", "/Prompts");
            else RefreshStaticText();
        }
        public Rect SettingsRectAt(string key) => ReadRect(transforms[key]);
        readonly List<GameObject> settingsDropdownItems = new List<GameObject>();
        string settingsDropdownKey;
        int settingsScrollbarControl, settingsScrollbarRepeatFrame = -1;
        bool settingsScrollbarDragging;
        float settingsScrollbarOffset;
        Vector2 settingsScrollbarPress;

        public void ReleaseSettingsPointer()
        {
            if (settingsScrollbarControl != 0 && GUIUtility.hotControl == settingsScrollbarControl) GUIUtility.hotControl = 0;
            settingsScrollbarControl = 0;
            settingsScrollbarDragging = false;
        }

        public float SettingsScrollbarPointer(string scrollView, float scroll, bool allowPointer)
        {
            string barKey = scrollView + "/Scrollbar Vertical";
            string handleKey = barKey + "/Sliding Area/Handle";
            if (!transforms.TryGetValue(barKey, out var bar) || !bar.gameObject.activeInHierarchy
                || !transforms.TryGetValue(handleKey, out var handle)) return scroll;
            var viewport = transforms[scrollView + "/Viewport"];
            var content = transforms[scrollView + "/Viewport/Content"];
            float maximum = Mathf.Max(0, content.rect.height - viewport.rect.height);
            if (!allowPointer || maximum <= 0) { ReleaseSettingsPointer(); return Mathf.Clamp(scroll, 0, maximum); }
            Rect barRect = SettingsRectAt(barKey), handleRect = SettingsRectAt(handleKey);
            Rect slideRect = SettingsRectAt(barKey + "/Sliding Area");
            int control = GUIUtility.GetControlID(barKey.GetHashCode(), FocusType.Passive, barRect);
            var input = Event.current;
            if (input.type == EventType.MouseDown && input.button == 0 && barRect.Contains(input.mousePosition))
            {
                ReleaseSettingsPointer();
                settingsScrollbarControl = control; GUIUtility.hotControl = control;
                settingsScrollbarDragging = handleRect.Contains(input.mousePosition);
                settingsScrollbarOffset = settingsScrollbarDragging ? input.mousePosition.y - handleRect.y : handleRect.height * .5f;
                settingsScrollbarPress = input.mousePosition;
                settingsScrollbarRepeatFrame = -1;
                input.Use();
            }
            if (settingsScrollbarControl != control || GUIUtility.hotControl != control) return Mathf.Clamp(scroll, 0, maximum);
            if (input.type == EventType.MouseUp && input.button == 0)
            { ReleaseSettingsPointer(); input.Use(); return Mathf.Clamp(scroll, 0, maximum); }
            if (input.type == EventType.MouseDrag && input.button == 0)
            {
                settingsScrollbarDragging = true;
                float travel = slideRect.height - handleRect.height;
                if (travel > 0) scroll = maximum * Mathf.Clamp01((input.mousePosition.y - settingsScrollbarOffset - slideRect.y) / travel);
                input.Use();
            }
            // Original Scrollbar.ClickRepeat advances by its size once per rendered frame.
            if (!settingsScrollbarDragging && settingsScrollbarRepeatFrame != Time.frameCount)
            {
                settingsScrollbarRepeatFrame = Time.frameCount;
                if (!handleRect.Contains(settingsScrollbarPress))
                {
                    float size = Mathf.Clamp01(viewport.rect.height / content.rect.height);
                    scroll += (settingsScrollbarPress.y < handleRect.y ? -1 : 1) * size * maximum;
                    scroll = Mathf.Round(Mathf.Clamp01(scroll / maximum) * 10000) / 10000 * maximum;
                }
            }
            return Mathf.Clamp(scroll, 0, maximum);
        }
        public void SettingsLayout(string page, float scroll)
        {
            string content = "menu2/Canvas/" + page + "/VideoBox/Scroll View/Viewport/Content";
            var target = transforms[content];
            bool rebuild = settingsLayoutPage != page || settingsLayoutGeneration != characterNavigationPageGeneration ||
                settingsLayoutWidth != Screen.width || settingsLayoutHeight != Screen.height;
            if (!rebuild && settingsLayoutScroll == scroll) return;
            settingsLayoutPage = page; settingsLayoutGeneration = characterNavigationPageGeneration;
            settingsLayoutWidth = Screen.width; settingsLayoutHeight = Screen.height; settingsLayoutScroll = scroll;
            var rows = SettingsChildren(content);
            float y = 0;
            foreach (var pair in rows)
            {
                if (!pair.Value.gameObject.activeSelf) continue;
                if (rebuild)
                {
                    pair.Value.SetSiblingIndex(definitions[pair.Key].order);
                    pair.Value.anchorMin = pair.Value.anchorMax = new Vector2(0, 1);
                    pair.Value.anchoredPosition = new Vector2(0, -y);
                    LayoutSettingsRow(pair.Key, pair.Value);
                }
                y += definitions[pair.Key].size.y;
            }
            target.sizeDelta = new Vector2(target.sizeDelta.x, y);
            string scrollView = "menu2/Canvas/" + page + "/VideoBox/Scroll View";
            var viewport = transforms[scrollView + "/Viewport"];
            var scrollbar = transforms[scrollView + "/Scrollbar Vertical"];
            bool overflow = y > transforms[scrollView].rect.height + .01f;
            // ScrollRect originally drove this zero-size serialized viewport and reserved the
            // scrollbar width plus its -3 spacing. IMGUI owns scrolling in the native menu.
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(overflow ? -(definitions[scrollView + "/Scrollbar Vertical"].size.x - 3) : 0, 0);
            scrollbar.gameObject.SetActive(overflow);
            float viewportHeight = viewport.rect.height;
            float clampedScroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, y - viewportHeight));
            target.anchoredPosition = new Vector2(0, clampedScroll);
            string handle = scrollView + "/Scrollbar Vertical/Sliding Area/Handle";
            if(transforms.TryGetValue(handle,out var h))
            {
                float fraction = Mathf.Clamp01(viewportHeight / Mathf.Max(viewportHeight, y));
                float top = 1 - (y <= viewportHeight ? 0 : clampedScroll / (y - viewportHeight)) * (1-fraction);
                h.anchorMin = new Vector2(0, top-fraction);h.anchorMax = new Vector2(1, top);
                h.anchoredPosition = definitions[handle].position;
                h.sizeDelta = definitions[handle].size;
            }
            Canvas.ForceUpdateCanvases();
        }
        void LayoutSettingsRow(string key, RectTransform row)
        {
            // The authored HorizontalLayoutGroups drive Name/Value heights and column positions.
            // Their serialized transforms have zero height until that layout pass runs.
            float x = 0;
            foreach (var child in SettingsChildren(key))
            {
                if (!child.Value.gameObject.activeSelf) continue;
                var definition = definitions[child.Key];
                if (definition.anchorMin != Vector2.zero || definition.anchorMax != Vector2.zero) continue;
                child.Value.anchorMin = new Vector2(0, 0);
                child.Value.anchorMax = new Vector2(0, 1);
                child.Value.pivot = new Vector2(0, 1);
                child.Value.sizeDelta = new Vector2(definition.size.x, 0);
                child.Value.anchoredPosition = new Vector2(x, 0);
                x += definition.size.x;
            }
        }
        public float SettingsScrollTo(string page,string row,float scroll)
        {
            string prefix="menu2/Canvas/"+page+"/VideoBox/Scroll View/Viewport";
            Rect viewport=SettingsRectAt(prefix), item=SettingsRectAt(prefix+"/Content/"+row);
            float scale=Screen.height/600f;
            float canvasScale=transforms[prefix].GetComponentInParent<Canvas>().scaleFactor;
            if(item.y<viewport.y)scroll-=(viewport.y-item.y)*scale/canvasScale;
            if(item.yMax>viewport.yMax)scroll+=(item.yMax-viewport.yMax)*scale/canvasScale;
            return Mathf.Max(0,scroll);
        }
        public void SettingsControl(string page, NativeSettingsControl control, bool focused)
        {
            string row="menu2/Canvas/"+page+"/VideoBox/Scroll View/Viewport/Content/"+control.node;
            ApplySettingsFocus(row, transforms[row], focused);
            if(control.options!=null)SetText(row+"/Value/Dropdown/Label",control.value);
            else
            {
                SetText(row+"/Value/SliderValue",control.value);
                string sliderKey=row+"/Value/Slider";
                var sliderTransform=transforms[sliderKey];
                var slider=sliderTransform.GetComponent<Slider>();
                if (!slider && prefabAuthored) throw new InvalidOperationException("Missing authored Slider: " + sliderKey);
                if(!slider){slider=sliderTransform.gameObject.AddComponent<Slider>();slider.fillRect=transforms[sliderKey+"/Fill Area/Fill"];slider.handleRect=transforms[sliderKey+"/Handle Slide Area/Handle"];slider.transition=Selectable.Transition.None;slider.navigation=new Navigation{mode=Navigation.Mode.None};}
                slider.minValue=control.minimum;slider.maxValue=control.maximum;slider.wholeNumbers=control.wholeNumbers;
                if (!settingsSliderGenerations.TryGetValue(slider, out var generation) || generation != characterNavigationPageGeneration)
                {
                    // Show restores the authored handle transforms; refresh them once even when the value is unchanged.
                    slider.SetValueWithoutNotify(control.normalized > .5f ? control.minimum : control.maximum);
                    settingsSliderGenerations[slider] = characterNavigationPageGeneration;
                }
                slider.SetValueWithoutNotify(Mathf.Lerp(control.minimum,control.maximum,control.normalized));
            }
        }
        public Rect[] SettingsDropdown(string page,NativeSettingsControl control,int highlighted)
        {
            string key="menu2/Canvas/"+page+"/VideoBox/Scroll View/Viewport/Content/"+control.node+"/Value/Dropdown/Template";
            if(settingsDropdownKey!=key){ClearSettingsDropdown();settingsDropdownKey=key;}
            var template=transforms[key];template.gameObject.SetActive(true);template.parent.parent.parent.SetAsLastSibling();
            var content=transforms[key+"/Viewport/Content"];var item=transforms[key+"/Viewport/Content/Item"];
            float height=definitions[key+"/Viewport/Content/Item"].size.y;if(height<=0)height=56;
            item.gameObject.SetActive(false);
            while(settingsDropdownItems.Count<control.options.Length){var clone=UnityEngine.Object.Instantiate(item.gameObject,content);clone.name="Native option";settingsDropdownItems.Add(clone);}
            template.sizeDelta=new Vector2(template.sizeDelta.x,height*control.options.Length);
            // The template's ScrollRect originally expands its serialized zero-size viewport.
            // Every option fits the native template, so scrolling remains owned by the settings page.
            var viewport=transforms[key+"/Viewport"];
            viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;
            viewport.offsetMin=viewport.offsetMax=Vector2.zero;
            if(transforms.TryGetValue(key+"/Scrollbar",out var scrollbar))scrollbar.gameObject.SetActive(false);
            content.anchorMin=content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);
            content.anchoredPosition=Vector2.zero;
            content.sizeDelta=new Vector2(definitions[key+"/Viewport/Content"].size.x,height*control.options.Length);
            var rects=new Rect[control.options.Length];
            for(int i=0;i<settingsDropdownItems.Count;i++)
            {
                var go=settingsDropdownItems[i];go.SetActive(i<control.options.Length);if(i>=control.options.Length)continue;
                var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(0,-i*height);
                rt.sizeDelta=new Vector2(definitions[key+"/Viewport/Content/Item"].size.x,height);
                ApplySettingsFocus(key+"/Viewport/Content/Item",go.transform,i==highlighted);
                foreach(var text in go.GetComponentsInChildren<TMP_Text>(true)){text.text=control.options[i];text.color=definitions[key+"/Viewport/Content/Item/Item Label"].fontColor;}
                foreach(var image in go.GetComponentsInChildren<Image>(true))
                {
                    if(image.name.IndexOf("Checkmark",StringComparison.OrdinalIgnoreCase)>=0)image.gameObject.SetActive(i==control.option);

                }
            }
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < rects.Length; i++) rects[i] = ReadRect(settingsDropdownItems[i].GetComponent<RectTransform>());
            return rects;
        }
        public void ClearSettingsDropdown(){settingsLayoutGeneration = -1; ReleaseSettingsPointer();foreach(var item in settingsDropdownItems)UnityEngine.Object.Destroy(item);settingsDropdownItems.Clear();if(settingsDropdownKey!=null && transforms.TryGetValue(settingsDropdownKey,out var template))template.gameObject.SetActive(false);settingsDropdownKey=null;}
    }
}
