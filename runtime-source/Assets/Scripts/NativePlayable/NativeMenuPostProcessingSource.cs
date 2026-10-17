using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeMenuPostProcessingVolumeSource
    {
        public string path, profilePath, profileSha256, profileBase64;
        public bool active, enabled, isGlobal;
        public float blendDistance, weight, priority;
    }
    public sealed class NativeMenuPostProcessingSource : MonoBehaviour
    {
        public string layerJson;
        public NativeMenuPostProcessingVolumeSource[] volumes;
    }
}
