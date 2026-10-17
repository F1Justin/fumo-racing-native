using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        bool discordMainPage, discordPointerHovered;
        public int DiscordAnimatorPageResetCount { get; private set; }
        public Animator DiscordAnimator => transforms.TryGetValue("menu2/Canvas/MainMenu/discordserverbutton", out var target) ? target.GetComponent<Animator>() : null;
        public void ResetDiscordPointerForPage(bool mainPage)
        {
            if (discordMainPage == mainPage) return;
            discordMainPage = mainPage; discordPointerHovered = false;
            SetActive("menu2/Canvas/MainMenu/tooltip", false);
            if (transforms.TryGetValue("menu2/Canvas/MainMenu/discordserverbutton", out var target)
                && target.TryGetComponent<Animator>(out var animator))
            { animator.Rebind(); DiscordAnimatorPageResetCount++; }
        }
        public bool PointerInside(string key, Vector2 screenPosition, out Rect guiRect)
        {
            guiRect = default;
            if (!transforms.TryGetValue(key, out var target) || !target.gameObject.activeInHierarchy) return false;
            var graphic = target.GetComponent<Graphic>();
            var canvas = graphic ? graphic.canvas : target.GetComponentInParent<Canvas>();
            if (!canvas || !canvas.enabled) return false;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && !camera) return false;
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; target.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            float scale = Screen.height / 600f;
            guiRect = new Rect(min.x / scale, (Screen.height - max.y) / scale, (max.x - min.x) / scale, (max.y - min.y) / scale);
            return RectTransformUtility.RectangleContainsScreenPoint(target, screenPosition, camera);
        }
        public void SetDiscordTooltip(bool visible)
        {
            ResetDiscordPointerForPage(true);
            const string page = "menu2/Canvas/MainMenu";
            const string key = page + "/tooltip";
            if (!transforms.TryGetValue(page, out var main) || !main.gameObject.activeInHierarchy) visible = false;
            const string button = page + "/discordserverbutton";
            if (discordPointerHovered != visible && transforms.TryGetValue(button, out var target)
                && target.TryGetComponent<Animator>(out var animator))
            {
                animator.SetTrigger(visible ? "hover" : "idle");
                discordPointerHovered = visible;
            }
            SetActive(key, visible);
            if (visible)
            {
                // Show restores authored activeSelf every frame, so hover restores the tooltip subtree in the same draw.
                foreach (var pair in transforms)
                    if (pair.Key.StartsWith(key + "/", System.StringComparison.Ordinal))
                        pair.Value.gameObject.SetActive(definitions[pair.Key].active);
            }
        }
    }
}
