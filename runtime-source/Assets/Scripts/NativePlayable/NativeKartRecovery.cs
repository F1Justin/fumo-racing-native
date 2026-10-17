using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-60)]
    public sealed class NativeKartRecovery : MonoBehaviour
    {
        public NativeKartRecoveryState State { get; } = new NativeKartRecoveryState();
        public bool Pending => State.Pending;
        public float FadeAlpha => fade ? fade.alpha : 0f;
        public bool FadeEnabled => fade && fade.gameObject.activeSelf;
        public int NetworkRecoveryEvents => networkRequests;
        public int TickCount { get; private set; }
        public Action TeleportRequested;
        public Func<bool> CanRequest;
        int networkRequests;
        string telemetryRecoveryOperation;
        CanvasGroup fade;
        NativeKartMotor frozenMotor;
        GameObject frozenEffect;
        public bool FrozenFall => frozenMotor;

        public bool RequestFrozen(GameObject effectPrefab)
        {
            if (!Request()) return false;
            if (effectPrefab)
            {
                frozenEffect = Instantiate(effectPrefab, transform.position, transform.rotation);
                NativeParticleQualityRegistry.RegisterHierarchy(frozenEffect);
            }
            frozenMotor = GetComponent<NativeKartMotor>();
            if (frozenMotor) frozenMotor.SetHitFrozen(true);
            NativeTelemetry.Event("vehicle.frozen-fall", "applied", detail: "entity=" + GetInstanceID());
            return true;
        }

        void ClearFrozenFall()
        {
            if (frozenEffect) Destroy(frozenEffect);
            frozenEffect = null;
            if (frozenMotor) frozenMotor.SetHitFrozen(false);
            frozenMotor = null;
        }

        public void Initialize(Action teleportRequested, bool localPlayer, Material imageMaterial)
        {
            TeleportRequested = teleportRequested;
            if(fade){fade.gameObject.SetActive(localPlayer);if(!localPlayer)fade.alpha=0;}
            if (!localPlayer || fade) return;
            var overlay = new GameObject("Original recovery fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            overlay.transform.SetParent(transform, false);
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            fade = overlay.GetComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.blocksRaycasts = false;
            var imageObject = new GameObject("Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(overlay.transform, false);
            var image = imageObject.GetComponent<Image>();
            image.color = Color.black;
            image.material = imageMaterial;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        }

        public bool Request(bool restartPending = false)
        {
            if(CanRequest!=null&&!CanRequest()){NativeTelemetry.Count("vehicle.recovery.ignored");return false;}
            if (!State.Request(restartPending)) {NativeTelemetry.Count("vehicle.recovery.already-pending");return false;}
            if (telemetryRecoveryOperation != null) NativeTelemetry.End(telemetryRecoveryOperation, "vehicle.recovery", "cancelled", "restarted");
            telemetryRecoveryOperation = NativeTelemetry.Begin("vehicle.recovery", "entity=" + GetInstanceID());
            PlayFade();
            return true;
        }
        void PlayFade()
        { if(fade && fade.gameObject.activeSelf){StopAllCoroutines();StartCoroutine(Fading());} }
        public void ApplyNetworkRecovery(int requests)
        {
            if(requests<=networkRequests)return;
            networkRequests=requests;PlayFade();
        }

        void Update()
        {
            if (Time.deltaTime <= 0f) return;
            TickCount++;
            if (State.Advance(Time.deltaTime))
            {
                TeleportRequested?.Invoke();
                ClearFrozenFall();
                NativeTelemetry.End(telemetryRecoveryOperation, "vehicle.recovery", TeleportRequested == null ? "ignored" : "completed", TeleportRequested == null ? "no-teleport-consumer" : "teleport-callback-returned");
                telemetryRecoveryOperation = null;
            }
        }

        public void ResetState()
        {
            CancelTelemetryRecovery("reset");
            StopAllCoroutines();
            ClearFrozenFall();
            State.Reset();
            networkRequests=0;
            TickCount = 0;
            if (fade) fade.alpha = 0f;
        }

        void OnDestroy() { ClearFrozenFall(); CancelTelemetryRecovery("destroyed"); }

        void CancelTelemetryRecovery(string reason)
        {
            if (telemetryRecoveryOperation == null) return;
            NativeTelemetry.End(telemetryRecoveryOperation, "vehicle.recovery", "cancelled", reason);
            telemetryRecoveryOperation = null;
        }

        IEnumerator Fading()
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 2f;
                fade.alpha = t;
                yield return null;
            }
            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / .33f;
                fade.alpha = 1f - Mathf.Clamp01(t);
                yield return null;
            }
            fade.alpha = 0f;
        }
    }
}
