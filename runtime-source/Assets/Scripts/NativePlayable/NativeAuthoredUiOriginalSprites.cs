using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        [Serializable] public sealed class OriginalSpriteRectExpectation
        {
            public float x, y, width, height;
            public Vector2 position => new Vector2(x, y);
            public Vector2 size => new Vector2(width, height);
        }
        [Serializable] public sealed class OriginalSpriteExpectation
        {
            public string id, guid, sourceAsset, sourceBundle, sourceBundleSHA256, sourceSerializedFile;
            public long sourcePathId;
            public float pixelsPerUnit;
            public OriginalSpriteRectExpectation rect;
            public Vector2 pivot, textureRectOffset, meshMin, meshMax;
            public Vector4 border, padding;
            public int vertexCount, indexCount;
            public string vertexHash, triangleHash, textureRGBAHash;
        }
        [Serializable] public sealed class OriginalSpriteSource { public int sourceCount, sourceImageCount; public OriginalSpriteExpectation[] rows; }
        [Serializable] public sealed class OriginalSpriteObservation
        {
            public string state, key, sourceId, guid;
            public bool passed, sourceReference, activeSourceReference, textureReference, geometry, sourceBounds, mask, visible;
            public int sourceInstanceId, spriteInstanceId, activeSpriteInstanceId, textureInstanceId, vertexCount, indexCount;
            public float pixelsPerUnit;
            public Rect rect;
            public Vector2 pivot, textureRectOffset;
            public Vector4 border, padding;
            public Bounds bounds, expectedSourceBounds;
            public string vertexHash, triangleHash;
        }
        static bool SpriteNear(float a, float b) => Mathf.Abs(a - b) < .0001f;
        static bool SpriteNear(Vector2 a, Vector2 b) => SpriteNear(a.x,b.x) && SpriteNear(a.y,b.y);
        static bool SpriteNear(Vector4 a, Vector4 b) => SpriteNear(a.x,b.x) && SpriteNear(a.y,b.y) && SpriteNear(a.z,b.z) && SpriteNear(a.w,b.w);
        static string SpriteDigest(Action<BinaryWriter> write)
        {
            using (var bytes = new MemoryStream())
            {
                using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) write(writer);
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        public OriginalSpriteObservation ObserveOriginalSprite(string state, string key, Sprite expected, OriginalSpriteExpectation source, bool requireMask)
        {
            var image = transforms[key].GetComponent<Image>(); var actual = image.sprite;
            var result = new OriginalSpriteObservation { state=state, key=key, sourceId=source.id, guid=source.guid,
                sourceInstanceId=expected ? expected.GetInstanceID() : 0, spriteInstanceId=actual ? actual.GetInstanceID() : 0,
                activeSpriteInstanceId=image.overrideSprite ? image.overrideSprite.GetInstanceID() : 0,
                sourceReference=actual && actual==expected, activeSourceReference=image.overrideSprite==expected,
                textureReference=actual && expected && actual.texture==expected.texture,
                visible=VisibleGraphic(key), mask=!requireMask || (image.GetComponent<Mask>() && image.GetComponent<Mask>().enabled && image.maskable)
            };
            if (!actual) return result;
            result.pixelsPerUnit=actual.pixelsPerUnit; result.rect=actual.rect; result.pivot=actual.pivot;
            result.border=actual.border; result.textureRectOffset=actual.textureRectOffset;
            result.padding=DataUtility.GetPadding(actual); result.bounds=actual.bounds; result.expectedSourceBounds=expected.bounds;
            result.sourceBounds=result.bounds==expected.bounds;
            result.vertexCount=actual.vertices.Length; result.indexCount=actual.triangles.Length;
            result.textureInstanceId=actual.texture.GetInstanceID();
            result.vertexHash=SpriteDigest(w => { foreach(var v in actual.vertices) { w.Write(v.x); w.Write(v.y); } });
            result.triangleHash=SpriteDigest(w => { foreach(var v in actual.triangles) w.Write(v); });
            result.geometry=SpriteNear(result.pixelsPerUnit, source.pixelsPerUnit) &&
                SpriteNear(result.rect.position,source.rect.position) && SpriteNear(result.rect.size,source.rect.size) &&
                SpriteNear(result.pivot,source.pivot) && SpriteNear(result.border,source.border) &&
                SpriteNear(result.textureRectOffset,source.textureRectOffset) && SpriteNear(result.padding,source.padding) &&
                result.vertexCount==source.vertexCount && result.indexCount==source.indexCount &&
                result.vertexHash==source.vertexHash && result.triangleHash==source.triangleHash &&
                SpriteNear(new Vector2(actual.vertices.Min(v=>v.x),actual.vertices.Min(v=>v.y)),source.meshMin) &&
                SpriteNear(new Vector2(actual.vertices.Max(v=>v.x),actual.vertices.Max(v=>v.y)),source.meshMax);
            result.passed=result.sourceReference && result.activeSourceReference && result.textureReference && result.geometry && result.sourceBounds && result.mask && result.visible;
            return result;
        }
        internal Rect OriginalDiscScreenRect()
        {
            var corners=new Vector3[4]; transforms["menu2/Canvas/MusicRoom/Disc/labelmask"].GetWorldCorners(corners);
            return Rect.MinMaxRect(Mathf.Max(0,Mathf.Floor(corners.Min(v=>v.x))),Mathf.Max(0,Mathf.Floor(corners.Min(v=>v.y))),
                Mathf.Min(Screen.width,Mathf.Ceil(corners.Max(v=>v.x))),Mathf.Min(Screen.height,Mathf.Ceil(corners.Max(v=>v.y))));
        }
        readonly System.Collections.Generic.Dictionary<Image,bool> discReferenceOriginalEnabled=new System.Collections.Generic.Dictionary<Image,bool>();
        internal GameObject DiscComparisonBackground()
        {
            var canvas = transforms["menu2/Canvas/MusicRoom/Disc"].GetComponentInParent<Canvas>();
            var background = new GameObject("Sprite comparison constant background", typeof(RectTransform), typeof(Image));
            var rect = background.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false); rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var graphic = background.GetComponent<Image>(); graphic.color = Color.black; graphic.raycastTarget = false;
            return background;
        }
        internal GameObject SourceDiscRenderReference(Sprite selected)
        {
            const string key="menu2/Canvas/MusicRoom/Disc";
            var original=transforms[key]; var clone=UnityEngine.Object.Instantiate(original.gameObject,original.parent);
            clone.name="Independent original Sprite render reference"; clone.transform.SetSiblingIndex(original.GetSiblingIndex());
            foreach(var image in clone.GetComponentsInChildren<Image>(true))
            {
                string relative=""; var node=image.transform;
                while(node!=clone.transform) { relative="/"+node.name+relative; node=node.parent; }
                var source=Definition(key+relative);
                image.overrideSprite=null; image.sprite=relative=="/labelmask/label" ? selected : relative=="/labelmask" ? NativeMenuSpriteData.Source.labelMask : source.sprite;
                image.enabled=source.imageEnabled; image.type=(Image.Type)source.imageType; image.maskable=source.maskable;
                image.preserveAspect=source.preserveAspect; image.useSpriteMesh=source.imageUseSpriteMesh;
                image.pixelsPerUnitMultiplier=source.imagePixelsPerUnitMultiplier; image.color=source.color;
                var mask=image.GetComponent<Mask>(); if(mask) { mask.enabled=source.maskEnabled; mask.showMaskGraphic=source.showMask; }
            }
            foreach(var image in original.GetComponentsInChildren<Image>(true)) { discReferenceOriginalEnabled[image]=image.enabled; image.enabled=false; }
            return clone;
        }
        internal void RestoreDiscRenderReference(GameObject clone)
        {
            if(clone) { clone.SetActive(false); UnityEngine.Object.Destroy(clone); }
            foreach(var pair in discReferenceOriginalEnabled) if(pair.Key) pair.Key.enabled=pair.Value;
            discReferenceOriginalEnabled.Clear();
        }
        internal Image OriginalSpriteImage(string key) => transforms[key].GetComponent<Image>();
    }
    public sealed partial class NativeRaceHud { internal NativeAuthoredUi OriginalSpriteUi => authoredUi; }
}
