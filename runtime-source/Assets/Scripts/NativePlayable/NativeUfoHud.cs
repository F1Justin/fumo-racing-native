using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeUfoHud : MonoBehaviour
    {
        public NativeUfoPlayer player;
        public RectTransform previewNode;
        public RectTransform[] slotNodes;
        public NativeKartItemHud.ImageData[] images;
        public Material imageMaterial;
        [SerializeField] bool prefabAuthored;
        public Color emptyColor = new Color(0, 0, 0, .4f);
        public float pulseScale = 3, pulseDuration = .35f;
        Vector3[] authoredSlotScales;
        Canvas canvas;
        Image preview;
        Image[] slots;
        int seenCollected;
        float pulseStart;
        int pulseIndex = -1;
        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (!prefabAuthored)
            {
            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(800, 600);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 1;
            foreach (var data in images)
            {
                var image = data.node.gameObject.GetComponent<Image>() ?? data.node.gameObject.AddComponent<Image>(); image.sprite = data.sprite; image.color = data.color;
                image.type = (Image.Type)data.type; image.preserveAspect = data.preserveAspect; image.material = imageMaterial; image.raycastTarget = false;
            }
            }
            authoredSlotScales = new Vector3[slotNodes.Length];
            for (int i = 0; i < slotNodes.Length; i++) authoredSlotScales[i] = slotNodes[i].localScale;
            preview = previewNode.GetComponent<Image>(); slots = new Image[slotNodes.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = slotNodes[i].GetComponent<Image>();
        }
        public void SetVisible(bool visible) { if (!canvas) canvas = GetComponent<Canvas>(); canvas.enabled = visible; }
        void LateUpdate()
        {
            if (!player || player.State == null || slots == null) return;
            var state = player.State;
            if (seenCollected != state.collected) { seenCollected = state.collected; pulseIndex = state.count - 1; pulseStart = Time.time; }
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].gameObject.SetActive(player.active && state.count > 0);
                slots[i].color = i < state.count ? player.catalog.colors[state.colors[i]] : emptyColor;
                float scale = i == pulseIndex ? Mathf.Lerp(pulseScale, 1f, Mathf.Clamp01((Time.time - pulseStart) / Mathf.Max(.001f, pulseDuration))) : 1f;
                slotNodes[i].localScale = (prefabAuthored ? authoredSlotScales[i] : Vector3.one) * scale;
            }
            preview.gameObject.SetActive(player.active && state.count > 0 && player.PreviewIcon);
            preview.sprite = player.PreviewIcon;
        }
    }
}
