using System;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeSplashAudioProducer : MonoBehaviour
    {
        public AudioSource source;
        public float duration;
        public string originalTargetLevel;
        public Canvas canvas;
        public RectTransform logoRect, textRect;
        public string scalerJson, logoJson, textJson;
        public Sprite logoSprite;
        public NativeUiFontData textFont;
        public Material textMaterial;
        public AnimationCurve logoAlpha, textAlpha;
        public Color sourceCameraBackground;
        Image logo;
        TMP_Text text;
        Action completed, finished;
        float started;
        float skipStarted, skipVolume;
        bool fadingSkip;
        Image fadeOverlay;
        GameObject fadeLayer;
        const float SkipFadeDuration = .25f;
        public bool IsRunning { get; private set; }
        public bool MenuRevealed { get; private set; }
        public bool Nonrendered { get; private set; }
        public bool WasSkipped { get; private set; }
        public int PlayCount { get; private set; }
        public int BeginCount { get; private set; }
        public int CompletionCount { get; private set; }
        public float Elapsed => IsRunning ? Time.time - started : duration;

        public void Begin(Action onComplete, Action onFinished = null)
        {
            if (IsRunning || BeginCount != 0) throw new InvalidOperationException("Splash producer already started");
            if (!source || !source.clip || !canvas || !logoRect || !textRect || textFont == null || duration <= 0)
                throw new InvalidOperationException("Original splash source capture is incomplete");
            completed = onComplete;
            finished = onFinished;
            Nonrendered = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (!Nonrendered)
            {
                var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                JsonUtility.FromJsonOverwrite(scalerJson, scaler);
                logo = logoRect.gameObject.AddComponent<Image>();
                JsonUtility.FromJsonOverwrite(WithoutEditorReferences(logoJson), logo);
                logo.sprite = logoSprite;
                logo.material = null;
                logo.raycastTarget = false;
                text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
                JsonUtility.FromJsonOverwrite(WithoutEditorReferences(textJson), text);
                NativeChineseFont.Apply(text,textFont,textMaterial);
                if(NativeLocalization.IsChinese)text.text=NativeLocalization.Get("NATIVE_SPLASH_NOTICE",text.text);
                text.spriteAsset = null;
                text.raycastTarget = false;
            }
            source.playOnAwake = false;
            NativeAudioRuntime.Route(source, 1);
            BeginCount++;
            if (!NativeAudioRuntime.Muted)
            {
                source.enabled = true;
                source.Play();
                PlayCount++;
            }
            started = Time.time;
            IsRunning = true;
            ApplyVisuals(0);
            Debug.Log("NATIVE_SPLASH_STARTED duration=" + duration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Invoke(nameof(EndSplash), duration);
        }

        public void Skip()
        {
            if (!IsRunning || fadingSkip) return;
            WasSkipped = true;
            CancelInvoke(nameof(EndSplash));
            fadingSkip = true;
            skipStarted = Time.unscaledTime;
            skipVolume = source.volume;
            if (!Nonrendered)
            {
                var layer = new GameObject("Splash Fade", typeof(RectTransform), typeof(Canvas));
                fadeLayer = layer;
                var fadeCanvas = layer.GetComponent<Canvas>();
                fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                fadeCanvas.sortingOrder = short.MaxValue;
                var overlay = new GameObject("Black", typeof(RectTransform), typeof(Image));
                overlay.transform.SetParent(layer.transform, false);
                var rect = overlay.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                fadeOverlay = overlay.GetComponent<Image>();
                fadeOverlay.raycastTarget = false;
                fadeOverlay.color = new Color(0, 0, 0, 0);
            }
        }

        void LateUpdate()
        {
            if (!IsRunning) return;
            if (!fadingSkip) { ApplyVisuals(Elapsed); return; }
            float elapsed = Time.unscaledTime - skipStarted;
            if (elapsed < SkipFadeDuration)
            {
                float alpha = Mathf.SmoothStep(0, 1, elapsed / SkipFadeDuration);
                if (fadeOverlay) fadeOverlay.color = new Color(0, 0, 0, alpha);
                source.volume = skipVolume * (1 - alpha);
                return;
            }
            if (!MenuRevealed)
            {
                if (fadeOverlay) fadeOverlay.color = Color.black;
                canvas.enabled = false;
                RevealMenu();
                return;
            }
            float progress = (elapsed - SkipFadeDuration) / SkipFadeDuration;
            if (fadeOverlay) fadeOverlay.color = new Color(0, 0, 0, 1 - Mathf.SmoothStep(0, 1, progress));
            if (progress >= 1) EndSplash();
        }
        static string WithoutEditorReferences(string json)
        {
            // Export-time instance IDs belong to the editor process; assets are restored from prefab references.
            return Regex.Replace(json, "(\"instanceID\"\\s*:\\s*)-?\\d+", "${1}0");
        }
        void ApplyVisuals(float elapsed)
        {
            if (Nonrendered) return;
            var color = logo.color;
            color.a = logoAlpha.Evaluate(elapsed);
            logo.color = color;
            color = text.color;
            color.a = textAlpha.Evaluate(elapsed);
            text.color = color;
        }
        void EndSplash()
        {
            if (!IsRunning) return;
            IsRunning = false;
            RevealMenu();
            CompletionCount++;
            var finish = finished;
            finished = null;
            finish?.Invoke();
            Debug.Log("NATIVE_SPLASH_COMPLETED skipped=" + WasSkipped + " elapsed="
                + (Time.time - started).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " completion=" + CompletionCount);
            if (fadeLayer) Destroy(fadeLayer);
            gameObject.SetActive(false);
        }
        void RevealMenu()
        {
            if (MenuRevealed) return;
            MenuRevealed = true;
            source.Stop();
            completed?.Invoke();
            completed = null;
        }
        void OnDestroy() { CancelInvoke(nameof(EndSplash)); if (fadeLayer) Destroy(fadeLayer); }
    }
}
