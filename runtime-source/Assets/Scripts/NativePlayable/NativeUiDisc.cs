using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeUiDisc : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 center = rect.center;
            mesh.AddVert(center, color, Vector2.one * 0.5f);
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2 * Mathf.PI / segments;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + Vector2.Scale(radial, rect.size * 0.5f), color, radial * 0.5f + Vector2.one * 0.5f);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
