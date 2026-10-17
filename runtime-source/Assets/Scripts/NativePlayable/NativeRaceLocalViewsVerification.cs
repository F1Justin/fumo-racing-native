using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
namespace Fumo.NativePlayable
{
    public sealed class NativeLocalViewsVerifierHost : MonoBehaviour
    {
        public static void StartPersistent(IEnumerator verification)
        {
            var host = new GameObject("Local race views verification host");
            DontDestroyOnLoad(host);
            host.AddComponent<NativeLocalViewsVerifierHost>().StartCoroutine(verification);
        }
    }
    public sealed partial class NativeRace
    {
        [Serializable] sealed class LocalViewRuntimeReport
        {
            public bool passed, accepted, ordinaryPixelsAccepted, physicalJoinAccepted, renderEvidenceAvailable, nonrenderedDiagnostic;
            public string[] pending;
            public string inputScope;
            public string error, assemblySHA256, sourceSHA256, graphicsDeviceType, phase;
            public int engineErrors;
            public string engineErrorScope="NativeRace.Start log subscription through two frames after injected-device cleanup";
            public NativeLocalViewsSource expectedSource;
            public List<string> checks = new List<string>();
            public List<LocalViewRuntimeSample> views = new List<LocalViewRuntimeSample>();
            public string scope = "Actual owner-bound production views and per-human consumers; required ordinary 2/3/4-player pixel pairs and physical device joins remain open";
        }
        [Serializable] sealed class LocalViewRuntimeSample
        {
            public int slot, racer, playerIndex, cameraId, targetId, frame, itemInventoryId;
            public string stage="pre-assert-binding";
            public string inputUserId, time, lap, speed;
            public float viewportX, viewportY, viewportWidth, viewportHeight;
            public NativeRaceHud.LocalCanvasSample[] canvases;
            public NativeAuthoredUi.LocalLeafSample[] leaves;
            public NativeAuthoredUi.DynamicHudSample[] hud;
        }
        int localViewsOnGuiFrame = -1;
        internal IEnumerator CheckLocalRaceViewsLive()
        {
            bool nonrendered = Environment.GetCommandLineArgs().Contains("--local-race-views-nonrendered-diagnostic");
            var report = new LocalViewRuntimeReport { nonrenderedDiagnostic=nonrendered, pending=nonrendered ? new[]{"actual OnGUI", "visible TMP values", "viewport leaf geometry/alpha", "ordinary 2/3/4 pixel pairs", "physical device joins", "leave/cleanup scene lifecycle"} : new[]{"ordinary 2/3/4 pixel pairs", "physical device joins", "leave/cleanup scene lifecycle"} };
            if(nonrendered) report.scope="Nonrendered actual production objects/routes; OnGUI/pixels and physical join evidence remain pending";
            var inputs = new List<InputDevice>();
            report.inputScope = nonrendered ? "NativeInjected device-state via actual PIM/PlayerInput/InputUser and production menu submits" : "existing actual owners; physical event evidence pending";
            var stack = new Stack<IEnumerator>(); stack.Push(CheckLocalRaceViewsCore(report, inputs));
            while (stack.Count > 0)
            {
                bool next; object value;
                try { next = stack.Peek().MoveNext(); value = next ? stack.Peek().Current : null; }
                catch (Exception error) { report.error = error.ToString(); report.passed = false; break; }
                if (!next) { stack.Pop(); continue; }
                if (value is IEnumerator nested) { stack.Push(nested); continue; }
                yield return value;
            }
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception error) { report.error = error.ToString(); report.passed = false; }
            }
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            foreach(var input in inputs)
                if(input.added)
                    try { InputSystem.RemoveDevice(input); } catch(Exception error) {report.error=error.ToString();report.passed=false;}
#endif
            yield return null; yield return null;
            report.engineErrors=errorCount;
            if(report.engineErrors>0)
            { report.checks.Add("FAIL engine error/exception count="+report.engineErrors); report.passed=false; }
            else report.checks.Add("PASS engine error/exception count=0");
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(Path.Combine(reportDirectory,"local-race-views-report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("LOCAL_RACE_VIEWS_RESULT passed="+report.passed);
            Application.Quit(report.passed ? 0 : 2);
        }
        IEnumerator CheckLocalRaceViewsCore(LocalViewRuntimeReport report, List<InputDevice> inputs)
        {
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
            if(report.nonrenderedDiagnostic) yield return PrepareInjectedLocalHumansForRace(4, inputs);
#else
            if(report.nonrenderedDiagnostic) throw new InvalidOperationException("Injected local race diagnostic requires Player input runtime");
#endif
            double deadline = Time.realtimeSinceStartupAsDouble + 120;
            while (localRaceViews.Count < 2 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Action<bool, string> require = (pass, text) => { report.checks.Add((pass ? "PASS " : "FAIL ") + text); if (!pass) throw new InvalidOperationException(text); };
            {
                require(localRaceViews.Count >= 2, "actual joined owners entered production local race views");
                if (!report.nonrenderedDiagnostic)
                {
                    while (localViewsOnGuiFrame < 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                    require(localViewsOnGuiFrame >= 0, "actual OnGUI populated per-view HUD");
                    report.renderEvidenceAvailable=true;
                }
                yield return null; yield return null;
                report.assemblySHA256 = ItemPrefabFileSha256(typeof(NativeRace).Assembly.Location);
                report.sourceSHA256 = ItemPrefabBytesSha256(localViewsSource.bytes);
                for (int count = 1; count <= 4; count++)
                    for (int index = 0; index < count; index++)
                        require(OriginalLocalViewport(count,index) == localViewReference.viewports.Single(row => row.count == count && row.index == index).Value, "source viewport " + count + "/" + index);
                require(localRaceViews.Select(view => view.camera.GetInstanceID()).Distinct().Count() == localRaceViews.Count, "unique actual player cameras");
                report.expectedSource=localViewReference;report.graphicsDeviceType=SystemInfo.graphicsDeviceType.ToString();report.phase=phase.ToString();
                foreach(var view in localRaceViews)
                {
                    var owner=SourceActualLocalOwner(view.slot);
                    report.views.Add(new LocalViewRuntimeSample {slot=view.slot,racer=view.racer,playerIndex=owner?owner.playerIndex:-1,inputUserId=owner&&owner.user.valid?owner.user.id.ToString():"invalid",cameraId=view.camera.GetInstanceID(),targetId=view.rig.target?view.rig.target.GetInstanceID():0,frame=Time.frameCount,itemInventoryId=view.item&&view.item.inventory?view.item.inventory.GetInstanceID():0,viewportX=view.camera.rect.x,viewportY=view.camera.rect.y,viewportWidth=view.camera.rect.width,viewportHeight=view.camera.rect.height,canvases=view.hud.ReadLocalCanvases()});
                }
                foreach (var view in localRaceViews)
                {
                    var owner = SourceActualLocalOwner(view.slot);
                    var human = sourceRaceState.Humans[view.slot];
                    require(owner && owner.user.valid && owner.user.id == view.ownerId && human.InputUserIndex == owner.user.index && human.RacerIndex == view.racer, "view binds exact actual owner/racer " + view.slot);
                    require(view.rig.target == racers[view.racer], "camera follows own racer " + view.slot);
                    require(view.camera.rect == localViewReference.viewports.Single(row => row.count == localRaceViews.Count && row.index == localRaceViews.IndexOf(view)).Value, "actual camera source viewport " + view.slot);
                    require(view.camera.nearClipPlane == localViewReference.cameraNear && view.camera.farClipPlane == localViewReference.cameraFar && view.camera.cullingMask == localViewReference.cameraMask && view.camera.depth == localViewReference.cameraDepth && !view.camera.targetTexture, "source camera fields " + view.slot);
                    var canvas = view.hud.ReadLocalCanvases();
                    require(canvas.Length > 0 && canvas.All(c => c.cameraId == view.camera.GetInstanceID() && c.renderMode == localViewReference.hudRenderMode && c.planeDistance == localViewReference.hudRuntimePlaneDistance && c.sortingOrder == localViewReference.hudSortingOrder && c.referenceWidth == localViewReference.referenceWidth && c.referenceHeight == localViewReference.referenceHeight && c.match == localViewReference.scalerMatch), "own camera/plane/scaler source binding " + view.slot);
                    require(view.item && view.item.inventory == racers[view.racer].GetComponent<NativeKartItemInventory>(), "item HUD consumes own inventory " + view.slot);
                    if (report.renderEvidenceAvailable)
                    {
                    var samples = view.hud.ReadOriginalDynamicHud();
                    var laps = samples.Single(row => row.path == "HUD/Canvas/lapsbg/Laps");
                    require(laps.text == Mathf.Clamp(progress[view.racer].CompletedLaps+1,1,RaceLaps)+"/"+RaceLaps, "actual rendered own lap text " + view.slot);
                    var leaves = view.hud.ReadLocalLeaves();
                    require(leaves.Length == 6 && leaves.All(leaf=>leaf.cameraId==view.camera.GetInstanceID() && leaf.active && leaf.alpha>0 && leaf.width>0 && leaf.height>0 && new Rect(leaf.x,leaf.y,leaf.width,leaf.height).Overlaps(view.camera.pixelRect)), "actual HUD leaves intersect own viewport " + view.slot);
                    require(view.hud.RaceFinished == progress[view.racer].Finished, "independent local finish consumer " + view.slot);
                    report.views.Add(new LocalViewRuntimeSample { stage="validated-consumer",slot=view.slot,racer=view.racer,playerIndex=owner.playerIndex,inputUserId=owner.user.id.ToString(),cameraId=view.camera.GetInstanceID(),targetId=view.rig.target.GetInstanceID(),frame=Time.frameCount,itemInventoryId=view.item.inventory.GetInstanceID(),viewportX=view.camera.rect.x,viewportY=view.camera.rect.y,viewportWidth=view.camera.rect.width,viewportHeight=view.camera.rect.height,canvases=canvas,leaves=leaves,hud=samples,time=samples.Single(row=>row.path=="HUD/Canvas/Time").text,lap=laps.text,speed=samples.Single(row=>row.path=="HUD/Canvas/SpeedGroup/Speed").text });
                    }
                    else
                    {
                        require(view.finish.Ended == progress[view.racer].Finished, "actual independent finish camera route " + view.slot);
                        report.views.Add(new LocalViewRuntimeSample {stage="validated-consumer",slot=view.slot,racer=view.racer,playerIndex=owner.playerIndex,inputUserId=owner.user.id.ToString(),cameraId=view.camera.GetInstanceID(),targetId=view.rig.target.GetInstanceID(),frame=Time.frameCount,itemInventoryId=view.item.inventory.GetInstanceID(),viewportX=view.camera.rect.x,viewportY=view.camera.rect.y,viewportWidth=view.camera.rect.width,viewportHeight=view.camera.rect.height,canvases=canvas});
                    }
                }
                if(report.renderEvidenceAvailable)
                {
                require(!SettingsOpen && (phase == Phase.Countdown || phase == Phase.Racing), "shared minimap GUI check runs in active race state");
                require(hud.OriginalMinimapBound(racers.Length, out string minimapDetail), "shared minimap actual visible sprite and racer points: " + minimapDetail);
                var globalMap = hud.ReadSharedMinimapCanvas();
                require(globalMap && globalMap.renderMode == RenderMode.ScreenSpaceOverlay && !globalMap.worldCamera, "shared global minimap remains full-screen overlay");
                var fullFrame = new Vector3[4]; ((RectTransform)globalMap.transform).GetWorldCorners(fullFrame);
                require(Mathf.Abs(fullFrame[2].x-fullFrame[0].x-Screen.width)<1 && Mathf.Abs(fullFrame[2].y-fullFrame[0].y-Screen.height)<1, "shared minimap canvas covers complete framebuffer");
                require(localRaceViews.All(view=>!view.hud.LocalMinimapVisible), "local HUD copies exclude shared minimap");
                }
                var router = itemWorld.GetComponent<NativeInsanityHudRouter>();
                require(router && router.humanPlayers.Length == localRaceViews.Count && localRaceViews.All(view => router.humanPlayers.Contains(racers[view.racer]) && router.hudCanvases[Array.IndexOf(router.humanPlayers,racers[view.racer])] == view.hud.PrepareInsanityPlayerHudCanvas()), "insanity exact per-human HUD consumer");
                if(report.nonrenderedDiagnostic)
                {
                    require(localRaceViews.Count==4,"actual four injected PIM owners entered production views");
                    var frozen=localRaceViews.Select(view=>new {view.slot,view.racer,view.ownerId,rect=view.camera.rect,cameraId=view.camera.GetInstanceID()}).ToArray();
                    var departingOwner=SourceActualLocalOwner(0);
                    var departingDevices=inputs.Where(input=>departingOwner.user.pairedDevices.Contains(input)).ToArray();
                    require(departingDevices.Length>0,"diagnostic removes exact injected devices paired to frozen first owner");
#if ENABLE_INPUT_SYSTEM && !UNITY_EDITOR
                    foreach(var input in departingDevices)InputSystem.RemoveDevice(input);
#endif
                    double leaveDeadline=Time.realtimeSinceStartupAsDouble+15;
                    while(SourceActualLocalOwner(0) && Time.realtimeSinceStartupAsDouble<leaveDeadline)yield return null;
                    yield return null; yield return null;
                    require(!SourceActualLocalOwner(0)&&!sourceRaceState.Humans[0].Joined,"real device lost invalidates frozen human owner");
                    require(localRaceViews.Count==4&&frozen.All(row=>localRaceViews.Any(view=>view.slot==row.slot&&view.racer==row.racer&&view.ownerId==row.ownerId&&view.camera.GetInstanceID()==row.cameraId&&view.camera.rect==row.rect)),"device leave preserves source initial camera/viewport/racer ownership");
                    var extraCameras=localRaceViews.Where(view=>view.camera!=raceCamera).Select(view=>view.camera).ToArray();
                    ExecuteMenuAction(NativeMenuAction.MainMenu);
                    yield return null; yield return null;
                    require(phase==Phase.Menu&&!sourceRaceState.InRace&&localRaceViews.Count==0,"production menu dispatcher executes source cleanup and disposes local HUD contexts");
                    require(raceCamera.rect==new Rect(0,0,1,1)&&extraCameras.All(camera=>!camera),"cleanup restores whole viewport and destroys extra cameras");
                    report.pending=new[]{"actual OnGUI", "visible TMP values", "viewport leaf geometry/alpha", "ordinary 2/3/4 pixel pairs", "physical device joins"};
                }
                report.passed = true;
            }

        }
    }
    public sealed partial class NativeRaceHud
    {
        [Serializable] public sealed class LocalCanvasSample
        { public string path; public bool active, enabled, rootCanvas, hasScaler, scalerEnabled, overrideSorting; public int canvasId, cameraId, renderMode, sortingOrder, targetDisplay, scaleMode, screenMatchMode, rootCanvasId, sortingLayerId; public float planeDistance, referenceWidth, referenceHeight, match, referencePixelsPerUnit, scaleFactor; }
        internal NativeAuthoredUi.LocalLeafSample[] ReadLocalLeaves() => authoredUi.ReadLocalLeaves();
        internal Canvas ReadSharedMinimapCanvas() => authoredUi?.SharedMinimapCanvas;
        internal bool LocalMinimapVisible => authoredUi != null && authoredUi.SharedMinimapVisible;
        static string LocalCanvasPath(Transform node)
        { return node.parent ? LocalCanvasPath(node.parent)+"/"+node.name : node.name; }
        public LocalCanvasSample[] ReadLocalCanvases()
        {
            var canvases = new List<Canvas>();
            if (canvas != null) canvases.Add(canvas.LocalCanvas);
            if (authoredUi != null) canvases.Add(authoredUi.LocalHudCanvas);
            return canvases.Select(value => { var scaler=value.GetComponent<CanvasScaler>();return new LocalCanvasSample{path=LocalCanvasPath(value.transform),active=value.gameObject.activeInHierarchy,enabled=value.enabled,rootCanvas=value.isRootCanvas,hasScaler=scaler,scalerEnabled=scaler&&scaler.enabled,overrideSorting=value.overrideSorting,targetDisplay=value.targetDisplay,scaleMode=scaler?(int)scaler.uiScaleMode:-1,screenMatchMode=scaler?(int)scaler.screenMatchMode:-1,rootCanvasId=value.rootCanvas?value.rootCanvas.GetInstanceID():0,sortingLayerId=value.sortingLayerID,referencePixelsPerUnit=scaler?scaler.referencePixelsPerUnit:-1,scaleFactor=scaler?scaler.scaleFactor:-1,canvasId=value.GetInstanceID(),cameraId=value.worldCamera?value.worldCamera.GetInstanceID():0,renderMode=(int)value.renderMode,sortingOrder=value.sortingOrder,planeDistance=value.planeDistance,referenceWidth=scaler?scaler.referenceResolution.x:-1,referenceHeight=scaler?scaler.referenceResolution.y:-1,match=scaler?scaler.matchWidthOrHeight:-1}; }).ToArray();
        }
    }
    public sealed partial class NativeUiCanvas { internal Canvas LocalCanvas => root.GetComponent<Canvas>(); }
    public sealed partial class NativeAuthoredUi
    {
        [Serializable] public sealed class LocalLeafSample
        { public string path; public bool active; public int cameraId; public float x,y,width,height,alpha; }
        internal LocalLeafSample[] ReadLocalLeaves()
        {
            return new[]{"TimeText","Time","lapsbg/LapsTxt","lapsbg/Laps","SpeedGroup/SpeedText","SpeedGroup/Speed"}.Select(path=>
            {
                var node=transforms["HUD/Canvas/"+path];var canvas=node.GetComponentInParent<Canvas>(true);
                var corners=new Vector3[4];node.GetWorldCorners(corners);
                var pixels=corners.Select(point=>RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,point)).ToArray();
                float alpha=node.GetComponent<Graphic>().color.a;
                foreach(var group in node.GetComponentsInParent<CanvasGroup>(true))alpha*=group.alpha;
                return new LocalLeafSample{path="HUD/Canvas/"+path,active=node.gameObject.activeInHierarchy,cameraId=canvas.worldCamera.GetInstanceID(),x=pixels.Min(v=>v.x),y=pixels.Min(v=>v.y),width=pixels.Max(v=>v.x)-pixels.Min(v=>v.x),height=pixels.Max(v=>v.y)-pixels.Min(v=>v.y),alpha=alpha};
            }).ToArray();
        }
        internal Canvas SharedMinimapCanvas => transforms["GlobalHUD/Canvas/minimap"].GetComponentInParent<Canvas>(true); internal bool SharedMinimapVisible => transforms["GlobalHUD/Canvas/minimap"].gameObject.activeInHierarchy; internal Canvas LocalHudCanvas => transforms["HUD/Canvas/TimeBG"].GetComponentInParent<Canvas>(true); }
}
