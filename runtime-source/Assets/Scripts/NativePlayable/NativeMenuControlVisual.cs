using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Both states are authored assets. The idle sprite/color live on the Image.
    [DisallowMultipleComponent]
    public sealed class NativeMenuControlVisual : MonoBehaviour
    {
        public Image target;
        public Sprite selectedSprite;
        Sprite idleSprite;
        bool captured;
        public void Apply(bool selected)
        {
            if (!captured) { idleSprite = target.sprite; captured = true; }
            target.overrideSprite = null;
            target.sprite = selected ? selectedSprite : idleSprite;
        }
    }
}
