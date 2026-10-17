using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeMenuSpriteData : ScriptableObject
    {
        public Sprite defaultButton, selectedButton, labelMask;
        static NativeMenuSpriteData data;
        public static NativeMenuSpriteData Source => data ? data : data = NativeContent.Load<NativeMenuSpriteData>("NativeMenu/OriginalButtonSprites");
        public static Sprite At(bool selected)
        {
            if (!Source) throw new InvalidOperationException("Original menu button Sprite resource missing");
            return selected ? Source.selectedButton : Source.defaultButton;
        }
    }
}
