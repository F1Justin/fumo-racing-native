using System;
using UnityEngine;
namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeLocalViewsSource
    {
        [Serializable] public sealed class SourceHash { public string path, sha256; }
        [Serializable] public sealed class Viewport
        { public int count, index; public float x, y, width, height; public Rect Value => new Rect(x,y,width,height); }
        public int schemaVersion, cameraMask, cameraDepth, cameraTargetDisplay, cameraTargetTextureFileId;
        public int hudRenderMode, hudSortingOrder, scalerMode, scalerScreenMatchMode;
        public float cameraNear, cameraFar, cameraFov, hudSerializedPlaneDistance, hudRuntimePlaneDistance;
        public float scalerMatch, referenceWidth, referenceHeight, referencePixelsPerUnit;
        public SourceHash[] sourceHashes;
        public Viewport[] viewports;
    }
}
