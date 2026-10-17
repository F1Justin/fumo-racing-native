using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAudioRuntime
    {
        internal static bool UiVerificationPlayback { get; set; }
        internal static event Action<AudioSource, AudioClip> UiOneShotPlayed;
        internal static void ObserveUiOneShot(AudioSource source, AudioClip clip) => UiOneShotPlayed?.Invoke(source, clip);
    }
}
