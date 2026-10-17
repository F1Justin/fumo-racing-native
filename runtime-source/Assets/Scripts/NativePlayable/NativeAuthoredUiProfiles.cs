using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        const string ProfileRoot = "profiles/Canvas/Profiles/";
        const string ProfileContent = ProfileRoot + "ProfilesBox/Scroll View/Viewport/Content";
        readonly List<RectTransform> profileRows = new List<RectTransform>();
        float profileScroll;
        string profilePage;
        int profileFocus = -1;

        public Rect[] ProfilesPage(string page, string[] labels, NativeSettingsControl[] controls, int selected, bool allowPointer = true)
        {
            bool editing = page == "EditProfile", device = page == "ProfileDevices", deleting = page == "DeleteProfile";
            string rootKey = editing ? "profiles/Canvas/Rebindings/" : ProfileRoot;
            Show("profiles", editing ? "Rebindings" : "Profiles", "/Tabs", "/PreviewBox", "/PreviewDescription", "/ScreenOptions");
            SetActive("profiles/Canvas/bg", true);
            if (!editing)
            {
                SetActive(ProfileRoot + "ProfilesBox", !device && !deleting);
                SetActive(ProfileRoot + "NewSelectDevice", device);
                SetActive(ProfileRoot + "ConfirmDeletionPanel", deleting);
            }
            if (profilePage != page) { ReleaseSettingsPointer(); profilePage = page; profileScroll = 0; profileFocus = -1; }
            if (editing)
            {
                var panel = transforms[rootKey + "ProfilesBox"];
                var canvasRect = panel.GetComponentInParent<Canvas>().GetComponent<RectTransform>().rect;
                panel.anchoredPosition = new Vector2(0, 24);
                panel.sizeDelta = new Vector2(Mathf.Min(definitions[rootKey + "ProfilesBox"].size.x, canvasRect.width - 96),
                    Mathf.Min(definitions[rootKey + "ProfilesBox"].size.y, canvasRect.height - 260));
            }
            LayoutProfilePrompts(rootKey + "Prompts", editing);
            if (deleting)
            {
                var result = new Rect[2];
                for (int i = 0; i < 2; i++)
                {
                    string key = ProfileRoot + "ConfirmDeletionPanel/" + (i == 0 ? "True" : "False");
                    SetText(key + "/Name/Property", labels[i]);
                    SetActive(key + "/Name/selected", i == selected);
                    ApplySettingsFocus(key, transforms[key], i == selected);
                    result[i] = RectAt(key);
                }
                return result;
            }
            string contentKey = editing ? rootKey + "ProfilesBox/Scroll View/Viewport/Content"
                : device ? ProfileRoot + "NewSelectDevice/Scroll View/Viewport/Content" : ProfileContent;
            var content = transforms[contentKey];
            var viewport = transforms[contentKey.Substring(0, contentKey.LastIndexOf('/'))];
            var rows = new List<RectTransform>();
            float height = 56, spacing = editing ? 0 : -3;
            if (editing)
            {
                SetText(contentKey + "/Header/Name/profileediting", NativeInputBindings.Draft?.name ?? "");
                rows.Add(transforms[contentKey + "/Header"]);
                foreach (var pair in transforms.Where(p => p.Key.StartsWith(contentKey + "/", StringComparison.Ordinal)
                    && p.Key.Substring(contentKey.Length + 1).IndexOf('/') < 0)) pair.Value.gameObject.SetActive(pair.Key.EndsWith("/Header", StringComparison.Ordinal));
                foreach (var control in controls)
                {
                    string key = contentKey + "/" + control.node;
                    SetActive(key, true);
                    SetText(key + "/Value/RebindText", NativeInputBindings.IsRebinding && rows.Count - 1 == selected ? "..." : control.value ?? "");
                    ApplySettingsFocus(key, transforms[key], rows.Count - 1 == selected);
                    rows.Add(transforms[key]);
                }
            }
            else
            {
                var template = transforms[ProfileContent + "/CreateNewProfile"];
                template.gameObject.SetActive(false);
                while (profileRows.Count < labels.Length)
                    profileRows.Add(UnityEngine.Object.Instantiate(template, content, false));
                for (int i = 0; i < profileRows.Count; i++)
                {
                    var row = profileRows[i]; row.gameObject.SetActive(i < labels.Length);
                    if (i >= labels.Length) continue;
                    row.SetParent(content, false);
                    row.Find("Name/Property").GetComponent<TMP_Text>().text = labels[i];
                    row.Find("Name/selected").gameObject.SetActive(i == selected);
                    ApplySettingsFocus(ProfileContent + "/CreateNewProfile", row, i == selected);
                    rows.Add(row);
                }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; row.anchorMin = row.anchorMax = row.pivot = new Vector2(0, 1);
                row.anchoredPosition = new Vector2(0, -i * (height + spacing));
                row.SetAsLastSibling();
            }
            // Original ContentSizeFitter sets the preferred row height independently of viewport height.
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0, 1);
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(0, rows.Count * (height + spacing) - spacing));
            Canvas.ForceUpdateCanvases();
            float maxScroll = Mathf.Max(0, content.rect.height - viewport.rect.height);
            if (allowPointer && Event.current.type == EventType.ScrollWheel && ProfileRect(viewport).Contains(Event.current.mousePosition))
            { profileScroll = Mathf.Clamp(profileScroll + Event.current.delta.y * 32, 0, maxScroll); Event.current.Use(); }
            int focusedRow = selected + (editing ? 1 : 0);
            if (profileFocus != selected && focusedRow < rows.Count)
            {
                float top = focusedRow * (height + spacing);
                if (top < profileScroll) profileScroll = top;
                if (top + height > profileScroll + viewport.rect.height) profileScroll = top + height - viewport.rect.height;
                profileFocus = selected;
            }
            profileScroll = Mathf.Clamp(profileScroll, 0, maxScroll);
            content.anchoredPosition = new Vector2(0, profileScroll);
            string handleKey = contentKey.Substring(0, contentKey.IndexOf("/Viewport", StringComparison.Ordinal)) + "/Scrollbar Vertical/Sliding Area/Handle";
            if (transforms.TryGetValue(handleKey, out var handle))
            {
                float fraction = Mathf.Clamp01(viewport.rect.height / Mathf.Max(viewport.rect.height, content.rect.height));
                float top = 1 - (maxScroll > 0 ? profileScroll / maxScroll : 0) * (1 - fraction);
                handle.anchorMin = new Vector2(0, top - fraction); handle.anchorMax = new Vector2(1, top); handle.offsetMin = handle.offsetMax = Vector2.zero;
            }
            Canvas.ForceUpdateCanvases();
            string scrollView = contentKey.Substring(0, contentKey.IndexOf("/Viewport", StringComparison.Ordinal));
            profileScroll = SettingsScrollbarPointer(scrollView, profileScroll, allowPointer);
            content.anchoredPosition = new Vector2(0, profileScroll);
            if (transforms.TryGetValue(handleKey, out handle))
            {
                float fraction = Mathf.Clamp01(viewport.rect.height / Mathf.Max(viewport.rect.height, content.rect.height));
                float top = 1 - (maxScroll > 0 ? profileScroll / maxScroll : 0) * (1 - fraction);
                handle.anchorMin = new Vector2(0, top - fraction); handle.anchorMax = new Vector2(1, top);
            }
            Canvas.ForceUpdateCanvases();
            Rect clip = ProfileRect(viewport);
            return rows.Skip(editing ? 1 : 0).Select(row =>
            {
                Rect rect = ProfileRect(row);
                return Rect.MinMaxRect(Mathf.Max(rect.xMin, clip.xMin), Mathf.Max(rect.yMin, clip.yMin), Mathf.Min(rect.xMax, clip.xMax), Mathf.Min(rect.yMax, clip.yMax));
            }).ToArray();
        }
        void LayoutProfilePrompts(string key, bool editing)
        {
            if (prefabAuthored) { SetActive(key + "/BText", true); return; }
            var target = transforms[key];
            var layout = target.GetComponent<HorizontalLayoutGroup>();
            if (!layout) layout = target.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 70; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true; layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            if (editing) target.anchoredPosition = new Vector2(0, 73);
            SetActive(key + "/BText", true);
        }
        static Rect ProfileRect(RectTransform target)
        {
            var corners = new Vector3[4]; target.GetWorldCorners(corners); float scale = Screen.height / 600f;
            return new Rect(corners[0].x / scale, (Screen.height - corners[1].y) / scale, (corners[2].x - corners[0].x) / scale, (corners[1].y - corners[0].y) / scale);
        }
    }
}
