using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeUiCanvas
    {
        readonly GameObject root;
        readonly RectTransform transform;
        readonly Material imageMaterial;
        readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
        readonly List<RawImage> pictures = new List<RawImage>();
        readonly List<Image> sprites = new List<Image>();
        readonly List<NativeUiDisc> discs = new List<NativeUiDisc>();
        readonly List<RectMask2D> marqueeMasks=new List<RectMask2D>();
        int marqueeCount;
        public RectTransform RootTransform=>transform;
        int labelCount, pictureCount, spriteCount, discCount;
        bool drawing;

        public NativeUiCanvas(Material material, int sortingOrder = 100, string name = "Native original UI Canvas")
        {
            imageMaterial = material;
            root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            transform = root.GetComponent<RectTransform>();
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800, 600);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1;
            scaler.referencePixelsPerUnit = 100;
        }

        public void SetVisible(bool visible) { if (root) root.SetActive(visible); }

        public void Begin()
        {
            drawing = Event.current.type == EventType.Repaint;
            if (!drawing) return;
            SetVisible(true);
            labelCount = pictureCount = spriteCount = discCount = marqueeCount = 0;
            sourceGraphics.Clear(); duplicateSources.Clear();
        }

        public void End()
        {
            if (!drawing) return;
            HideUnused(labels, labelCount);
            HideUnused(pictures, pictureCount);
            HideUnused(sprites, spriteCount);
            HideUnused(discs, discCount);
            HideUnused(marqueeMasks,marqueeCount);
            SourceRenderFixtureHook?.Invoke(this);
            drawing = false;
        }

        public void Label(Rect rect, string value, float size, Color color, TextAnchor alignment, NativeUiFontData data,
            TextAlignmentOptions? exactAlignment = null, bool richText = false, bool autoSize = false, float minimum = 10, float maximum = 72, bool enableKerning = true, string sourcePath = null, TextOverflowModes overflow = TextOverflowModes.Overflow, bool localize = true)
        {
            if (!drawing || data == null) return;
            TextMeshProUGUI label = Item(labels, labelCount++, "Text", sourcePath);
            if(label.transform.parent!=transform)label.transform.SetParent(transform,false);
            Place(label.rectTransform, rect);
            NativeChineseFont.Apply(label,data);
            label.fontSize = size;
            label.enableAutoSizing = autoSize || NativeLocalization.IsChinese;
            label.fontSizeMin = NativeLocalization.IsChinese ? Mathf.Min(minimum,size*.6f) : minimum;
            label.fontSizeMax = NativeLocalization.IsChinese ? size : maximum;
            NativeMenuTextLayout.ApplyMainLabel(label, sourcePath);
            label.enableWordWrapping = false;
            label.enableKerning = enableKerning;
            label.overflowMode = overflow;
            label.richText = richText;
            label.raycastTarget = false;
            label.margin = Vector4.zero;
            label.alignment = exactAlignment ?? Alignment(alignment);
            label.color = color;
            label.text = localize?NativeLocalization.TranslateText(value):value;
        }

        public void MarqueeLabel(Rect rect,string value,float size,Color color,NativeUiFontData data)
        {
            if(!drawing)return;
            Label(rect,value,size,color,TextAnchor.MiddleLeft,data,localize:false);
            var label=labels[labelCount-1];label.enableAutoSizing=false;
            float preferred=Mathf.Max(rect.width,label.GetPreferredValues(value).x+2);
            float travel=Mathf.Max(0,preferred-rect.width),offset=0;
            if(travel>0)
            {
                const float speed=35,pause=1.2f;
                float duration=travel/speed,cycle=Time.unscaledTime%(duration*2+pause*2);
                offset=cycle<pause?0:cycle<pause+duration?(cycle-pause)*speed:cycle<pause*2+duration?travel:travel-(cycle-pause*2-duration)*speed;
            }
            int index=marqueeCount++;
            if(index==marqueeMasks.Count)
            {
                var node=new GameObject("Text viewport",typeof(RectTransform),typeof(RectMask2D));node.transform.SetParent(transform,false);
                marqueeMasks.Add(node.GetComponent<RectMask2D>());
            }
            var mask=marqueeMasks[index];mask.gameObject.SetActive(true);mask.transform.SetAsLastSibling();
            Place((RectTransform)mask.transform,rect);label.transform.SetParent(mask.transform,false);
            Place(label.rectTransform,new Rect(-offset,0,preferred,rect.height));
        }

        public void Picture(Rect rect, Texture texture, Color color, Rect? uv = null, float angle = 0, string sourcePath = null)
        {
            if (!drawing || !texture) return;
            RawImage picture = Item(pictures, pictureCount++, "Texture", sourcePath);
            Place(picture.rectTransform, rect);
            picture.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
            picture.texture = texture;
            picture.uvRect = uv ?? new Rect(0, 0, 1, 1);
            picture.color = color;
        }

        public void RotatedPicture(Rect rect, Texture texture, float angle, string sourcePath = null)
        {
            if (!drawing || !texture) return;
            Picture(rect, texture, Color.white, null, angle, sourcePath);
            var target = pictures[pictureCount - 1].rectTransform;
            target.pivot = new Vector2(.5f, .5f);
            target.anchoredPosition = new Vector2(rect.center.x, -rect.center.y);
        }

        public void Sprite(Rect rect, Sprite sprite, bool sliced, string sourcePath = null)
        {
            if (!drawing || !sprite) return;
            Image image = Item(sprites, spriteCount++, "Sprite", sourcePath);
            Place(image.rectTransform, rect);
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.fillCenter = true;
            image.color = Color.white;
        }

        public void Line(Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < 0.0001f) return;
            Picture(new Rect(a.x, a.y - thickness / 2, delta.magnitude + 1, thickness), Texture2D.whiteTexture, color,
                null, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            if (drawing)
            {
                RectTransform line = pictures[pictureCount - 1].rectTransform;
                line.pivot = new Vector2(0, 0.5f);
                line.anchoredPosition = new Vector2(a.x, -a.y);
            }
        }

        public void Dot(Vector2 point, float size, Color color)
        {
            if (!drawing) return;
            NativeUiDisc disc = Item(discs, discCount++, "Map marker");
            Place(disc.rectTransform, new Rect(point.x - size / 2, point.y - size / 2, size, size));
            disc.color = color;
        }

        T Item<T>(List<T> items, int index, string name, string sourcePath = null) where T : Graphic
        {
            if (index == items.Count)
            {
                var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
                node.transform.SetParent(transform, false);
                node.layer = root.layer;
                T graphic = node.AddComponent<T>();
                graphic.raycastTarget = false;
                if (!(graphic is TMP_Text)) graphic.material = imageMaterial;
                items.Add(graphic);
            }
            T item = items[index];
            item.gameObject.SetActive(true);
            item.transform.SetAsLastSibling();
            BindSource(item, sourcePath, name, index);
            return item;
        }

        static void Place(RectTransform target, Rect rect)
        {
            target.anchorMin = target.anchorMax = new Vector2(0, 1);
            target.pivot = new Vector2(0, 1);
            target.anchoredPosition = new Vector2(rect.x, -rect.y);
            target.sizeDelta = rect.size;
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
        }

        static void HideUnused<T>(List<T> items, int start) where T : Component
        {
            for (int i = start; i < items.Count; i++) items[i].gameObject.SetActive(false);
        }

        static TextAlignmentOptions Alignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Left;
            }
        }
    }
}
