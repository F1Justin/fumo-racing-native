using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeItemPrefabMotion : MonoBehaviour
    {
        public Vector3 rotationPerSecond;
        public bool overrideLocalPosition;
        public float bobAmplitude, bobRate;
        public string sourceComponentId, sourceSiblingPath;
        public int UpdateTicks { get; private set; }
        public float LastDeltaTime { get; private set; }

        void Update()
        {
            UpdateTicks++;
            LastDeltaTime = Time.deltaTime;
            if (overrideLocalPosition)
                transform.localPosition = new Vector3(0f,
                    Mathf.Sin(Time.time * bobRate) * bobAmplitude + bobAmplitude * .5f, 0f);
            transform.Rotate(rotationPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
