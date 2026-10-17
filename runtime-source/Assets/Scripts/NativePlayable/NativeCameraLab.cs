using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Unity.Cinemachine;

namespace Fumo.NativePlayable
{
    public enum NativeCameraLabMode { Scout, ManualFlightAutoGimbal, AutoFlightManualGimbal, FramingAutopilot, Fpv, ManualTake, AutoSaved }
    [Serializable] public sealed class NativeCameraLabLayout
    {
        public int schema = 1;
        public string layoutId, scenePath, trackId, trackRoot;
        public NativeCameraLabStation[] stations = Array.Empty<NativeCameraLabStation>();
    }
    [Serializable] public sealed class NativeCameraLabStation
    {
        public string cameraId, name;
        public Vector3 worldPosition, localPosition;
        public Quaternion rotation, localRotation;
        public Vector2 sensorSize, lensShift;
        public float focalLength, nearClip, farClip, speed, stability, coverageRadius, pathDuration;
        public float minimumFov, maximumFov, targetScreenHeight, angularSpeed, acceleration, fpvCoupling, roll;
        public Vector3 mountOffset, coverageStartLocal, coverageEndLocal;
        public bool autoZoom, hasCoverageStart, hasCoverageEnd;
        public int gateFit, kind, mode;
        public bool loopPath, autoEligible;
        public string[] targetIds;
        public Vector3[] rawSamples, pathSamples, localPathSamples;
        public float[] sampleTimes;
    }
    // Runs only after an explicit Lab start. All driving remains the existing local authority.
    [DefaultExecutionOrder(31970)]
    public sealed class NativeCameraLab : MonoBehaviour
    {
        public static NativeCameraLab Active { get; private set; }
        public static bool IsActive => Active && Active.running;
        public static bool TrafficEnabled => Active && Active.running && Active.continuousTraffic;
        public static bool WorldPaused => IsActive && Active.worldPaused;
        public NativeRace Race;
        public InputActionAsset Controls;
        public NativeCameraLabMode Mode;
        public bool continuousTraffic = true, worldPaused, autoZoom, showPanel = true;
        public float speed = 20, sensitivity = .12f, stability = .6f, focalLength = 35, roll, fpvCoupling = .5f;
        public float angularSpeed = 140, acceleration = 30;
        public string LastError = "", SelectedId = "", FramingLimit = "";
        public bool CaptureInput { get; private set; }
        public int ActualInputFrames { get; private set; }
        public string[] TargetIds = Array.Empty<string>();
        public string sessionId;
        public int simulationTick;
        public double presentationTime;
        public string LayoutPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../stage5/broadcast/layouts/forest-lab-session.json"));
        public bool Running => running;
        public Camera StateCamera => state;
        public NativeBroadcastOutput Output => output;
        public string AutomaticCameraId => running && Mode == NativeCameraLabMode.AutoSaved ? SelectedId : null;
        public readonly List<string> RecentErrors = new List<string>();
        public readonly List<string> Journal = new List<string>();
        bool running;
        Camera state;
        NativeBroadcastOutput output;
        InputActionAsset input;
        InputAction move, lift, look, padLook, fast, fine, zoom, escape, pause, capture, slower, faster;
        Vector3 velocity;
        Vector2 pendingMove, pendingTurn;
        float pendingLift;
        float elapsed, nextSample;
        readonly List<Vector3> rawSamples = new List<Vector3>();
        readonly List<float> sampleTimes = new List<float>();
        readonly Dictionary<string, Camera> instances = new Dictionary<string, Camera>();
        readonly Dictionary<string, Renderer[]> actorRenderers = new Dictionary<string, Renderer[]>();
        CinemachineCamera cm;
        CinemachinePanTilt panTilt;
        int targetToggle = -1;
        Vector2 panelScroll;
        Rect panel = new Rect(12, 12, 340, 600);
        string layoutId;
        public static NativeCameraLab Ensure()
        {
            if (!NativeReleaseFeatures.BroadcastEnabled) throw new InvalidOperationException("Camera Lab is unavailable in the core release");
            if (Active) return Active;
            var race = UnityEngine.Object.FindFirstObjectByType<NativeRace>();
            if (!race) throw new InvalidOperationException("No NativeRace in current scene");
            var lab = race.gameObject.GetComponent<NativeCameraLab>();
            if (!lab) lab = race.gameObject.AddComponent<NativeCameraLab>();
            lab.Race = race; return lab;
        }
        void Awake() { Active = this; Application.logMessageReceived += ReceiveLog; }
        void ReceiveLog(string message,string trace,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;RecentErrors.Add(message);if(RecentErrors.Count>12)RecentErrors.RemoveAt(0);}
        public void StartSession(bool traffic = true, int count = 8)
        {
            if (!NativeReleaseFeatures.BroadcastEnabled) throw new InvalidOperationException("Camera Lab is unavailable in the core release");
            if (running) return;
            if (Race && Race.StartupSplashActive) throw new InvalidOperationException("Wait for startup splash to finish");
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first");
            var net = NativeRaceNetwork.Instance;
            if (!net || !Race || Race.trackId != "forest") throw new InvalidOperationException("ForestRace is required");
            simulationTick = 0; ActualInputFrames = 0; rawSamples.Clear(); sampleTimes.Clear(); actorRenderers.Clear();
            running = true; continuousTraffic = traffic; sessionId = Guid.NewGuid().ToString("N"); layoutId = Guid.NewGuid().ToString("N");
            try
            {
                net.Config.racers = Mathf.Clamp(count, 2, 100); net.Config.laps = 1;
                net.BeginLocalBroadcastDemo(count==8?NativeSessionMode.QuickRace:NativeSessionMode.CustomRace);
                output = NativeBroadcastOutput.For(Race.raceCamera);
                state = new GameObject("Lab Scout runtime") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                state.CopyFrom(Race.raceCamera); state.enabled = false; state.targetTexture = null;
                state.usePhysicalProperties = true; state.sensorSize = new Vector2(36,24); focalLength = 35;
                var car = Race.racers[0].transform;
                state.transform.SetPositionAndRotation(car.position - car.forward * 14 + Vector3.up * 7, Quaternion.LookRotation(car.position + Vector3.up - (car.position - car.forward * 14 + Vector3.up * 7)));
                TargetIds = new[] { RacerId(0) }; Mode = NativeCameraLabMode.Scout; elapsed = 0;
                SetupInputs(); output.Acquire("Lab", "start local authority traffic");
                NativeBroadcastManualCamera.Active?.Release();
                Application.runInBackground = true;
                var saved=NativeContent.Load<TextAsset>("NativeOnline/Broadcast/Layouts/forest-lab");if(saved)LoadLayoutJson(saved.text);
                Log("started " + count + " AI; continuous=" + traffic);
            }
            catch { running = false; throw; }
        }
        public void StopSession()
        {
            if (!running) return;
            ReleaseInput(); worldPaused = false; Time.timeScale = 1; NativeAudioRuntime.SetPaused(false);
            input?.Disable(); if (input) Destroy(input); input = null;
            if (output && output.Owner == "Lab") output.Release("Lab stopped");
            if (state) Destroy(state.gameObject); state = null;
            foreach (var c in instances.Values) if (c) Destroy(c.gameObject); instances.Clear();
            if (cm) Destroy(cm.gameObject); cm = null;
            running = false; NativeRaceNetwork.Instance?.Leave(true); Log("stopped");
        }
        void OnDestroy() { Application.logMessageReceived -= ReceiveLog; StopSession(); if (Active == this) Active = null; }
        public string RacerId(int seat) => "lab:" + sessionId + ":seat:" + seat;
        public int Seat(string id)
        {
            string prefix = "lab:" + sessionId + ":seat:";
            return id != null && id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.Substring(prefix.Length), out var n) && n >= 0 && n < (Race.racers?.Length ?? 0) ? n : -1;
        }
        public void SelectTargets(string[] ids)
        {
            if (ids == null || ids.Length < 1 || ids.Length > 4 || ids.Distinct().Count() != ids.Length || ids.Any(id=>Seat(id)<0))
                throw new ArgumentException("Select 1–4 unique current RacerIds");
            TargetIds = (string[])ids.Clone(); actorRenderers.Clear(); Log("subjects " + string.Join(",",ids));
        }
        public void SetMode(NativeCameraLabMode mode)
        {
            if ((mode == NativeCameraLabMode.ManualTake || mode == NativeCameraLabMode.AutoSaved || mode == NativeCameraLabMode.AutoFlightManualGimbal) && !NativeBroadcastCameraRegistry.Find(SelectedId))
                throw new InvalidOperationException("Select a saved station first");
            if (mode == NativeCameraLabMode.AutoFlightManualGimbal && NativeBroadcastCameraRegistry.Find(SelectedId).path.Length < 2) throw new InvalidOperationException("Save an edited flight route first");
            if (mode == NativeCameraLabMode.AutoSaved) output.Release("automatic authored candidate requested");
            else output.Acquire("Lab", "Lab mode " + mode);
            LastError = ""; Mode = mode; elapsed = 0; velocity = Vector3.zero; Log("mode " + mode);
        }
        public void Take(string id, bool automatic)
        {
            var d = NativeBroadcastCameraRegistry.Find(id);
            if (!d) throw new ArgumentException("Unknown cameraId " + id);
            var ids = (d.targetIds ?? Array.Empty<string>()).Select(RemapId).Where(v => v != null).ToArray();
            if (ids.Length > 0) SelectTargets(ids);
            focalLength=d.focalLength; autoZoom=d.autoZoom; speed=d.movementSpeed; stability=d.stability; angularSpeed=d.angularSpeed; acceleration=d.acceleration; fpvCoupling=d.fpvCoupling;roll=d.fpvRoll;
            SelectedId = id; SetMode(automatic ? NativeCameraLabMode.AutoSaved : NativeCameraLabMode.ManualTake);
        }
        public void SetPaused(bool value) { worldPaused = value; Time.timeScale = value ? 0 : 1; NativeAudioRuntime.SetPaused(value); Log(value ? "world paused; Scout remains live" : "world resumed"); }
        public void SetPose(Vector3 position, Quaternion rotation) { if (!state) throw new InvalidOperationException("Lab not running"); state.transform.SetPositionAndRotation(position, rotation); velocity = Vector3.zero; }
        void Log(string message) { Journal.Add(DateTime.UtcNow.ToString("O") + " " + message); if (Journal.Count > 250) Journal.RemoveAt(0); }
        public static InputActionAsset DefaultControls()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>(); var map = asset.AddActionMap("CameraLab");
            var movement = map.AddAction("Move",InputActionType.Value); movement.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d"); movement.AddBinding("<Gamepad>/leftStick");
            var elevation = map.AddAction("Lift",InputActionType.Value); elevation.AddCompositeBinding("1DAxis").With("Positive","<Keyboard>/e").With("Negative","<Keyboard>/q"); elevation.AddCompositeBinding("1DAxis").With("Positive","<Gamepad>/rightTrigger").With("Negative","<Gamepad>/leftTrigger");
            map.AddAction("Look",InputActionType.Value,"<Mouse>/delta"); map.AddAction("PadLook",InputActionType.Value,"<Gamepad>/rightStick");
            foreach (var pair in new[]{new[]{"Fast","<Keyboard>/leftShift"},new[]{"Fine","<Keyboard>/leftCtrl"},new[]{"Zoom","<Mouse>/scroll/y"},new[]{"Escape","<Keyboard>/escape"},new[]{"Pause","<Keyboard>/p"},new[]{"Capture","<Mouse>/rightButton"},new[]{"Slower","<Keyboard>/leftBracket"},new[]{"Faster","<Keyboard>/rightBracket"}}) map.AddAction(pair[0], pair[0]=="Zoom"?InputActionType.Value:InputActionType.Button,pair[1]);
            map["Escape"].AddBinding("<Gamepad>/buttonEast"); map["Pause"].AddBinding("<Gamepad>/start"); map["Capture"].AddBinding("<Gamepad>/rightShoulder"); map["Capture"].AddBinding("<Keyboard>/f7");
            map["PadLook"].AddCompositeBinding("2DVector").With("Up","<Keyboard>/upArrow").With("Down","<Keyboard>/downArrow").With("Left","<Keyboard>/leftArrow").With("Right","<Keyboard>/rightArrow"); map["Fast"].AddBinding("<Gamepad>/leftShoulder");
            return asset;
        }
        void SetupInputs()
        {
            if (!Controls) Controls = NativeContent.Load<InputActionAsset>("NativeOnline/Broadcast/CameraLabControls");
            input = Controls ? Instantiate(Controls) : DefaultControls();
            var map = input.FindActionMap("CameraLab",true); move=map["Move"];lift=map["Lift"];look=map["Look"];padLook=map["PadLook"];fast=map["Fast"];fine=map["Fine"];zoom=map["Zoom"];escape=map["Escape"];pause=map["Pause"];capture=map["Capture"];slower=map["Slower"];faster=map["Faster"]; map.Enable();
            move.performed += ctx => { if (CaptureInput) pendingMove = ctx.ReadValue<Vector2>(); };
            lift.performed += ctx => { if (CaptureInput) pendingLift = ctx.ReadValue<float>(); };
            padLook.performed += ctx => { if (CaptureInput) pendingTurn = ctx.ReadValue<Vector2>(); };
        }
        public void ReleaseInput() { CaptureInput = false; velocity = Vector3.zero; pendingMove = pendingTurn = Vector2.zero; pendingLift = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        void OnApplicationFocus(bool value) { if (!value) ReleaseInput(); }
        bool TextOrPanelFocus()
        {
            if (GUIUtility.hotControl != 0 || GUI.GetNameOfFocusedControl().StartsWith("LabText",StringComparison.Ordinal)) return true;
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject &&
                (es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() || es.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>())) return true;
            return false;
        }
        void Update()
        {
            if (!running || !state || input == null) return;
            if (escape.WasPressedThisFrame()) { ReleaseInput(); return; }
            if (!Application.isFocused || TextOrPanelFocus()) { ReleaseInput(); return; }
            if (pause.WasPressedThisFrame()) SetPaused(!worldPaused);
            if (capture.WasPressedThisFrame())
            {
                bool over = showPanel && Mouse.current != null && panel.Contains(new Vector2(Mouse.current.position.x.ReadValue(), Screen.height-Mouse.current.position.y.ReadValue()));
                if (!over || !(capture.activeControl?.device is Mouse)) { CaptureInput = !CaptureInput; Cursor.lockState = CaptureInput ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !CaptureInput; }
            }
            if (slower.WasPressedThisFrame()) speed = Mathf.Max(.5f,speed*.8f);
            if (faster.WasPressedThisFrame()) speed = Mathf.Min(200,speed*1.25f);
            if (!CaptureInput) { velocity = Vector3.zero; return; }
            float dt = Mathf.Min(.05f, Time.unscaledDeltaTime), multiplier = fast.IsPressed()?4:fine.IsPressed()?.15f:1;
            Vector2 m = move.ReadValue<Vector2>(), delta = look.ReadValue<Vector2>()*sensitivity + padLook.ReadValue<Vector2>()*angularSpeed*dt;
            if (m == Vector2.zero) m = pendingMove;
            float up = lift.ReadValue<float>(); if (up == 0) up = pendingLift;
            if (padLook.ReadValue<Vector2>() == Vector2.zero) delta += pendingTurn * angularSpeed * dt;
            pendingMove = pendingTurn = Vector2.zero; pendingLift = 0;
            if (m.sqrMagnitude>0 || delta.sqrMagnitude>0 || up!=0 || zoom.ReadValue<float>()!=0) ActualInputFrames++;
            if (Mode==NativeCameraLabMode.Scout || Mode==NativeCameraLabMode.ManualFlightAutoGimbal || Mode==NativeCameraLabMode.Fpv)
            {
                Vector3 desired=(state.transform.forward*m.y+state.transform.right*m.x+Vector3.up*up)*speed*multiplier;
                velocity=Vector3.MoveTowards(velocity,desired,acceleration*dt);state.transform.position+=velocity*dt;
            }
            if (Mode==NativeCameraLabMode.Scout || Mode==NativeCameraLabMode.AutoFlightManualGimbal || Mode==NativeCameraLabMode.Fpv)
            {
                Vector3 e=state.transform.eulerAngles;float pitch=Mathf.DeltaAngle(0,e.x); state.transform.rotation=Quaternion.Euler(Mathf.Clamp(pitch-delta.y,-89,89),e.y+delta.x, Mode==NativeCameraLabMode.Fpv ? roll + m.x*20*fpvCoupling*(1-stability) : 0);
            }
            focalLength = Mathf.Clamp(focalLength-zoom.ReadValue<float>()*.035f,8,300);
        }
        Bounds SubjectBounds()
        {
            return NativeBroadcastAuthoredCamera.GroupBounds(TargetIds.Select(id=>Seat(id)).Where(seat=>seat>=0).Select(seat=>Race.racers[seat].transform).ToArray());
        }
        void FixedUpdate() { if (running && !worldPaused) simulationTick++; }
        void LateUpdate()
        {
            if (!running || !state || !output) return;
            presentationTime=Time.realtimeSinceStartupAsDouble;
            if (Mode == NativeCameraLabMode.AutoSaved) return;
            float dt=Mathf.Min(.05f,Time.unscaledDeltaTime);elapsed+=dt;presentationTime=Time.realtimeSinceStartupAsDouble;
            string id="scout:"+sessionId;
            if (Mode==NativeCameraLabMode.ManualTake || Mode==NativeCameraLabMode.AutoSaved || Mode==NativeCameraLabMode.AutoFlightManualGimbal)
            {
                var definition=NativeBroadcastCameraRegistry.Find(SelectedId);
                if(!definition){LastError="Selected cameraId no longer exists";Mode=NativeCameraLabMode.Scout;}
                else
                {
                    if(!instances.TryGetValue(SelectedId,out var instance)||!instance)instances[SelectedId]=instance=NativeBroadcastCameraRegistry.CreateInstance(definition,"Lab");
                    Transform a=TargetIds.Length>0 && Seat(TargetIds[0])>=0?Race.racers[Seat(TargetIds[0])].transform:null;
                    Transform b=TargetIds.Length>1 && Seat(TargetIds[1])>=0?Race.racers[Seat(TargetIds[1])].transform:null;
                    definition.EvaluateGroupInto(instance,TargetIds.Select(target=>Seat(target)).Where(seat=>seat>=0).Select(seat=>Race.racers[seat].transform).ToArray(),elapsed);
                    if(Mode==NativeCameraLabMode.AutoFlightManualGimbal)state.transform.position=instance.transform.position;
                    else { state.transform.SetPositionAndRotation(instance.transform.position,instance.transform.rotation); NativeBroadcastManualCamera.CopyLens(instance,state); }
                    id=SelectedId;
                }
            }
            Bounds bounds=SubjectBounds();
            if (Mode==NativeCameraLabMode.FramingAutopilot && TargetIds.Length>0)
            {
                var car=Race.racers[Seat(TargetIds[0])].transform;
                var destination=bounds.center-car.forward*16+Vector3.up*8;
                state.transform.position=Vector3.MoveTowards(state.transform.position,destination,speed*dt);
            }
            if (Mode==NativeCameraLabMode.ManualFlightAutoGimbal || Mode==NativeCameraLabMode.FramingAutopilot || (Mode==NativeCameraLabMode.ManualTake && NativeBroadcastCameraRegistry.Find(SelectedId).kind != NativeBroadcastAuthoredCameraKind.FixedUltraWide && NativeBroadcastCameraRegistry.Find(SelectedId).kind != NativeBroadcastAuthoredCameraKind.FixedLow))
            {
                var direction=bounds.center-state.transform.position;
                if(direction.sqrMagnitude>.01f)state.transform.rotation=Quaternion.RotateTowards(state.transform.rotation,Quaternion.LookRotation(direction),angularSpeed*(1-stability*.7f)*dt);
            }
            FramingLimit="";
            if(autoZoom && Mode!=NativeCameraLabMode.Scout)
            {
                var inv=Quaternion.Inverse(state.transform.rotation);float tan=0;
                for(int i=0;i<8;i++)
                {
                    Vector3 p=inv*(new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z)-state.transform.position);
                    if(p.z<.5f){FramingLimit="Subject behind camera; selection retained";continue;}
                    tan=Mathf.Max(tan,Mathf.Abs(p.y)/(p.z*.78f),Mathf.Abs(p.x)/(p.z*.78f*Mathf.Max(.5f,Race.raceCamera.aspect)));
                }
                float wanted=24/(2*Mathf.Max(.001f,tan)); if(wanted<8)FramingLimit="Group wider than lens range; selection retained";
                focalLength=Mathf.MoveTowards(focalLength,Mathf.Clamp(wanted,8,300),80*dt);
            }
            state.usePhysicalProperties=true;state.focalLength=focalLength;
            // One reusable Cinemachine rotation adapter; no Brain competing for Program ownership.
            var fixedDefinition=NativeBroadcastCameraRegistry.Find(SelectedId);
            if((Mode==NativeCameraLabMode.ManualTake || Mode==NativeCameraLabMode.AutoSaved) && fixedDefinition && fixedDefinition.kind==NativeBroadcastAuthoredCameraKind.FixedTelePan)
                ApplyPanTiltAdapter();
            if(Time.unscaledTime>=nextSample && (Mode==NativeCameraLabMode.Scout || Mode==NativeCameraLabMode.ManualFlightAutoGimbal || Mode==NativeCameraLabMode.Fpv))
            {nextSample=Time.unscaledTime+.1f;rawSamples.Add(state.transform.position);sampleTimes.Add(elapsed);if(rawSamples.Count>12000){rawSamples.RemoveAt(0);sampleTimes.RemoveAt(0);}}
            output.Submit("Lab",id,state);
        }
        public void ApplyAutomaticFraming(Camera camera,float dt)
        {
            if(!autoZoom)return;
            Bounds group=SubjectBounds();Quaternion inv=Quaternion.Inverse(camera.transform.rotation);float tan=0;FramingLimit="";
            for(int i=0;i<8;i++){Vector3 p=inv*(new Vector3((i&1)==0?group.min.x:group.max.x,(i&2)==0?group.min.y:group.max.y,(i&4)==0?group.min.z:group.max.z)-camera.transform.position);if(p.z<.5f){FramingLimit="Subject behind camera; selection retained";continue;}tan=Mathf.Max(tan,Mathf.Abs(p.y)/(p.z*.78f),Mathf.Abs(p.x)/(p.z*.78f*Mathf.Max(.5f,Race.raceCamera.aspect)));}
            float wanted=24/(2*Mathf.Max(.001f,tan));if(wanted<8)FramingLimit="Group wider than lens range; selection retained";
            focalLength=Mathf.MoveTowards(focalLength,Mathf.Clamp(wanted,8,300),80*Mathf.Min(.05f,dt));camera.focalLength=focalLength;
        }
        void ApplyPanTiltAdapter()
        {
            if(!cm) { var go=new GameObject("Lab Cinemachine pan adapter") {hideFlags=HideFlags.HideAndDontSave}; cm=go.AddComponent<CinemachineCamera>();cm.enabled=false;panTilt=go.AddComponent<CinemachinePanTilt>();panTilt.ReferenceFrame=CinemachinePanTilt.ReferenceFrames.World;panTilt.PanAxis.Recentering.Enabled=false;panTilt.TiltAxis.Recentering.Enabled=false; }
            cm.transform.position=state.transform.position;Vector3 e=state.transform.eulerAngles;panTilt.PanAxis.Value=e.y;panTilt.TiltAxis.Value=Mathf.DeltaAngle(0,e.x);
            cm.Lens.FieldOfView=state.fieldOfView;cm.InternalUpdateCameraState(Vector3.up,-1);
            state.transform.rotation=cm.State.GetFinalOrientation();
        }
        public string SaveStation(string name, NativeBroadcastAuthoredCameraKind kind,string cameraId=null)
        {
            if(!state)throw new InvalidOperationException("Lab not running");
            var d=string.IsNullOrEmpty(cameraId)?null:NativeBroadcastCameraRegistry.Find(cameraId);
            if(!string.IsNullOrEmpty(cameraId)&&!d)throw new ArgumentException("Unknown station to update");
            if(!d){var go=new GameObject(string.IsNullOrWhiteSpace(name)?"Lab Station":name);d=go.AddComponent<NativeBroadcastAuthoredCamera>();d.cameraId=Guid.NewGuid().ToString("N");}else if(!string.IsNullOrWhiteSpace(name))d.name=name;d.transform.SetPositionAndRotation(state.transform.position,state.transform.rotation);
            d.kind=kind;d.focalLength=state.focalLength;d.sensorSize=state.sensorSize;d.lensShift=state.lensShift;d.gateFit=state.gateFit;d.targetIds=(string[])TargetIds.Clone();d.controlMode=Mode;d.movementSpeed=speed;d.stability=stability;d.autoZoom=autoZoom;d.angularSpeed=angularSpeed;d.acceleration=acceleration;d.fpvCoupling=fpvCoupling;d.fpvRoll=roll;
            d.GetComponent<Camera>().nearClipPlane=state.nearClipPlane;d.GetComponent<Camera>().farClipPlane=state.farClipPlane;
            d.rawFlightSamples=rawSamples.ToArray();d.rawFlightTimes=sampleTimes.ToArray();d.ApplyPhysicalLens();SelectedId=d.cameraId;
            SaveLayout(LayoutPath);Log("saved station "+SelectedId);return SelectedId;
        }
        public void SaveLayout(string path)
        {
            var root=Race.transform;var layout=new NativeCameraLabLayout {layoutId=layoutId,scenePath=Race.gameObject.scene.path,trackId=Race.trackId,trackRoot=root.name,
                stations=NativeBroadcastCameraRegistry.Definitions.Select(d=>new NativeCameraLabStation {
                    cameraId=d.cameraId,name=d.name,worldPosition=d.transform.position,rotation=d.transform.rotation,localPosition=root.InverseTransformPoint(d.transform.position),localRotation=Quaternion.Inverse(root.rotation)*d.transform.rotation,
                    sensorSize=d.sensorSize,lensShift=d.lensShift,focalLength=d.focalLength,gateFit=(int)d.gateFit,nearClip=d.GetComponent<Camera>().nearClipPlane,farClip=d.GetComponent<Camera>().farClipPlane,
                    kind=(int)d.kind,mode=(int)d.controlMode,speed=d.movementSpeed,stability=d.stability,coverageRadius=d.coverageRadius,pathDuration=d.pathDuration,loopPath=d.loopPath,autoEligible=d.autoEligible,
                    autoZoom=d.autoZoom,minimumFov=d.minimumFov,maximumFov=d.maximumFov,targetScreenHeight=d.targetScreenHeight,mountOffset=d.mountOffset,angularSpeed=d.angularSpeed,acceleration=d.acceleration,fpvCoupling=d.fpvCoupling,roll=d.fpvRoll,
                    hasCoverageStart=d.coverageStart,hasCoverageEnd=d.coverageEnd,coverageStartLocal=d.coverageStart?root.InverseTransformPoint(d.coverageStart.position):Vector3.zero,coverageEndLocal=d.coverageEnd?root.InverseTransformPoint(d.coverageEnd.position):Vector3.zero,
                    localPathSamples=d.path.Where(t=>t).Select(t=>root.InverseTransformPoint(t.position)).ToArray(),targetIds=d.targetIds,rawSamples=d.rawFlightSamples,sampleTimes=d.rawFlightTimes,pathSamples=d.path.Where(t=>t).Select(t=>t.position).ToArray() }).ToArray()};
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(layout,true));if(File.Exists(path))File.Delete(path);File.Move(temp,path);
        }
        public void LoadLayout(string path)
        {
            LoadLayoutJson(File.ReadAllText(path));
        }
        public void LoadLayoutJson(string json)
        {
            var layout=JsonUtility.FromJson<NativeCameraLabLayout>(json);
            if(layout.schema!=1 || layout.trackId!=Race.trackId || layout.scenePath!=Race.gameObject.scene.path || layout.trackRoot!=Race.transform.name)throw new InvalidOperationException("Layout scene/track/root mismatch");
            layoutId=layout.layoutId;
            foreach(var row in layout.stations)
            {
                var d=NativeBroadcastCameraRegistry.Find(row.cameraId);
                if(!d) d=new GameObject(row.name).AddComponent<NativeBroadcastAuthoredCamera>();
                d.cameraId=row.cameraId;d.transform.SetPositionAndRotation(Race.transform.TransformPoint(row.localPosition),Race.transform.rotation*row.localRotation);
                RestoreDefinition(d,row,Race.transform);
                d.targetIds=(row.targetIds??Array.Empty<string>()).Select(RemapId).Where(id=>id!=null).ToArray();
                        }
            Log("loaded layout "+layoutId);
        }
        string RemapId(string id) => NativeBroadcastCameraRegistry.BindingSeat(id, Race.racers.Length) is int seat && seat >= 0 ? RacerId(seat) : null;
        public static void RestoreDefinition(NativeBroadcastAuthoredCamera d, NativeCameraLabStation row, Transform root)
        {
            d.kind=(NativeBroadcastAuthoredCameraKind)row.kind;d.focalLength=row.focalLength;d.sensorSize=row.sensorSize;d.lensShift=row.lensShift;d.gateFit=(Camera.GateFitMode)row.gateFit;d.coverageRadius=row.coverageRadius;
            d.movementSpeed=row.speed;d.stability=row.stability;d.controlMode=(NativeCameraLabMode)row.mode;d.pathDuration=row.pathDuration;d.loopPath=row.loopPath;d.autoEligible=row.autoEligible;d.autoZoom=row.autoZoom;
            d.minimumFov=row.minimumFov>0?row.minimumFov:10;d.maximumFov=row.maximumFov>0?row.maximumFov:90;d.targetScreenHeight=row.targetScreenHeight>0?row.targetScreenHeight:.25f;d.mountOffset=row.mountOffset;
            d.angularSpeed=row.angularSpeed>0?row.angularSpeed:140;d.acceleration=row.acceleration>0?row.acceleration:30;d.fpvCoupling=row.fpvCoupling;d.fpvRoll=row.roll;
            d.targetIds=row.targetIds??Array.Empty<string>();d.rawFlightSamples=row.rawSamples??Array.Empty<Vector3>();d.rawFlightTimes=row.sampleTimes??Array.Empty<float>();
            d.GetComponent<Camera>().nearClipPlane=row.nearClip;d.GetComponent<Camera>().farClipPlane=row.farClip;
            foreach(var old in d.path)if(old) {if(Application.isPlaying)Destroy(old.gameObject);else UnityEditorDestroy(old.gameObject);}
            Vector3[] points=row.localPathSamples!=null&&row.localPathSamples.Length>0?row.localPathSamples.Select(root.TransformPoint).ToArray():row.pathSamples??Array.Empty<Vector3>();
            d.path=points.Select((v,i)=>{var p=new GameObject("Lab Path "+i).transform;p.SetParent(d.transform);p.position=v;return p;}).ToArray();
            RestoreMarker(ref d.coverageStart,row.hasCoverageStart,root.TransformPoint(row.coverageStartLocal),d.transform,"Coverage start");
            RestoreMarker(ref d.coverageEnd,row.hasCoverageEnd,root.TransformPoint(row.coverageEndLocal),d.transform,"Coverage end");d.ApplyPhysicalLens();
        }
        static void UnityEditorDestroy(GameObject go) { DestroyImmediate(go); }
        static void RestoreMarker(ref Transform marker,bool exists,Vector3 position,Transform parent,string name)
        {if(!exists){if(marker){if(Application.isPlaying)Destroy(marker.gameObject);else DestroyImmediate(marker.gameObject);}marker=null;return;}if(!marker)marker=new GameObject(name).transform;marker.SetParent(parent);marker.position=position;}
        public void ClearFlightRecording() {rawSamples.Clear();sampleTimes.Clear();elapsed=0;nextSample=0;Log("raw flight recording reset");}
        public void SaveEditedRoute(Vector3[] edited=null,float duration=8,bool loop=true)
        {
            var d=NativeBroadcastCameraRegistry.Find(SelectedId);if(!d)throw new InvalidOperationException("Save/select a station first");
            Vector3[] points=edited;
            if(points==null)
            {
                var retained=new List<Vector3>();foreach(var point in rawSamples)if(retained.Count==0 || Vector3.Distance(retained[retained.Count-1],point)>=1)retained.Add(point);
                points=retained.ToArray();
            }
            if(points.Length<2 || points.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)))throw new ArgumentException("An edited route needs at least two finite, distinct flight points");
            foreach(var old in d.path)if(old)Destroy(old.gameObject);
            d.path=points.Select((v,i)=>{var t=new GameObject("Edited spline "+i).transform;t.SetParent(d.transform);t.position=v;return t;}).ToArray();
            d.kind=NativeBroadcastAuthoredCameraKind.HelicopterWide;d.pathDuration=Mathf.Max(.1f,duration);d.loopPath=loop;d.rawFlightSamples=rawSamples.ToArray();d.rawFlightTimes=sampleTimes.ToArray();
            SaveLayout(LayoutPath);Log("edited spline saved separately from raw flight, points="+points.Length);
        }
        void OnGUI()
        {
            if(!running || !showPanel || CaptureInput)return;
            panel.height=Mathf.Clamp(Screen.height-24,180,600);panel.width=Mathf.Min(340,Screen.width-24);panel.x=Mathf.Clamp(panel.x,0,Mathf.Max(0,Screen.width-panel.width));panel.y=Mathf.Clamp(panel.y,0,Mathf.Max(0,Screen.height-panel.height));
            panel=GUI.Window(190819, panel, DrawPanel,"FOREST CAMERA LAB");
        }
        void DrawPanel(int window)
        {
            panelScroll=GUILayout.BeginScrollView(panelScroll,GUILayout.Height(panel.height-35));
            GUILayout.Label("RMB / F7 / RB 捕获 · Esc 释放 · WASD / 左杆飞行\nQ/E 升降 · Shift 加速 · Ctrl 精细 · P 暂停世界");
            GUILayout.Label("模式 "+Mode+"  | 输出 "+output?.CameraId);
            GUILayout.BeginHorizontal();if(GUILayout.Button("自由飞行"))SetMode(NativeCameraLabMode.Scout);if(GUILayout.Button("手飞自动拍"))SetMode(NativeCameraLabMode.ManualFlightAutoGimbal);GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();if(GUILayout.Button("航线 / 手动云台"))Try(()=>SetMode(NativeCameraLabMode.AutoFlightManualGimbal));if(GUILayout.Button("构图飞行"))SetMode(NativeCameraLabMode.FramingAutopilot);if(GUILayout.Button("FPV"))SetMode(NativeCameraLabMode.Fpv);GUILayout.EndHorizontal();
            autoZoom=GUILayout.Toggle(autoZoom,"独立自动变焦");GUILayout.Label("焦距 "+focalLength.ToString("F1")+" mm");focalLength=GUILayout.HorizontalSlider(focalLength,8,300);
            GUILayout.Label("速度 "+speed.ToString("F1")+" m/s");speed=GUILayout.HorizontalSlider(speed,.5f,120);GUILayout.Label("稳定 "+stability.ToString("F2"));stability=GUILayout.HorizontalSlider(stability,0,1);
            GUILayout.Label("选主体（固定身份，多选最多4车）");
            GUILayout.BeginHorizontal();for(int i=0;i<Race.racers.Length && i<8;i++){bool selected=TargetIds.Contains(RacerId(i));bool next=GUILayout.Toggle(selected,i.ToString(),"Button");if(next!=selected)targetToggle=i;}GUILayout.EndHorizontal();
            if(targetToggle>=0){var ids=TargetIds.ToList();string id=RacerId(targetToggle);if(ids.Contains(id)){if(ids.Count>1)ids.Remove(id);}else if(ids.Count<4)ids.Add(id);SelectTargets(ids.ToArray());targetToggle=-1;}
            if(GUILayout.Button(worldPaused?"继续世界":"暂停世界（仍可飞行）"))SetPaused(!worldPaused);
            if(NativeBroadcastCameraRegistry.Find(SelectedId)&&GUILayout.Button("提交选中机位的位置 / 镜头"))Try(()=>SaveStation(null,NativeBroadcastCameraRegistry.Find(SelectedId).kind,SelectedId));
            if(GUILayout.Button("到这里放固定长焦 / 保存"))Try(()=>SaveStation("Forest tele "+NativeBroadcastCameraRegistry.Definitions.Length,NativeBroadcastAuthoredCameraKind.FixedTelePan));
            foreach(var d in NativeBroadcastCameraRegistry.Definitions.Take(6))
            {GUILayout.BeginHorizontal();GUILayout.Label(d.name);if(GUILayout.Button("人工TAKE"))Try(()=>Take(d.cameraId,false));if(GUILayout.Button("自动调用"))Try(()=>Take(d.cameraId,true));GUILayout.EndHorizontal();}
            GUILayout.BeginHorizontal();if(GUILayout.Button("重录飞行"))ClearFlightRecording();if(GUILayout.Button("样本转编辑航线"))Try(()=>SaveEditedRoute());GUILayout.EndHorizontal();
            GUILayout.Label("FPV 联动 "+fpvCoupling.ToString("F2"));fpvCoupling=GUILayout.HorizontalSlider(fpvCoupling,0,1);
            GUILayout.Label("横滚 "+roll.ToString("F1")+"°");roll=GUILayout.HorizontalSlider(roll,-45,45);
            if(GUILayout.Button("加载已保存布局"))Try(()=>LoadLayout(LayoutPath));
            GUILayout.Label("实际输入帧 "+ActualInputFrames+" | AI "+Race.racers.Length+" | tick "+simulationTick);
            if(FramingLimit!="")GUILayout.Label(FramingLimit);if(LastError!="")GUILayout.Label(LastError);GUILayout.EndScrollView();GUI.DragWindow(new Rect(0,0,340,22));
        }
        void Try(Action action) { try { action(); LastError=""; } catch(Exception e){LastError=e.Message;} }
    }
    public sealed partial class NativeRace
    {
        public string LabVehicleId(int seat) => raceConfig?.roster != null && seat >= 0 && seat < raceConfig.roster.Count ? raceConfig.roster[seat].vehicleId : null;
        public string LabCharacterId(int seat) => raceConfig?.roster != null && seat >= 0 && seat < raceConfig.roster.Count ? raceConfig.roster[seat].characterId : null;
    }

}
