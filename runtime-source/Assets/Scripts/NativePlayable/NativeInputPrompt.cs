using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // The Prefab owns geometry and graphics. This component changes only the bound glyph/text state.
    [DefaultExecutionOrder(1000)]
    public sealed class NativeInputPrompt : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public string action = "Ok";
            public int player = -1;
            public Image icon;
            public Image[] alternateIcons = Array.Empty<Image>();
            public TMP_Text caption;
            public NativePromptSpriteCatalog catalog;
            [NonSerialized] public string keyboardControl;
            [NonSerialized] string lastCaption, plainCaption;
            public void Present()
            {
                if (!icon || !icon.transform.parent.gameObject.activeInHierarchy) return;
                int human = player < 0 ? NativeInputBindings.PromptPlayer : player;
                var device = NativeInputBindings.PromptDevice(human);
                var state = NativeMainPromptSource.Read(action, human, device, catalog, keyboardControl);
                icon.gameObject.SetActive(true); icon.enabled = state.spriteFound;
                icon.sprite = state.sprite;
                foreach (var alternate in alternateIcons) if (alternate) alternate.gameObject.SetActive(false);
                if (!caption) return;
                if (caption.text != lastCaption) plainCaption = caption.text;
                string value = state.spriteFound || string.IsNullOrEmpty(state.displayBinding) ? plainCaption
                    : "[" + state.displayBinding + "] " + plainCaption;
                if (caption.text != value) caption.text = value;
                lastCaption = value;
            }
        }
        public Binding binding = new Binding();
        void LateUpdate() => binding.Present();
        public static string ActionFor(string path, string phrase)
        {
            if (path.Contains("MusicRoom/") && phrase == "PROMPT_PAUSE") return "Use";
            if (phrase == "PROFILES_DELETE") return "DeleteProfile";
            if (phrase == "PROMPT_PROFILE") return "Use";
            if (phrase == "PAUSE_RESUME") return "Pause";
            if (phrase == "PROMPT_SELECT" || phrase == "RESULTS_PROMPT_CONTINUE" || phrase == "SELECTION_JOIN") return "Ok";
            if (phrase == "PROMPT_GOBACK" || phrase == "PROMPT_EXIT") return "Cancel";
            return null;
        }
        public static Binding Bind(Transform root, string action, NativePromptSpriteCatalog catalog)
        {
            var images = root.GetComponentsInChildren<Image>(true).Where(image => image.name.StartsWith("icon", StringComparison.Ordinal)).ToArray();
            if (images.Length == 0) return null;
            var primary = images.FirstOrDefault(image => image.name == "icon") ?? images[0];
            return new Binding { action = action, icon = primary, alternateIcons = images.Where(image => image != primary).ToArray(),
                caption = root.GetComponent<TMP_Text>(), catalog = catalog };
        }
    }
}
