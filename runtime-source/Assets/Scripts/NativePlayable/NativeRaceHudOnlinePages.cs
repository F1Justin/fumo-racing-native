using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        readonly Dictionary<string, NativeOnlinePageView> onlinePages = new Dictionary<string, NativeOnlinePageView>();
        NativeOnlinePageView onlinePageView, onlineRowView;
        public NativeOnlinePageView OnlinePageView => onlinePageView;
        public RectTransform OnlineRowRect => onlineRowView ? (RectTransform)onlineRowView.transform : null;
        public Action<string> OnlineHover;
        bool PointerButton(Rect rect)
        {
            bool clicked = authoredUi != null && authoredUi.PrefabAuthored
                ? authoredUi.ConsumePointer(rect) : GUI.Button(rect, GUIContent.none, hitArea);
            if (clicked) NativeInputBindings.RecordPromptDevice(UnityEngine.InputSystem.Mouse.current);
            return clicked;
        }
        bool usingOnlineAuthoring;
        void EnsureAuthoredMenus()
        {
            if (authoredUi != null || !authoredUiData) return;
            var prefab = NativeContent.Load<GameObject>("NativeOnline/OriginalMenus");
            if (!prefab) throw new InvalidOperationException("Missing OriginalMenus.prefab; run the Editor menu migration");
            authoredUi = new NativeAuthoredUi(authoredUiData, menuFont, uiImageMaterial, prefab);
        }
        public void UseOnlineAuthoring(bool active)
        {
            if (active == usingOnlineAuthoring) return;
            usingOnlineAuthoring = active;
            EnsureAuthoredMenus();
            authoredUi?.Hide();
        }
        public void DisposeOnlineAuthoring()
        {
            usingOnlineAuthoring = false;
            DisposeOnlinePages();
        }
        public float BeginOnlinePage(string page)
        {
            onlineRowView = null;
            foreach (var entry in onlinePages) if (entry.Key != page) entry.Value.gameObject.SetActive(false);
            if (!onlinePages.TryGetValue(page, out onlinePageView))
            {
                var prefab = NativeContent.Load<NativeOnlinePageView>("NativeOnline/Pages/" + page);
                if (!prefab) throw new InvalidOperationException("Missing authored Online page: " + page);
                // Entry is a complete composition already nested under the authored menus.
                // Reuse that exact instance so Prefab overrides affect both sides at runtime.
                onlinePageView = page == "Entry" && authoredUi != null
                    ? authoredUi.AuthoringRoot.GetComponentsInChildren<NativeOnlinePageView>(true).FirstOrDefault(view => view.name == page) : null;
                if (!onlinePageView) onlinePageView = UnityEngine.Object.Instantiate(prefab);
                onlinePageView.name = "Online " + page; onlinePages.Add(page, onlinePageView);
            }
            onlinePageView.Begin();
            LoadMenuTextures();
            return Screen.width * 600f / Screen.height;
        }
        NativeOnlineElement OnlineElement(string key)
        { var page = onlineRowView ? onlineRowView : onlinePageView; return page ? page.Use(key) : null; }
        public void BeginOnlineRow(string key, int index)
        { onlineRowView = onlinePageView.Row(key, index); }
        public void EndOnlineRow() { onlineRowView = null; }
        public void EndOnlinePage()
        {
            onlinePageView?.End();
        }
        public void HideOnlinePages()
        { foreach (var page in onlinePages.Values) if (page) page.gameObject.SetActive(false); }
        public void DisposeOnlinePages()
        { foreach (var page in onlinePages.Values) if (page) UnityEngine.Object.Destroy(page.gameObject); onlinePages.Clear(); }
        public Rect OnlineElementRect(string key)
        {
            var element = OnlineElement(key);
            return element && element.gameObject.activeInHierarchy ? NativeOnlineLobbyView.LogicalRect(element.Rect) : Rect.zero;
        }
        public NativeOnlineElement OnlineInput(string key)
            => OnlineElement("field-" + key);
    }
}
