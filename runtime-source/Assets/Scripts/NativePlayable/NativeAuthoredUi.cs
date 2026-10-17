using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi : IDisposable
    {
        readonly GameObject root;
        readonly Dictionary<string, RectTransform> transforms = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, NativeAuthoredUiNode> definitions = new Dictionary<string, NativeAuthoredUiNode>();
        readonly NativeUiFontData font;
        string visiblePage;
        string[] visiblePages;
        readonly List<RectTransform> pageRoots = new List<RectTransform>();
        NativeOnlinePageView[] onlinePageViews;
        bool pageRequested, preservePage;
        public int PageTreeTransitions { get; private set; }
        void CachePageRoots()
        {
            foreach (var pair in transforms)
                if (definitions[pair.Key].path.Count(c => c == '/') == 1) pageRoots.Add(pair.Value);
            onlinePageViews = root.GetComponentsInChildren<NativeOnlinePageView>(true);
        }
        // BeginFrame still runs for every IMGUI event. Visibility is reconciled after
        // that event's page request, so a stable page never goes through Hide/Show.
        public void BeginPageFrame(bool preserve)
        {
            pageRequested = false;
            preservePage = preserve;
        }
        public void EndPageFrame()
        {
            if (pageRequested || preservePage || (visiblePage == null && visiblePages == null)) return;
            // The caller may have already drawn race nodes (notably the shared
            // minimap in split-screen). Retire only the old requested pages;
            // full Hide here would erase those nodes until another GUI event.
            if (visiblePage != null) SetActive(visiblePage, false);
            if (visiblePages != null)
                foreach (var page in visiblePages) SetActive(page, false);
            ResetCountdownAnimation();
            SetResultsCanvasOrder(false);
            PageTreeTransitions++;
            visiblePage = null;
            visiblePages = null;
        }
        readonly Dictionary<string, TMP_Text> staticText = new Dictionary<string, TMP_Text>();
        readonly Dictionary<string, (string phrase, string fallback)> localizedBindings = new Dictionary<string, (string, string)>();
        readonly Dictionary<string,NativeUiFontData> textFonts=new Dictionary<string,NativeUiFontData>();
        // Prefabs store their authored text, so the first Show must localize it too.
        int textLanguage = -1;
        readonly bool prefabAuthored;
        public GameObject AuthoringRoot => root;
        public bool PrefabAuthored => prefabAuthored;
        public NativeOnlineMotionSettings Motion => prefabAuthored && root ? root.GetComponent<NativeOnlinePointer>().motion : null;
        public bool ConsumePointer(Rect rect) => root && root.GetComponent<NativeOnlinePointer>() && root.GetComponent<NativeOnlinePointer>().Consume(rect);
        public NativeAuthoredUi(NativeAuthoredUiData data, NativeUiFontData textFont, Material imageMaterial, GameObject prefab = null)
        {
            font = textFont;
            if (prefab)
            {
                prefabAuthored = true;
                root = UnityEngine.Object.Instantiate(prefab);
                root.name = "Prefab authored UI";
                foreach (var binding in root.GetComponentsInChildren<NativeOnlineAuthoredNode>(true))
                {
                    var node = binding.Read();
                    string key = node.scene + "/" + node.path;
                    transforms.Add(key, (RectTransform)binding.transform); definitions.Add(key, node);
                    if (binding.TryGetComponent<TMP_Text>(out var text) && binding.bindLocalizedText) staticText.Add(key, text);
                    NativeMenuTextLayout.Register(key, node);
                }
                BindInputPrompts(); CachePageRoots(); Hide(); return;
            }
            root = new GameObject("Original authored menus");
            foreach (var group in data.nodes.GroupBy(n => n.scene))
            {
                var canvasNode = group.Single(n => n.path == "Canvas");
                var sceneRoot = new GameObject(group.Key + " authored Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                sceneRoot.transform.SetParent(root.transform, false);
                var canvas = sceneRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = group.Key == "GlobalHUD" ? 100 : 99;
                canvas.pixelPerfect = canvasNode.canvasPixelPerfect;
                canvas.additionalShaderChannels = (AdditionalCanvasShaderChannels)canvasNode.canvasShaderChannels;
                var scaler = sceneRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = canvasNode.hasCanvasScaler ? (CanvasScaler.ScaleMode)canvasNode.uiScaleMode : CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.scaleFactor = canvasNode.scaleFactor;
                scaler.referencePixelsPerUnit = canvasNode.referencePixelsPerUnit;
                scaler.screenMatchMode = (CanvasScaler.ScreenMatchMode)canvasNode.screenMatchMode;
                scaler.referenceResolution = canvasNode.referenceResolution.sqrMagnitude > 0 ? canvasNode.referenceResolution : new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = canvasNode.referenceResolution.sqrMagnitude > 0 ? canvasNode.match : 1;
                if (group.Key == "ceremony/ceremony")
                {
                    // The recovered constant-pixel canvas makes titles tiny on Retina windows.
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1280, 720);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = 1;
                }
                var parents = new Dictionary<long, RectTransform>();
                foreach (var node in group.OrderBy(n => n.path.Count(c => c == '/')).ThenBy(n => n.order))
                {
                    if (node.path == "Canvas") { parents[node.id] = sceneRoot.GetComponent<RectTransform>(); continue; }
                    if (!parents.TryGetValue(node.parent, out var parent)) continue;
                    var go = new GameObject(node.path.Substring(node.path.LastIndexOf('/') + 1), typeof(RectTransform));
                    var target = go.GetComponent<RectTransform>(); target.SetParent(parent, false);
                    target.anchorMin = node.anchorMin; target.anchorMax = node.anchorMax; target.pivot = node.pivot;
                    target.anchoredPosition = node.position; target.sizeDelta = node.size;
                    target.localRotation = node.rotation; target.localScale = node.scale;
                    parents[node.id] = target;
                    string key = node.scene + "/" + node.path; transforms[key] = target; definitions[key] = node;
                    if (node.canvas)
                    {
                        var nested = go.AddComponent<Canvas>();
                        go.AddComponent<NativeAuthoredCanvasState>().Configure(nested, node,
                            canvas.sortingOrder + node.canvasSortingOrder - canvasNode.canvasSortingOrder);
                    }
                    if (node.image)
                    {
                        var image = go.AddComponent<Image>(); image.sprite = node.sprite; image.color = node.color;
                        image.enabled = node.imageEnabled; image.material = node.imageOverride ? node.imageOverride : imageMaterial; image.type = (Image.Type)node.imageType;
                        image.maskable = node.maskable; image.preserveAspect = node.preserveAspect; image.raycastTarget = false;
                        image.fillMethod = (Image.FillMethod)node.imageFillMethod;
                        image.fillOrigin = node.imageFillOrigin;
                        image.fillAmount = node.imageFillAmount;
                        image.fillClockwise = node.imageFillClockwise;
                        image.useSpriteMesh = node.imageUseSpriteMesh;
                        image.pixelsPerUnitMultiplier = node.imagePixelsPerUnitMultiplier;
                        ObserveOriginalImageInitialization(key, image);
                    }
                    if (node.rawImage)
                    {
                        var image = go.AddComponent<RawImage>(); image.texture = node.texture; image.color = node.color;
                        image.enabled = node.rawImageEnabled; image.material = node.imageOverride ? node.imageOverride : imageMaterial; image.maskable = node.maskable; image.raycastTarget = false;
                    }
                    if (node.mask) { var mask = go.AddComponent<Mask>(); mask.showMaskGraphic = node.showMask; mask.enabled = node.maskEnabled; }
                    if (node.rectMask)
                    {
                        var mask = go.AddComponent<RectMask2D>();
                        mask.enabled = node.rectMaskEnabled;
                        mask.padding = node.rectMaskPadding;
                        mask.softness = node.rectMaskSoftness;
                    }
                    if (node.grid)
                    {
                        var grid = go.AddComponent<GridLayoutGroup>(); grid.enabled = node.gridEnabled;
                        grid.cellSize = node.cellSize; grid.spacing = node.spacing;
                        grid.startCorner = (GridLayoutGroup.Corner)node.startCorner; grid.startAxis = (GridLayoutGroup.Axis)node.startAxis;
                        grid.childAlignment = (TextAnchor)node.childAlignment; grid.constraint = (GridLayoutGroup.Constraint)node.constraint;
                        grid.constraintCount = node.constraintCount;
                        grid.padding = new RectOffset(node.padding.m_Left, node.padding.m_Right, node.padding.m_Top, node.padding.m_Bottom);
                    }
                    if (node.horizontalLayout)
                    {
                        var layout = go.AddComponent<HorizontalLayoutGroup>();
                        layout.enabled = node.horizontalLayoutEnabled;
                        layout.spacing = node.layoutSpacing;
                        layout.childAlignment = (TextAnchor)node.childAlignment;
                        layout.childForceExpandWidth = node.childForceExpandWidth;
                        layout.childForceExpandHeight = node.childForceExpandHeight;
                        layout.childControlWidth = node.childControlWidth;
                        layout.childControlHeight = node.childControlHeight;
                        layout.childScaleWidth = node.childScaleWidth;
                        layout.childScaleHeight = node.childScaleHeight;
                        layout.reverseArrangement = node.reverseArrangement;
                        layout.padding = new RectOffset(node.padding.m_Left, node.padding.m_Right, node.padding.m_Top, node.padding.m_Bottom);
                    }
                    if (node.layoutElement) go.AddComponent<LayoutElement>().ignoreLayout = node.ignoreLayout;
                    if (node.hasText)
                    {
                        var text = go.AddComponent<TextMeshProUGUI>(); var originalFont = node.fontIndex >= 0 && data.fonts != null ? data.fonts[node.fontIndex] : font;
                        text.enabled = node.textEnabled; textFonts[key]=originalFont;NativeChineseFont.Apply(text,originalFont,node.textMaterialOverride);text.fontStyle = (FontStyles)node.fontStyle;
                        text.text = LocalizedText(node);
                        staticText[key] = text;
                        NativeMenuTextLayout.Register(key, node);
                        NativeMenuTextLayout.Apply(text, node, 1, key);
                        text.color = node.fontColor; text.enableVertexGradient = node.enableVertexGradient;
                        text.colorGradient = new VertexGradient { topLeft = node.gradientTopLeft, topRight = node.gradientTopRight, bottomLeft = node.gradientBottomLeft, bottomRight = node.gradientBottomRight };
                        text.alignment = (TextAlignmentOptions)node.alignment;
                        text.enableWordWrapping = node.wrap; text.enableKerning = node.enableKerning; text.raycastTarget = false;
                    }
                    if (group.Key == "GlobalHUD" && node.path.StartsWith("Canvas/Results/", StringComparison.Ordinal) && node.path.EndsWith("/banner", StringComparison.Ordinal))
                        go.AddComponent<CanvasGroup>().alpha = .5f;
                    if (node.animator)
                    {
                        if (!node.animatorController) throw new InvalidOperationException("Original UI Animator controller missing: " + node.path);
                        var animator = go.AddComponent<Animator>();
                        animator.runtimeAnimatorController = node.animatorController;
                        animator.updateMode = (AnimatorUpdateMode)node.animatorUpdateMode;
                        animator.cullingMode = (AnimatorCullingMode)node.animatorCullingMode;
                        animator.applyRootMotion = node.animatorApplyRootMotion;
                        // Preserve the original controller across per-draw Hide; actual page changes explicitly Rebind.
                        animator.keepAnimatorStateOnDisable = node.animatorKeepStateOnDisable || node.animatorPreserveStateAcrossFrameHide;
                        animator.enabled = node.animatorEnabled;
                    }
                    go.SetActive(node.active);
                    target.SetSiblingIndex(node.order);
                }
            }
            BindInputPrompts(); CachePageRoots(); Hide();
        }
        public Rect RectAt(string key)
        {
            if (!transforms.TryGetValue(key, out var target)) throw new InvalidOperationException("Authored UI node missing: " + key);
            Canvas.ForceUpdateCanvases();
            return ReadRect(target);
        }
        static Rect ReadRect(RectTransform target) => NativeOnlineLobbyView.LogicalRect(target);
        public NativeAuthoredUiNode Definition(string key) => definitions[key];
        public bool HasNode(string key) => transforms.ContainsKey(key);
        public Dictionary<string, RectTransform> MenuTransformGraph() => transforms
            .Where(pair => pair.Key.StartsWith("menu2/", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value);
        public bool ApplyMenuButton(string key, float focus, bool selected, bool disabled = false)
        {
            if (!transforms.TryGetValue(key, out var target)) return false;
            if (!target.TryGetComponent<NativeMenuButtonVisual>(out var visual))
                throw new InvalidOperationException("Menu button needs the one-time Editor migration: " + key);
            visual.Apply(definitions[key].size, focus, selected, disabled);
            return true;
        }
        public IEnumerable<NativeAuthoredUiNode> Nodes(string prefix) => definitions.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pair => pair.Value);
        public bool VisibleGraphic(string key)
        {
            return transforms.TryGetValue(key, out var node) && node.gameObject.activeInHierarchy
                && node.TryGetComponent<Graphic>(out var graphic) && graphic.enabled && graphic.color.a > 0;
        }
        public bool OriginalCharacterSelectionBound(string characterId, int selectableCount, out string detail)
        {
            const string prefix = "selection/Canvas/CharSelection/CharacterGrid/";
            var slots = definitions.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(pair.Value.characterId)).ToArray();
            int visibleCount = slots.Count(pair => transforms[pair.Key].gameObject.activeInHierarchy);
            bool complete = slots.Length == 24 && visibleCount == selectableCount;
            var selected = slots.Single(pair => pair.Value.characterId == characterId);
            var selector = transforms["selection/Canvas/CharSelection/P1Select"];
            float distance = Vector2.Distance(selector.position, transforms[selected.Key].position);
            bool aligned = distance < .01f;
            detail = "Original slot topology=" + slots.Length + "; selectable slots visible=" + visibleCount + "/" + selectableCount + "; character=" + characterId + "; selector distance=" + distance;
            return complete && aligned;
        }
        public bool OriginalPromptLayoutBound(string page, out string detail)
        {
            string key = "menu2/Canvas/" + page + "/Prompts";
            if (!transforms.TryGetValue(key, out var node)) { detail = "Missing authored prompts: " + page; return false; }
            var layout = node.GetComponent<HorizontalLayoutGroup>();
            bool visible = VisibleGraphic(key + "/AText") && VisibleGraphic(key + "/AText/icon")
                && VisibleGraphic(key + "/BText") && VisibleGraphic(key + "/BText/icon");
            bool dynamic = layout && layout.enabled && layout.childControlWidth && !layout.childForceExpandWidth;
            bool pause = page != "MusicRoom" || (VisibleGraphic(key + "/BText (1)")
                && transforms[key + "/BText (1)"].GetComponent<TMP_Text>().text.StartsWith("<color=red>Space</color> ", StringComparison.Ordinal)
                && !transforms[key + "/BText (1)/icon"].gameObject.activeSelf);
            detail = page + " prompts visible=" + visible + "; dynamic preferred width=" + dynamic + "; pause binding=" + pause;
            return visible && dynamic && pause;
        }
        public bool OriginalMusicDiscBound(Sprite cover, out string detail)
        {
            const string key = "menu2/Canvas/MusicRoom/Disc";
            var maskNode = transforms[key + "/labelmask"];
            var maskImage = maskNode.GetComponent<Image>();
            var mask = maskNode.GetComponent<Mask>();
            var label = transforms[key + "/labelmask/label"].GetComponent<Image>();
            bool visible = VisibleGraphic(key) && VisibleGraphic(key + "/labelmask") && VisibleGraphic(key + "/labelmask/label");
            bool masked = mask && mask.enabled && maskImage.sprite && label.maskable;
            bool originalMask = maskImage.sprite == Definition(key + "/labelmask").sprite
                && maskImage.overrideSprite == maskImage.sprite;
            bool selected = label.sprite == cover && label.overrideSprite == cover;
            detail = "Disc visible=" + visible + "; center mask=" + masked + "; source mask reference=" + originalMask + "; source cover reference=" + selected;
            return visible && masked && originalMask && selected;
        }
        public void PlaceAt(string key, string targetKey) { transforms[key].position = transforms[targetKey].position; }
        public void SetAsLastSibling(string key) { if (transforms.TryGetValue(key, out var node)) node.SetAsLastSibling(); }
        public void RawTexture(string key, Texture texture)
        {
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<RawImage>(out var image))
            { image.texture = texture; node.gameObject.SetActive(true); }
        }
        public void Show(string scene, string page, params string[] hide)
        {
            pageRequested = true;
            RefreshStaticText();
            string prefix = scene + "/Canvas/" + page;
            if (visiblePage != prefix)
            {
                characterNavigationPageGeneration++;
                PageTreeTransitions++;
                visiblePages = null;
                foreach (var pair in transforms)
                {
                    if (definitions[pair.Key].path.Count(c => c == '/') == 1) SetActive(pair.Key, pair.Key == prefix);
                    else if (pair.Key.StartsWith(prefix + "/", StringComparison.Ordinal)) {
                        SetActive(pair.Key, definitions[pair.Key].active);
                        pair.Value.anchoredPosition = definitions[pair.Key].position;
                        pair.Value.sizeDelta = definitions[pair.Key].size;
                    }
                }
                SetResultsCanvasOrder(prefix == "GlobalHUD/Canvas/Results");
                visiblePage = prefix;
            }
            foreach (string name in hide)
                foreach (var pair in transforms)
                    if (pair.Key.StartsWith(prefix + "/", StringComparison.Ordinal) && pair.Key.Contains(name)) pair.Value.gameObject.SetActive(false);
        }
        public void ShowPages(params string[] pages) => ShowPages(false, pages);
        public void ShowPages(bool restoreLayout, params string[] pages)
        {
            pageRequested = true;
            RefreshStaticText();
            if (visiblePages != null && visiblePages.SequenceEqual(pages))
            {
                // Menu transitions sample offsets relative to the authored layout.
                // Keep that explicit reset without hiding/reactivating the pages.
                if (restoreLayout)
                    foreach (var pair in transforms)
                        if (pages.Any(prefix => pair.Key == prefix || pair.Key.StartsWith(prefix + "/", StringComparison.Ordinal)))
                        {
                            if (pair.Value.anchoredPosition != definitions[pair.Key].position) pair.Value.anchoredPosition = definitions[pair.Key].position;
                            if (pair.Value.sizeDelta != definitions[pair.Key].size) pair.Value.sizeDelta = definitions[pair.Key].size;
                        }
                return;
            }
            PageTreeTransitions++;
            SetResultsCanvasOrder(pages.Contains("GlobalHUD/Canvas/Results"));
            foreach (var pair in transforms)
            {
                bool belongs = pages.Any(prefix => pair.Key == prefix || pair.Key.StartsWith(prefix + "/", StringComparison.Ordinal));
                SetActive(pair.Key, definitions[pair.Key].path.Count(c => c == '/') == 1 ? pages.Contains(pair.Key) : belongs && definitions[pair.Key].active);
                pair.Value.anchoredPosition = definitions[pair.Key].position;
                pair.Value.sizeDelta = definitions[pair.Key].size;
            }
            visiblePage = null;
            visiblePages = (string[])pages.Clone();
        }
        public void Offset(string key, Vector2 offset)
        {
            var node = transforms[key];
            var position = definitions[key].position + new Vector2(offset.x, -offset.y) * 1.8f;
            if (node.anchoredPosition != position) node.anchoredPosition = position;
        }
        public void Size(string key, Vector2 size) { var node = transforms[key]; if (node.sizeDelta != size) node.sizeDelta = size; }
        public void Rotation(string key, float angle) { var node = transforms[key]; var rotation = Quaternion.Euler(0, 0, angle); if (node.localRotation != rotation) node.localRotation = rotation; }
        public void AssignOriginalSprite(string key, Sprite source)
        {
            if (!source) throw new InvalidOperationException("Original Sprite missing: " + key);
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<Image>(out var image))
            {
                image.overrideSprite = null;
                image.sprite = source;
            }
        }
        public void Hide()
        {
            ResetCountdownAnimation();
            SetResultsCanvasOrder(false);
            foreach (var page in pageRoots)
                if (page && page.gameObject.activeSelf) page.gameObject.SetActive(false);
            foreach (var page in onlinePageViews)
                if (page && page.gameObject.activeSelf) page.gameObject.SetActive(false);
            if (visiblePage != null || visiblePages != null) PageTreeTransitions++;
            visiblePage = null;
            visiblePages = null;
        }
        static string LocalizedText(NativeAuthoredUiNode node) => string.IsNullOrEmpty(node.phrase)
            ? NativeLocalization.AuthoredText(node.text) : NativeLocalization.Get(node.phrase, node.text);
        void RefreshStaticText()
        {
            if (textLanguage == NativeLocalization.Language) return;
            textLanguage = NativeLocalization.Language;
            foreach(var pair in textFonts)if(!prefabAuthored && transforms.TryGetValue(pair.Key,out var target) && target.TryGetComponent<TMP_Text>(out var text))NativeChineseFont.Apply(text,pair.Value,definitions[pair.Key].textMaterialOverride);
            foreach (var pair in textFonts)
                if (!prefabAuthored && transforms.TryGetValue(pair.Key, out var layoutNode) && layoutNode.TryGetComponent<TMP_Text>(out var label))
                    NativeMenuTextLayout.Apply(label, definitions[pair.Key], 1, pair.Key);
            foreach (var pair in staticText) pair.Value.text = LocalizedText(definitions[pair.Key]);
            foreach (var pair in localizedBindings) WriteLocalizedText(pair.Key, pair.Value.phrase, pair.Value.fallback);
        }
        bool BindTextAllowed(string key) => !prefabAuthored || !transforms.TryGetValue(key, out var node)
            || !node.TryGetComponent<NativeOnlineAuthoredNode>(out var binding) || binding.bindLocalizedText;
        void WriteLocalizedText(string key, string phrase, string fallback)
        {
            if (!BindTextAllowed(key)) return;
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<TMP_Text>(out var text))
                text.text = NativeLocalization.Get(phrase, fallback);
        }
        public void BindLocalizedText(string key, string phrase, string fallback)
        {
            staticText.Remove(key);
            localizedBindings[key] = (phrase, fallback);
            WriteLocalizedText(key, phrase, fallback);
        }
        public void SetTextMaximumSize(string key, float maximum)
        {
            if (prefabAuthored) return;
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<TMP_Text>(out var text))
            {
                text.enableAutoSizing = true;
                text.fontSizeMax = maximum;
                text.fontSize = maximum;
            }
        }
        public void ResetTextLayout(string key)
        {
            if (prefabAuthored) return;
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<TMP_Text>(out var text))
                NativeMenuTextLayout.Apply(text, definitions[key], 1, key);
        }
        public void SetText(string key, string value)
        {
            if (!BindTextAllowed(key)) return;
            staticText.Remove(key);
            localizedBindings.Remove(key);
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<TMP_Text>(out var text))
            {
                var translated = NativeLocalization.TranslateText(value);
                if (text.text != translated) text.text = translated;
            }
        }
        public void TextColor(string key, Color value) { if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<TMP_Text>(out var text)) if (text.color != value) text.color = value; }
        public void SetSprite(string key, Sprite value)
        {
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<Image>(out var image)) if (image.sprite != value) image.sprite = value;
        }
        public void SetActive(string key, bool active)
        {
            if (!transforms.TryGetValue(key, out var node) || !node) return;
            var binding = prefabAuthored ? node.GetComponent<NativeOnlineAuthoredNode>() : null;
            bool visible = active && (!binding || binding.allowRuntimeVisibility);
            if (node.gameObject.activeSelf != visible) node.gameObject.SetActive(visible);
        }
        void SetResultsCanvasOrder(bool active)
        {
            if (transforms.TryGetValue("GlobalHUD/Canvas/Results", out var node))
                node.GetComponentInParent<Canvas>(true).sortingOrder = active ? 200 : 100;
        }
        public void ResultBackdrop()
        {
            SetResultsCanvasOrder(true);
            const string key = "GlobalHUD/Canvas/Results/Fade";
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<Image>(out var image))
            { image.enabled = true; image.color = definitions[key].color; }
        }
        static readonly Gradient resultRankGradient = CreateResultRankGradient();
        public static Color OriginalRankColor(int rank, int racerCount) => resultRankGradient.Evaluate((Mathf.Max(1, rank) - 1f) / Mathf.Max(1, racerCount));
        static Gradient CreateResultRankGradient()
        {
            var rankGradient = new Gradient();
            rankGradient.SetKeys(new[] {
                new GradientColorKey(new Color(1, .90831417f, .4669811f), 0),
                new GradientColorKey(new Color(1, .82791096f, .6367924f), 8192f / 65535),
                new GradientColorKey(new Color(1, .66098154f, .514151f), 16384f / 65535),
                new GradientColorKey(new Color(1, .47172558f, .3820755f), 24576f / 65535),
                new GradientColorKey(new Color(1, .28773582f, .322585f), 32768f / 65535),
                new GradientColorKey(new Color(.9716981f, .30709326f, .50361633f), 42020f / 65535),
                new GradientColorKey(new Color(.5849056f, .16278034f, .49609706f), 53199f / 65535),
                new GradientColorKey(new Color(.20484492f, 0, .754717f), 1)
            }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 8192f / 65535), new GradientAlphaKey(1, 1) });
            return rankGradient;
        }
        public void ResultPlayer(string name, int place, string time, int racerCount)
        {
            const string key = "GlobalHUD/Canvas/Results/playersresult/ResultPlayerUI";
            var node = transforms[key];
            node.anchorMin = new Vector2(0, 1); node.anchorMax = new Vector2(1, 1);
            node.pivot = new Vector2(.5f, 1);
            node.anchoredPosition = Vector2.zero;
            node.sizeDelta = new Vector2(0, definitions[key].size.y);
            node.gameObject.SetActive(true);
            for (int i = 1; i < 4; i++) SetActive(key + " (" + i + ")", false);
            SetActive(key + "/readyfade", false);
            SetActive(key + "/fumo", false);
            SetText(key + "/character", name); SetText(key + "/rank", Mathf.Max(1, place).ToString());
            SetText(key + "/Time", time);
            TextColor(key + "/rank", resultRankGradient.Evaluate((Mathf.Max(1, place) - 1f) / Mathf.Max(1, racerCount)));
            transforms[key + "/banner"].GetComponent<Image>().color = Color.red;
        }
        readonly List<RectTransform> resultRows = new List<RectTransform>();
        public void Scoreboard(string[] names, string[] times, int[] ranks, int[] scores, Sprite[] icons, bool[] humans, int[] scoreDiffs, int racerCount, float elapsed)
        {
            const string key = "GlobalHUD/Canvas/Results/scoreboard/Scroll View/Viewport/Content/ScoreboardEntry";
            var template = transforms[key]; template.gameObject.SetActive(false);
            while (resultRows.Count < names.Length)
            {
                var row = UnityEngine.Object.Instantiate(template, template.parent, false);
                row.name = "Native result entry " + resultRows.Count; resultRows.Add(row);
            }
            var content = (RectTransform)template.parent;
            content.sizeDelta = new Vector2(content.sizeDelta.x, 64.71f * names.Length);
            var viewport = transforms["GlobalHUD/Canvas/Results/scoreboard/Scroll View/Viewport"];
            float scroll = elapsed < 2 ? 1 : Mathf.Cos(2f / (names.Length + 1) * (elapsed - 2) * Mathf.PI) * .5f + .5f;
            float scrollOffset = Mathf.Max(0, 64.71f * names.Length - viewport.rect.height) * (1 - scroll);
            for (int i = 0; i < resultRows.Count; i++)
            {
                var row = resultRows[i]; row.gameObject.SetActive(i < names.Length);
                if (i >= names.Length) continue;
                row.anchoredPosition = definitions[key].position - Vector2.up * (64.71f * i - scrollOffset);
                row.Find("name").GetComponent<TMP_Text>().text = names[i];
                row.Find("time").GetComponent<TMP_Text>().text = times[i];
                row.Find("rank").GetComponent<TMP_Text>().text = ranks[i] > 0 ? ranks[i].ToString() : "";
                row.Find("score").gameObject.SetActive(scores != null);
                row.Find("scoreDiff").gameObject.SetActive(true);
                row.Find("scoreDiff").GetComponent<TMP_Text>().text = scoreDiffs[i] > 0 ? "+" + scoreDiffs[i] : "";
                row.Find("rank").GetComponent<TMP_Text>().color = resultRankGradient.Evaluate((Mathf.Max(1, ranks[i]) - 1f) / Mathf.Max(1, racerCount));
                var banner = row.Find("banner").GetComponent<Image>();
                banner.color = humans[i] ? new Color(1, 0, 0, .5f) : new Color(1, 1, 1, .5f);
                if (scores != null) row.Find("score").GetComponent<TMP_Text>().text = scores[i].ToString();
                row.Find("icon").GetComponent<Image>().sprite = icons == null ? null : icons[i];
                row.Find("icon").gameObject.SetActive(icons != null && icons[i]);
                row.Find("name").GetComponent<TMP_Text>().color = Color.white;
            }
        }
        public void Dispose() { if (root) UnityEngine.Object.Destroy(root); }
    }
}
