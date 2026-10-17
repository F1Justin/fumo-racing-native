using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Authored form geometry belongs entirely to the Prefab. Only focus changes the frame sprite.
    public sealed class NativeOnlineProfileView : MonoBehaviour
    {
        public TMP_InputField nameField;
        public Image nameFrame;
        public Sprite normalFrame, focusedFrame;
        public void Present(bool selected)
        {
            if (nameFrame) nameFrame.sprite = selected || (nameField && nameField.isFocused) ? focusedFrame : normalFrame;
            foreach (var element in GetComponentsInChildren<NativeOnlineElement>(true))
            {
                if (element.key != "profile-save" && element.key != "profiles-back") continue;
                var prompt = element.GetComponent<NativeInputPrompt>();
                if (prompt)
                {
                    prompt.binding.action = element.key == "profile-save" ? nameField && nameField.isFocused ? "Ok" : "Use" : "Cancel";
                    prompt.binding.keyboardControl = element.key == "profile-save" ? "space" : "escape";
                }
            }
        }
    }
}
