using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Fumo.NativePlayable
{
    public static class NativeChineseFont
    {
        static TMP_FontAsset font;
        static readonly Dictionary<Material,Material> materials=new Dictionary<Material,Material>();
        public static TMP_FontAsset Font
        {
            get
            {
                if(font)return font;
                var source=NativeContent.Load<Font>("NativeSettings/KNMaiyuan-Regular");
                if(!source)throw new InvalidOperationException("KN Maiyuan font resource missing");
                var shader=Shader.Find("Fumo/UI/DistanceField");
                if(!shader)throw new InvalidOperationException("Native UI font shader missing");
                if(Shader.Find("TextMeshPro/Mobile/Distance Field")==null)
                    typeof(TMP_FontAsset).Assembly.GetType("TMPro.ShaderUtilities")?.GetField("k_ShaderRef_MobileSDF",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)?.SetValue(null,shader);
                font=TMP_FontAsset.CreateFontAsset(source,64,8,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
                if(!font)throw new InvalidOperationException("KN Maiyuan SDF font creation failed");
                font.name="KN Maiyuan Chinese";font.material.shader=shader;
                return font;
            }
        }
        static Material StyledMaterial(Material original)
        {
            if(!original)return Font.material;
            Material result;if(materials.TryGetValue(original,out result) && result)return result;
            // Preserve menu outlines and glow while using the Maiyuan atlas and SDF metrics.
            result=new Material(Font.material){name="KN Maiyuan "+original.name,shader=original.shader};
            foreach(string key in new[]{"_FaceColor","_OutlineColor","_GlowColor","_UnderlayColor"})
                if(original.HasProperty(key) && result.HasProperty(key))result.SetColor(key,original.GetColor(key));
            foreach(string key in new[]{"_FaceDilate","_OutlineWidth","_OutlineSoftness","_GlowOffset","_GlowInner","_GlowOuter","_GlowPower","_UnderlayOffsetX","_UnderlayOffsetY","_UnderlayDilate","_UnderlaySoftness"})
                if(original.HasProperty(key) && result.HasProperty(key))result.SetFloat(key,original.GetFloat(key));
            result.shaderKeywords=original.shaderKeywords;
            materials[original]=result;return result;
        }
        public static void Apply(TMP_Text text,NativeUiFontData original,Material material=null)
        {
            var target=NativeLocalization.IsChinese?Font:original.GetFont();
            if(text.font!=target)text.font=target;
            var source=material?material:original.material;
            var shared=NativeLocalization.IsChinese?StyledMaterial(source):source;
            if(text.fontSharedMaterial!=shared)text.fontSharedMaterial=shared;
        }
    }
}
