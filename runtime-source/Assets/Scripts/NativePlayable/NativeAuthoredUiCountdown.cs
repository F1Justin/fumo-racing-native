using TMPro;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        const string CountdownNode = "GlobalHUD/Canvas/starttext";
        // Exact unweighted keys from startingtext.anim; the clip also animates its unusual Z scale.
        static readonly AnimationCurve countdownScaleX = new AnimationCurve(
            new Keyframe(0.0f, 1.9393f, 0.0f, 0.0f),
            new Keyframe(0.16666667f, 1.1f, -0.40000066f, -0.39999995f),
            new Keyframe(1.0f, 1.0f, -2.9325486e-08f, 0.0f));
        static readonly AnimationCurve countdownScaleY = new AnimationCurve(
            new Keyframe(0.0f, 1.9393f, 0.0f, 0.0f),
            new Keyframe(0.16666667f, 1.1f, -0.40000066f, -0.39999995f),
            new Keyframe(1.0f, 1.0f, -2.9325486e-08f, 0.0f));
        static readonly AnimationCurve countdownScaleZ = new AnimationCurve(
            new Keyframe(0.0f, 1.9393f, 0.0f, 0.0f),
            new Keyframe(0.16666667f, 11.0f, -9.721078e-06f, 0.0f),
            new Keyframe(1.0f, 1.0f, 1.1603038e-06f, 0.0f));
        static readonly AnimationCurve countdownGreen = new AnimationCurve(
            new Keyframe(0.0f, 1.0f, 0.0f, float.NegativeInfinity),
            new Keyframe(0.083333336f, 1.0f, 0.0f, 0.0f),
            new Keyframe(0.16666667f, 0.7341389f, 5.70476e-07f, 0.0f));
        static readonly AnimationCurve countdownBlue = new AnimationCurve(
            new Keyframe(0.0f, 1.0f, 0.0f, float.NegativeInfinity),
            new Keyframe(0.083333336f, 1.0f, 0.0f, 0.0f),
            new Keyframe(0.16666667f, 0.0f, 2.1457672e-06f, 0.0f));
        static readonly AnimationCurve countdownRed = new AnimationCurve(
            new Keyframe(0.0f, 1.0f, 0.0f, 0.0f),
            new Keyframe(1.0f, 1.0f, 0.0f, 0.0f));

        public static void SampleCountdownAnimation(float elapsed, out Vector3 scale, out Color bottom)
        {
            float time = Mathf.Clamp(elapsed, 0f, 1f);
            scale = new Vector3(countdownScaleX.Evaluate(time), countdownScaleY.Evaluate(time), countdownScaleZ.Evaluate(time));
            bottom = new Color(countdownRed.Evaluate(time), countdownGreen.Evaluate(time), countdownBlue.Evaluate(time), 1f);
        }

        public void ApplyCountdownAnimation(float elapsed)
        {
            if (!transforms.TryGetValue(CountdownNode, out var node)) return;
            SampleCountdownAnimation(elapsed, out var scale, out var bottom);
            if (Motion)
            {
                float t = Mathf.Clamp(elapsed, 0, 1);
                scale = new Vector3(Motion.countdownScaleX.Evaluate(t), Motion.countdownScaleY.Evaluate(t), Motion.countdownScaleZ.Evaluate(t));
                bottom = new Color(Motion.countdownRed.Evaluate(t), Motion.countdownGreen.Evaluate(t), Motion.countdownBlue.Evaluate(t), 1);
            }
            node.localScale = prefabAuthored ? Vector3.Scale(definitions[CountdownNode].scale, scale) : scale;
            if (node.TryGetComponent<TMP_Text>(out var text))
            {
                text.colorGradient = new VertexGradient { topLeft = Color.white, topRight = Color.white, bottomLeft = bottom, bottomRight = bottom };
                // OnGUI runs after TMP's normal rebuild. Submit this frame's
                // text, gradient and SDF scale together instead of showing the
                // previous digit / color for a frame at each animation boundary.
                if (Event.current == null || Event.current.type == EventType.Repaint)
                    text.ForceMeshUpdate();
            }
        }

        public void ResetCountdownAnimation()
        {
            if (!transforms.TryGetValue(CountdownNode, out var node)) return;
            var source = definitions[CountdownNode];
            node.localScale = source.scale;
            if (node.TryGetComponent<TMP_Text>(out var text))
                text.colorGradient = new VertexGradient { topLeft = source.gradientTopLeft, topRight = source.gradientTopRight,
                    bottomLeft = source.gradientBottomLeft, bottomRight = source.gradientBottomRight };
        }
    }
}
