using UnityEngine;
using System.Linq;
namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(31980)]
    public sealed class NativeBroadcastManualCamera : MonoBehaviour
    {
        public static NativeBroadcastManualCamera Active { get; private set; }
        public Camera ProgramCamera;
        public Transform Target, Secondary;
        public NativeBroadcastAuthoredCamera Selected;
        float takenAt;
        Camera runtime;
        NativeBroadcastOutput ownedOutput;
        public void Take(NativeBroadcastAuthoredCamera camera)
        {
            Release();if(!camera)return;
            Selected = camera; takenAt = Time.unscaledTime;
            if (camera) runtime = NativeBroadcastCameraRegistry.CreateInstance(camera, "manual");
            if (ProgramCamera) {ownedOutput=NativeBroadcastOutput.For(ProgramCamera);ownedOutput.Acquire("Manual", "authored TAKE");}
        }
        public void Release()
        {
            Selected = null;
            if (runtime) Destroy(runtime.gameObject); runtime = null;
            if (ownedOutput && ownedOutput.Owner == "Manual") ownedOutput.Release("manual released");ownedOutput=null;
        }
        public static void CopyLens(Camera source, Camera destination)
        {
            destination.usePhysicalProperties = source.usePhysicalProperties;
            destination.sensorSize = source.sensorSize; destination.lensShift = source.lensShift; destination.gateFit = source.gateFit;
            if (source.usePhysicalProperties) destination.focalLength = source.focalLength;
            else destination.fieldOfView = source.fieldOfView;
            destination.nearClipPlane = source.nearClipPlane; destination.farClipPlane = source.farClipPlane;
            destination.orthographic = source.orthographic; destination.orthographicSize = source.orthographicSize;
        }
        void OnEnable() { Active = this; }
        void OnDisable() { Release(); if (Active == this) Active = null; }
        void LateUpdate()
        {
            if (!Selected || !ProgramCamera) return;
            if (!runtime) return;
            var race=UnityEngine.Object.FindFirstObjectByType<NativeRace>();var targets=race?(Selected.targetIds??new string[0]).Select(id=>NativeBroadcastCameraRegistry.BindingSeat(id,race.racers.Length)).Where(i=>i>=0).Select(i=>race.racers[i].transform).ToArray():new Transform[0];
            Selected.EvaluateGroupInto(runtime,targets.Length>0?targets:new[]{Target,Secondary},Time.unscaledTime-takenAt);
            if(!ownedOutput||ownedOutput.Program!=ProgramCamera){if(ownedOutput&&ownedOutput.Owner=="Manual")ownedOutput.Release("Program camera changed");ownedOutput=NativeBroadcastOutput.For(ProgramCamera);ownedOutput.Acquire("Manual","Program camera changed");}
            NativeBroadcastOutput.For(ProgramCamera).Submit("Manual", Selected.cameraId, runtime);
        }
    }
}
