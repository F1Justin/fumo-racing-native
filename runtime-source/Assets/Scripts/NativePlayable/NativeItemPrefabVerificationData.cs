using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemPrefabVerificationData : ScriptableObject
    {
        public const string ResourcePath = "NativeVerification/ItemPrefabBehaviors";
        public const int SourceRecordCount = 53;
        public NativeItemPrefabRuntimeVerification.Reference source;
        public NativeItemPrefabRuntimeVerification.Binding[] bindings;
        public string baselineSHA256;
        public TextAsset sourceBaseline;
        public SourceResource[] sourceResources;

        [Serializable] public sealed class SourceResource
        {
            public string expectedIdentity, sourceGuid, sourcePath;
            public GameObject effectPrefab;
            public AudioClip audioClip;
        }
    }
}
