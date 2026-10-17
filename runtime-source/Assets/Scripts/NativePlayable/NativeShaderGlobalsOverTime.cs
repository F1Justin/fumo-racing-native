using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeShaderGlobalsOverTime : MonoBehaviour
    {
        public string shaderKey;
        public float delay;
        public float targetTime = 220f;
        float elapsed;
        public float Elapsed => elapsed;
        public static event System.Action<NativeShaderGlobalsOverTime, bool> Observation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetObservation() { Observation = null; }

        void Awake() { elapsed = -delay; Observation?.Invoke(this, true); }

        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            if (elapsed >= targetTime)
            {
                elapsed = targetTime;
                enabled = false;
            }
            Shader.SetGlobalFloat(shaderKey, Mathf.Clamp01(elapsed / targetTime));
            Observation?.Invoke(this, false);
        }

    }
}
