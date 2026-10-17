using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public sealed class NativeKartFeedbackSettings
    {
        public Color _color = Color.white;
        public Color _color2 = Color.white;
        public float _heatDecay = 0.03f;
        public float _heatGain = 0.025f;
        public float _dirtGain = 0.175f;
        public float _dirtLose = 0.016f;
        public float _dirtWheelGain = 1.5f;
        public float _dirtWheelLose = 0.175f;
        public float _dirtWaterLose = 4f;
        public float _dirtWaterLoseStill = 0.3f;
        public float _wheelHeatMinSpd = 0.7f;
        public float _wheelHeatMax = 0.9f;
        public float _wheelHeatGain = 0.15f;
        public float _wheelHeatLose = 0.1f;
        public float _dirtColorBlend = 0.1f;
        public Color _dirtColor = Color.white;
        public float _dirtColorEnabled;
    }

    public struct NativeKartFeedbackInput
    {
        public float speedRatio;
        public float topSpeedModifier;
        public bool onGrass;
        public bool inWater;
        public bool hasSurface;
        public Color surfaceColor;
        public float surfaceColorEnabled;
    }

    [Serializable]
    public struct NativeKartFeedbackState
    {
        public float heat;
        public float dirt;
        public float wheelDirt;
        public float wheelHeat;
        public Color dirtColor;
        public float dirtColorEnabled;

        public static NativeKartFeedbackState Initial(NativeKartFeedbackSettings settings)
        {
            return new NativeKartFeedbackState
            {
                dirtColor = settings._dirtColor,
                dirtColorEnabled = settings._dirtColorEnabled
            };
        }

        public static NativeKartFeedbackState Step(
            NativeKartFeedbackState previous, NativeKartFeedbackInput input,
            NativeKartFeedbackSettings settings, float dt)
        {
            if (dt <= 0f) return previous;

            float speed = input.speedRatio;
            previous.heat = Mathf.MoveTowards(previous.heat, speed,
                (speed > previous.heat ? input.topSpeedModifier * settings._heatGain : settings._heatDecay) * dt);

            if (!input.onGrass)
            {
                previous.dirt -= speed * settings._dirtLose * dt;
                previous.wheelDirt -= speed * settings._dirtWheelLose * dt;
            }
            else
            {
                if (input.hasSurface)
                {
                    if (previous.dirt <= 0f)
                    {
                        previous.dirtColor = input.surfaceColor;
                        previous.dirtColorEnabled = input.surfaceColorEnabled;
                    }
                    float blend = Mathf.Clamp01(dt * settings._dirtColorBlend * speed);
                    previous.dirtColor = Color.Lerp(previous.dirtColor, input.surfaceColor, blend);
                    previous.dirtColorEnabled = Mathf.Lerp(previous.dirtColorEnabled,
                        input.surfaceColorEnabled, blend);
                }

                if (input.inWater)
                {
                    float loss = Mathf.Lerp(settings._dirtWaterLoseStill,
                        settings._dirtWaterLose, Mathf.Clamp01(speed)) * dt;
                    previous.dirt -= loss;
                    previous.wheelDirt -= loss;
                }
                else
                {
                    previous.dirt += speed * settings._dirtGain * dt;
                    previous.wheelDirt += speed * settings._dirtWheelGain * dt;
                }
            }

            float wheelTarget = speed < settings._wheelHeatMinSpd
                ? 0f : settings._wheelHeatMinSpd + (1f - settings._wheelHeatMinSpd) * speed;
            float wheelRate = speed - previous.wheelHeat < 0f
                ? settings._wheelHeatLose : settings._wheelHeatGain;
            previous.wheelHeat = Mathf.Clamp(Mathf.MoveTowards(previous.wheelHeat, wheelTarget,
                wheelRate * dt), 0f, settings._wheelHeatMax);
            previous.dirt = Mathf.Clamp01(previous.dirt);
            previous.wheelDirt = Mathf.Clamp(previous.wheelDirt, 0f, 0.75f);
            return previous;
        }
    }

    public sealed class NativeKartFeedback : MonoBehaviour
    {
        public NativeKartMotor motor;
        public NativeKartFeedbackSettings settings = new NativeKartFeedbackSettings();
        public Renderer[] targets;
        public NativeKartFeedbackState State { get; private set; }

        static readonly int SpeedIntensityId = Shader.PropertyToID("_SpeedIntensity");
        static readonly int DirtAmountId = Shader.PropertyToID("_DirtAmount");
        static readonly int WheelDirtAmountId = Shader.PropertyToID("_WheelDirtAmount");
        static readonly int WheelHeatAmountId = Shader.PropertyToID("_WheelHeatAmount");
        static readonly int DirtColorId = Shader.PropertyToID("_DirtColor");
        static readonly int DirtColorEnabledId = Shader.PropertyToID("_DirtColorEnabled");
        struct Binding
        {
            public Renderer renderer;
            public int materialIndex;
        }

        Binding[] bindings = new Binding[0];
        Renderer[] boundTargets;
        MaterialPropertyBlock block;

        void Awake()
        {
            if (!motor) motor = GetComponentInParent<NativeKartMotor>();
            block = new MaterialPropertyBlock();
            RebindTargets();
            Reset();
        }

        public void RebindTargets()
        {
            boundTargets = targets;
            if (targets == null)
            {
                bindings = new Binding[0];
                return;
            }

            var found = new System.Collections.Generic.List<Binding>();
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                Renderer target = targets[targetIndex];
                if (!target) continue;
                Material[] materials = target.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material && material.shader && material.shader.name == "Fumo/RestoredKart")
                        found.Add(new Binding { renderer = target, materialIndex = materialIndex });
                }
            }
            bindings = found.ToArray();
        }

        void Update()
        {
            if (motor) Step(motor.FeedbackInput, Time.deltaTime);
        }

        public void Reset()
        {
            if (settings == null) settings = new NativeKartFeedbackSettings();
            State = NativeKartFeedbackState.Initial(settings);
            Apply();
        }

        public void Step(NativeKartFeedbackInput input, float dt)
        {
            if (dt <= 0f) return;
            if (settings == null) settings = new NativeKartFeedbackSettings();
            State = NativeKartFeedbackState.Step(State, input, settings, dt);
            Apply();
        }

        void Apply()
        {
            if (boundTargets != targets) RebindTargets();
            if (block == null) block = new MaterialPropertyBlock();
            for (int i = 0; i < bindings.Length; i++)
            {
                Renderer target = bindings[i].renderer;
                if (!target) continue;
                int materialIndex = bindings[i].materialIndex;
                // Paint may already live in this material slot's property block.
                target.GetPropertyBlock(block, materialIndex);
                block.SetFloat(SpeedIntensityId, State.heat * 5f);
                block.SetFloat(DirtAmountId, State.dirt);
                block.SetFloat(WheelDirtAmountId, State.wheelDirt);
                block.SetFloat(WheelHeatAmountId, State.wheelHeat);
                block.SetColor(DirtColorId, State.dirtColor);
                block.SetFloat(DirtColorEnabledId, State.dirtColorEnabled);
                target.SetPropertyBlock(block, materialIndex);
            }
        }
    }
}
