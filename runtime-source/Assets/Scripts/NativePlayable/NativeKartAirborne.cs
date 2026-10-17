using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeKartAirborne
    {
        public static float TurnAngle(float wishTurn, NativeKartSettings settings, float dt)
        {
            return settings._turnForce * Mathf.Clamp(wishTurn, -1f, 1f) * dt * settings._turnVelAirMult;
        }

        public static float StepGravity(float verticalSpeed, float gravity, float dt)
        {
            return verticalSpeed - dt * gravity;
        }

        public static bool RequestJump(bool pending, bool grounded, bool frozen, NativeKartSettings settings)
        {
            return pending || (settings._jumpForce > 0f && grounded && !frozen);
        }

        public static float JumpVerticalSpeed(Vector3 forward, float speed, NativeKartSettings settings)
        {
            return LeaveGroundVerticalSpeed(forward, speed, false) + settings._jumpForce;
        }

        public static float LeaveGroundVerticalSpeed(Vector3 forward, float speed, bool dynamicGravity)
        {
            Vector3 direction = forward.normalized;
            Vector3 motion = direction * speed;
            if (!dynamicGravity) motion.y = 0f;
            return Vector3.Project(motion, direction).y;
        }

        public static bool ResolveGrounded(bool validGround, CollisionFlags flags)
        {
            return validGround && (flags & CollisionFlags.Below) != 0;
        }

        public static float LandingSpeed(float speed, Vector3 velocity, int flips, bool convertVelocity,
            NativeKartSettings settings)
        {
            velocity.y = 0f;
            return (convertVelocity ? velocity.magnitude : speed) + flips * settings._flipSpeedBoost;
        }

        public static float AlignmentFactor(NativeKartSettings settings, float dt)
        {
            return 2f * settings._turnForce * dt * dt * settings._turnAirMult;
        }

        public static float ClampSpeed(float speed, NativeKartSettings settings)
        {
            speed = Mathf.Clamp(speed, 0f, settings._topSpeed + settings._topSpeedAdd + settings._driftSpeedMaxGain);
            return Mathf.Clamp(speed, 0f, settings._topSpeedCap);
        }
    }
}
