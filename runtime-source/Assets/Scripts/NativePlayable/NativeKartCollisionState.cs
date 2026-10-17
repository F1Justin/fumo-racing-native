#if UNITY_6000_0_OR_NEWER
using PhysicMaterial = UnityEngine.PhysicsMaterial;
#endif
using UnityEngine;

namespace Fumo.NativePlayable
{
    // Fields mirror the collision state used by VehicleCharacter.Update and its controller callback.
    public struct NativeKartCollisionState
    {
        public Vector3 force;
        public Vector3 upNormal;
        public Vector3 hitNormal;
        public Vector3 impactPoint;
        public PhysicMaterial surfaceMaterial;
        public float groundAngle;
        public float crashTime;
        public bool validGround;
        public bool groundWater;
        public bool onGrass;
        public bool afterGrounded;

        public void AdvanceCrashTime(float dt)
        {
            crashTime = Mathf.Max(0f, crashTime - dt);
        }

        public void DampenForce(float dt, float forceFriction)
        {
            force = Vector3.Lerp(force, Vector3.zero, Mathf.Clamp01(forceFriction * dt));
        }

        public Vector3 MovementVelocity(Vector3 velocityWithGravity)
        {
            return velocityWithGravity + force;
        }

        public void ResetAirborneSideContact()
        {
            afterGrounded = false;
        }

        public bool HasSideContactNormal()
        {
            return hitNormal.sqrMagnitude >= 1e-10f;
        }

        public void ClearSideContact()
        {
            hitNormal = Vector3.zero;
        }

        public bool ResolveGroundedSideContact(Quaternion rotation, ref float speed)
        {
            Vector3 horizontalNormal = hitNormal;
            horizontalNormal.y = 0f;
            horizontalNormal.Normalize();
            float incoming = Vector3.Dot(rotation * Vector3.forward, horizontalNormal);
            if (incoming >= 0f) return false;

            Vector3 localResponse = Quaternion.Inverse(rotation) * (horizontalNormal * incoming);
            speed -= localResponse.z * -incoming;
            return true;
        }

        public void ApplyContact(Vector3 contactNormal, Vector3 contactPoint, float slopeLimit,
            Vector3 vehicleRight, PhysicMaterial material, bool grass, bool water, bool player,
            float crashFactor, float crashMaxRate, ref NativeKartCollisionState other,
            bool hasOtherVehicle)
        {
            if (Vector3.Angle(Vector3.up, contactNormal) <= slopeLimit)
            {
                groundAngle = Vector3.SignedAngle(Vector3.up, contactNormal, vehicleRight);
                upNormal = contactNormal;
                validGround = true;
                surfaceMaterial = material;
            }
            else
            {
                hitNormal = contactNormal;
                impactPoint = contactPoint;
            }

            groundWater = water;
            onGrass = grass || water;
            if (!player || crashTime > 0f || !hasOtherVehicle) return;

            Vector3 direction = contactNormal;
            direction.y = 0f;
            direction.Normalize();
            force = direction * crashFactor;
            other.force = -direction * crashFactor;
            crashTime = other.crashTime = crashMaxRate;
        }

        // The first airborne side contact arms the original wall-response branch.
        public bool ResolveAirborneSideContact(ref Vector3 velocity, ref float speed,
            float crashFactor, float crashMaxRate)
        {
            if (!afterGrounded)
            {
                afterGrounded = true;
                return false;
            }

            Vector3 horizontalNormal = hitNormal;
            horizontalNormal.y = 0f;
            horizontalNormal.Normalize();
            float incoming = Vector3.Dot(velocity, horizontalNormal);
            if (incoming >= 0f) return false;

            float impact = -incoming;
            Vector3 horizontalVelocity = velocity;
            horizontalVelocity.y = 0f;
            velocity -= horizontalVelocity.normalized * impact;
            speed -= impact;
            if (crashTime <= 0f)
            {
                force = horizontalNormal * crashFactor;
                crashTime = crashMaxRate;
            }
            return true;
        }
    }
}
