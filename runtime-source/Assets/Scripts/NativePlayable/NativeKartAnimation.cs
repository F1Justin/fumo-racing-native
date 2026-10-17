using UnityEngine;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-40)]
    public sealed class NativeKartAnimation : MonoBehaviour
    {
        public NativeKartMotor motor;
        public Animator animator;
        public Transform accelerationPivot;
        public float accelerationAmplitude = 3f, accelerationFriction = 16f;
        public float accelerationMaxAngle = 40f, accelerationSmooth = 12f;
        public float AverageAcceleration { get; private set; }
        public int ParameterSteps { get; private set; }
        int lastMotorStep = -1;
        bool resetPending;
        void OnEnable() { if (resetPending) ResetPlayback(); }

        void FixedUpdate()
        {
            if (resetPending && animator && animator.gameObject.activeInHierarchy) ResetPlayback();
            if (Time.timeScale > 0f && motor && animator)
                SetParameters(motor.Speed, motor.IsDrifting, motor.Grounded, motor.IsWheelie);
        }

        void Update()
        {
            if (!motor || !accelerationPivot || Time.timeScale <= 0f || lastMotorStep == motor.FixedStepCount) return;
            lastMotorStep = motor.FixedStepCount;
            StepAcceleration(motor.SpeedBeforeStep, motor.Speed, motor.LastStepDeltaTime);
        }

        public void SetParameters(float speed, bool drifting, bool grounded, bool wheeling)
        {
            animator.SetFloat("vspeed", speed / 140f);
            animator.SetBool("drift", drifting);
            animator.SetBool("isGrounded", grounded);
            animator.SetBool("whillie", wheeling);
            ParameterSteps++;
        }

        public void StepAcceleration(float speedBefore, float speedAfter, float dt)
        {
            if (dt <= 0f) return;
            AverageAcceleration += (speedAfter - speedBefore) * accelerationAmplitude;
            AverageAcceleration = Mathf.Lerp(AverageAcceleration, 0f, accelerationFriction * dt);
            accelerationPivot.localRotation = Quaternion.Lerp(accelerationPivot.localRotation,
                Quaternion.Euler(Mathf.Clamp(-AverageAcceleration, -accelerationMaxAngle, accelerationMaxAngle), 0f, 0f),
                accelerationSmooth * dt);
        }

        public void ResetPlayback()
        {
            AverageAcceleration = 0f;
            lastMotorStep = motor ? motor.FixedStepCount : -1;
            ParameterSteps = 0;
            if (accelerationPivot) accelerationPivot.localRotation = Quaternion.identity;
            if (!animator) return;
            // Lobby return also resets hidden racers. Defer animator evaluation until visible.
            if (!animator.gameObject.activeInHierarchy) { resetPending = true; return; }
            resetPending = false;
            animator.Rebind();
            SetParameters(0f, false, true, false);
            animator.Play("Base Layer.Movement", 0, 0f);
            animator.Update(0f);
        }
    }
}
