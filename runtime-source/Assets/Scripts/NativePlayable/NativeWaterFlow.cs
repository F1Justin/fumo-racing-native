using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeWaterFlow : MonoBehaviour
    {
        public float speed, distance, bumpSpeed, bumpDistance;
        public bool syncWind;
        public Transform wind;
        Vector2 albedo, normal;

        void Update()
        {
            if (!syncWind)
            {
                albedo = Quaternion.AngleAxis(Time.time * speed, Vector3.forward) * Vector2.one * distance;
                normal = Quaternion.AngleAxis(Time.time * bumpSpeed, Vector3.forward) * Vector2.one * bumpDistance;
                if (wind) wind.rotation = Quaternion.LookRotation(new Vector3(normal.x, 0, normal.y), Vector3.zero) * Quaternion.Euler(0, -90, 0);
            }
            else if (wind)
            {
                var rotation = wind.rotation;
                var direction = new Quaternion(rotation.x, rotation.z, rotation.y, -rotation.w) * Vector3.up * .2f;
                albedo = direction * Time.time * speed;
                normal = direction * Time.time * bumpSpeed;
            }
            Shader.SetGlobalFloat("_WaterLocalUvX", albedo.x);
            Shader.SetGlobalFloat("_WaterLocalUvZ", albedo.y);
            Shader.SetGlobalFloat("_WaterLocalUvNX", normal.x);
            Shader.SetGlobalFloat("_WaterLocalUvNZ", normal.y);
        }
    }
}
