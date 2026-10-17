using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fumo.NativePlayable
{
    internal sealed class NativeResultsAvatarSet : IDisposable
    {
        readonly GameObject[] roots = new GameObject[4];
        readonly RenderTexture[] textures = new RenderTexture[4];
        [Serializable] internal sealed class RenderObservation
        {
            public int humanIndex,depth,graphicsShaderLevel,supportedRenderTargetCount;
            public string requestedPath,actualPath,graphicsTier,graphicsAPI,graphicsDevice,colorSpace,textureFormat;
            public bool textureSRGB,cameraHDR,usesUnityDefaults;
            public bool backendCompatibilityApplied,pointEnabledBeforeRender,pointEnabledAfterRender;
            public string[] materials;
        }
        readonly RenderObservation[] observations=new RenderObservation[4];
        internal RenderObservation[] RenderingObservations => observations.Where(value=>value!=null).ToArray();
        public RenderTexture Create(int humanIndex, GameObject prefab)
        {
            if (humanIndex < 0 || humanIndex > 3 || !prefab) throw new ArgumentException("Original result avatar source missing");
            if (roots[humanIndex]) throw new InvalidOperationException("Result avatar already initialized");
            var source = NativeContent.Load<TextAsset>("NativeMenu/results-preview");
            if (!source) throw new InvalidOperationException("Original results preview definition missing");
            var d = JsonUtility.FromJson<NativeResultsPreviewDefinition>(source.text);
            var root = new GameObject("Original ResultPlayerUI avatar " + humanIndex); roots[humanIndex] = root;
            root.transform.position = d.worldOrigin + Vector3.right * (25 * humanIndex);
            var cameraNode = new GameObject("ResultsCamera"); cameraNode.transform.SetParent(root.transform, false);
            cameraNode.transform.localPosition = d.cameraPosition; cameraNode.transform.localRotation = d.cameraRotation;
            var camera = cameraNode.AddComponent<Camera>(); camera.enabled = false; camera.useOcclusionCulling = false;
            if (!d.cameraUsesUnityDefaults)
            {
                camera.fieldOfView = d.fieldOfView; camera.nearClipPlane = d.near; camera.farClipPlane = d.far;
                camera.orthographic = d.orthographic; camera.orthographicSize = d.orthographicSize;
            }
            camera.clearFlags = d.clearFlags; camera.backgroundColor = d.background;
            camera.cullingMask = LayerMask.GetMask(d.renderLayerName);
            var texture = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            texture.filterMode = FilterMode.Bilinear; texture.Create(); textures[humanIndex] = texture; camera.targetTexture = texture;
            Light pointLight=null;
            if (d.pointLight)
            {
                var node = new GameObject("Original results point light");
                var light = node.AddComponent<Light>(); pointLight=light;
                node.transform.position = root.transform.TransformPoint(d.lightPosition);
                node.transform.SetParent(root.transform, true);
                light.type = LightType.Point;
                light.range = d.lightRange; light.intensity = d.lightIntensity; light.color = d.lightColor;
            }
            var model = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            model.transform.localPosition = d.modelPosition; model.transform.localRotation = d.modelRotation;
            if (!d.preserveModelScale) model.transform.localScale = d.modelScale;
            var lod = model.GetComponent<LODGroup>();
            if (lod) { lod.enabled = false; if (model.transform.childCount > 0) model.transform.GetChild(0).gameObject.SetActive(false); UnityEngine.Object.Destroy(lod); }
            int layer = LayerMask.NameToLayer(d.renderLayerName); model.layer = layer;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>()) { renderer.gameObject.layer = layer; renderer.lightProbeUsage = LightProbeUsage.Off; }
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var audio in model.GetComponentsInChildren<AudioSource>()) { audio.Stop(); audio.enabled = false; }
            var compatibility=NativeResultsRenderCompatibility.Render(camera,pointLight);
            observations[humanIndex]=new RenderObservation {
                backendCompatibilityApplied=compatibility.applied,pointEnabledBeforeRender=compatibility.pointEnabledBeforeRender,pointEnabledAfterRender=compatibility.pointEnabledAfterRender,
                humanIndex=humanIndex,requestedPath=camera.renderingPath.ToString(),actualPath=camera.actualRenderingPath.ToString(),
                graphicsTier=Graphics.activeTier.ToString(),graphicsAPI=SystemInfo.graphicsDeviceType.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName,
                graphicsShaderLevel=SystemInfo.graphicsShaderLevel,supportedRenderTargetCount=SystemInfo.supportedRenderTargetCount,
                colorSpace=QualitySettings.activeColorSpace.ToString(),depth=texture.depth,textureFormat=texture.format.ToString(),textureSRGB=texture.sRGB,
                cameraHDR=camera.allowHDR,usesUnityDefaults=d.cameraUsesUnityDefaults,
                materials=model.GetComponentsInChildren<Renderer>(true).SelectMany(renderer=>renderer.sharedMaterials.Where(material=>material).Select(material=>
                    "renderer="+renderer.name+" active="+renderer.gameObject.activeInHierarchy+" shader="+material.shader.name+" supported="+material.shader.isSupported+
                    " deferredPass="+material.FindPass("DEFERRED")+" passCount="+material.passCount+" keywords="+string.Join(",",material.shaderKeywords))).ToArray()
            };
            return texture;
        }
        public void Dispose()
        {
            for (int i = 0; i < 4; i++)
            {
                if (roots[i]) { roots[i].SetActive(false); UnityEngine.Object.Destroy(roots[i]); }
                if (textures[i]) { textures[i].Release(); UnityEngine.Object.Destroy(textures[i]); }
                roots[i] = null; textures[i] = null;
            }
        }
    }
}
