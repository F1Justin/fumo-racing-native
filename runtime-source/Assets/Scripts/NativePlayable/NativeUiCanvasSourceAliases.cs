using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeUiCanvas
    {
        public Action<NativeUiCanvas> SourceRenderFixtureHook;
        readonly Dictionary<string, Graphic> sourceGraphics = new Dictionary<string, Graphic>();
        readonly HashSet<string> duplicateSources = new HashSet<string>();
        [Serializable] public sealed class SourceGraphicSample
        {
            public string sourcePath, poolCategory, graphicName, contentReference, material, renderingMaterial;
            public int frame, instanceId, poolIndex, graphicDepth;
            public bool bound, duplicateWriter;
            public Vector3[] worldCorners;
            public Vector2[] screenCorners;
            public Rect uvRect;
            public NativeAuthoredUi.RequiredLeafSample leaf;
        }
        void BindSource(Graphic graphic, string sourcePath, string category, int index)
        {
            var identity = graphic.GetComponent<NativeUiSourceIdentity>();
            if (!identity) identity = graphic.gameObject.AddComponent<NativeUiSourceIdentity>();
            identity.sourcePath = sourcePath; identity.frame = Time.frameCount;
            identity.poolCategory = category; identity.poolIndex = index;
            if (string.IsNullOrEmpty(sourcePath)) return;
            if (sourceGraphics.ContainsKey(sourcePath)) duplicateSources.Add(sourcePath);
            sourceGraphics[sourcePath] = graphic;
        }
        public bool TryGetSourceGraphic(string sourcePath, out Graphic graphic)
        {
            graphic = null;
            if (duplicateSources.Contains(sourcePath) || !sourceGraphics.TryGetValue(sourcePath, out var candidate) || !candidate) return false;
            var identity = candidate.GetComponent<NativeUiSourceIdentity>();
            if (!identity || identity.frame != Time.frameCount || identity.sourcePath != sourcePath) return false;
            graphic = candidate; return true;
        }
        public SourceGraphicSample SourceSample(string sourcePath)
        {
            bool bound = TryGetSourceGraphic(sourcePath, out var graphic);
            var sample = new SourceGraphicSample { sourcePath=sourcePath, frame=Time.frameCount, bound=bound,
                duplicateWriter=duplicateSources.Contains(sourcePath), leaf=NativeAuthoredUi.ReadGraphicRequiredLeaf(sourcePath,graphic) };
            if (!bound) return sample;
            var identity=graphic.GetComponent<NativeUiSourceIdentity>();
            sample.instanceId=graphic.GetInstanceID(); sample.poolIndex=identity.poolIndex; sample.poolCategory=identity.poolCategory;
            sample.worldCorners=new Vector3[4]; graphic.rectTransform.GetWorldCorners(sample.worldCorners);
            var canvas=graphic.canvas;
            var camera=canvas && canvas.renderMode!=RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            sample.screenCorners=new Vector2[4];
            for(int i=0;i<4;i++) sample.screenCorners[i]=RectTransformUtility.WorldToScreenPoint(camera,sample.worldCorners[i]);
            sample.graphicName=graphic.name; sample.graphicDepth=graphic.canvasRenderer.absoluteDepth;
            sample.material=graphic.material ? graphic.material.name : null;
            sample.renderingMaterial=graphic.materialForRendering ? graphic.materialForRendering.name : null;
            var raw=graphic as RawImage; var image=graphic as Image; var text=graphic as TMP_Text; var legacyText=graphic as Text;
            if(raw) { sample.uvRect=raw.uvRect; sample.contentReference=raw.texture ? raw.texture.name+"#"+raw.texture.GetInstanceID() : null; }
            else if(image) sample.contentReference=image.sprite ? image.sprite.name+"#"+image.sprite.GetInstanceID() : null;
            else if(text) sample.contentReference=text.text+" | font="+(text.font ? text.font.name+"#"+text.font.GetInstanceID() : "null");
            else if(legacyText) sample.contentReference=legacyText.text+" | font="+(legacyText.font ? legacyText.font.name+"#"+legacyText.font.GetInstanceID() : "null");
            return sample;
        }
    }
    public sealed partial class NativeRaceHud
    {
        public NativeUiCanvas MainWriterCanvas => canvas;
    }
}
