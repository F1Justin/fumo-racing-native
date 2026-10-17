using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-2000)]
    public sealed class NativeTelemetryRuntime : MonoBehaviour
    {
        float nextHealth, sum, maximum;
        int frames, longFrames;
        [Serializable] sealed class UploadConfiguration { public int schemaVersion; public string endpoint; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!NativeTelemetry.Available) return;
            var owner = new GameObject("Native telemetry"); DontDestroyOnLoad(owner); owner.AddComponent<NativeTelemetryRuntime>();
        }
        void Awake()
        {
            NativeTelemetry.Metadata("platform", Application.platform.ToString());
            NativeTelemetry.Metadata("version", Application.version);
            NativeTelemetry.Metadata("unityVersion", Application.unityVersion);
            NativeTelemetry.Metadata("releaseProfile", NativeReleaseFeatures.Profile);
            NativeTelemetry.Metadata("graphicsApi", SystemInfo.graphicsDeviceType.ToString());
            NativeTelemetry.Metadata("gpu", SystemInfo.graphicsDeviceName);
            NativeTelemetry.Metadata("os", SystemInfo.operatingSystem);
            NativeTelemetry.Metadata("architecture", (IntPtr.Size * 8).ToString());
            NativeTelemetry.Metadata("screen", Screen.width + "x" + Screen.height);
            try
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FUMO_TELEMETRY_ENDPOINT")))
                {
                    var asset = NativeContent.Load<TextAsset>("NativeTelemetry/config");
                    if (asset) { var config = JsonUtility.FromJson<UploadConfiguration>(asset.text); if (config != null && config.schemaVersion == 1) NativeTelemetry.ConfigureUpload(config.endpoint); }
                }
            }
            catch { NativeTelemetry.Event("telemetry.config", "ignored", "invalidUploadConfiguration"); }
            NativeTelemetry.SetContext("sceneId", SceneManager.GetActiveScene().name);
            NativeTelemetry.Event("app.identity", "completed", null, "gpu=" + SystemInfo.graphicsDeviceName + ";api=" + SystemInfo.graphicsDeviceType + ";os=" + SystemInfo.operatingSystem + ";architecture=" + (IntPtr.Size * 8));
            NativeTelemetry.Event("app.runtime.ready", "completed", null, "gpu=" + SystemInfo.graphicsDeviceName + ";api=" + SystemInfo.graphicsDeviceType);
            SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded;
            nextHealth = Time.realtimeSinceStartup + 1;
        }
        int generation;
        void Loaded(Scene scene, LoadSceneMode mode)
        {
            NativeTelemetry.SetContext("sceneId", scene.name); NativeTelemetry.SetContext("sceneGeneration", (++generation).ToString());
            NativeTelemetry.Event("scene.loaded", "completed", null, scene.name);
        }
        void Unloaded(Scene scene) { NativeTelemetry.Event("scene.unloaded", "completed", null, scene.name); }
        void Update()
        {
            float dt = Time.unscaledDeltaTime; frames++; sum += dt; maximum = Math.Max(maximum, dt); if (dt > .05f) longFrames++;
            if (Time.realtimeSinceStartup < nextHealth) return;
            nextHealth = Time.realtimeSinceStartup + 1;
            NativeTelemetry.Metric("frame.meanMs", frames > 0 ? sum / frames * 1000 : 0);
            NativeTelemetry.Metric("frame.maxMs", maximum * 1000); NativeTelemetry.Count("frame.longCount", longFrames);
            NativeTelemetry.Metric("gc.collections", GC.CollectionCount(0)); NativeTelemetry.Metric("memory.managedBytes", GC.GetTotalMemory(false));
            NativeTelemetry.SetContext("focused", Application.isFocused ? "true" : "false");
            NativeTelemetry.Metric("time.scale", Time.timeScale); NativeTelemetry.Health(); frames = longFrames = 0; sum = maximum = 0;
        }
        void OnApplicationFocus(bool focused) { NativeTelemetry.SetContext("focused", focused ? "true" : "false"); NativeTelemetry.Event("app.focus.changed", "applied"); }
        void OnApplicationPause(bool paused) { NativeTelemetry.SetContext("backgrounded", paused ? "true" : "false"); NativeTelemetry.Event("app.pause.changed", "applied"); }
        void OnApplicationQuit() { NativeTelemetry.Stop(); }
        void OnDestroy() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded; }
    }
}
