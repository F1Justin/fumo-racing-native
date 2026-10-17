using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeUiCanvas
    {
        CanvasGroup opacity;
        public void SetOpacity(float value)
        {
            if (!opacity) opacity = root.AddComponent<CanvasGroup>();
            opacity.alpha = Mathf.Clamp01(value);
        }
    }
}
