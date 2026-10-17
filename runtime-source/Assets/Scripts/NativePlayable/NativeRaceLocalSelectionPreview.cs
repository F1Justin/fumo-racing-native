using System;
using System.Linq;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        sealed class LocalPreview
        {
            public GameObject Root, Pivot;
            public Camera Camera;
            public RenderTexture Texture;
            public Light[] Lights;
            public int Character=-1,Vehicle=-1,Skin=-1;
            public float Started,PausedAt=-1;
        }
        readonly LocalPreview[] localPreviews = new LocalPreview[4];
        bool TickLocalSelectionPreviews()
        {
            if(!localSelectionActive)
            {foreach(var preview in localPreviews)if(preview!=null && preview.Root)preview.Root.SetActive(false);return false;}
            if(selectionPreviewDefinition==null)selectionPreviewDefinition=NativeSelectionPreviewSettings.Load();
            if(menuPresentation)menuPresentation.SetSelectionVisible(true,selectionPreviewDefinition);
            var rows=selectionPreviewDefinition.players;
            if(rows==null||rows.Length!=4||rows.Select(row=>row.humanIndex).Distinct().Count()!=4)throw new InvalidOperationException("Four source preview owners are missing");
            for(int slot=0;slot<4;slot++)
            {
                var box=localSelection.ReadBox(slot);var preview=localPreviews[slot];
                if(box==null){if(preview!=null&&preview.Root)preview.Root.SetActive(false);continue;}
                var row=rows.Single(source=>source.humanIndex==slot);
                if(preview==null)
                {
                    preview=new LocalPreview();localPreviews[slot]=preview;
                    preview.Root=new GameObject("Original local preview P"+(slot+1));preview.Root.transform.position=new Vector3(slot*2000,-2000,0);
                    var node=new GameObject("PreviewCamera");node.transform.SetParent(preview.Root.transform,false);
                    node.transform.localPosition=row.cameraPosition;node.transform.rotation=row.cameraRotation;node.transform.localScale=row.cameraScale;
                    preview.Camera=node.AddComponent<Camera>();preview.Camera.enabled=false;
                    preview.Camera.clearFlags=row.clearFlags;preview.Camera.backgroundColor=row.background;
                    preview.Camera.fieldOfView=row.fieldOfView;preview.Camera.nearClipPlane=row.near;preview.Camera.farClipPlane=row.far;preview.Camera.depth=row.cameraDepth;
                    preview.Camera.cullingMask=1<<SelectionPreviewLayer;
                    preview.Texture=new RenderTexture(row.width,row.height,row.depthBits,row.format,row.readWrite);preview.Texture.Create();preview.Camera.targetTexture=preview.Texture;
                    preview.Lights=selectionPreviewDefinition.lights.Select(source=>{var lampNode=new GameObject(source.name);lampNode.transform.SetParent(preview.Root.transform,false);lampNode.transform.rotation=source.rotation;var lamp=lampNode.AddComponent<Light>();lamp.type=LightType.Directional;lamp.color=source.color;lamp.intensity=source.intensity;lamp.shadows=source.shadows;lamp.cullingMask=1<<SelectionPreviewLayer;lamp.enabled=false;return lamp;}).ToArray();
                }
                preview.Root.SetActive(true);
                bool config=localProfiles[slot]!=null&&localProfiles[slot].Open;
                if(config){if(preview.PausedAt<0)preview.PausedAt=Time.time;continue;}
                if(preview.PausedAt>=0){preview.Started+=Time.time-preview.PausedAt;preview.PausedAt=-1;}
                if(preview.Character!=box.Character||preview.Vehicle!=box.Vehicle||preview.Skin!=box.Skin)
                {
                    if(preview.Pivot){preview.Pivot.SetActive(false);Destroy(preview.Pivot);}
                    preview.Pivot=new GameObject("Preview");preview.Pivot.transform.SetParent(preview.Root.transform,false);
                    preview.Pivot.transform.localRotation=row.pivotRotation;preview.Pivot.transform.localScale=row.pivotScale;
                    var entry=vehicleRoster.characters[box.Character];var vehicle=Instantiate(entry.vehicleModels[box.Vehicle],preview.Pivot.transform,false);
                    vehicle.transform.localPosition=Vector3.up*entry.vehicleHeights[box.Vehicle];vehicle.transform.localRotation=Quaternion.Euler(0,180,0);
                    var driver=Instantiate(vehicleRoster.DriverModelAt(box.Character,box.Skin),preview.Pivot.transform,false);
                    driver.transform.localPosition=Vector3.up*entry.vehicleHeights[box.Vehicle];driver.transform.localRotation=Quaternion.Euler(0,180,0);
                    foreach(var collider in preview.Pivot.GetComponentsInChildren<Collider>())collider.enabled=false;
                    foreach(var audio in preview.Pivot.GetComponentsInChildren<AudioSource>()){audio.Stop();audio.enabled=false;}
                    foreach(var child in preview.Pivot.GetComponentsInChildren<Transform>(true))child.gameObject.layer=SelectionPreviewLayer;
                    vehicleRoster.ColorsAt(box.Character,box.Skin,out var a,out var b);NativePreviewKartProperties.RefreshSelection(vehicle,a,b);
                    preview.Character=box.Character;preview.Vehicle=box.Vehicle;preview.Skin=box.Skin;preview.Started=Time.time;
                }
                NativePreviewKartProperties.InitializeMenuStarts(preview.Pivot);
                var environment=NativeMenuEnvironment.Capture();var lights=FindObjectsOfType<Light>();var enabled=lights.Select(light=>light.enabled).ToArray();
                try
                {
                    selectionPreviewDefinition.environment.Apply();foreach(var light in lights)light.enabled=false;
                    for(int i=0;i<preview.Lights.Length;i++)preview.Lights[i].enabled=selectionPreviewDefinition.lights[i].enabled;
                    preview.Camera.Render();
                }
                finally{for(int i=0;i<lights.Length;i++)if(lights[i])lights[i].enabled=enabled[i];environment.Apply();}
                preview.Pivot.transform.localRotation=row.pivotRotation*Quaternion.Euler(0,(Time.time-preview.Started)*row.rotationSpeed,0);
            }
            return true;
        }
        void DrawLocalSelectionPreviews()
        {
            for(int slot=0;slot<4;slot++)if(localPreviews[slot]!=null&&localPreviews[slot].Root.activeSelf)
                hud.LocalPreviewTexture(selectionPreviewDefinition.players.Single(row=>row.humanIndex==slot).rawImagePath,localPreviews[slot].Texture);
        }
        void ClearLocalSelectionPreviews()
        {
            foreach(var preview in localPreviews)if(preview!=null)
            {if(preview.Root)Destroy(preview.Root);if(preview.Texture){preview.Texture.Release();Destroy(preview.Texture);}}
        }
    }
}
