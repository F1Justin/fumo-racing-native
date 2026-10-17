using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeStaticKartColor : MonoBehaviour
    {
        public Renderer[] targets;
        public Color color, color2, dirtColor;
        public float heat, dirt, wheelDirt, wheelHeat, dirtColorEnabled;
        public string sourceIdentity;
        MaterialPropertyBlock block;
        void Awake() { block = new MaterialPropertyBlock(); }
        void Update() { Apply(); }
        public void Apply()
        {
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_Color2", color2);
            block.SetFloat("_SpeedIntensity", heat * 5f);
            block.SetFloat("_DirtAmount", dirt);
            block.SetFloat("_WheelDirtAmount", wheelDirt);
            block.SetFloat("_WheelHeatAmount", wheelHeat);
            block.SetFloat("_DirtColorEnabled", dirtColorEnabled);
            block.SetColor("_DirtColor", dirtColor);
            foreach (Renderer target in targets) target.SetPropertyBlock(block);
        }
    }
}
