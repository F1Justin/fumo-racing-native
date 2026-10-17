using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeVehicleModuleHudFamily
    {
        public string id,scene,sourceName;
        public float limit,danger;
        public Color baseColor,fullColor,noneColor,dangerColor;
    }
    [Serializable] public sealed class NativeVehicleModuleHudCanvas
    {
        public int renderMode,sortingOrder,shaderChannels,scaleMode,screenMatchMode;
        public float planeDistance,match,referencePixelsPerUnit,initialGroupAlpha;
        public bool pixelPerfect;
        public Vector2 referenceResolution;
    }
    [Serializable] public sealed class NativeVehicleModuleHudRect
    {
        public float x,y,width,height;
        public Rect Value=>new Rect(x,y,width,height);
    }
    [Serializable] public sealed class NativeVehicleModuleHudSprite
    {
        public string name,guid,vertexHash,triangleHash,rgbaHash;
        public NativeVehicleModuleHudRect rect,textureRect;
        public Vector2 pivot,offset,textureRectOffset;
        public float pixelsPerUnit;
        public Vector2[] vertices;
        public int[] triangles;
    }
    [Serializable] public sealed class NativeVehicleModuleHudSource
    {
        public int familyCount;
        public NativeAuthoredUiNode[] nodes;
        public NativeVehicleModuleHudFamily[] families;
        public NativeVehicleModuleHudCanvas canvas;
        public NativeVehicleModuleHudSprite[] sprites;
        public static NativeVehicleModuleHudSource Read()
        {
            var resource=NativeContent.Load<TextAsset>("NativeVerification/VehicleModuleHudSource");
            if(!resource)throw new InvalidOperationException("Original vehicle module HUD source baseline missing");
            return JsonUtility.FromJson<NativeVehicleModuleHudSource>(resource.text);
        }
    }
    public sealed class NativeVehicleModuleHud : MonoBehaviour
    {
        [Serializable] public sealed class NodeState
        {
            public string path,spriteName,sourceSpriteGuid;
            public bool active,enabled,maskable,preserveAspect,fillClockwise,useSpriteMesh,defaultMaterial;
            public int type,fillMethod,fillOrigin;
            public float alpha,fillAmount,pixelsPerUnitMultiplier;
            public Color color;
            public Vector2 anchorMin,anchorMax,pivot,position,size;
            public Vector3 scale;
            public Rect screenRect,spriteRect,spriteTextureRect;
            public Vector2 spritePivot,spriteTextureRectOffset;
            public float spritePixelsPerUnit;
            public Vector2[] spriteVertices;
            public ushort[] spriteTriangles;
        }
        [Serializable] public sealed class State
        {
            public string family,scene;
            public bool human,visible,ready,canvasEnabled,cameraBound;
            public int stage,renderMode,sortingOrder,nodeCount;
            public float charge,limit,planeDistance,groupAlpha;
            public Vector2 referenceResolution;
            public NodeState[] nodes;
        }
        NativeKartMotor motor;
        Camera playerCamera;
        NativeVehicleModuleHudSource source;
        NativeAuthoredUiData resource;
        NativeVehicleModuleHudFamily family;
        Canvas canvas;
        CanvasScaler scaler;
        RectTransform barRoot;
        CanvasGroup group;
        Image fill;
        bool human,visible,onlineAuthored;
        readonly Dictionary<string,RectTransform> nodes=new Dictionary<string,RectTransform>();
        readonly Dictionary<string,NativeAuthoredUiNode> definitions=new Dictionary<string,NativeAuthoredUiNode>();

        public void Present(NativeKartMotor value,Camera camera,bool isHuman,bool isVisible, bool useOnlinePrefab = false)
        {
            if (onlineAuthored != useOnlinePrefab) { ClearBar(); if (canvas) Destroy(canvas.gameObject); canvas = null; family = null; onlineAuthored = useOnlinePrefab; }
            motor=value;playerCamera=camera;human=isHuman;visible=isVisible;
            if(!human||!motor||motor.stats==null){if(canvas)canvas.enabled=false;return;}
            if(source==null)source=NativeVehicleModuleHudSource.Read();
            var next=source.families.SingleOrDefault(f=>f.id==motor.stats._moduleKind.ToString());
            if(next==null)
            {
                ClearBar();family=null;if(canvas)canvas.enabled=false;return;
            }
            if(!camera)throw new InvalidOperationException("Original human vehicle module HUD camera missing");
            if (onlineAuthored)
            {
                if (family == null || family.id != next.id || !canvas)
                {
                    ClearBar(); if (canvas) Destroy(canvas.gameObject);
                    var prefab = NativeContent.Load<NativeOnlineModuleHudView>("NativeOnline/ModuleBars/" + next.id);
                    if (!prefab) throw new InvalidOperationException("Missing authored Online module bar: " + next.id);
                    var view = Instantiate(prefab); family = view.family;
                    canvas = view.GetComponent<Canvas>(); scaler = view.GetComponent<CanvasScaler>();
                    barRoot = view.barRoot; group = view.group; fill = view.fill;
                    foreach (var binding in view.GetComponentsInChildren<NativeOnlineAuthoredNode>(true))
                    { nodes.Add(binding.source.path, (RectTransform)binding.transform); definitions.Add(binding.source.path, binding.Read()); }
                }
            }
            else
            {
                if(!canvas)CreateCanvas();
                if(family!=next){ClearBar();family=next;CreateBar();}
            }
            canvas.worldCamera=camera;
            Apply();
        }
        void CreateCanvas()
        {
            var root=new GameObject("Original vehicle module HUD Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            root.layer=5;
            canvas=root.GetComponent<Canvas>();canvas.renderMode=(RenderMode)source.canvas.renderMode;
            canvas.worldCamera=playerCamera;canvas.planeDistance=source.canvas.planeDistance;
            canvas.sortingOrder=source.canvas.sortingOrder;canvas.pixelPerfect=source.canvas.pixelPerfect;
            canvas.additionalShaderChannels=(AdditionalCanvasShaderChannels)source.canvas.shaderChannels;
            scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=(CanvasScaler.ScaleMode)source.canvas.scaleMode;
            scaler.referenceResolution=source.canvas.referenceResolution;scaler.screenMatchMode=(CanvasScaler.ScreenMatchMode)source.canvas.screenMatchMode;
            scaler.matchWidthOrHeight=source.canvas.match;scaler.referencePixelsPerUnit=source.canvas.referencePixelsPerUnit;
        }
        void CreateBar()
        {
            if(!resource)resource=NativeContent.Load<NativeAuthoredUiData>("NativeVehicleModuleHud/OriginalModuleBars");
            if(!resource)throw new InvalidOperationException("Original vehicle module HUD exported node resource missing");
            var parents=new Dictionary<long,RectTransform>();
            foreach(var definition in resource.nodes.Where(n=>n.scene==family.scene).OrderBy(n=>n.path.Count(c=>c=='/')).ThenBy(n=>n.order))
            {
                var obj=new GameObject(definition.path.Split('/').Last(),typeof(RectTransform),typeof(Image));obj.layer=5;
                var rect=obj.GetComponent<RectTransform>();rect.SetParent(definition.parent==0?canvas.transform:parents[definition.parent],false);
                parents[definition.id]=rect;nodes[definition.path]=rect;definitions[definition.path]=definition;
                var image=obj.GetComponent<Image>();image.sprite=definition.sprite;image.material=definition.imageOverride;
                image.enabled=definition.imageEnabled;image.type=(Image.Type)definition.imageType;image.color=definition.color;
                image.maskable=definition.maskable;image.preserveAspect=definition.preserveAspect;
                image.fillMethod=(Image.FillMethod)definition.imageFillMethod;image.fillOrigin=definition.imageFillOrigin;
                image.fillAmount=definition.imageFillAmount;image.fillClockwise=definition.imageFillClockwise;
                image.useSpriteMesh=definition.imageUseSpriteMesh;image.pixelsPerUnitMultiplier=definition.imagePixelsPerUnitMultiplier;
                image.raycastTarget=true;
                if(definition.parent==0){barRoot=rect;group=obj.AddComponent<CanvasGroup>();group.alpha=source.canvas.initialGroupAlpha;}
                if(definition.path.EndsWith("/bar",StringComparison.Ordinal))fill=image;
            }
            if(nodes.Count!=4||!barRoot||!fill)throw new InvalidOperationException("Original vehicle module HUD node tree incomplete");
        }
        void Apply()
        {
            if(!canvas||family==null||!motor)return;
            canvas.enabled=human&&visible;
            if (!onlineAuthored) foreach(var pair in nodes)
            {
                var definition=definitions[pair.Key];var rect=pair.Value;
                rect.gameObject.SetActive(definition.active);rect.anchorMin=definition.anchorMin;rect.anchorMax=definition.anchorMax;
                rect.pivot=definition.pivot;rect.anchoredPosition=definition.position;rect.sizeDelta=definition.size;
                rect.localRotation=definition.rotation;rect.localScale=definition.scale;
            }
            float charge=motor.ModuleCharge,limit=motor.stats._moduleLimit;
            fill.fillAmount=limit>0?charge/limit:0;
            if(motor.stats._moduleKind==NativeVehicleModuleKind.BroomAirBoost)fill.color=charge>=limit?family.fullColor:family.baseColor;
            else if(motor.stats._moduleKind==NativeVehicleModuleKind.GenjiGrassBoost)fill.color=charge>=limit?family.fullColor:motor.ModuleStage>0?family.baseColor:family.noneColor;
            else fill.color=charge>family.danger?family.dangerColor:family.baseColor;
            group.alpha=charge>0?1:0;
        }
        void LateUpdate(){Apply();}
        void ClearBar()
        {
            if(barRoot){barRoot.gameObject.SetActive(false);Destroy(barRoot.gameObject);}
            nodes.Clear();definitions.Clear();barRoot=null;group=null;fill=null;
        }
        void OnDestroy(){if(canvas)Destroy(canvas.gameObject);}
        public NativeOnlineModuleHudView AuthoredView => canvas ? canvas.GetComponent<NativeOnlineModuleHudView>() : null;
        public int PresentedMotorInstanceId => motor ? motor.GetInstanceID() : 0;
        public int PresentedCameraInstanceId => playerCamera ? playerCamera.GetInstanceID() : 0;
        public State ReadState()
        {
            return new State{family=family?.id??"None",scene=family?.scene??"",human=human,visible=visible,
                charge=motor?motor.ModuleCharge:0,stage=motor?motor.ModuleStage:0,ready=motor&&motor.ModuleReady,
                limit=motor&&motor.stats!=null?motor.stats._moduleLimit:0,canvasEnabled=canvas&&canvas.enabled,
                renderMode=canvas?(int)canvas.renderMode:-1,sortingOrder=canvas?canvas.sortingOrder:0,planeDistance=canvas?canvas.planeDistance:0,
                cameraBound=canvas&&canvas.worldCamera==playerCamera,referenceResolution=scaler?scaler.referenceResolution:Vector2.zero,
                nodeCount=nodes.Count,groupAlpha=group?group.alpha:0,nodes=nodes.Select(pair=>
                {
                    var rect=pair.Value;var image=rect.GetComponent<Image>();var sprite=image.sprite;var corners=new Vector3[4];rect.GetWorldCorners(corners);
                    var screen=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(playerCamera,c)).ToArray();
                    return new NodeState{path=pair.Key,active=rect.gameObject.activeInHierarchy&&canvas.enabled,enabled=image.enabled,
                        alpha=(group?group.alpha:0)*image.color.a,type=(int)image.type,fillMethod=(int)image.fillMethod,fillOrigin=image.fillOrigin,
                        fillClockwise=image.fillClockwise,useSpriteMesh=image.useSpriteMesh,fillAmount=image.fillAmount,
                        pixelsPerUnitMultiplier=image.pixelsPerUnitMultiplier,preserveAspect=image.preserveAspect,maskable=image.maskable,
                        defaultMaterial=image.material==Graphic.defaultGraphicMaterial,color=image.color,anchorMin=rect.anchorMin,anchorMax=rect.anchorMax,
                        pivot=rect.pivot,position=rect.anchoredPosition,size=rect.sizeDelta,scale=rect.localScale,
                        screenRect=Rect.MinMaxRect(screen.Min(p=>p.x),screen.Min(p=>p.y),screen.Max(p=>p.x),screen.Max(p=>p.y)),
                        sourceSpriteGuid=definitions[pair.Key].spriteGuid,spriteName=sprite?sprite.name:"",spriteRect=sprite?sprite.rect:default,
                        spritePivot=sprite?sprite.pivot:Vector2.zero,spriteTextureRect=sprite?sprite.textureRect:default,
                        spriteTextureRectOffset=sprite?sprite.textureRectOffset:Vector2.zero,spritePixelsPerUnit=sprite?sprite.pixelsPerUnit:0,
                        spriteVertices=sprite?sprite.vertices:Array.Empty<Vector2>(),spriteTriangles=sprite?sprite.triangles:Array.Empty<ushort>()};
                }).ToArray()};
        }
        public bool OriginalSpriteBound(string path,Sprite expected)=>nodes.TryGetValue(path,out var rect)&&rect.GetComponent<Image>().sprite==expected;
    }
}
