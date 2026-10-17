using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public static class NativeMainAliasEvidence
    {
        public static IDisposable Mutate(Graphic target, string kind)
        {
            if (kind == "disabled") { bool old = target.enabled; target.enabled = false; return new Restore(() => target.enabled = old); }
            if (kind == "alpha-zero") { var old = target.color; target.color = new Color(old.r, old.g, old.b, 0); return new Restore(() => target.color = old); }
            if (kind == "blank-text")
            {
                var text = target as TMP_Text;
                if (text) { string old = text.text; text.text = ""; text.ForceMeshUpdate(); return new Restore(() => { text.text = old; text.ForceMeshUpdate(); }); }
                var legacy = target as Text; if (!legacy) throw new ArgumentException("text target required");
                string previous = legacy.text; legacy.text = ""; return new Restore(() => legacy.text = previous);
            }
            if (kind == "blank-texture") { var raw = target as RawImage; if (!raw) throw new ArgumentException("RawImage target required"); var old = raw.texture; var blank = new Texture2D(1, 1, TextureFormat.RGBA32, false); blank.SetPixel(0, 0, Color.clear); blank.Apply(); raw.texture = blank; return new Restore(() => { raw.texture = old; UnityEngine.Object.Destroy(blank); }); }
            if (kind == "opaque-cover")
            {
                var root = new GameObject("Alias occlusion mutation", typeof(RectTransform), typeof(Canvas), typeof(Image));
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32767;
                var rect = root.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var cover = root.GetComponent<Image>(); cover.color = Color.black; cover.raycastTarget = false;
                return new Restore(() => { root.SetActive(false); UnityEngine.Object.Destroy(root); });
            }
            throw new ArgumentException("Unknown alias mutation");
        }
        sealed class Restore : IDisposable
        {
            Action action; public Restore(Action action) { this.action = action; }
            public void Dispose() { if (action == null) return; action(); action = null; }
        }
    }
}
