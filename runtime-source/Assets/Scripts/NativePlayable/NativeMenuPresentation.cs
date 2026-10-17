using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fumo.NativePlayable
{
    public sealed class NativeMenuPresentation : MonoBehaviour
    {
        public GameObject content;
        public NativeMenuRandomDisplay randomDisplay;
        public Camera menuCamera, raceCamera;
        public Light[] raceLights;
        public bool[] raceLightEnabled;
        public NativeMenuEnvironment menuEnvironment, raceEnvironment;
        public AudioClip menuMusic, raceMusic;
        [NonSerialized] public AudioSource soundtrack;
        bool applied, visible;
        bool selectionVisible;
        Camera selectionScreenCamera;
        GameObject selectionBackground;
        const int SelectionBackgroundLayer = 30;

        public bool IsVisible => applied && visible;

        public void SetVisible(bool show)
        {
            if (!show && selectionVisible) SetSelectionVisible(false, null);
            if (applied && visible == show) return;
            visible = show;
            applied = true;
            if (Application.isPlaying && show && randomDisplay) randomDisplay.Populate();
            content.SetActive(show);
            menuCamera.enabled = show;
            raceCamera.enabled = !show;
            for (int i = 0; i < raceLights.Length; i++)
                if (raceLights[i]) raceLights[i].enabled = !show && raceLightEnabled[i];
            (show ? menuEnvironment : raceEnvironment).Apply();
            if (Application.isPlaying && soundtrack)
            {
                var clip = show ? menuMusic : raceMusic;
                if (soundtrack.clip != clip)
                {
                    soundtrack.clip = clip;
                    if (clip && soundtrack.enabled) soundtrack.Play();
                }
            }
            if (Application.isPlaying) Debug.Log("NATIVE_MENU_VISIBLE " + show);
        }
        public void SetSelectionVisible(bool show, NativeSelectionPreviewDefinition selection)
        {
            if (selectionVisible == show) return;
            selectionVisible = show;
            if (show)
            {
                content.SetActive(false);
                menuCamera.enabled = false;
                if (!selectionScreenCamera)
                    selectionScreenCamera = new GameObject("Original selection screen camera").AddComponent<Camera>();
                selectionScreenCamera.clearFlags = selection.clearFlags; selectionScreenCamera.backgroundColor = selection.background;
                selectionScreenCamera.fieldOfView = selection.fieldOfView; selectionScreenCamera.cullingMask = 1 << SelectionBackgroundLayer;
                if (!selectionBackground)
                {
                    var sourceBackground = NativeContent.Load<GameObject>("NativeSelectionPreview/screen-background");
                    selectionBackground = Instantiate(sourceBackground);
                    selectionBackground.name = "Original selection background";
                    foreach (var node in selectionBackground.GetComponentsInChildren<Transform>(true))
                        node.gameObject.layer = SelectionBackgroundLayer;
                }
                selectionBackground.SetActive(true);
                selectionScreenCamera.transform.SetPositionAndRotation(selection.screenCameraPosition, selection.screenCameraRotation);
                selectionScreenCamera.transform.localScale = selection.screenCameraScale;
                selectionScreenCamera.nearClipPlane = selection.screenCameraNear;
                selectionScreenCamera.farClipPlane = selection.screenCameraFar;
                selectionScreenCamera.depth = selection.screenCameraDepth;
                selectionScreenCamera.enabled = true;
                selection.environment.Apply();
            }
            else
            {
                content.SetActive(visible);
                if (selectionScreenCamera) selectionScreenCamera.enabled = false;
                if (selectionBackground) selectionBackground.SetActive(false);
                menuCamera.enabled = visible;
                if (visible) menuEnvironment.Apply();
            }
        }
        public bool SelectionVisible => selectionVisible;
        public Camera SelectionScreenCamera => selectionScreenCamera;
        public GameObject SelectionBackground => selectionBackground;
    }

    [Serializable]
    public sealed class NativeMenuEnvironment
    {
        public AmbientMode ambientMode;
        public Color ambient, sky, equator, ground, fogColor, subtractiveShadow;
        public float ambientIntensity, fogDensity, fogStart, fogEnd, reflectionIntensity;
        public bool fog;
        public FogMode fogMode;
        public Material skybox;
        public Light sun;
        public DefaultReflectionMode reflectionMode;
        public Texture reflection;
        public float[] ambientProbe;

        public static NativeMenuEnvironment Capture()
        {
            var value = new NativeMenuEnvironment
            {
                ambientMode = RenderSettings.ambientMode,
                ambient = RenderSettings.ambientLight,
                sky = RenderSettings.ambientSkyColor,
                equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                fog = RenderSettings.fog,
                fogMode = RenderSettings.fogMode,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                skybox = RenderSettings.skybox,
                sun = RenderSettings.sun,
                subtractiveShadow = RenderSettings.subtractiveShadowColor,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                reflectionMode = RenderSettings.defaultReflectionMode,
                reflection = RenderSettings.customReflectionTexture,
                ambientProbe = new float[27]
            };
            var probe = RenderSettings.ambientProbe;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    value.ambientProbe[channel * 9 + coefficient] = probe[channel, coefficient];
            return value;
        }

        public void Apply()
        {
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.skybox = skybox;
            RenderSettings.sun = sun;
            RenderSettings.subtractiveShadowColor = subtractiveShadow;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.defaultReflectionMode = reflectionMode;
            RenderSettings.customReflectionTexture = reflection;
            var probe = new SphericalHarmonicsL2();
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    probe[channel, coefficient] = ambientProbe[channel * 9 + coefficient];
            RenderSettings.ambientProbe = probe;
        }
    }
}
