using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [CreateAssetMenu(menuName = "THFR/Selection Preview Settings")]
    public sealed class NativeSelectionPreviewSettings : ScriptableObject
    {
        public const string ResourcePath = "NativeOnline/SelectionPreviewSettings";
        public NativeSelectionPreviewDefinition definition;
        public static NativeSelectionPreviewDefinition Load()
        {
            var settings = NativeContent.Load<NativeSelectionPreviewSettings>(ResourcePath);
            if (!settings || settings.definition == null)
                throw new InvalidOperationException("SelectionPreviewSettings.asset missing; run the Editor-first migration");
            return settings.definition;
        }
    }
}
