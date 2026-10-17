using UnityEngine;
using System.Linq;

namespace Fumo.NativePlayable
{
    public enum NativeBroadcastAuthoredCameraKind { FixedUltraWide, FixedLow, FixedTelePan, FixedZoomFollow, RailSide, HelicopterTele, HelicopterWide, FpvEscort, FpvReveal, MountedFront, MountedRear }

    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class NativeBroadcastAuthoredCamera : MonoBehaviour
    {
        public string cameraId;
        public NativeBroadcastAuthoredCameraKind kind;
        public bool autoEligible = true, autoZoom;
        public float angularSpeed=140, acceleration=30, fpvCoupling=.5f, fpvRoll;
        public string[] targetIds = new string[0];
        public NativeCameraLabMode controlMode;
        public float movementSpeed = 20, stability = .6f;
        public Vector3[] rawFlightSamples = new Vector3[0];
        public float[] rawFlightTimes = new float[0];
        public Transform coverageStart, coverageEnd;
        public Transform[] path = new Transform[0];
        // Retained for older authored objects; the physical lens below now drives projection.
        [HideInInspector] public float fieldOfView = 55;
        [Min(1)] public float focalLength = 24;
        public Vector2 sensorSize = new Vector2(36, 24);
        public Vector2 lensShift = Vector2.zero;
        public Camera.GateFitMode gateFit = Camera.GateFitMode.Vertical;
        [Range(5, 140)] public float minimumFov = 10, maximumFov = 90;
        [Range(.02f, .9f)] public float targetScreenHeight = .25f;
        public float pathDuration = 8, coverageRadius = 40;
        public Vector3 mountOffset = new Vector3(0, 2, 1);
        public bool loopPath = true;
        // Diagnostics are populated only by measured inspection; NaN means unmeasured.
        [System.NonSerialized] public float measuredScreenOccupancy = float.NaN;
        [System.NonSerialized] public float measuredOcclusionRatio = float.NaN;
        [System.NonSerialized] public float measuredRemainingSeconds = float.NaN;
        [System.NonSerialized] public string diagnostics = "未测";
        Camera view, runtime;
        public Camera PreviewCamera { get { if (!runtime) runtime = NativeBroadcastCameraRegistry.CreateInstance(this, "preview"); return runtime; } }
        void OnEnable() { if (string.IsNullOrEmpty(cameraId)) cameraId = System.Guid.NewGuid().ToString("N"); ApplyPhysicalLens(); }
        void OnDisable() { if (runtime) { if (Application.isPlaying) Destroy(runtime.gameObject); else DestroyImmediate(runtime.gameObject); } runtime = null; }
        void OnValidate() { focalLength = Mathf.Max(1, focalLength); sensorSize.x = Mathf.Max(1, sensorSize.x); sensorSize.y = Mathf.Max(1, sensorSize.y); ApplyPhysicalLens(); }
        public void ApplyPhysicalLens()
        {
            if (!view) view = GetComponent<Camera>(); Camera c = view; if (!c) return;
            c.enabled = false; c.usePhysicalProperties = true; c.sensorSize = sensorSize; c.lensShift = lensShift; c.gateFit = gateFit; c.focalLength = focalLength;
        }
        public void Evaluate(Transform target, Transform secondary, float elapsed)
        {
            EvaluateInto(PreviewCamera, target, secondary, elapsed);
        }
        public void EvaluateInto(Camera c, Transform target, Transform secondary, float elapsed)
        {
            c.transform.SetPositionAndRotation(transform.position, transform.rotation);
            c.usePhysicalProperties = true; c.sensorSize = sensorSize; c.lensShift = lensShift; c.gateFit = gateFit; c.focalLength = focalLength; c.nearClipPlane = GetComponent<Camera>().nearClipPlane; c.farClipPlane = GetComponent<Camera>().farClipPlane;
            bool mounted = kind == NativeBroadcastAuthoredCameraKind.MountedFront || kind == NativeBroadcastAuthoredCameraKind.MountedRear;
            if (mounted && target)
            {
                c.transform.position = target.TransformPoint(mountOffset);
                c.transform.rotation = target.rotation * Quaternion.Euler(0, kind == NativeBroadcastAuthoredCameraKind.MountedRear ? 180 : 0, 0);
                return;
            }
            bool moving = kind == NativeBroadcastAuthoredCameraKind.RailSide || kind == NativeBroadcastAuthoredCameraKind.HelicopterTele || kind == NativeBroadcastAuthoredCameraKind.HelicopterWide || kind == NativeBroadcastAuthoredCameraKind.FpvEscort || kind == NativeBroadcastAuthoredCameraKind.FpvReveal;
            if (moving && path != null && path.Length > 1)
            {
                float t = elapsed / Mathf.Max(.1f, pathDuration); t = loopPath ? Mathf.Repeat(t, 1) : Mathf.Clamp01(t);
                float index = t * (loopPath ? path.Length : path.Length - 1);
                int a = Mathf.Min(Mathf.FloorToInt(index), loopPath ? path.Length - 1 : path.Length - 2);
                float u = index - a;
                Vector3 p0 = PathPosition(a - 1), p1 = PathPosition(a), p2 = PathPosition(a + 1), p3 = PathPosition(a + 2);
                c.transform.position = .5f * ((2 * p1) + (-p0 + p2) * u + (2*p0 - 5*p1 + 4*p2 - p3)*u*u + (-p0 + 3*p1 - 3*p2 + p3)*u*u*u);
            }
            bool aim = kind != NativeBroadcastAuthoredCameraKind.FixedUltraWide && kind != NativeBroadcastAuthoredCameraKind.FixedLow;
            if (target && aim)
            {
                Vector3 center = target.position + Vector3.up * 1.3f;
                if (secondary) center = (center + secondary.position + Vector3.up * 1.3f) * .5f;
                if ((center - c.transform.position).sqrMagnitude > .01f) c.transform.rotation = Quaternion.LookRotation(center - c.transform.position);
                if (autoZoom)
                {
                    float height = secondary ? Mathf.Max(2.6f, Vector3.Distance(target.position, secondary.position) + 2.6f) : 2.6f;
                    float fov = Mathf.Clamp(2 * Mathf.Atan(height / (2 * Mathf.Max(1, Vector3.Distance(center, c.transform.position)) * targetScreenHeight)) * Mathf.Rad2Deg, minimumFov, maximumFov);
                    c.focalLength = Camera.FieldOfViewToFocalLength(fov, c.sensorSize.y);
                }
            }
        }
        public static Bounds GroupBounds(Transform[] subjects)
        {
            bool any=false;Bounds group=default;
            foreach(var t in subjects)
            {
                if(!t)continue;bool visible=false;Bounds body=new Bounds(t.position+Vector3.up,Vector3.one*2);
                foreach(var r in t.GetComponentsInChildren<Renderer>(true))if(r&&r.enabled&&r.gameObject.activeInHierarchy&&(r is MeshRenderer||r is SkinnedMeshRenderer)){if(!visible)body=r.bounds;else body.Encapsulate(r.bounds);visible=true;}
                if(!any)group=body;else group.Encapsulate(body);any=true;
            }
            group.Expand(.6f);return group;
        }
        public void EvaluateGroupInto(Camera c,Transform[] subjects,float elapsed)
        {
            subjects=subjects.Where(t=>t).ToArray();EvaluateInto(c,subjects.FirstOrDefault(),subjects.Skip(1).FirstOrDefault(),elapsed);
            bool mounted=kind==NativeBroadcastAuthoredCameraKind.MountedFront||kind==NativeBroadcastAuthoredCameraKind.MountedRear;
            if(subjects.Length==0||mounted||kind==NativeBroadcastAuthoredCameraKind.FixedUltraWide||kind==NativeBroadcastAuthoredCameraKind.FixedLow)return;
            Bounds group=GroupBounds(subjects);Vector3 direction=group.center-c.transform.position;if(direction.sqrMagnitude>.01f)c.transform.rotation=Quaternion.LookRotation(direction);
            if(autoZoom)
            {
                Quaternion inv=Quaternion.Inverse(c.transform.rotation);float tan=0;
                for(int i=0;i<8;i++){Vector3 p=inv*(new Vector3((i&1)==0?group.min.x:group.max.x,(i&2)==0?group.min.y:group.max.y,(i&4)==0?group.min.z:group.max.z)-c.transform.position);if(p.z>.5f)tan=Mathf.Max(tan,Mathf.Abs(p.y)/(p.z*.78f),Mathf.Abs(p.x)/(p.z*.78f*Mathf.Max(.5f,c.aspect)));}
                c.focalLength=Mathf.Clamp(24/(2*Mathf.Max(.001f,tan)),8,300);
            }
        }
        Vector3 PathPosition(int i)
        {
            i = loopPath ? (i % path.Length + path.Length) % path.Length : Mathf.Clamp(i, 0, path.Length - 1);
            return path[i] ? path[i].position : transform.position;
        }
        void OnDrawGizmos()
        {
            Camera c = PreviewCamera; Gizmos.color = Color.cyan;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, c.fieldOfView, 15, .3f, c.aspect); Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireSphere(transform.position, coverageRadius);
            if (coverageStart && coverageEnd) { Gizmos.color = Color.yellow; Gizmos.DrawLine(coverageStart.position, coverageEnd.position); Gizmos.DrawWireSphere(coverageStart.position, 2); Gizmos.DrawWireSphere(coverageEnd.position, 2); }
            Gizmos.color = Color.magenta;
            if (path != null) for (int i = 0; i < path.Length; i++) if (path[i]) { Gizmos.DrawWireSphere(path[i].position, 1); if (i > 0 && path[i - 1]) Gizmos.DrawLine(path[i - 1].position, path[i].position); }
        }
    }
}
