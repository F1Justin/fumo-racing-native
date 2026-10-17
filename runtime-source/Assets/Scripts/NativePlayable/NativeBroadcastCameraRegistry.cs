using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    // The definition is scene-authored. Each consumer evaluates into its own disposable instance.
    public static class NativeBroadcastCameraRegistry
    {
        public const string EmergencyId="emergency-follow";
        public static string[] CameraIds => new[]{EmergencyId}.Concat(Definitions.Select(d=>d.cameraId)).ToArray();
        public static NativeBroadcastAuthoredCamera[] Definitions => UnityEngine.Object.FindObjectsByType<NativeBroadcastAuthoredCamera>(FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene.IsValid()).OrderBy(c => c.cameraId).ToArray();
        public static void LoadSavedDefinitions(NativeRace race)
        {
            if (!NativeReleaseFeatures.BroadcastEnabled) return;
            var saved=NativeContent.Load<TextAsset>("NativeOnline/Broadcast/Layouts/forest-lab");if(!saved)return;
            var layout=JsonUtility.FromJson<NativeCameraLabLayout>(saved.text);
            if(layout.schema!=1||layout.trackId!=race.trackId||layout.scenePath!=race.gameObject.scene.path||layout.trackRoot!=race.transform.name)return;
            foreach(var row in layout.stations)if(!Find(row.cameraId)){var d=new GameObject(row.name).AddComponent<NativeBroadcastAuthoredCamera>();d.cameraId=row.cameraId;d.transform.SetPositionAndRotation(race.transform.TransformPoint(row.localPosition),race.transform.rotation*row.localRotation);NativeCameraLab.RestoreDefinition(d,row,race.transform);}
        }
        public static int BindingSeat(string id,int count)
        { if(string.IsNullOrEmpty(id))return -1;int colon=id.LastIndexOf(':');return int.TryParse(id.Substring(colon+1),out int n)&&n>=0&&n<count?n:-1; }
        public static NativeBroadcastAuthoredCamera Find(string id) => Definitions.FirstOrDefault(c => c.cameraId == id);
        public static Camera CreateInstance(NativeBroadcastAuthoredCamera definition, string consumer)
        {
            var go = new GameObject("Camera runtime " + consumer + " " + definition.cameraId) { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>(); c.CopyFrom(definition.GetComponent<Camera>()); c.enabled = false; c.targetTexture = null;
            c.transform.SetPositionAndRotation(definition.transform.position, definition.transform.rotation);
            return c;
        }
    }

    [DefaultExecutionOrder(31990)]
    public sealed class NativeBroadcastOutput : MonoBehaviour
    {
        public Camera Program;
        public string Owner { get; private set; } = "Automatic";
        public string CameraId { get; private set; } = NativeBroadcastCameraRegistry.EmergencyId;
        public string LastReason { get; private set; } = "";
        public int Transitions { get; private set; }
        Camera snapshot, pending;
        int pendingFrame = -1;
        string pendingId;
        public static NativeBroadcastOutput For(Camera program)
        {
            var o = program.GetComponent<NativeBroadcastOutput>();
            if (!o) o = program.gameObject.AddComponent<NativeBroadcastOutput>();
            o.Program = program; return o;
        }
        public void Acquire(string owner, string reason)
        {
            if (Owner == owner) return;
            if (!snapshot) { var go = new GameObject("Program lens before takeover") { hideFlags = HideFlags.HideAndDontSave }; snapshot = go.AddComponent<Camera>(); snapshot.enabled = false; NativeBroadcastManualCamera.CopyLens(Program, snapshot); }
            Owner = owner; LastReason = reason; pending = null; Cut();
        }
        public void Release(string reason)
        {
            if (snapshot) { NativeBroadcastManualCamera.CopyLens(snapshot, Program); Destroy(snapshot.gameObject); snapshot = null; }
            Owner = "Automatic"; CameraId = NativeBroadcastCameraRegistry.EmergencyId; LastReason = reason; pending = null; Cut();
        }
        public void Submit(string owner, string id, Camera state)
        {
            if (Owner != owner) return;
            pending = state; pendingId = id; pendingFrame = Time.frameCount;
        }
        public void CommitAutomatic(string id, Vector3 position, Quaternion rotation, float fov)
        {
            if (Owner != "Automatic") return;
            SetId(id); Program.transform.SetPositionAndRotation(position, rotation);
            Program.usePhysicalProperties = false; Program.fieldOfView = fov; Program.nearClipPlane = .3f;
        }
        public void CommitAutomatic(string id, Camera state)
        {
            if (Owner != "Automatic") return;
            SetId(id); Apply(state);
        }
        public void Fallback(string reason) { if (Owner == "Automatic") { SetId(NativeBroadcastCameraRegistry.EmergencyId); LastReason = reason; } }
        void SetId(string id) { if (CameraId != id) { CameraId = id; Cut(); } }
        void Cut()
        {
            Transitions++;
            var p = Program.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();
            if (p && p.enabled && p.haveBundlesBeenInited) p.ResetHistory();
        }
        void Apply(Camera source) { Program.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation); NativeBroadcastManualCamera.CopyLens(source, Program); }
        void LateUpdate() { if (pending && pendingFrame == Time.frameCount) { SetId(pendingId); Apply(pending); } }
        void OnDisable() { if (Program) Release("output disabled"); }
    }
}
