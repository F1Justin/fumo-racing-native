using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeSelectionPreviewPlayerDefinition
    {
        public int humanIndex;
        public string playerBoxPath, rawImagePath, sourcePlayerBoxIdentity, sourceCameraIdentity,
            sourcePivotIdentity, sourceRawImageIdentity, sourceBaseRTIdentity, sourceCameraPath, sourcePivotPath;
        public Vector3 cameraPosition, cameraScale, pivotScale;
        public Quaternion cameraRotation, pivotRotation;
        public float fieldOfView, near, far, rotationSpeed, cameraDepth;
        public int width, height, depthBits, cullingMask;
        public CameraClearFlags clearFlags;
        public Color background;
        public RenderTextureFormat format;
        public RenderTextureReadWrite readWrite;
    }
}
