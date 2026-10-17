using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativePreviewKartProperties : MonoBehaviour
    {
        public Renderer[] targets;
        public string[] targetIdentities;
        public int[] targetLightProbeUsage, targetReflectionProbeUsage;
        public bool sourceEnabled, selectionOwner, sourceHasVehicleCharacter;
        public bool menuStartApplied, lastApplyWasStart;
        public int selectionRefreshCount;
        public Color color, color2, dirtColor;
        public float heat, dirt, wheelDirt, wheelHeat, dirtColorEnabled;
        public string sourceIdentity;
        MaterialPropertyBlock block;
        public static NativePreviewKartProperties RefreshSelection(GameObject vehicle, Color first, Color second)
        {
            NativePreviewKartProperties owner = null;
            foreach (var component in vehicle.GetComponents<NativePreviewKartProperties>())
            {
                if (!component.selectionOwner) continue;
                if (owner) throw new InvalidOperationException("Duplicate original root CKC selection owner");
                owner = component;
            }
            var manifest = vehicle.GetComponent<NativePreviewKartSourceManifest>();
            if (!manifest) throw new InvalidOperationException("Original vehicle CKC manifest missing");
            if (manifest.rootOwnerCount != (owner ? 1 : 0)) throw new InvalidOperationException("Original root CKC owner count mismatch");
            manifest.selectionContext = true;
            if (owner) { owner.SetColors(first, second); owner.selectionRefreshCount++; }
            return owner;
        }
        public void SetColors(Color first, Color second)
        {
            color = first;
            Apply();
            color2 = second;
            Apply();
        }
        public static void InitializeMenuStarts(GameObject model)
        {
            foreach (var properties in model.GetComponentsInChildren<NativePreviewKartProperties>(true))
            {
                if (properties.menuStartApplied || !properties.sourceEnabled || !properties.gameObject.activeInHierarchy) continue;
                if (properties.sourceHasVehicleCharacter) throw new InvalidOperationException("Preview CKC speed producer requires source binding: " + properties.sourceIdentity);
                properties.ApplyParameters(0f, 0f);
                properties.menuStartApplied = true;
                properties.lastApplyWasStart = true;
            }
        }
        public void Apply()
        {
            ApplyParameters(heat, wheelHeat);
            lastApplyWasStart = false;
        }
        void ApplyParameters(float speed, float appliedWheelHeat)
        {
            if (targets == null) throw new InvalidOperationException("Original CKC targets missing: " + sourceIdentity);
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_Color2", color2);
            block.SetFloat("_SpeedIntensity", speed * 5f);
            block.SetFloat("_DirtAmount", dirt);
            block.SetFloat("_WheelDirtAmount", wheelDirt);
            block.SetFloat("_WheelHeatAmount", appliedWheelHeat);
            block.SetFloat("_DirtColorEnabled", dirtColorEnabled);
            block.SetColor("_DirtColor", dirtColor);
            foreach (var target in targets)
            {
                if (!target) throw new InvalidOperationException("Original CKC target unresolved: " + sourceIdentity);
                target.SetPropertyBlock(block);
            }
        }
    }
}
