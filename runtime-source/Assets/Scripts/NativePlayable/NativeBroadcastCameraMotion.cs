using UnityEngine;

namespace Fumo.NativePlayable
{
    // Persistent virtual camera state. Selection never interpolates the Program camera.
    public sealed class NativeBroadcastCameraMotion
    {
        public Vector3 position, velocity;
        public Quaternion rotation;
        public float fieldOfView, age, usableSeconds;
        bool initialized;
        float turnSpeed;
        public void Step(Vector3 destination, Vector3 aim, float fov, float dt, bool moving, bool gimbal,
            float maximumSpeed = 100, float acceleration = 90, float maximumTurn = 100, float bank = 0)
        {
            Quaternion desired = Quaternion.LookRotation(aim - destination, Vector3.up) * Quaternion.Euler(0, 0, bank);
            if (!initialized)
            {
                position = destination; rotation = desired; fieldOfView = fov; initialized = true;
            }
            dt = Mathf.Clamp(dt, 0, .1f); age += dt;
            if (moving)
            {
                Vector3 delta = destination - position;
                Vector3 desiredVelocity = delta.sqrMagnitude < .0001f ? Vector3.zero
                    : delta.normalized * Mathf.Min(maximumSpeed, Mathf.Sqrt(2 * acceleration * delta.magnitude));
                velocity = Vector3.MoveTowards(velocity, desiredVelocity, acceleration * dt);
                position += velocity * dt;
            }
            else { position = destination; velocity = Vector3.zero; }
            desired = Quaternion.LookRotation(aim - position, Vector3.up) * Quaternion.Euler(0, 0, bank);
            if (gimbal)
            {
                float angle = Quaternion.Angle(rotation, desired);
                turnSpeed = Mathf.MoveTowards(turnSpeed, Mathf.Min(maximumTurn, angle * 5), maximumTurn * 3 * dt);
                rotation = Quaternion.RotateTowards(rotation, desired, turnSpeed * dt);
                fieldOfView = Mathf.MoveTowards(fieldOfView, fov, 22 * dt);
            }
            else { rotation = desired; fieldOfView = fov; turnSpeed = 0; }
        }
    }
}
