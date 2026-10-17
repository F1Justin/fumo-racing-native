using System;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartItemHud : MonoBehaviour
    {
        [Serializable] public sealed class ImageData
        {
            public RectTransform node;
            public Sprite sprite;
            public Color color;
            public int type;
            public bool preserveAspect, mask;
        }
        [Serializable] public sealed class CurveData
        {
            public RectTransform node;
            public string property;
            public AnimationCurve curve;
        }
        [Serializable] public sealed class ClipData
        {
            public string name;
            public float length;
            public bool loop;
            public CurveData[] curves;
        }
        public NativeKartItemInventory inventory;
        public GameObject panel;
        public RectTransform iconNode, characterIconNode;
        public Material imageMaterial;
        public ImageData[] images;
        public ClipData[] clips;
        public Sprite[] rouletteIcons;
        public float randomRate = 0.05f;
        [NonSerialized] public Image icon, characterIcon;
        public string CurrentState { get; private set; }
        [SerializeField] bool prefabAuthored;
        readonly System.Collections.Generic.Dictionary<RectTransform, Vector3> authoredPositions = new System.Collections.Generic.Dictionary<RectTransform, Vector3>();
        readonly System.Collections.Generic.Dictionary<RectTransform, Vector3> authoredScales = new System.Collections.Generic.Dictionary<RectTransform, Vector3>();
        Canvas canvas;
        public bool LocalRaceFinished { get; private set; }
        public void FreezeForLocalFinisher() {LocalRaceFinished=true;if(panel){var group=panel.GetComponent<CanvasGroup>();if(!group)group=panel.AddComponent<CanvasGroup>();group.alpha=0;}}
        int seenGive, seenDecide, seenDone;
        float nextIconTime, animationStart;
        int iconIndex;
        ClipData activeClip;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (!prefabAuthored)
            {
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (!scaler) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800, 600);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            foreach (var data in images)
            {
                var image = data.node.gameObject.GetComponent<Image>();
                if (!image) image = data.node.gameObject.AddComponent<Image>();
                image.sprite = data.sprite;
                image.color = data.color;
                image.type = (Image.Type)data.type;
                image.preserveAspect = data.preserveAspect;
                image.material = imageMaterial;
                image.raycastTarget = false;
                if (data.mask && !data.node.GetComponent<Mask>()) data.node.gameObject.AddComponent<Mask>();
            }
            }
            foreach (var clip in clips) foreach (var binding in clip.curves)
            { authoredPositions[binding.node] = binding.node.anchoredPosition; authoredScales[binding.node] = binding.node.localScale; }
            icon = iconNode.GetComponent<Image>();
            characterIcon = characterIconNode.GetComponent<Image>();
            ResetState();
        }

        public void BindWholeScreen()
        {
            if (!canvas) canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
        }
        public void BindLocalCamera(Camera camera)
        {
            if (!canvas) canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = 1; canvas.sortingOrder = 113; canvas.targetDisplay = 0;
        }
        public void SetVisible(bool visible)
        {
            if (!canvas) canvas = GetComponent<Canvas>();
            canvas.enabled = visible;
        }

        public void ResetState()
        {
            LocalRaceFinished=false;
            if(panel&&panel.GetComponent<CanvasGroup>())panel.GetComponent<CanvasGroup>().alpha=1;
            seenGive = seenDecide = seenDone = 0;
            nextIconTime = 0f;
            activeClip = null;
            CurrentState = "";
            if (characterIcon) characterIcon.enabled = false;
            panel.SetActive(false);
        }

        void Play(string state)
        {
            panel.SetActive(true);
            CurrentState = state;
            activeClip = Array.Find(clips, clip => clip.name == state);
            animationStart = Time.time;
        }

        void LateUpdate()
        {
            if (!inventory || Time.timeScale <= 0f) return;
            if (inventory.GiveCount != seenGive)
            {
                Play("itemidle");
                iconIndex = UnityEngine.Random.Range(0, rouletteIcons.Length);
                nextIconTime = Time.time;
            }
            if (inventory.HasItem && !inventory.ItemDecided && Time.time >= nextIconTime && rouletteIcons.Length > 0)
            {
                icon.sprite = rouletteIcons[iconIndex++ % rouletteIcons.Length];
                NativeAudioRuntime.PlayEvent("item.roulette", transform, UnityEngine.Random.Range(0.9f, 1.25f));
                nextIconTime = Time.time + randomRate;
            }
            if (inventory.HasItem && inventory.ItemDecided)
            {
                icon.sprite = inventory.CurrentIcon;
                if (inventory.DecideCount != seenDecide)
                {
                    string characterId = inventory.catalog.Find(inventory.CurrentId)?.characterConditionId;
                    var race = FindObjectOfType<NativeRace>();
                    var roster = race ? race.vehicleRoster : null;
                    int index = roster && !string.IsNullOrEmpty(characterId)
                        ? roster.CharacterIndexOf(characterId) : -1;
                    characterIcon.enabled = index >= 0;
                    if (index >= 0) characterIcon.sprite = roster.CharacterIconAt(index);
                    NativeAudioRuntime.PlayEvent("item.decided", transform);
                    Play("decidedItem");
                }
            }
            if (inventory.DoneCount != seenDone && !inventory.HasItem)
            {
                Play("itemused");
                characterIcon.enabled = false;
            }
            seenGive = inventory.GiveCount;
            seenDecide = inventory.DecideCount;
            seenDone = inventory.DoneCount;
            if (activeClip == null) return;
            float time = Time.time - animationStart;
            time = activeClip.loop && activeClip.length > 0f ? time % activeClip.length : Mathf.Min(time, activeClip.length);
            foreach (var binding in activeClip.curves) Apply(binding, binding.curve.Evaluate(time));
        }

        void Apply(CurveData binding, float value)
        {
            string property = binding.property;
            int axis = property.EndsWith(".x") ? 0 : property.EndsWith(".y") ? 1 : 2;
            if (property.StartsWith("m_LocalScale."))
            { var v = binding.node.localScale; v[axis] = prefabAuthored ? authoredScales[binding.node][axis] * value : value; binding.node.localScale = v; }
            else if (property.StartsWith("localEulerAnglesRaw."))
            { var v = binding.node.localEulerAngles; v[axis] = value; binding.node.localEulerAngles = v; }
            else if (property.StartsWith("m_AnchoredPosition."))
            { var v = binding.node.anchoredPosition; v[axis] = prefabAuthored ? authoredPositions[binding.node][axis] + value - binding.curve.keys[0].value : value; binding.node.anchoredPosition = v; }
            else if (property == "m_Color.a")
            { var image = binding.node.GetComponent<Image>(); var color = image.color; color.a = value; image.color = color; }
        }
    }
}
