using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed class NativeKartFinishCamera : MonoBehaviour
    {
        public const string CameraBindingPath = "VerticalPivot/LookBehindPivot/SpeedPivot/Main Camera";
        public Animator Animator { get; private set; }
        public bool Ended { get; private set; }
        public int EndTriggerCount { get; private set; }

        public static NativeKartFinishCamera Attach(NativeKartCamera rig, RuntimeAnimatorController controller)
        {
            if (!controller) { NativeTelemetry.Event("camera.consumer.failure", "failed", "requiredFinishControllerMissing"); throw new ArgumentNullException(nameof(controller)); }
            rig.verticalPivot.name = "VerticalPivot";
            rig.lookBackPivot.name = "LookBehindPivot";
            rig.speedPivot.name = "SpeedPivot";
            rig.view.name = "Main Camera";
            var finish = rig.GetComponent<NativeKartFinishCamera>();
            if (!finish) finish = rig.gameObject.AddComponent<NativeKartFinishCamera>();
            finish.Animator = rig.GetComponent<Animator>();
            if (!finish.Animator) finish.Animator = rig.gameObject.AddComponent<Animator>();
            finish.Animator.enabled = false;
            finish.Animator.runtimeAnimatorController = controller;
            finish.Animator.applyRootMotion = false;
            finish.Animator.updateMode = AnimatorUpdateMode.Normal;
            finish.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            finish.ResetForRace();
            NativeTelemetry.Event("camera.bound", "applied", "finishAnimator", "cameraId=" + rig.view.GetInstanceID());
            return finish;
        }

        public void OnEndedRace()
        {
            Animator.enabled = true;
            Animator.SetTrigger("end");
            Ended = true;
            EndTriggerCount++;
            NativeTelemetry.Event("camera.finish.started", "applied", "endTrigger", "triggerCount=" + EndTriggerCount);
        }

        public void ResetForRace()
        {
            Animator.enabled = false;
            Animator.Rebind();
            Animator.ResetTrigger("end");
            Ended = false;
            EndTriggerCount = 0;
            NativeTelemetry.Event("camera.finish.reset", "applied");
        }
    }
}
