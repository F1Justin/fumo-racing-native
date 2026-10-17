using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeCeremonyView : MonoBehaviour
    {
        public Transform[] podiumAnchors;
        [Serializable]
        public sealed class AnimatedTransform
        {
            public Transform target;
            public string sourceTrackId, sourceClip;
            public float length, timeOffset;
            public Vector3 offsetPosition;
            public Quaternion offsetRotation = Quaternion.identity;
            public AnimationCurve[] position, rotation;
            public void Sample(float time)
            {
                float localTime = Mathf.Clamp(time + timeOffset, 0, length);
                if (position != null && position.Length == 3)
                    target.localPosition = offsetPosition + offsetRotation * new Vector3(position[0].Evaluate(localTime), position[1].Evaluate(localTime), position[2].Evaluate(localTime));
                if (rotation != null && rotation.Length == 4)
                {
                    var value = new Quaternion(rotation[0].Evaluate(localTime), rotation[1].Evaluate(localTime), rotation[2].Evaluate(localTime), rotation[3].Evaluate(localTime));
                    target.localRotation = offsetRotation * value.normalized;
                }
            }
        }
        [Serializable]
        public sealed class ActivationWindow
        {
            public GameObject target;
            public string sourceTrackId;
            public float start, duration;
            public bool Active(float time) => time >= start && time < start + duration;
        }
        public AnimatedTransform[] animatedTransforms;
        public ActivationWindow[] activationWindows;
        public Transform cameraSource, championStatueAnchor;
        public Camera originalCamera;
        public AudioClip ceremonyMusic;
        public AnimationCurve fadeAlpha, completeTextY;
        public float finishSignalTime;
        public float PlaybackTime { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool Finished => PlaybackTime >= finishSignalTime;
        public float FadeAlpha => fadeAlpha == null ? 0 : Mathf.Clamp01(fadeAlpha.Evaluate(PlaybackTime));
        public float CompleteTextY => completeTextY == null ? 0 : completeTextY.Evaluate(PlaybackTime);
        public bool CompleteTitleVisible => ActiveTarget("CompleteText");
        public bool PlayerResultVisible => ActiveTarget("PlayerText");
        double playbackStarted;
        Camera playbackCamera;
        float previousFov, previousNear, previousFar;
        Vector3 previousCameraPosition;
        Quaternion previousCameraRotation;
        CameraClearFlags previousClear;
        Color previousBackground;
        int previousMask;
        bool environmentApplied;
        ShadowResolution previousShadowResolution;
        ShadowProjection previousShadowProjection;
        int previousShadowCascades;
        bool[] activeWindows;

        bool ActiveTarget(string name)
        {
            if (activationWindows == null) return false;
            foreach (var window in activationWindows)
                if (window.target && window.target.name == name) return window.Active(PlaybackTime);
            return false;
        }
        public void BindChampion(GameObject template)
        {
            if (!championStatueAnchor || !template) throw new InvalidOperationException("Original champion statue binding is missing.");
            foreach (Transform child in championStatueAnchor) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var avatar = Instantiate(template, championStatueAnchor, false);
            avatar.name = "Cup Champion Statue";
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }
        public void BeginPlayback(Camera camera)
        {
            if (!camera || !cameraSource || !originalCamera || animatedTransforms == null || animatedTransforms.Length == 0)
                throw new InvalidOperationException("Original ceremony cinematic bindings are missing.");
            StopPlayback();
            playbackCamera = camera;
            previousCameraPosition = camera.transform.position; previousCameraRotation = camera.transform.rotation;
            previousFov = camera.fieldOfView; previousNear = camera.nearClipPlane; previousFar = camera.farClipPlane;
            previousClear = camera.clearFlags; previousBackground = camera.backgroundColor; previousMask = camera.cullingMask;
            camera.fieldOfView = originalCamera.fieldOfView; camera.nearClipPlane = originalCamera.nearClipPlane;
            camera.farClipPlane = originalCamera.farClipPlane; camera.clearFlags = originalCamera.clearFlags;
            camera.backgroundColor = originalCamera.backgroundColor; camera.cullingMask = originalCamera.cullingMask;
            playbackStarted = Time.timeAsDouble;
            IsPlaying = true;
            activeWindows = new bool[activationWindows.Length];
            foreach (var window in activationWindows) if (window.target) window.target.SetActive(false);
            SamplePlayback(camera, 0);
        }
        public bool TickPlayback(Camera camera)
        {
            if (!IsPlaying || !camera) return false;
            SamplePlayback(camera, (float)(Time.timeAsDouble - playbackStarted));
            return true;
        }
        public void SeekPlayback(Camera camera, float time)
        {
            playbackStarted = Time.timeAsDouble - Mathf.Clamp(time, 0, finishSignalTime);
            SamplePlayback(camera, time);
        }
        public void SamplePlayback(Camera camera, float time)
        {
            PlaybackTime = Mathf.Clamp(time, 0, finishSignalTime);
            foreach (var track in animatedTransforms) if (track.target) track.Sample(PlaybackTime);
            for (int i = 0; i < activationWindows.Length; i++)
            {
                var window = activationWindows[i];
                bool active = window.Active(PlaybackTime);
                if (window.target && window.target.activeSelf != active) window.target.SetActive(active);
                // Timeline activation restarts authored one-shot particle effects on each rising edge.
                if (window.target && active && (activeWindows == null || !activeWindows[i]))
                    foreach (var particle in window.target.GetComponentsInChildren<ParticleSystem>(true)) particle.Play(false);
                if (activeWindows != null) activeWindows[i] = active;
            }
            if (camera && cameraSource) camera.transform.SetPositionAndRotation(cameraSource.position, cameraSource.rotation);
        }
        public void StopPlayback()
        {
            IsPlaying = false;
            if (playbackCamera)
            {
                playbackCamera.transform.SetPositionAndRotation(previousCameraPosition, previousCameraRotation);
                playbackCamera.fieldOfView = previousFov; playbackCamera.nearClipPlane = previousNear; playbackCamera.farClipPlane = previousFar;
                playbackCamera.clearFlags = previousClear; playbackCamera.backgroundColor = previousBackground; playbackCamera.cullingMask = previousMask;
                playbackCamera = null;
            }
        }
        void OnDisable() { RestoreEnvironment(); }
        void OnGUI()
        {
            if (!IsPlaying || FadeAlpha <= 0) return;
            var oldColor = GUI.color; int oldDepth = GUI.depth;
            GUI.depth = -1000; GUI.color = new Color(0, 0, 0, FadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = oldColor; GUI.depth = oldDepth;
        }

        public Material skybox;
        public UnityEngine.Rendering.AmbientMode ambientMode;
        public Color ambient, sky, equator, ground, fogColor;
        public float ambientIntensity, fogDensity, fogStart, fogEnd;
        public bool fog;
        public FogMode fogMode;
        public Texture2D[] lightmapColors, lightmapDirections, lightmapMasks;
        public Renderer[] mappedRenderers;
        public int[] lightmapIndices;
        public Vector4[] lightmapOffsets;
        public LightmapsMode lightmapMode;
        LightmapData[] previousMaps;
        LightmapsMode previousMode;
        Material previousSkybox;
        UnityEngine.Rendering.AmbientMode previousAmbientMode;
        Color previousAmbient, previousSky, previousEquator, previousGround, previousFogColor;
        float previousIntensity, previousFogDensity, previousFogStart, previousFogEnd;
        bool previousFog;
        FogMode previousFogMode;
        public void ApplyEnvironment()
        {
            if (environmentApplied) return;
            environmentApplied = true;
            previousShadowResolution = QualitySettings.shadowResolution;
            previousShadowProjection = QualitySettings.shadowProjection;
            previousShadowCascades = QualitySettings.shadowCascades;
            // Dense alpha-clipped foliage aliases badly in the race preset's single low-resolution map.
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 2;
            previousMaps = LightmapSettings.lightmaps; previousMode = LightmapSettings.lightmapsMode;
            previousSkybox = RenderSettings.skybox; previousAmbientMode = RenderSettings.ambientMode;
            previousAmbient = RenderSettings.ambientLight; previousSky = RenderSettings.ambientSkyColor;
            previousEquator = RenderSettings.ambientEquatorColor; previousGround = RenderSettings.ambientGroundColor;
            previousIntensity = RenderSettings.ambientIntensity; previousFog = RenderSettings.fog;
            previousFogColor = RenderSettings.fogColor; previousFogMode = RenderSettings.fogMode;
            previousFogDensity = RenderSettings.fogDensity; previousFogStart = RenderSettings.fogStartDistance; previousFogEnd = RenderSettings.fogEndDistance;
            RenderSettings.skybox = skybox; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = ambientIntensity; RenderSettings.fog = fog; RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode; RenderSettings.fogDensity = fogDensity; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            var maps = new LightmapData[lightmapColors.Length];
            for (int i = 0; i < maps.Length; i++) maps[i] = new LightmapData { lightmapColor = lightmapColors[i], lightmapDir = lightmapDirections[i], shadowMask = lightmapMasks[i] };
            LightmapSettings.lightmaps = maps; LightmapSettings.lightmapsMode = lightmapMode;
            for (int i = 0; i < mappedRenderers.Length; i++) if (mappedRenderers[i]) { mappedRenderers[i].lightmapIndex = lightmapIndices[i]; mappedRenderers[i].lightmapScaleOffset = lightmapOffsets[i]; }
        }
        public void RestoreEnvironment()
        {
            StopPlayback();
            if (!environmentApplied) return;
            environmentApplied = false;
            QualitySettings.shadowResolution = previousShadowResolution;
            QualitySettings.shadowProjection = previousShadowProjection;
            QualitySettings.shadowCascades = previousShadowCascades;
            LightmapSettings.lightmaps = previousMaps; LightmapSettings.lightmapsMode = previousMode;
            RenderSettings.skybox = previousSkybox; RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientLight = previousAmbient;
            RenderSettings.ambientSkyColor = previousSky; RenderSettings.ambientEquatorColor = previousEquator; RenderSettings.ambientGroundColor = previousGround;
            RenderSettings.ambientIntensity = previousIntensity; RenderSettings.fog = previousFog; RenderSettings.fogColor = previousFogColor;
            RenderSettings.fogMode = previousFogMode; RenderSettings.fogDensity = previousFogDensity; RenderSettings.fogStartDistance = previousFogStart; RenderSettings.fogEndDistance = previousFogEnd;
        }
    }

}
