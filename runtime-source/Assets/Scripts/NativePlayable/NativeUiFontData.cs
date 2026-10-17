using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeUiFontData
    {
        [Serializable]
        public struct GlyphData
        {
            public uint index;
            public float width, height, bearingX, bearingY, advance, scale;
            public int x, y, rectWidth, rectHeight;
        }
        [Serializable]
        public struct CharacterData { public uint unicode, glyphIndex; public float scale; }

        [Serializable] public struct PairData
        { public uint first,second; public float firstX,firstY,firstAdvance,firstYAdvance,secondX,secondY,secondAdvance,secondYAdvance; public int flags; }
        [SerializeReference] public NativeUiFontData[] fallbacks=new NativeUiFontData[0];
        public PairData[] pairs=new PairData[0];
        public Font dynamicFallbackSource;
        [NonSerialized] TMP_FontAsset dynamicFallback;
        public string name;
        public Texture2D atlas;
        public Material material;
        public TMP_FontAsset fontAsset;
        public FaceInfo face;
        public int atlasWidth, atlasHeight, padding, renderMode;
        public GlyphData[] glyphs;
        public CharacterData[] characters;
        [NonSerialized] TMP_FontAsset font;
        [NonSerialized] TMP_FontAsset restoredFontAsset;

        public TMP_FontAsset GetFont()
        {
            if (fontAsset)
            {
                if (restoredFontAsset != fontAsset) { RestoreFallbacks(fontAsset); restoredFontAsset = fontAsset; }
                return fontAsset;
            }
            if (font) return font;
            font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            font.name = name;
            JsonUtility.FromJsonOverwrite("{\"m_Version\":\"1.1.0\",\"m_AtlasWidth\":" + atlasWidth.ToString(CultureInfo.InvariantCulture)
                + ",\"m_AtlasHeight\":" + atlasHeight.ToString(CultureInfo.InvariantCulture)
                + ",\"m_AtlasPadding\":" + padding.ToString(CultureInfo.InvariantCulture)
                + ",\"m_AtlasRenderMode\":" + renderMode.ToString(CultureInfo.InvariantCulture) + "}", font);
            font.faceInfo = face;
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            if (font.atlasWidth != atlasWidth || font.atlasHeight != atlasHeight || font.atlasPadding != padding
                || (int)font.atlasRenderMode != renderMode)
                throw new InvalidOperationException("TMP atlas metadata was not restored for " + name);
#if UNITY_6000_0_OR_NEWER
            // TMP destroys a transient font's atlas/material on unload. Runtime
            // fallback fonts own copies, while the editor persists source assets.
            if (Application.isPlaying)
            {
                // Imported atlases can have no CPU pixel data. Preserve the
                // source format and sampling state, and copy the GPU resource.
                var ownedAtlas = new Texture2D(atlas.width, atlas.height, atlas.format, atlas.mipmapCount,
                    !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(atlas.graphicsFormat))
                {
                    name = atlas.name, filterMode = atlas.filterMode,
                    wrapModeU = atlas.wrapModeU, wrapModeV = atlas.wrapModeV, wrapModeW = atlas.wrapModeW,
                    anisoLevel = atlas.anisoLevel, mipMapBias = atlas.mipMapBias
                };
                ownedAtlas.Apply(false, true);
                Graphics.CopyTexture(atlas, ownedAtlas);
                font.atlasTextures = new[] { ownedAtlas };
                font.material = UnityEngine.Object.Instantiate(material);
                font.material.mainTexture = ownedAtlas;
            }
            else
#endif
            {
                font.atlasTextures = new[] { atlas };
                font.material = material;
            }
            var lookup = new Dictionary<uint, Glyph>();
            font.glyphTable.Clear();
            foreach (GlyphData data in glyphs)
            {
                var glyph = new Glyph(data.index, new GlyphMetrics(data.width, data.height, data.bearingX, data.bearingY, data.advance),
                    new GlyphRect(data.x, data.y, data.rectWidth, data.rectHeight), data.scale, 0);
                font.glyphTable.Add(glyph);
                lookup.Add(data.index, glyph);
            }
            font.characterTable.Clear();
            foreach (CharacterData data in characters)
                font.characterTable.Add(new TMP_Character(data.unicode, font, lookup[data.glyphIndex]) { scale = data.scale });
            font.fontFeatureTable.glyphPairAdjustmentRecords.Clear();
            foreach(var pair in pairs)
            {
#if UNITY_6000_0_OR_NEWER
                var first = new GlyphAdjustmentRecord(pair.first, new GlyphValueRecord(pair.firstX, pair.firstY, pair.firstAdvance, pair.firstYAdvance));
                var second = new GlyphAdjustmentRecord(pair.second, new GlyphValueRecord(pair.secondX, pair.secondY, pair.secondAdvance, pair.secondYAdvance));
                var record = new GlyphPairAdjustmentRecord(first, second)
                { featureLookupFlags = (UnityEngine.TextCore.LowLevel.FontFeatureLookupFlags)pair.flags };
#else
                var first=new TMP_GlyphAdjustmentRecord(pair.first,new TMP_GlyphValueRecord(pair.firstX,pair.firstY,pair.firstAdvance,pair.firstYAdvance));
                var second=new TMP_GlyphAdjustmentRecord(pair.second,new TMP_GlyphValueRecord(pair.secondX,pair.secondY,pair.secondAdvance,pair.secondYAdvance));
                var record=new TMP_GlyphPairAdjustmentRecord(first,second);
                var flagProperty=typeof(TMP_GlyphPairAdjustmentRecord).GetProperty("featureLookupFlags");
                if(flagProperty!=null && flagProperty.CanWrite)flagProperty.SetValue(record,Enum.ToObject(flagProperty.PropertyType,pair.flags),null);
                else {var flagField=typeof(TMP_GlyphPairAdjustmentRecord).GetField("m_FeatureLookupFlags",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);if(flagField!=null)flagField.SetValue(record,Enum.ToObject(flagField.FieldType,pair.flags));}
#endif
                font.fontFeatureTable.glyphPairAdjustmentRecords.Add(record);
            }
            RestoreFallbacks(font);
            font.ReadFontAssetDefinition();
            return font;
        }
        public bool HasCharacter(char character) => GetFont().HasCharacter(character,true,true);
        [Serializable] public sealed class FontProbe
        {
            public string sourceName,loadFaceError,dynamicName,error;
            public int fallbackCount,nullFallbackCount,dynamicCharacterCount;
            public uint[] unicodes,glyphIndices;
            public bool[] rootCharacters,dynamicCharacters;
            public bool sourcePresent,dynamicPresent;
        }
        public FontProbe DiagnoseCharacters(string text)
        {
            var probe=new FontProbe{unicodes=new uint[text.Length],glyphIndices=new uint[text.Length],rootCharacters=new bool[text.Length],dynamicCharacters=new bool[text.Length]};
            try
            {
                var root=GetFont();probe.sourcePresent=dynamicFallbackSource!=null;probe.sourceName=dynamicFallbackSource?dynamicFallbackSource.name:"";
                probe.dynamicPresent=dynamicFallback!=null;probe.dynamicName=dynamicFallback?dynamicFallback.name:"";probe.fallbackCount=root.fallbackFontAssetTable.Count;
                foreach(var fallback in root.fallbackFontAssetTable)if(fallback==null)probe.nullFallbackCount++;
                FontEngine.InitializeFontEngine();var status=FontEngine.LoadFontFace(dynamicFallbackSource,48);probe.loadFaceError=status.ToString();
                for(int i=0;i<text.Length;i++){probe.unicodes[i]=text[i];if(status==FontEngineError.Success)probe.glyphIndices[i]=SourceGlyphIndex(text[i]);}
                for(int i=0;i<text.Length;i++){probe.rootCharacters[i]=root.HasCharacter(text[i],true,true);if(dynamicFallback)probe.dynamicCharacters[i]=dynamicFallback.HasCharacter(text[i],false,true);}
                if(dynamicFallback)probe.dynamicCharacterCount=dynamicFallback.characterTable.Count;
            }
            catch(Exception error){probe.error=error.ToString();}
            return probe;
        }
        static uint SourceGlyphIndex(uint unicode)
        {
            var method=typeof(FontEngine).GetMethod("GetGlyphIndex",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(uint)},null);
            if(method!=null)return (uint)method.Invoke(null,new object[]{unicode});
            Glyph glyph;return FontEngine.TryGetGlyphWithUnicodeValue(unicode,GlyphLoadFlags.LOAD_DEFAULT,out glyph)?glyph.index:0;
        }
        void RestoreFallbacks(TMP_FontAsset target)
        {
            if(target.fallbackFontAssetTable==null)target.fallbackFontAssetTable=new List<TMP_FontAsset>();
            target.fallbackFontAssetTable.RemoveAll(asset=>asset==null || asset==target);
            foreach(var fallback in fallbacks)if(fallback!=null){var asset=fallback.GetFont();if(asset!=target && !target.fallbackFontAssetTable.Contains(asset))target.fallbackFontAssetTable.Add(asset);}
            if(dynamicFallbackSource!=null)
            {
                if(dynamicFallback==null)
                {
                    var nativeShader = Shader.Find("Fumo/UI/DistanceField");
                    if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null && nativeShader != null)
                        typeof(TMP_FontAsset).Assembly.GetType("TMPro.ShaderUtilities")?.GetField("k_ShaderRef_MobileSDF", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, nativeShader);
                    dynamicFallback=TMP_FontAsset.CreateFontAsset(dynamicFallbackSource,48,5,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
                    if(dynamicFallback==null)throw new InvalidOperationException("Original source-font dynamic fallback creation failed");
                    dynamicFallback.name="Native Custom "+dynamicFallbackSource.name;
                    var shader=Shader.Find("Fumo/UI/DistanceField");if(shader!=null)dynamicFallback.material.shader=shader;
                }
                if(!target.fallbackFontAssetTable.Contains(dynamicFallback))target.fallbackFontAssetTable.Add(dynamicFallback);
            }
            // The language menu displays the Chinese option before Chinese is selected.
            if(Application.isPlaying && !target.fallbackFontAssetTable.Contains(NativeChineseFont.Font))target.fallbackFontAssetTable.Add(NativeChineseFont.Font);
        }
    }
}
