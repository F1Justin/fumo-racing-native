using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeSelectionPreviewLight
    {
        public string name;
        public Color color;
        public float intensity;
        public Quaternion rotation;
        public LightShadows shadows;
        public bool enabled;
    }
    [Serializable] public sealed class NativeSelectionPreviewBanner
    {
        public string id, sourcePath, sourceSHA256;
        public int width, height;
    }
    [Serializable] public sealed class NativeSelectionPreviewDefinition
    {
        public string sourceScene, sourceSHA256;
        public NativeSelectionPreviewBanner[] banners;
        public NativeSelectionPreviewPlayerDefinition[] players;
        public NativeMenuEnvironment environment;
        public NativeSelectionPreviewLight[] lights;
        public CameraClearFlags clearFlags;
        public Color background;
        public float fieldOfView;
        public Vector3 screenCameraPosition, screenCameraScale;
        public Quaternion screenCameraRotation;
        public float screenCameraNear, screenCameraFar, screenCameraDepth;
        public Vector3 previewCameraPosition;
        public Quaternion previewCameraRotation;
        public float previewFieldOfView = 60, previewNear = .1f, previewFar = 50, previewRotationSpeed = 15;
        public int previewWidth = 512, previewHeight = 512;
        public int previewDepth;
        [Header("Online player row: match the front three-quarter character portraits")]
        public Vector3 rosterModelEuler = new Vector3(0, 180, 0);
        [Tooltip("Horizontal angle between the vehicle forward direction and the screen plane: 0 = side, 90 = front.")]
        [Range(0, 90)] public float rosterAngleToScreen = 40;
        [Range(0, 89)] public float rosterElevation = 15;
        public bool rosterFacingLeft = true;
        [Min(1)] public float rosterFramingPadding = 1.1f;
        public Vector2Int rosterPreviewSize = new Vector2Int(128, 80);
    }

    public sealed partial class NativeRace
    {
        GameObject selectionPreviewRoot, selectionPreviewModel;
        Camera selectionPreviewCamera;
        RenderTexture selectionPreviewTexture;
        NativeSelectionPreviewDefinition selectionPreviewDefinition;
        NativeMenuEnvironment selectionPreviewEnvironmentAtRender;
        Light[] selectionPreviewLights;
        const int SelectionPreviewLayer = 31;
        int previewCharacter = -1, previewVehicle = -1, previewSkin = -1;
        float previewRotationStarted;
        void TickSelectionPreview()
        {
            if (TickLocalSelectionPreviews()) return;
            if (selectionPreviewDefinition == null)
                selectionPreviewDefinition = NativeSelectionPreviewSettings.Load();
            bool selection = phase == Phase.Menu && !settingsOpen && (hud.MenuPage == NativeMenuPage.Characters || hud.MenuPage == NativeMenuPage.Vehicles
                || hud.MenuPage == NativeMenuPage.Skins || hud.MenuPage == NativeMenuPage.Cups || hud.MenuPage == NativeMenuPage.Tracks || hud.MenuPage == NativeMenuPage.Custom);
            bool online = Net && Net.Open;
            if (online && (Net.InRace || Net.Loading)) selection = false;
            if (menuPresentation) menuPresentation.SetSelectionVisible(selection, selectionPreviewDefinition);
            if (online)
            {
                if ((Net.InRace || Net.Loading) && selectionPreviewRoot) selectionPreviewRoot.SetActive(false);
                return;
            }
            bool visible = phase == Phase.Menu && !settingsOpen && (hud.MenuPage == NativeMenuPage.Characters || hud.MenuPage == NativeMenuPage.Vehicles || hud.MenuPage == NativeMenuPage.Skins);
            if (!visible) { if (selectionPreviewRoot) selectionPreviewRoot.SetActive(false); return; }
            if (SuspendCharacterProfilePreview()) return;
            int ci = hud.MenuPage == NativeMenuPage.Characters ? selectableCharacters[hud.SelectedMenuIndex] : selectedCharacterIndex;
            int vi = hud.MenuPage == NativeMenuPage.Vehicles ? hud.SelectedMenuIndex : selectedVehicleIndex;
            int si = vehicleRoster.ClampSkin(ci, hud.MenuPage == NativeMenuPage.Skins ? hud.SelectedMenuIndex : ci == selectedCharacterIndex ? selectedSkinIndex : 0);
            RenderSelectionPreview(ci, vi, si);
        }
        readonly System.Collections.Generic.Dictionary<string,Texture2D> networkRosterPreviews=new System.Collections.Generic.Dictionary<string,Texture2D>();
        internal Texture NetworkRosterVehiclePreview(string character,string vehicle,int skin)
        {
            int ci=vehicleRoster.CharacterIndexOf(character),vi=vehicleRoster.VehicleIndexOf(vehicle);
            if(ci<0||vi<0)return null;
            skin=vehicleRoster.ClampSkin(ci,skin);
            if(selectionPreviewDefinition==null)selectionPreviewDefinition=NativeSelectionPreviewSettings.Load();
            vehicleRoster.ColorsAt(ci,skin,out var colorA,out var colorB);
            string key=ci+":"+vi+":"+skin+":"+ColorUtility.ToHtmlStringRGBA(colorA)+":"+ColorUtility.ToHtmlStringRGBA(colorB);
            var size=selectionPreviewDefinition.rosterPreviewSize;
            int width=Mathf.Max(1,size.x),height=Mathf.Max(1,size.y);
            Texture2D thumbnail;if(networkRosterPreviews.TryGetValue(key,out thumbnail))return thumbnail;
            RenderSelectionPreview(ci,vi,skin);
            var driver=selectionPreviewModel.transform.GetChild(1).gameObject;
            bool active=driver.activeSelf;var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            try
            {
                driver.SetActive(false);RenderSelectionPreview(ci,vi,skin,true);
                Graphics.Blit(selectionPreviewTexture,target);RenderTexture.active=target;
                thumbnail=new Texture2D(width,height,TextureFormat.RGBA32,false,true){name="Online vehicle "+key,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                thumbnail.ReadPixels(new Rect(0,0,width,height),0,0);thumbnail.Apply(false,true);
                networkRosterPreviews.Add(key,thumbnail);
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);driver.SetActive(active);RenderSelectionPreview(ci,vi,skin);}
            return thumbnail;
        }
        int networkPreviewFrame = -1;
        string networkRotationKey;float networkRotationStarted;
        internal Texture NetworkSelectionPreview(string character, string vehicle, int skin)
        {
            if (!vehicleRoster) return null;
            int ci = vehicleRoster.CharacterIndexOf(character), vi = vehicleRoster.VehicleIndexOf(vehicle);
            if (ci < 0 || vi < 0) return null;
            if (selectionPreviewDefinition == null)
                selectionPreviewDefinition = NativeSelectionPreviewSettings.Load();
            skin = vehicleRoster.ClampSkin(ci, skin);
            string rotationKey=ci+":"+vi+":"+skin;
            if(networkRotationKey!=rotationKey){networkRotationKey=rotationKey;networkRotationStarted=Time.time;}
            if (networkPreviewFrame != Time.frameCount || ci != previewCharacter || vi != previewVehicle || skin != previewSkin)
            {
                RenderSelectionPreview(ci, vi, skin,false,networkRotationStarted);
                networkPreviewFrame = Time.frameCount;
            }
            return selectionPreviewTexture;
        }
        void RenderSelectionPreview(int ci, int vi, int si,bool rosterView=false,float? rotationStart=null)
        {
            if (!selectionPreviewRoot)
            {
                selectionPreviewRoot = new GameObject("Original selection preview");
                selectionPreviewRoot.transform.position = new Vector3(0, -2000, 0);
                var camera = new GameObject("PreviewCamera"); camera.transform.SetParent(selectionPreviewRoot.transform, false);
                selectionPreviewCamera = camera.AddComponent<Camera>();
                selectionPreviewCamera.clearFlags = CameraClearFlags.SolidColor; selectionPreviewCamera.backgroundColor = Color.clear;
                selectionPreviewCamera.fieldOfView = selectionPreviewDefinition.previewFieldOfView; selectionPreviewCamera.nearClipPlane = selectionPreviewDefinition.previewNear; selectionPreviewCamera.farClipPlane = selectionPreviewDefinition.previewFar;
                camera.transform.localPosition = selectionPreviewDefinition.previewCameraPosition;
                camera.transform.localRotation = selectionPreviewDefinition.previewCameraRotation;
                selectionPreviewTexture = new RenderTexture(selectionPreviewDefinition.previewWidth, selectionPreviewDefinition.previewHeight, selectionPreviewDefinition.previewDepth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                selectionPreviewTexture.Create(); selectionPreviewCamera.targetTexture = selectionPreviewTexture;
                selectionPreviewCamera.enabled = false;
                selectionPreviewLights = selectionPreviewDefinition.lights.Select(original =>
                {
                    var node = new GameObject(original.name); node.transform.SetParent(selectionPreviewRoot.transform, false);
                    node.transform.rotation = original.rotation;
                    var lamp = node.AddComponent<Light>(); lamp.type = LightType.Directional;
                    lamp.color = original.color; lamp.intensity = original.intensity; lamp.shadows = original.shadows;
                    lamp.cullingMask = 1 << SelectionPreviewLayer; lamp.enabled = false;
                    return lamp;
                }).ToArray();
                selectionPreviewCamera.cullingMask = 1 << SelectionPreviewLayer;
            }
            selectionPreviewRoot.SetActive(true);
            if (ci != previewCharacter || vi != previewVehicle || si != previewSkin)
            {
                if (selectionPreviewModel) { selectionPreviewModel.SetActive(false); Destroy(selectionPreviewModel); }
                selectionPreviewModel = new GameObject("Preview model"); selectionPreviewModel.transform.SetParent(selectionPreviewRoot.transform, false);
                selectionPreviewModel.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var entry = vehicleRoster.characters[ci];
                var vehicle = Instantiate(entry.vehicleModels[vi], selectionPreviewModel.transform, false);
                vehicle.transform.localPosition = Vector3.up * entry.vehicleHeights[vi];
                vehicle.transform.localRotation = Quaternion.identity;
                var driver = Instantiate(vehicleRoster.DriverModelAt(ci, si), selectionPreviewModel.transform, false);
                driver.transform.localPosition = Vector3.up * entry.vehicleHeights[vi];
                driver.transform.localRotation = Quaternion.identity;
                foreach (var collider in selectionPreviewModel.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (var audio in selectionPreviewModel.GetComponentsInChildren<AudioSource>()) { audio.Stop(); audio.enabled = false; }
                foreach (var node in selectionPreviewModel.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = SelectionPreviewLayer;
                vehicleRoster.ColorsAt(ci, si, out var colorA, out var colorB);
                NativePreviewKartProperties.RefreshSelection(vehicle, colorA, colorB);
                previewCharacter = ci; previewVehicle = vi; previewSkin = si; previewRotationStarted = Time.time;
            }
            if(rotationStart.HasValue)
            {
                previewRotationStarted=rotationStart.Value;
                selectionPreviewModel.transform.localRotation=Quaternion.Euler(0,180+(Time.time-previewRotationStarted)*selectionPreviewDefinition.previewRotationSpeed,0);
            }
            NativePreviewKartProperties.InitializeMenuStarts(selectionPreviewModel);
            var cameraPosition=selectionPreviewCamera.transform.position;var cameraRotation=selectionPreviewCamera.transform.rotation;float cameraAspect=selectionPreviewCamera.aspect;
            if(rosterView)
            {
                // Static thumbnail orientation is independent of the spinning personal preview.
                selectionPreviewModel.transform.localRotation=Quaternion.Euler(selectionPreviewDefinition.rosterModelEuler);
                var renderers=selectionPreviewModel.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&(r is MeshRenderer||r is SkinnedMeshRenderer)).ToArray();
                if(renderers.Length>0)
                {
                    var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                    var size=selectionPreviewDefinition.rosterPreviewSize;
                    selectionPreviewCamera.aspect=(float)Mathf.Max(1,size.x)/Mathf.Max(1,size.y);
                    float yaw=(90-selectionPreviewDefinition.rosterAngleToScreen)*Mathf.Deg2Rad;
                    float elevation=selectionPreviewDefinition.rosterElevation*Mathf.Deg2Rad;
                    float side=selectionPreviewDefinition.rosterFacingLeft?1:-1;
                    var direction=new Vector3(side*Mathf.Sin(yaw)*Mathf.Cos(elevation),Mathf.Sin(elevation),-Mathf.Cos(yaw)*Mathf.Cos(elevation));
                    var rotation=Quaternion.LookRotation(-direction,Vector3.up);
                    var inverse=Quaternion.Inverse(rotation);
                    float tangent=Mathf.Tan(selectionPreviewCamera.fieldOfView*Mathf.Deg2Rad*.5f);
                    float distance=0;
                    // Fit all eight corners in camera space, including depth, so every vehicle fits.
                    for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                    {
                        var corner=inverse*Vector3.Scale(bounds.extents,new Vector3(x,y,z));
                        float projected=Mathf.Max(Mathf.Abs(corner.y),Mathf.Abs(corner.x)/selectionPreviewCamera.aspect);
                        distance=Mathf.Max(distance,projected*Mathf.Max(1,selectionPreviewDefinition.rosterFramingPadding)/tangent-corner.z);
                    }
                    selectionPreviewCamera.transform.SetPositionAndRotation(bounds.center+direction*distance,rotation);
                }
                vehicleRoster.ColorsAt(ci,si,out var first,out var second);
                NativePreviewKartProperties.RefreshSelection(selectionPreviewModel.transform.GetChild(0).gameObject,first,second);
            }
            var environment = NativeMenuEnvironment.Capture();
            var sceneLights = FindObjectsOfType<Light>().Where(light => !selectionPreviewLights.Contains(light)).ToArray();
            var enabled = sceneLights.Select(light => light.enabled).ToArray();
            try
            {
                selectionPreviewDefinition.environment.Apply();
                for (int i = 0; i < sceneLights.Length; i++) sceneLights[i].enabled = false;
                for (int i = 0; i < selectionPreviewLights.Length; i++) selectionPreviewLights[i].enabled = selectionPreviewDefinition.lights[i].enabled;
                selectionPreviewEnvironmentAtRender = NativeMenuEnvironment.Capture();
                selectionPreviewCamera.Render();
            }
            finally
            {
                foreach (var light in selectionPreviewLights) light.enabled = false;
                for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i]) sceneLights[i].enabled = enabled[i];
                environment.Apply();
                selectionPreviewCamera.transform.position=cameraPosition;selectionPreviewCamera.transform.rotation=cameraRotation;selectionPreviewCamera.aspect=cameraAspect;
            }
            selectionPreviewModel.transform.localRotation = Quaternion.Euler(0, 180 + (Time.time - previewRotationStarted) * selectionPreviewDefinition.previewRotationSpeed, 0);
        }
        internal string SelectionPreviewOriginalPropertiesReport()
        {
            return NativePreviewRenderProbe.Capture(selectionPreviewCamera, selectionPreviewTexture, selectionPreviewModel,
                selectionPreviewEnvironmentAtRender, selectionPreviewDefinition);
        }
        internal bool SelectionPreviewUsesOriginalLighting(out string detail)
        {
            if (selectionPreviewDefinition == null || selectionPreviewLights == null)
            {
                detail = "Preview has not rendered";
                return false;
            }
            bool matches = selectionPreviewLights.Length == 2 && selectionPreviewDefinition.lights.Length == 2;
            for (int i = 0; i < selectionPreviewLights.Length && matches; i++)
            {
                var actual = selectionPreviewLights[i]; var source = selectionPreviewDefinition.lights[i];
                matches &= actual.type == LightType.Directional && actual.color == source.color && actual.intensity == source.intensity
                    && Mathf.Abs(Quaternion.Dot(actual.transform.rotation, source.rotation)) > .99999f && !actual.enabled
                    && actual.cullingMask == 1 << SelectionPreviewLayer;
            }
            matches &= selectionPreviewCamera.cullingMask == 1 << SelectionPreviewLayer && selectionPreviewModel
                && selectionPreviewModel.GetComponentsInChildren<Transform>(true).All(node => node.gameObject.layer == SelectionPreviewLayer);
            detail = "Original selection directional lights=" + selectionPreviewLights.Length + "; source ambient=" + selectionPreviewDefinition.environment.ambientMode + "; isolated preview layer=" + SelectionPreviewLayer;
            return matches;
        }
        void DrawSelectionPreview()
        {
            if (!selectionPreviewRoot || !selectionPreviewRoot.activeSelf || !selectionPreviewTexture) return;
            var rect = hud.MenuPage == NativeMenuPage.Characters ? hud.PreviewRect : hud.VehiclePreviewRect;
            hud.PreviewPicture(rect, selectionPreviewTexture);
        }
        void ClearSelectionPreview()
        {
            ClearResultsPreview();
            foreach(var thumbnail in networkRosterPreviews.Values)if(thumbnail)Destroy(thumbnail);
            networkRosterPreviews.Clear();
            if (selectionPreviewRoot) Destroy(selectionPreviewRoot);
            if (selectionPreviewTexture) { selectionPreviewTexture.Release(); Destroy(selectionPreviewTexture); }
        }
    }
}
