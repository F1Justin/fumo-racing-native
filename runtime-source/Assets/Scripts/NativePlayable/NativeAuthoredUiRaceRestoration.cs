using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        readonly UnityEngine.Object[] rearTargets = new UnityEngine.Object[2];
        readonly UnityEngine.Object[] radarTargets = new UnityEngine.Object[2];
        readonly Vector3[] rearPositions = new Vector3[2], radarPositions = new Vector3[2];
        readonly int[] rearFrames = { -1, -1 }, radarFrames = { -1, -1 };
        int previousRaceRank, rankFrame = -1;
        float rankStarted;
        bool rankInitialized, rankFrozen;
        static readonly AnimationCurve rankScaleX = new AnimationCurve(
            new Keyframe(0, 1.3812f, 0, 0),
            new Keyframe(.083333336f, 1.1906f, -2.2871995f, -2.2871997f),
            new Keyframe(.16666667f, 1, 8.2896565E-07f, 0));
        static readonly AnimationCurve rankScaleY = new AnimationCurve(
            new Keyframe(0, 1.3812f, 0, 0),
            new Keyframe(.083333336f, 1.3547838f, -.6339883f, -.6339883f),
            new Keyframe(.16666667f, 1, -1.7057495E-07f, 0));
        static readonly AnimationCurve rankRotation = new AnimationCurve(
            new Keyframe(0, -10.559f, 0, 0),
            new Keyframe(.16666667f, 0, -1.1328578E-05f, 0));

        public static Vector3 OriginalRankScaleAt(float seconds) => new Vector3(
            rankScaleX.Evaluate(seconds), rankScaleY.Evaluate(seconds), rankScaleX.Evaluate(seconds));
        public static float OriginalRankRotationAt(float seconds) => rankRotation.Evaluate(seconds);

        public void OriginalRaceRank(int rank, int racerCount, bool finished)
        {
            const string key = "HUD/Canvas/rankbg/Rank";
            RestoreOriginalRaceRankVisibility();
            var node = transforms[key];
            if (!rankInitialized)
            {
                previousRaceRank = 0;
                rankStarted = Time.time;
                rankInitialized = true;
            }
            if (finished) rankFrozen = true;
            if (rankFrame == Time.frameCount) return;
            rankFrame = Time.frameCount;
            if (!rankFrozen)
            {
                if (rank - 1 != previousRaceRank) rankStarted = Time.time;
                previousRaceRank = rank - 1;
                SetText(key, rank.ToString());
                TextColor(key, OriginalRankColor(rank, racerCount));
            }
            float elapsed = Mathf.Min(Time.time - rankStarted, .16666667f);
            var scale = Motion ? new Vector3(Motion.rankScaleX.Evaluate(elapsed), Motion.rankScaleY.Evaluate(elapsed), Motion.rankScaleX.Evaluate(elapsed)) : OriginalRankScaleAt(elapsed);
            float angle = Motion ? Motion.rankRotation.Evaluate(elapsed) : OriginalRankRotationAt(elapsed);
            node.localScale = prefabAuthored ? Vector3.Scale(definitions[key].scale, scale) : scale;
            node.localRotation = (prefabAuthored ? definitions[key].rotation : Quaternion.identity) * Quaternion.Euler(0, 0, angle);
        }

        public void RestoreOriginalRaceRankVisibility()
        {
            SetActive("HUD/Canvas/rankbg", true);
            SetActive("HUD/Canvas/rankbg/Rank", true);
        }

        public void ResetOriginalRaceRank()
        {
            rankInitialized = rankFrozen = false;
            rankFrame = -1;
            for (int i = 0; i < 2; i++)
            {
                rearTargets[i] = radarTargets[i] = null;
                rearFrames[i] = radarFrames[i] = -1;
                rearPositions[i] = radarPositions[i] = Vector3.zero;
            }
        }

        public void OriginalRearIndicator(int index, bool visible, float x, float scale,
            string caption, Color color, UnityEngine.Object target)
        {
            string key = "HUD/Canvas/Indicators/RearIndicator" + (index == 0 ? "" : " (1)");
            SetActive("HUD/Canvas/Indicators", true);
            var node = transforms[key];
            SetRaceIndicatorCaption(key, caption);
            TextColor(key, color);
            SetActive(key + "/Image", true);
            transforms[key + "/Image"].GetComponent<Image>().color = color;
            DrawHorizontalIndicator(node, index, visible, x, scale, target, rearTargets, rearFrames, rearPositions);
        }

        public void OriginalItemIndicator(int index, bool visible, float x, float scale,
            Sprite icon, UnityEngine.Object target)
        {
            string key = "HUD/Canvas/ItemIndicators/ItemRearIndicator (" + (index + 2) + ")";
            SetActive("HUD/Canvas/ItemIndicators", true);
            var node = transforms[key];
            SetActive(key + "/Image", true);
            var image = node.GetComponent<Image>();
            image.sprite = icon;
            image.color = Color.white;
            image.preserveAspect = true;
            DrawHorizontalIndicator(node, index, visible, x, scale, target, radarTargets, radarFrames, radarPositions);
        }

        [System.Serializable] public sealed class RaceHudNodeSnapshot
        {
            public string path, text, sprite, sourceSpriteGuid;
            public bool active;
            public float alpha, effectiveAlpha, fontSize;
            public Vector2 size, anchoredPosition;
            public Vector3 localPosition, localScale, localEulerAngles;
            public Color color;
            public Vector3[] worldCorners;
        }

        public RaceHudNodeSnapshot[] ReadRestoredRaceHud()
        {
            string[] keys = { "HUD/Canvas/rankbg/Rank", "HUD/Canvas/Indicators/RearIndicator",
                "HUD/Canvas/Indicators/RearIndicator (1)", "HUD/Canvas/ItemIndicators/ItemRearIndicator (2)",
                "HUD/Canvas/ItemIndicators/ItemRearIndicator (3)",
                "HUD/Canvas/Indicators/RearIndicator/Image", "HUD/Canvas/Indicators/RearIndicator (1)/Image",
                "HUD/Canvas/ItemIndicators/ItemRearIndicator (2)/Image", "HUD/Canvas/ItemIndicators/ItemRearIndicator (3)/Image" };
            var result = new RaceHudNodeSnapshot[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var node = transforms[keys[i]];
                var group = node.GetComponent<CanvasGroup>();
                var text = node.GetComponent<TMP_Text>();
                var image = node.GetComponent<Image>();
                var corners = new Vector3[4]; node.GetWorldCorners(corners);
                float effectiveAlpha = 1;
                foreach (var ancestorGroup in node.GetComponentsInParent<CanvasGroup>(true)) effectiveAlpha *= ancestorGroup.alpha;
                result[i] = new RaceHudNodeSnapshot { path = keys[i], active = node.gameObject.activeInHierarchy,
                    alpha = group ? group.alpha : 1, effectiveAlpha = effectiveAlpha, sourceSpriteGuid = definitions[keys[i]].spriteGuid, size = node.rect.size, anchoredPosition = node.anchoredPosition,
                    localPosition = node.localPosition, localScale = node.localScale, localEulerAngles = node.localEulerAngles,
                    text = text ? text.text : "", fontSize = text ? text.fontSize : 0,
                    sprite = image && image.sprite ? image.sprite.name : "", color = text ? text.color : image ? image.color : Color.white,
                    worldCorners = corners };
            }
            return result;
        }

        static void DrawHorizontalIndicator(RectTransform node, int index, bool visible, float x,
            float scale, UnityEngine.Object target, UnityEngine.Object[] previous, int[] frames, Vector3[] positions)
        {
            node.gameObject.SetActive(true);
            var group = node.GetComponent<CanvasGroup>();
            if (!group) group = node.gameObject.AddComponent<CanvasGroup>();
            if (!visible) { group.alpha = 0; previous[index] = null; frames[index] = -1; return; }
            node.localScale = Vector3.one * scale;
            Vector3 desired = new Vector3(x, 0, 0);
            if (frames[index] != Time.frameCount || previous[index] != target || group.alpha == 0)
            {
                frames[index] = Time.frameCount;
                positions[index] = previous[index] != target || group.alpha == 0 ? desired
                    : Vector3.Lerp(positions[index], desired, Time.deltaTime * 15);
                previous[index] = target;
            }
            node.localPosition = positions[index];
            group.alpha = 1;
        }
    }
}
