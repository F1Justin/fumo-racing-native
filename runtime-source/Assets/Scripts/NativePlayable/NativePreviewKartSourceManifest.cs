using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativePreviewKartSourceManifest : MonoBehaviour
    {
        public string sourceIdentity;
        public Renderer[] renderers;
        public string[] rendererIdentities;
        public int[] lightProbeUsage, reflectionProbeUsage;
        public string[] componentIdentities;
        public int[] targetCounts;
        public int rootOwnerCount;
        public bool selectionContext;
    }
}
