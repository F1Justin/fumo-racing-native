using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeResultsPreviewDefinition
    {
        public string sourceReceipt, renderLayerName;
        public Vector3 worldOrigin;
        public Vector3 cameraPosition, modelPosition, modelScale;
        public Quaternion cameraRotation, modelRotation;
        public float fieldOfView, near, far, orthographicSize;
        public bool orthographic, cameraUsesUnityDefaults, preserveModelScale, pointLight;
        public Vector3 lightPosition;
        public float lightRange, lightIntensity;
        public Color lightColor;
        public Color background;
        public CameraClearFlags clearFlags;
    }

    public sealed partial class NativeRace
    {
        GameObject resultsPreviewRoot, resultsPreviewModel;
        Camera resultsPreviewCamera;
        RenderTexture resultsPreviewTexture;
        Light resultsPreviewLight;
        NativeResultsRenderCompatibility.Observation resultsPreviewRenderCompatibility;
        NativeResultsPreviewDefinition resultsPreviewDefinition;
        int resultsPreviewCharacter = -1, resultsPreviewSkin = -1;
        int resultsPreviewRenderCount;

        void DrawResultsCharacterPreview()
        {
            if (phase != Phase.Finished || !hud.ResultsVisible || hud.ResultsScoreboardVisible || !vehicleRoster) return;
            if (resultsPreviewDefinition == null)
            {
                var source = NativeContent.Load<TextAsset>("NativeMenu/results-preview");
                if (!source) return;
                resultsPreviewDefinition = JsonUtility.FromJson<NativeResultsPreviewDefinition>(source.text);
            }
            if (!resultsPreviewRoot)
            {
                resultsPreviewRoot = new GameObject("Original results character preview");
                resultsPreviewRoot.transform.position = resultsPreviewDefinition.worldOrigin;
                var cameraNode = new GameObject("ResultsCamera");
                cameraNode.transform.SetParent(resultsPreviewRoot.transform, false);
                cameraNode.transform.localPosition = resultsPreviewDefinition.cameraPosition;
                cameraNode.transform.localRotation = resultsPreviewDefinition.cameraRotation;
                resultsPreviewCamera = cameraNode.AddComponent<Camera>();
                if (!resultsPreviewDefinition.cameraUsesUnityDefaults)
                {
                    resultsPreviewCamera.fieldOfView = resultsPreviewDefinition.fieldOfView;
                    resultsPreviewCamera.nearClipPlane = resultsPreviewDefinition.near;
                    resultsPreviewCamera.farClipPlane = resultsPreviewDefinition.far;
                    resultsPreviewCamera.orthographic = resultsPreviewDefinition.orthographic;
                    resultsPreviewCamera.orthographicSize = resultsPreviewDefinition.orthographicSize;
                }
                resultsPreviewCamera.useOcclusionCulling = false;
                resultsPreviewCamera.clearFlags = resultsPreviewDefinition.clearFlags;
                resultsPreviewCamera.backgroundColor = resultsPreviewDefinition.background;
                resultsPreviewCamera.cullingMask = LayerMask.GetMask(resultsPreviewDefinition.renderLayerName);
                resultsPreviewCamera.enabled = false;
                resultsPreviewTexture = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                resultsPreviewTexture.filterMode = FilterMode.Bilinear;
                resultsPreviewTexture.Create();
                resultsPreviewCamera.targetTexture = resultsPreviewTexture;
                if (resultsPreviewDefinition.pointLight)
                {
                    var lightNode = new GameObject("Original results point light");
                    resultsPreviewLight = lightNode.AddComponent<Light>();
                    lightNode.transform.SetParent(resultsPreviewRoot.transform, false);
                    lightNode.transform.localPosition = resultsPreviewDefinition.lightPosition;
                    resultsPreviewLight.type = LightType.Point;
                    resultsPreviewLight.range = resultsPreviewDefinition.lightRange;
                    resultsPreviewLight.intensity = resultsPreviewDefinition.lightIntensity;
                    resultsPreviewLight.color = resultsPreviewDefinition.lightColor;
                }
            }
            int skin = vehicleRoster.ClampSkin(selectedCharacterIndex, selectedSkinIndex);
            if (resultsPreviewCharacter != selectedCharacterIndex || resultsPreviewSkin != skin)
            {
                if (resultsPreviewModel) { resultsPreviewModel.SetActive(false); Destroy(resultsPreviewModel); }
                resultsPreviewModel = Instantiate(vehicleRoster.DriverModelAt(selectedCharacterIndex, skin), resultsPreviewRoot.transform, false);
                resultsPreviewModel.transform.localPosition = resultsPreviewDefinition.modelPosition;
                resultsPreviewModel.transform.localRotation = resultsPreviewDefinition.modelRotation;
                if (!resultsPreviewDefinition.preserveModelScale) resultsPreviewModel.transform.localScale = resultsPreviewDefinition.modelScale;
                var originalLod = resultsPreviewModel.GetComponent<LODGroup>();
                if (originalLod)
                {
                    originalLod.enabled = false;
                    if (resultsPreviewModel.transform.childCount > 0) resultsPreviewModel.transform.GetChild(0).gameObject.SetActive(false);
                    Destroy(originalLod);
                }
                int renderLayer = LayerMask.NameToLayer(resultsPreviewDefinition.renderLayerName);
                resultsPreviewModel.layer = renderLayer;
                foreach (var collider in resultsPreviewModel.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (var audio in resultsPreviewModel.GetComponentsInChildren<AudioSource>()) { audio.Stop(); audio.enabled = false; }
                foreach (var renderer in resultsPreviewModel.GetComponentsInChildren<Renderer>())
                {
                    renderer.gameObject.layer = renderLayer;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                }
                resultsPreviewCharacter = selectedCharacterIndex;
                resultsPreviewSkin = skin;
                resultsPreviewRenderCompatibility=NativeResultsRenderCompatibility.Render(resultsPreviewCamera,resultsPreviewLight);
                resultsPreviewRenderCount++;
            }
            hud.ResultCharacterTexture(resultsPreviewTexture);
        }
        void ClearResultsPreview()
        {
            if (resultsPreviewRoot) Destroy(resultsPreviewRoot);
            if (resultsPreviewTexture) { resultsPreviewTexture.Release(); Destroy(resultsPreviewTexture); }
            resultsPreviewRoot = resultsPreviewModel = null;
            resultsPreviewCamera = null; resultsPreviewTexture = null; resultsPreviewLight = null;
            resultsPreviewCharacter = resultsPreviewSkin = -1;
            resultsPreviewRenderCount = 0;
        }
    }
}
