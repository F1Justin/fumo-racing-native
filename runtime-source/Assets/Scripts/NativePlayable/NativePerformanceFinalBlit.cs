using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Fumo.NativePlayable
{
    // Installed only by the explicit performance experiment, never normal gameplay.
    public sealed class NativePerformanceFinalBlit : MonoBehaviour
    {
        readonly Dictionary<Camera, PostProcessLayer> layers = new Dictionary<Camera, PostProcessLayer>();
        void OnEnable() { Camera.onPreCull += Apply; }
        void OnDisable() { Camera.onPreCull -= Apply; layers.Clear(); }
        void Apply(Camera camera)
        {
            if (!layers.TryGetValue(camera, out var layer) || !layer)
            {
                if (!camera.TryGetComponent(out layer)) return;
                layers[camera] = layer;
            }
            if (!layer.finalBlitToCameraTarget) layer.finalBlitToCameraTarget = true;
        }
    }
}
