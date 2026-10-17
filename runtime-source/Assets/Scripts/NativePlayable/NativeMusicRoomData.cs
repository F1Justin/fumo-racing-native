using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeMusicRoomData : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string id, title, credits; public Sprite cover; public AudioClip clip; }
        public Entry[] entries;
        static NativeMusicRoomData data;
        public static NativeMusicRoomData Source => data ? data : data = NativeContent.Load<NativeMusicRoomData>("NativeMenu/MusicRoom");
        public static int Count => Source ? Source.entries.Length : 0;
        public static Entry At(int index) => Count == 0 ? null : Source.entries[(index % Count + Count) % Count];
    }
}
