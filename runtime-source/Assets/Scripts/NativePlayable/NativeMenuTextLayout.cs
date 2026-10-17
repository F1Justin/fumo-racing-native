using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeMenuTextLayout
    {
        static readonly Dictionary<string, NativeAuthoredUiNode> mainLabels = new Dictionary<string, NativeAuthoredUiNode>();
        public static float MainLabelRightMargin(string path, float margin) => NativeLocalization.IsChinese &&
            path == "menu2/Canvas/MainMenu/WheelRoot/WheelOptions/RaceButton/Text" ? 0 : margin;
        public static void Register(string path, NativeAuthoredUiNode node)
        {
            if (path.StartsWith("menu2/Canvas/MainMenu/") && path.EndsWith("Button/Text")) mainLabels[path] = node;
        }
        public static void Apply(TMP_Text text, NativeAuthoredUiNode node, float scale = 1, string path = null)
        {
            bool chineseSolo = NativeLocalization.IsChinese && path == "menu2/Canvas/MainMenu/WheelRoot/WheelOptions/SoloButton/Text";
            // The short Chinese caption uses the same type scale as the other secondary menu choices.
            float maximum = chineseSolo ? Mathf.Min(node.maxSize, 72) : node.maxSize;
            text.fontSize = (chineseSolo ? maximum : node.fontSize) * scale;
            text.enableAutoSizing = node.autoSize;
            text.fontSizeMin = node.minSize * scale;
            text.fontSizeMax = maximum * scale;
            var margin = node.margin;
            margin.z = MainLabelRightMargin(path, margin.z);
            text.margin = margin * scale;
        }
        public static void ApplyMainLabel(TMP_Text text, string path)
        {
            if (path != null && mainLabels.TryGetValue(path, out var node))
                Apply(text, node, 1 / NativeMenuTransitionPlayback.SourceScale, path);
        }
    }
}
