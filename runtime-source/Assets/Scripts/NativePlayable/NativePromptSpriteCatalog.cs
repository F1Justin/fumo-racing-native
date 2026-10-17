using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed class NativePromptSpriteCatalog : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string sourcePath, sourceGuid, sourceSHA256, spriteName;
            public Sprite sprite;
        }
        public Entry[] entries = Array.Empty<Entry>();
        public Sprite Find(string key)
        {
            foreach (var entry in entries) if (entry.spriteName == key) return entry.sprite;
            return null;
        }
    }
}
