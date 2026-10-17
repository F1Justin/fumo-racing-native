using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Prefab-authored spacing, with whole operations wrapping when the viewport is narrow.
    [ExecuteAlways]
    public sealed class NativePromptRowLayout : LayoutGroup
    {
        [Min(0)] public float operationSpacing = 24;
        [Min(0)] public float rowSpacing = 8;
        struct Item { public RectTransform rect; public float width, height; }
        readonly List<List<Item>> rows = new List<List<Item>>();
        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float width = padding.horizontal, minimum = 0;
            foreach (var child in rectChildren)
            {
                float preferred = LayoutUtility.GetPreferredWidth(child);
                width += preferred; minimum = Mathf.Max(minimum, preferred);
            }
            width += operationSpacing * Mathf.Max(0, rectChildren.Count - 1);
            SetLayoutInputForAxis(minimum + padding.horizontal, width, 0, 0);
        }
        void BuildRows()
        {
            rows.Clear();
            float available = Mathf.Max(1, rectTransform.rect.width - padding.horizontal), used = 0;
            List<Item> row = null;
            foreach (var child in rectChildren)
            {
                float width = Mathf.Min(available, LayoutUtility.GetPreferredWidth(child));
                if (row == null || used + operationSpacing + width > available)
                { row = new List<Item>(); rows.Add(row); used = 0; }
                if (row.Count > 0) used += operationSpacing;
                row.Add(new Item { rect = child, width = width, height = LayoutUtility.GetPreferredHeight(child) }); used += width;
            }
        }
        static float Height(List<Item> row)
        { float height = 0; foreach (var item in row) height = Mathf.Max(height, item.height); return height; }
        public override void CalculateLayoutInputVertical()
        {
            BuildRows(); float height = padding.vertical + rowSpacing * Mathf.Max(0, rows.Count - 1);
            foreach (var row in rows) height += Height(row);
            SetLayoutInputForAxis(height, height, 0, 1);
        }
        public override void SetLayoutHorizontal()
        {
            BuildRows();
            foreach (var row in rows)
            {
                float width = operationSpacing * (row.Count - 1); foreach (var item in row) width += item.width;
                float x = GetStartOffset(0, width);
                foreach (var item in row) { SetChildAlongAxis(item.rect, 0, x, item.width); x += item.width + operationSpacing; }
            }
        }
        public override void SetLayoutVertical()
        {
            float y = padding.top;
            foreach (var row in rows)
            {
                float height = Height(row);
                foreach (var item in row) SetChildAlongAxis(item.rect, 1, y + (height - item.height) * .5f, item.height);
                y += height + rowSpacing;
            }
        }
    }
}
