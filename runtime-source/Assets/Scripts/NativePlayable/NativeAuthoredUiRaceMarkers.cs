using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        readonly List<RectTransform> minimapPlayers = new List<RectTransform>();
        public void OriginalMinimap(Sprite sprite, Vector2[] positions, Color[] interiors, Color[] outlines = null)
        {
            const string key = "GlobalHUD/Canvas/minimap";
            var content = transforms[key];
            content.gameObject.SetActive(true);
            content.GetComponent<Image>().sprite = sprite;
            var template = transforms[key + "/outline"];
            template.gameObject.SetActive(false);
            while (minimapPlayers.Count < positions.Length)
            {
                var point = UnityEngine.Object.Instantiate(template, content, false);
                point.name = "Native minimap racer " + minimapPlayers.Count;
                minimapPlayers.Add(point);
            }
            for (int i = 0; i < minimapPlayers.Count; i++)
            {
                var point = minimapPlayers[i];
                point.gameObject.SetActive(i < positions.Length);
                if (i >= positions.Length) continue;
                point.localPosition = Vector3.Scale(new Vector3(positions[i].x, positions[i].y, 0), content.sizeDelta);
                point.GetComponent<Image>().color = outlines != null ? outlines[i] : i == 0 ? Color.red : Color.white;
                Color interior = interiors[i]; interior.a = 1;
                var interiorNode = point.Find("p");
                interiorNode.gameObject.SetActive(true);
                interiorNode.GetComponent<Image>().color = interior;
            }
        }
        readonly UnityEngine.Object[] frontIndicatorTargets = new UnityEngine.Object[2];
        readonly Vector2[] frontIndicatorPositions = new Vector2[2];
        readonly int[] frontIndicatorFrames = { -1, -1 };
        public void OriginalFrontIndicator(int index, bool visible, Vector2 viewport, float scale, string caption, Color color, UnityEngine.Object target)
        {
            const string root = "HUD/Canvas/FrontIndicators";
            var parent = transforms[root];
            parent.gameObject.SetActive(true);
            string key = root + "/FrontIndicator (" + (index + 2) + ")";
            var indicator = transforms[key];
            indicator.gameObject.SetActive(visible);
            if (!visible) { frontIndicatorTargets[index] = null; return; }
            SetRaceIndicatorCaption(key, caption);
            TextColor(key, color);
            transforms[key + "/Image"].gameObject.SetActive(true);
            transforms[key + "/Image"].GetComponent<Image>().color = color;
            indicator.localScale = Vector3.one * scale;
            bool updatePosition=frontIndicatorFrames[index]!=Time.frameCount || frontIndicatorTargets[index]!=target;
            frontIndicatorFrames[index] = Time.frameCount;
            var canvas = parent.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
            Vector2 desired = Vector2.Scale(viewport, canvas.rect.size);
            if(updatePosition)frontIndicatorPositions[index]=frontIndicatorTargets[index]!=target?desired
                : Vector2.Lerp(frontIndicatorPositions[index],desired,Time.deltaTime*45);
            indicator.anchoredPosition=frontIndicatorPositions[index];
            frontIndicatorTargets[index] = target;
        }
        void SetRaceIndicatorCaption(string key, string caption)
        {
            if (!BindTextAllowed(key)) return;
            staticText.Remove(key);
            localizedBindings.Remove(key);
            if (transforms.TryGetValue(key, out var node) && node.TryGetComponent<TMPro.TMP_Text>(out var text))
            {
                // Player names are literal data, including names that match localization keys or TMP tags.
                text.richText = false;
                text.text = caption;
            }
        }

        public bool OriginalFrontIndicatorsBound(out string detail)
        {
            const string root = "HUD/Canvas/FrontIndicators/FrontIndicator (";
            int count = 0;
            for (int i = 0; i < 2; i++)
            {
                string key = root + (i + 2) + ")";
                if (VisibleGraphic(key) && VisibleGraphic(key + "/Image")) count++;
            }
            detail = "Original front-rank CPU indicators visible=" + count;
            return count > 0;
        }
        public bool OriginalMinimapBound(Sprite sprite, int count, out string detail)
        {
            const string key = "GlobalHUD/Canvas/minimap";
            var content = transforms[key];
            bool bound = VisibleGraphic(key) && content.GetComponent<Image>().sprite == sprite
                && minimapPlayers.FindAll(point => point.gameObject.activeInHierarchy).Count == count
                && minimapPlayers.TrueForAll(point => point.sizeDelta == new Vector2(13, 13) && ((RectTransform)point.Find("p")).sizeDelta == new Vector2(10, 10));
            detail = "Original minimap sprite=" + (sprite ? sprite.name : "missing") + "; authored points=" + count + "; bound=" + bound;
            return bound;
        }
    }
}
