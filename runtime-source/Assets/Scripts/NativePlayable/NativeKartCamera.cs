using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable]
    public struct NativeKartCameraInput
    {
        public Vector3 position, velocity;
        public Quaternion rotation;
        public float speed, turn, driftDirection;
        public bool grounded, drifting, ended;
    }

    public sealed class NativeKartCamera : MonoBehaviour
    {
        public Camera view;
        public NativeKartMotor target;
        public Transform verticalPivot, lookBackPivot, speedPivot;
        public const float FollowSpeed = 9999999f;
        public const float TurnFollowSpeed = 4f;
        public const float VerticalFollowSpeed = 5f;
        public const float SpeedSmooth = 3f;
        public const float BaseSpeed = 150f;
        public const float SpeedAmplitude = 2f;
        public const float DriftAngle = 10f;
        public const float BaseDriftAngle = 15f;
        public const float DriftSmooth = 2f;
        public const float GravityAmplitude = 0.25f;
        public const float GravityAngle = 2f;
        public static readonly Vector3 CameraOffset = new Vector3(0f, 2.4f, -6.4999976f);
        public static readonly Quaternion CameraAngle = Quaternion.Euler(6f, 0f, 0f);
        public float DesiredPitch { get; private set; }
        public float DesiredDriftYaw { get; private set; }
        public float DesiredSpeedOffset { get; private set; }
        public int StepCount { get; private set; }
        public bool FirstPerson { get; private set; }
        public bool LookingBehind { get; private set; }
        public Transform FirstPersonAnchor { get; private set; }
        public float FirstPersonOffset { get; private set; }
        public Vector3 SmoothedFirstPersonOffset { get; private set; }
        public static readonly Vector3 FirstPersonPosition = new Vector3(0f, 0.83f, -0.5f);
        public bool enableObstruction = true;
        public const float ClipRadius = 0.3f;
        public const int ClipMask = 512;
        public const float ClipSmooth = 24f;
        public bool Obstructed { get; private set; }
        public int ObstructionLayer { get; private set; } = -1;
        public float ObstructionDistance { get; private set; }
        public float DesiredClipZ { get; private set; } = CameraOffset.z;
        public Vector3 ObstructionOrigin { get; private set; }
        public Vector3 ObstructionDirection { get; private set; }
        public int ObstructionProbeCount { get; private set; }

        void FixedUpdate()
        {
            if (enableObstruction && Time.timeScale > 0f) ProbeObstruction();
        }

        public void ProbeObstruction()
        {
            if (!enableObstruction || Time.timeScale <= 0f) return;
            bool wasObstructed = Obstructed;
            ObstructionOrigin = speedPivot.position + Vector3.up * CameraOffset.y;
            ObstructionDirection = -transform.forward;
            Obstructed = Physics.SphereCast(ObstructionOrigin, ClipRadius, ObstructionDirection,
                out RaycastHit hit, Mathf.Abs(CameraOffset.z) + ClipRadius, ClipMask);
            ObstructionLayer = Obstructed ? hit.collider.gameObject.layer : -1;
            ObstructionDistance = Obstructed ? hit.distance : 0f;
            DesiredClipZ = Obstructed ? -hit.distance : CameraOffset.z;
            ObstructionProbeCount++;
            if (wasObstructed != Obstructed) NativeTelemetry.Count(Obstructed ? "camera.obstruction.entered" : "camera.obstruction.cleared");
        }

        public static NativeKartCamera Attach(Camera camera, NativeKartMotor motor)
        {
            var rig = camera.GetComponentInParent<NativeKartCamera>();
            if (!rig)
            {
                rig = new GameObject("Original kart camera").AddComponent<NativeKartCamera>();
                rig.verticalPivot = Pivot("VerticalPivot", rig.transform);
                rig.lookBackPivot = Pivot("LookBackPivot", rig.verticalPivot);
                rig.speedPivot = Pivot("SpeedPivot", rig.lookBackPivot);
            }
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 40000f;
            bool targetChanged = rig.target != motor;
            rig.view = camera;
            rig.target = motor;
            var wheels = motor.GetComponent<NativeKartWheelVisual>();
            Transform fumo = wheels ? wheels.fumo : null;
            rig.FirstPersonAnchor = fumo && fumo.childCount > 0 ? fumo.GetChild(0) : null;
            rig.FirstPersonOffset = fumo ? fumo.localPosition.y : 0f;
            var hiding = camera.GetComponent<NativeKartFirstPersonHiding>();
            if (!hiding) hiding = camera.gameObject.AddComponent<NativeKartFirstPersonHiding>();
            hiding.Bind(rig, fumo);
            camera.transform.SetParent(rig.speedPivot, false);
            camera.transform.localPosition = CameraOffset;
            camera.transform.localRotation = CameraAngle;
            rig.SnapToTarget();
            string detail = "cameraId=" + camera.GetInstanceID() + " targetId=" + (motor ? motor.GetInstanceID() : 0);
            NativeTelemetry.Event("camera.bound", "applied", "kartFollow", detail);
            if (targetChanged) NativeTelemetry.Event("camera.target.changed", "applied", "kartFollow", detail);
            return rig;
        }

        public void SetLookBehind(bool held)
        {
            bool changed = LookingBehind != held;
            LookingBehind = held;
            lookBackPivot.localEulerAngles = held ? Vector3.up * 180f : Vector3.zero;
            if (changed) NativeTelemetry.Event("camera.lookBehind.changed", "applied", held ? "held" : "released", "cameraId=" + (view ? view.GetInstanceID() : 0));
        }

        public void ToggleView()
        {
            FirstPerson = !FirstPerson;
            view.transform.localPosition = FirstPerson
                ? FirstPersonPosition + Vector3.up * FirstPersonOffset : CameraOffset;
            NativeTelemetry.Event("camera.mode.changed", "applied", FirstPerson ? "firstPerson" : "thirdPerson", "cameraId=" + view.GetInstanceID());
        }

        static Transform Pivot(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        public NativeKartCameraInput ReadTarget(bool ended)
        {
            return new NativeKartCameraInput
            {
                position = target.transform.position,
                rotation = target.transform.rotation,
                velocity = target.DriveVelocity + Vector3.up * target.CurrentVerticalSpeed,
                speed = target.Speed,
                turn = target.CurrentTurn,
                driftDirection = target.DriftDirection,
                grounded = target.Grounded,
                drifting = target.IsDrifting,
                ended = ended
            };
        }

        public void SnapToTarget()
        {
            if (!target) return;
            Snap(ReadTarget(false));
        }

        public void Snap(NativeKartCameraInput input)
        {
            bool presentationChanged = FirstPerson || LookingBehind || Obstructed;
            Vector3 heading = input.rotation * Vector3.forward;
            heading.y = 0f;
            transform.SetPositionAndRotation(input.position,
                heading.sqrMagnitude > 1e-10f ? Quaternion.LookRotation(heading) : Quaternion.identity);
            verticalPivot.localRotation = Quaternion.identity;
            lookBackPivot.localRotation = Quaternion.identity;
            FirstPerson = LookingBehind = false;
            SmoothedFirstPersonOffset = Vector3.zero;
            speedPivot.localPosition = Vector3.zero;
            speedPivot.localRotation = Quaternion.identity;
            view.transform.localPosition = CameraOffset;
            view.transform.localRotation = CameraAngle;
            DesiredPitch = DesiredDriftYaw = DesiredSpeedOffset = 0f;
            StepCount = 0;
            Obstructed = false;
            ObstructionLayer = -1;
            ObstructionDistance = 0f;
            DesiredClipZ = CameraOffset.z;
            ObstructionOrigin = ObstructionDirection = Vector3.zero;
            ObstructionProbeCount = 0;
            if (presentationChanged) NativeTelemetry.Event("camera.mode.changed", "applied", "snappedToFollow", "cameraId=" + (view ? view.GetInstanceID() : 0));
        }

        public void Step(float deltaTime, bool ended)
        {
            if (target) Step(ReadTarget(ended), deltaTime);
        }

        public void Step(NativeKartCameraInput input, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            if (FirstPerson)
            {
                Vector3 anchor = FirstPersonAnchor && target
                    ? target.transform.InverseTransformPoint(FirstPersonAnchor.position) : Vector3.zero;
                SmoothedFirstPersonOffset = Vector3.Lerp(SmoothedFirstPersonOffset, anchor, deltaTime * 12f);
                view.transform.localPosition = FirstPersonPosition + Vector3.up * FirstPersonOffset
                    + SmoothedFirstPersonOffset;
            }
            else if (enableObstruction && !input.ended)
            {
                Vector3 cameraPosition = view.transform.localPosition;
                cameraPosition.z = Mathf.Lerp(cameraPosition.z, DesiredClipZ, deltaTime * ClipSmooth);
                view.transform.localPosition = cameraPosition;
            }
            Vector3 forward = input.rotation * Vector3.forward;
            Vector3 heading = new Vector3(forward.x, 0f, forward.z);
            float pitch;
            if (input.grounded)
            {
                pitch = Vector3.SignedAngle(heading, forward, input.rotation * Vector3.right);
            }
            else
            {
                float vertical = 0f;
                if (new Vector2(input.velocity.x, input.velocity.z).magnitude > 0.5f)
                {
                    heading = input.velocity;
                    vertical = input.velocity.y;
                }
                pitch = Mathf.Clamp(vertical * GravityAmplitude, -GravityAngle, 0f);
                heading.y = pitch;
            }
            DesiredPitch = pitch;
            DesiredSpeedOffset = input.ended || FirstPerson ? 0f : -input.speed / BaseSpeed * SpeedAmplitude;
            DesiredDriftYaw = input.drifting && !input.ended
                ? Mathf.Abs(input.turn) / BaseDriftAngle * DriftAngle * input.driftDirection : 0f;
            transform.position = Vector3.Lerp(transform.position, input.position, deltaTime * FollowSpeed);
            if (heading.sqrMagnitude > 1e-10f)
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(heading), deltaTime * TurnFollowSpeed);
            verticalPivot.localRotation = Quaternion.Lerp(verticalPivot.localRotation,
                Quaternion.Euler(pitch, 0f, 0f), deltaTime * VerticalFollowSpeed);
            Vector3 offset = speedPivot.localPosition;
            offset.z = Mathf.Lerp(offset.z, DesiredSpeedOffset, deltaTime * SpeedSmooth);
            speedPivot.localPosition = FirstPerson ? Vector3.zero : offset;
            speedPivot.localRotation = Quaternion.Lerp(speedPivot.localRotation,
                Quaternion.Euler(0f, DesiredDriftYaw, 0f), deltaTime * DriftSmooth);
            StepCount++;
        }
    }
}
