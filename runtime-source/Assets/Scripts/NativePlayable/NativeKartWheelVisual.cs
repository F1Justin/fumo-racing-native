using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    /// <summary>Per-wheel serialized values copied from the original VWheel entries.</summary>
    [Serializable]
    public sealed class NativeKartWheelParams
    {
        public Vector3 _torqueAxis, _suspensionAxis, _tiltAxis;
        public float _steering, _suspensionHeight, _wheelRadius, _tiltHeight;
    }

    /// <summary>Scalars copied from the original VehiclePhysics instance on the kart prefab.</summary>
    [Serializable]
    public sealed class NativeKartWheelSettings
    {
        public float _heightTilt = 0.375f;
        public float _tiltTurn = 8f;
        public float _tiltSpdMult = 1.5f;
        public float _maxTilt = 15f;
        public float maxSuspension = 8f;
        public float _landSuspension = 8f;
        public float targetGroundSuspension;
        public float targetAirSuspension = -0.6f;
        public float _tiltDamping = 15f;
        public float _tiltStiff = 200f;
        public float steeringWheelAngle = 110f;
        // Prefab values override the class defaults declared in the original script.
        public float stiffness = 150f;
        public float damping = 5f;
        public float valueThreshold = 0.01f;
        public float velocityThreshold = 0.01f;
        public NativeKartWheelParams[] _wheels = new NativeKartWheelParams[0];
    }

    /// <summary>Inputs the restored component reads from the vehicle every frame.</summary>
    public struct NativeKartWheelInput
    {
        public float speed;     // original VehicleCharacter._currentSpeed
        public float topSpeed;  // original VehicleCharacter._topSpeed
        public float steering;  // original _currentTurn / _turnMax
        public bool grounded;
        public bool landed;
    }

    [Serializable]
    public struct NativeKartWheelState
    {
        public float currentSuspension, currentVelocity;
        public float targetSuspension;
        public float _targetTilt, _currentTilt, _velocityTilt, _tilt;
        public float currentSteering, torqueSpeed;

        public static NativeKartWheelState Step(NativeKartWheelState previous, NativeKartWheelInput input,
            NativeKartWheelSettings settings, float dt)
        {
            var s = previous;
            if (input.landed) s.currentVelocity = settings._landSuspension;
            s.targetSuspension = input.grounded ? settings.targetGroundSuspension : settings.targetAirSuspension;
            s.currentSteering = input.steering;
            s.torqueSpeed = input.speed;
            float ratio = input.topSpeed != 0f ? input.speed / input.topSpeed : 0f;
            s._targetTilt = Mathf.Clamp(input.steering * settings._tiltTurn * (ratio * settings._tiltSpdMult),
                -settings._maxTilt, settings._maxTilt);

            float damping = Mathf.Max(0f, 1f - dt * settings.damping);
            s.currentVelocity = (s.targetSuspension - s.currentSuspension) * settings.stiffness * dt
                + damping * s.currentVelocity;
            s.currentSuspension += dt * s.currentVelocity;

            float tiltDamping = Mathf.Max(0f, 1f - dt * settings._tiltDamping);
            s._velocityTilt = (s._targetTilt - s._currentTilt) * settings._tiltStiff * dt
                + tiltDamping * s._velocityTilt;
            s._currentTilt += dt * s._velocityTilt;
            s._tilt = s._currentTilt;

            if (Mathf.Abs(s.currentSuspension - s.targetSuspension) < settings.valueThreshold
                && Mathf.Abs(s.currentVelocity) < settings.velocityThreshold)
            {
                s.currentSuspension = s.targetSuspension;
                s.currentVelocity = 0f;
            }
            return s;
        }

        /// <summary>Angular speed applied to a torque node, in original units per second.</summary>
        public float SpinRate(int index, NativeKartWheelSettings settings)
        {
            if (settings == null || index < 0 || index >= settings._wheels.Length) return 0f;
            return torqueSpeed / (2f * Mathf.PI * settings._wheels[index]._wheelRadius);
        }

        public Vector3 SpinEuler(int index, NativeKartWheelSettings settings, float dt) =>
            settings._wheels[index]._torqueAxis * (SpinRate(index, settings) * dt * Mathf.Rad2Deg);

        public Vector3 SteerEuler(int index, NativeKartWheelSettings settings) =>
            new Vector3(0f, 0f, settings._wheels[index]._steering * currentSteering);

        public Vector3 WheelTiltEuler(int index, NativeKartWheelSettings settings) =>
            settings._wheels[index]._tiltAxis * _tilt;

        public float ClampedSuspension(NativeKartWheelSettings settings) =>
            Mathf.Clamp(currentSuspension, -settings.maxSuspension, settings.maxSuspension);
    }

    /// <summary>
    /// Wheel spin, steering, suspension travel and body tilt restored from the original VehiclePhysics
    /// component (Update RVA 0x376C30). Driven from Update like the original; Time.deltaTime of 0 freezes it.
    /// </summary>
    public sealed class NativeKartWheelVisual : MonoBehaviour
    {
        public NativeKartMotor motor;
        public NativeKartWheelSettings settings;
        public Transform root, steeringWheel, fumo, sourceFrame;
        public Transform[] suspensions = new Transform[0];
        public Transform[] torques = new Transform[0];
        public bool frozen;

        NativeKartWheelState state;
        Quaternion rootRest, steeringWheelRest, fumoRest;
        Quaternion[] torqueRest = new Quaternion[0];
        Quaternion[] suspensionRest = new Quaternion[0];
        Vector3[] suspensionRestPosition = new Vector3[0];
        Vector3 rootRestPosition, fumoRestPosition;
        int landingCount;

        public NativeKartWheelState State => state;
        public Transform Root => root;
        public Transform SteeringWheel => steeringWheel;
        public Transform Fumo => fumo;
        public float MaxSuspensionHeight { get; private set; }

        void Awake()
        {
            if (root) { rootRest = root.localRotation; rootRestPosition = root.localPosition; }
            if (steeringWheel) steeringWheelRest = steeringWheel.localRotation;
            if (fumo) { fumoRestPosition = fumo.localPosition; fumoRest = fumo.localRotation; }
            torqueRest = new Quaternion[torques.Length];
            for (int i = 0; i < torques.Length; i++)
                if (torques[i]) torqueRest[i] = torques[i].localRotation;
            suspensionRest = new Quaternion[suspensions.Length];
            suspensionRestPosition = new Vector3[suspensions.Length];
            for (int i = 0; i < suspensions.Length; i++)
                if (suspensions[i])
                {
                    suspensionRest[i] = suspensions[i].localRotation;
                    suspensionRestPosition[i] = suspensions[i].localPosition;
                }
            MaxSuspensionHeight = 0f;
            if (settings != null)
                for (int i = 0; i < settings._wheels.Length; i++)
                    MaxSuspensionHeight = Mathf.Max(MaxSuspensionHeight, settings._wheels[i]._suspensionHeight);
            landingCount = motor ? motor.Landings : 0;
            Apply(Time.deltaTime);
        }

        public void ResetState()
        {
            state = default;
            landingCount = motor ? motor.Landings : 0;
            for (int i = 0; i < torques.Length; i++)
                if (torques[i]) torques[i].localRotation = torqueRest[i];
            Apply(0f);
        }

        /// <summary>Advances the restored state from an explicit input; test and fixture use only.</summary>
        public NativeKartWheelState StepForTest(NativeKartWheelInput input, float dt)
        {
            state = NativeKartWheelState.Step(state, input, settings, dt);
            Apply(dt);
            return state;
        }

        public Quaternion RestRotation(int index) =>
            index >= 0 && index < suspensionRest.Length ? suspensionRest[index] : Quaternion.identity;

        /// <summary>Landing equivalent to VehicleCharacter.onLand -> VehiclePhysics._vc_onLand.</summary>
        public void Land() => state.currentVelocity = settings != null ? settings._landSuspension : 8f;

        void Update()
        {
            if (settings == null || motor == null || frozen) return;
            if (motor.Landings != landingCount)
            {
                landingCount = motor.Landings;
                Land();
            }
            float dt = Time.deltaTime;
            state = NativeKartWheelState.Step(state, new NativeKartWheelInput
            {
                speed = motor.Speed,
                topSpeed = motor.stats != null ? motor.stats._topSpeed : 0f,
                steering = motor.CurrentSteering,
                grounded = motor.Grounded
            }, settings, dt);
            Apply(dt);
        }

        void Apply(float dt)
        {
            if (settings == null) return;
            float suspension = state.ClampedSuspension(settings);
            Vector3 sourceUp = sourceFrame ? sourceFrame.up : transform.up;
            for (int i = 0; i < suspensions.Length && i < settings._wheels.Length; i++)
            {
                var wheel = settings._wheels[i];
                var node = suspensions[i];
                if (!node) continue;
                node.localPosition = suspensionRestPosition[i]
                    + wheel._suspensionAxis * (suspension * wheel._suspensionHeight);
                float tiltSign = state._tilt > 0f && wheel._tiltHeight > 0f ? 1f
                    : state._tilt < 0f && wheel._tiltHeight < 0f ? -1f : 0f;
                node.position -= wheel._suspensionAxis
                    * (wheel._tiltHeight * Mathf.Sin(Mathf.Abs(state._tilt) * Mathf.Deg2Rad) * tiltSign);
                node.localRotation = suspensionRest[i]
                    * Quaternion.Euler(state.SteerEuler(i, settings))
                    * Quaternion.Euler(wheel._tiltAxis * state._tilt);
                if (i < torques.Length && torques[i])
                    torques[i].localRotation *= Quaternion.Euler(state.SpinEuler(i, settings, dt));
            }
            if (steeringWheel)
                steeringWheel.localRotation = steeringWheelRest
                    * Quaternion.Euler(0f, settings.steeringWheelAngle * state.currentSteering, 0f);
            if (root)
            {
                root.localPosition = rootRestPosition + Vector3.forward * (suspension * -MaxSuspensionHeight);
                root.position += sourceUp * (Mathf.Sin(Mathf.Abs(state._tilt) * Mathf.Deg2Rad) * settings._heightTilt);
                // The imported Armature/Root X rotations cancel; local Z is the vehicle's roll axis.
                root.localRotation = rootRest * Quaternion.Euler(0f, 0f, state._tilt);
            }
            if (fumo)
            {
                // Root inherits the Armature's 100x scale; the character pivot has unit parent scale.
                fumo.localPosition = fumoRestPosition + Vector3.up * (suspension * -MaxSuspensionHeight * 100f);
                fumo.position += sourceUp * (Mathf.Sin(Mathf.Abs(state._tilt) * Mathf.Deg2Rad) * settings._heightTilt);
                fumo.localRotation = fumoRest * Quaternion.Euler(0f, 0f, state._tilt);
            }
        }
    }
}
