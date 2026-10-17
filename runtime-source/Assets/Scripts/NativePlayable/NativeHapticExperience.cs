using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    [DefaultExecutionOrder(-75)]
    public sealed class NativeHapticExperience : MonoBehaviour
    {
        [Serializable] sealed class Receipt { public int index,events,writes; public string title,vehicle,status,mode,dominant; public bool playing,grounded,grass,water; public float elapsed,low,high,speed; }
        sealed class Clip
        {
            public string title,description,vehicle,action; public float seconds;
            public Clip(string title,string description,string vehicle,string action,float seconds=5) { this.title=title;this.description=description;this.vehicle=vehicle;this.action=action;this.seconds=seconds; }
        }
        static readonly Clip[] clips={
            new Clip("手柄校准 · 重量","握住手柄，感受低频马达的短促振动。","kart","calibrate-low",3),
            new Clip("手柄校准 · 细节","与上一项比较，感受高频马达的细碎振动。","kart","calibrate-high",3),
            new Clip("卡丁车 · 平路安静","平路输出为零。观察画面移动，手柄保持安静。","kart","road"),
            new Clip("卡丁车 · 铺装与草地","直行穿过草地：驶入时起振，草地上保持轻路感，回到铺装路面归零。","kart","surface",9),
            new Clip("卡丁车 · 漂移","普通漂移保持安静。危险模块另发阈值提示。","kart","drift"),
            new Clip("卡丁车 · 落地","行驶与飞行安静，落地时出现一次重量冲击。","kart","jump"),
            new Clip("卡丁车 · 撞墙","接触开始出现一次冲击，持续顶墙保持安静。","kart","impact"),
            new Clip("道具 · 获取与使用","拾取道具后释放，感受两种不同提示。","kart","item"),
            new Clip("道具 · 受击与恢复","受击出现重冲击，停转后恢复控制发一次长提示。","kart","damage",6),
            new Clip("摩托车 · 草地路感","接地移动时有轻路感，停下或腾空时归零。","bike","grass"),
            new Clip("摩托车 · Wheelie","抬起与结束姿态各提示一次，维持姿态保持安静。","bike","wheelie",6),
            new Clip("滑板 · 安静与落地","感受安静的滚动背景，再起跳落地。","skate","jump"),
            new Clip("清洁机器人 · 自动推进","自动推进与普通转向保持安静。","cleanerbot","servo"),
            new Clip("购物车 · 草地路感","草地保留轻路感，危险漂移另发阈值提示。","shoppingcart","grass"),
            new Clip("购物车 · 危险漂移","70% 短提示、90% 长提示；受伤覆盖预警，释放漂移清除预警。","shoppingcart","overheat",7),
            new Clip("扫帚 · 蓄满与飞行","蓄能安静，蓄满双提示后等待，再起跳单次释放。","broom","charge",9),
            new Clip("玄爷 · 草地一档","达到首档短提示，等待停转后离草，单次推进释放。","genji","genji1",6),
            new Clip("玄爷 · 草地二档","草地轻路感承托蓄能；首档提示后，二档离草单次释放。","genji","genji2",7),
            new Clip("玄爷 · 草地三档","满档双提示，等待停转后离草，最强单次释放。","genji","genji3",8)
        };
        NativeRace race; NativeKartMotor motor; NativePlayerHaptics haptics;
        GameObject arena,wall; Canvas canvas; TMP_Text title,description,status,progressText;
        Image progressFill; Vector3 road,grass,surfaceLane; int index,actionStage; float began,nextReceipt; bool playing,initialized,auto,advancing;
        float readyAt=-1,stageAt=-1;
        Coroutine warming;
        NativeHapticUiInput uiInput;
        string directory; Camera camera; Camera[] originalCameras; bool[] cameraEnabled; bool originalRaceEnabled;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(Environment.GetCommandLineArgs().Contains("--haptics-characterize") || Environment.GetCommandLineArgs().Contains("--haptics-characterize-ui-self-test") || FindObjectOfType<NativeHapticExperience>())return;
            new GameObject("Haptic experience").AddComponent<NativeHapticExperience>();
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            auto=args.Contains("--haptics-experience-auto");
            int report=Array.IndexOf(args,"--report-dir");
            directory=report>=0 && report+1<args.Length ? args[report+1] : Path.Combine(Application.persistentDataPath,"haptic-experience");
            if(args.Contains("--haptics-experience") || auto)
            {
                yield return null;
                while(FindObjectOfType<NativeRace>() && FindObjectOfType<NativeRace>().StartupSplashActive)yield return null;
                Begin();
            }
        }
        Gamepad SelectPad()
        {
            foreach(var d in InputSystem.devices)if(d is Gamepad p && p.native && p.added && NativeHapticDevices.IsDS4(p))return p;
            foreach(var d in InputSystem.devices)if(d is Gamepad p && p.native && p.added)return p;
            return null;
        }
        void Begin()
        {
            race=FindObjectOfType<NativeRace>(); if(!race || !race.vehicleRoster)return;
            originalRaceEnabled=race.enabled; race.enabled=false; Time.timeScale=1;
            if(race.menuPresentation)race.menuPresentation.SetVisible(false);
            race.hud.SetMenuPage(NativeMenuPage.None);race.hud.SetVisible(false);
            foreach(var m in race.racers) { m.driving=false; m.enabled=false; var ai=m.GetComponent<NativeKartOriginalAI>(); if(ai)ai.active=false; }
            motor=race.racers[0];
            originalCameras=FindObjectsOfType<Camera>();cameraEnabled=originalCameras.Select(c=>c.enabled).ToArray();
            foreach(var c in originalCameras)c.enabled=false;
            camera=new GameObject("触觉体验相机").AddComponent<Camera>();camera.fieldOfView=60;camera.farClipPlane=1200;
            camera.clearFlags=CameraClearFlags.Skybox;camera.backgroundColor=new Color(.4f,.65f,.8f);
            haptics=motor.GetComponent<NativePlayerHaptics>(); if(!haptics)haptics=motor.gameObject.AddComponent<NativePlayerHaptics>();
            haptics.Configure(race,motor,0); haptics.ExperienceDevice=SelectPad(); haptics.ExperienceActive=true;
            haptics.Observed+=OnEvent;
            road=new Vector3(0,10000,0); grass=road+Vector3.left*600;
            arena=new GameObject("触觉练习场");
            Surface("平路",road,new Vector3(800,1,4000),new Color(.2f,.24f,.3f));
            for(int i=-20;i<=120;i++)
            {
                var mark=Surface("路面标记",road+Vector3.forward*(i*12)+Vector3.up*.012f,new Vector3(.3f,.01f,4),Color.white);
                mark.transform.position+=Vector3.up*.5f;mark.GetComponent<Collider>().enabled=false;Destroy(mark.GetComponent<Collider>());
            }
            var g=Surface("草地",grass,new Vector3(400,1,4000),new Color(.18f,.4f,.17f));g.tag="Grass";
            surfaceLane=road+Vector3.right*1200;
            Surface("过渡道起点",surfaceLane+Vector3.back*180,new Vector3(100,1,440),new Color(.2f,.24f,.3f));
            var band=Surface("过渡道草地",surfaceLane+Vector3.forward*120,new Vector3(100,1,160),new Color(.18f,.4f,.17f));band.tag="Grass";
            Surface("过渡道终点",surfaceLane+Vector3.forward*850,new Vector3(100,1,1300),new Color(.2f,.24f,.3f));
            wall=Surface("碰撞障碍",road+Vector3.forward*65+Vector3.up*2.5f,new Vector3(18,6,2),new Color(.85f,.3f,.18f));wall.SetActive(false);
            BuildUI();uiInput=new NativeHapticUiInput();
            Directory.CreateDirectory(directory); haptics.TraceDirectory=directory; initialized=true;
            if(!auto && int.TryParse(Argument(args:Environment.GetCommandLineArgs(),name:"--haptics-start"),out int first))index=Mathf.Clamp(first-1,0,clips.Length-1);
            Prepare();
            Debug.Log("NATIVE_HAPTIC_EXPERIENCE_READY clips="+clips.Length);
            if(auto)Replay();
        }
        static string Argument(string[] args,string name) { int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length ? args[i+1] : null; }
        GameObject Surface(string name,Vector3 position,Vector3 scale,Color color)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(arena.transform);g.transform.position=position-Vector3.up*.5f;g.transform.localScale=scale;
            var mat=new Material(Shader.Find("Standard"));mat.color=color;g.GetComponent<Renderer>().material=mat;return g;
        }
        void CancelWarmup() { if(warming!=null) { StopCoroutine(warming);warming=null; } }
        void Prepare()
        {
            CancelWarmup();
            playing=false; motor.enabled=false; motor.driving=false;motor.throttle=0;motor.steering=0;motor.braking=motor.drifting=motor.jump=false;haptics.Stop();
            var c=clips[index]; race.vehicleRoster.ApplySelection(race.vehicleRoster.DefaultCharacterIndex,race.vehicleRoster.VehicleIndexOf(c.vehicle),motor);
            motor.GetComponent<NativeKartHit>()?.ResetState(); motor.GetComponent<NativeKartItemInventory>()?.ResetItems();
            bool grassy=c.action=="grass" || c.action.StartsWith("genji",StringComparison.Ordinal);
            var controller=motor.GetComponent<CharacterController>();
            float restHeight=controller.height*.5f-controller.center.y+controller.skinWidth+.02f;
            motor.ResetAt((c.action=="surface" ? surfaceLane : grassy?grass:road)+Vector3.up*restHeight,Quaternion.identity,false);
            camera.transform.position=motor.transform.position+new Vector3(0,5,-10);camera.transform.LookAt(motor.transform.position+Vector3.up*1.2f);
            wall.SetActive(c.action=="impact"); actionStage=0;readyAt=stageAt=-1;
            title.text=(index+1).ToString("00")+" / "+clips.Length+"    "+c.title;
            description.text=c.description; progressFill.fillAmount=0; progressText.text=auto ? "自动校验 · 实机体验准备中" : "按 A 开始体验";
            WriteReceipt();
        }
        void Replay() { if(!initialized)return;Prepare();warming=StartCoroutine(ArmClip()); }
        IEnumerator ArmClip()
        {
            motor.enabled=true;motor.driving=false;Physics.SyncTransforms();
            progressText.text="安静准备 · 车辆接地";
            yield return new WaitForSeconds(.4f);
            haptics.Stop();motor.ResetVehicleModule();began=Time.time;haptics.TraceClip=index+1;haptics.TraceBegan=began;
            motor.driving=true;playing=true;warming=null;
        }
        void Next() { if(!initialized)return;if(index<clips.Length-1) { index++;Prepare();if(auto)Replay(); } else { CancelWarmup();playing=false;motor.enabled=false;motor.driving=false;haptics.Mixer.Reset();haptics.Stop();progressText.text="全部体验完成 · 可选择重播";if(auto)Application.Quit(); } }
        void Previous() { if(index>0)index--;Prepare(); }
        void OnEvent(NativeHapticEvent e)
        {
            if(!playing)return;
            if(e.kind==NativeHapticKind.Ready)readyAt=Time.time;
            if(e.kind==NativeHapticKind.ChargeStage)stageAt=Time.time;
            if(e.kind==NativeHapticKind.Impact && clips[index].action=="impact")actionStage=1;
            File.AppendAllText(Path.Combine(directory,"events.jsonl"),JsonUtility.ToJson(new Receipt { index=index+1,title=clips[index].title,vehicle=clips[index].vehicle,status=e.kind+":"+e.stage,elapsed=Time.time-began,events=haptics.Mixer.AcceptedEvents })+"\n"); }
        void Update()
        {
            if(!initialized)
            {
                if(Keyboard.current!=null && Keyboard.current.f8Key.wasPressedThisFrame)Begin();
                return;
            }
            var pad=haptics.ExperienceDevice;
            if(pad==null || !pad.added) { haptics.ExperienceDevice=SelectPad();pad=haptics.ExperienceDevice; }
            if(!auto)
            {
                string command=Path.Combine(directory,"command.txt");
                if(File.Exists(command))
                {
                    string value=File.ReadAllText(command).Trim();File.Delete(command);
                    switch(value)
                    {
                        case "start":case "replay":Replay();break;
                        case "event":SetMode(0);break;case "legacy":SetMode(1);break;case "impact-only":SetMode(2);break;case "mechanisms-only":SetMode(3);break;
                        case "next":Next();break;
                        case "previous":Previous();break;
                        case "stop":CancelWarmup();playing=false;motor.enabled=false;motor.driving=false;haptics.Stop();break;
                        case "screen":ScreenCapture.CaptureScreenshot(Path.Combine(directory,"current.png"));break;
                        case "end":End();return;
                        case "quit":haptics.Stop();Application.Quit();return;
                    }
                }
                var k=Keyboard.current;
                if((pad!=null && pad.buttonNorth.wasPressedThisFrame) || (k!=null && k.cKey.wasPressedThisFrame))SetMode(haptics.Mixer.LegacyComparison ? 0 : 1);
                if((pad!=null && pad.buttonSouth.wasPressedThisFrame) || (k!=null && k.enterKey.wasPressedThisFrame)) { if(playing)Next();else Replay(); }
                if((pad!=null && pad.rightShoulder.wasPressedThisFrame) || (k!=null && k.rightArrowKey.wasPressedThisFrame))Next();
                if((pad!=null && pad.leftShoulder.wasPressedThisFrame) || (k!=null && k.leftArrowKey.wasPressedThisFrame))Previous();
                if((pad!=null && pad.buttonWest.wasPressedThisFrame) || (k!=null && k.rKey.wasPressedThisFrame))Replay();
                if(k!=null && k.escapeKey.wasPressedThisFrame) { End();return; }
            }
            status.text=haptics.Status+" · "+Mode+(haptics.Device != null && haptics.Device.Connected ? " · A 开始 / X 重播 / RB 下一项 / Y 对照" : " · 连接 DS4 模式手柄后开始");
            if(!playing || advancing)
            {
                if(Time.realtimeSinceStartup>nextReceipt) { nextReceipt=Time.realtimeSinceStartup+.2f;WriteReceipt(); }
                return;
            }
            var clip=clips[index];float t=Time.time-began;
            motor.throttle=.5f;motor.steering=0;motor.braking=false;motor.drifting=false;motor.jump=false;
            if(clip.action.StartsWith("calibrate",StringComparison.Ordinal))
            {
                motor.throttle=0;
                if(t>.5f && actionStage==0) { motor.PublishHaptic(clip.action=="calibrate-low" ? NativeHapticKind.CalibrationLow : NativeHapticKind.CalibrationHigh);actionStage++; }
            }
            if(clip.action=="drift" || clip.action=="overheat") { motor.braking=true;motor.drifting=true;motor.steering=.35f; }
            if(clip.action=="impact" && actionStage>0) { motor.throttle=0;motor.braking=true; }
            if(clip.action=="servo")motor.steering=Mathf.Sin(t*2)*.7f;
            if(clip.action=="jump" && t>1.4f && actionStage==0 && motor.Grounded) { motor.jump=true;actionStage++; }
            if(clip.action=="wheelie" && t>3.5f)motor.braking=true;
            if(clip.action=="wheelie" && t>.5f && actionStage<3 && t>.5f+actionStage*.12f) { motor.WheelieInputVertical(actionStage%2==0 ? 1 : -1);actionStage++; }
            if(clip.action=="wheelie" && actionStage>=3 && motor.IsWheelie)motor.WheelieInputVertical(t<1.05f ? -1 : 0);
            if(clip.action=="item")
            {
                var inventory=motor.GetComponent<NativeKartItemInventory>();
                if(t>.5f && actionStage==0) { inventory.TryGive("Power",false);actionStage++; }
                if(t>1.5f && actionStage==1) { inventory.UseItem();actionStage++; }
            }
            if(clip.action=="damage" && t>1.2f && actionStage==0) { motor.GetComponent<NativeKartHit>().TakeDamage(1.5f);actionStage++; }
            if(clip.action=="charge" && motor.ModuleReady && readyAt>=0 && Time.time-readyAt>1.2f && actionStage==0) { motor.jump=true;actionStage++; }
            if(clip.action.StartsWith("genji",StringComparison.Ordinal) && motor.Grounded && motor.OnGrass && motor.ModuleStage>=clip.action[clip.action.Length-1]-'0' && (clip.action.EndsWith("3") ? stageAt>=0 && Time.time-stageAt>1.2f : t>motor.ModuleStage+.55f) && actionStage==0)
            {
                var body=motor.GetComponent<CharacterController>();body.enabled=false;motor.transform.position+=Vector3.right*600;body.enabled=true;Physics.SyncTransforms();actionStage++;
            }
            progressFill.fillAmount=Mathf.Clamp01(t/clip.seconds);progressText.text=(auto ? "自动校验 · " : "体验中 · ")+Mathf.CeilToInt(Mathf.Max(0,clip.seconds-t))+" 秒";
            if(Time.realtimeSinceStartup>nextReceipt) { nextReceipt=Time.realtimeSinceStartup+.2f;WriteReceipt(); }
            if(t>=clip.seconds)
            {
                playing=false;motor.enabled=false;motor.driving=false;haptics.Mixer.Reset();haptics.Stop();progressText.text="本项结束 · X 重播 / RB 下一项";WriteReceipt();
                StartCoroutine(FinishClip(clip));
            }
        }
        IEnumerator FinishClip(Clip clip)
        {
            advancing=true;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,(index+1).ToString("00")+"-"+clip.vehicle+".png"));
            yield return null;advancing=false;if(auto)Next();
        }
        void LateUpdate()
        {
            if(!initialized || !camera || !motor)return;
            race.hud.SetVisible(false);
            camera.transform.position=Vector3.Lerp(camera.transform.position,motor.transform.position+new Vector3(0,5,-10),1-Mathf.Exp(-Time.unscaledDeltaTime*8));
            camera.transform.LookAt(motor.transform.position+Vector3.up*1.2f);
        }
        void WriteReceipt()
        {
            if(!haptics)return;
            var receipt=new Receipt { index=index+1,title=clips[index].title,vehicle=clips[index].vehicle,
                status=haptics.Status,mode=Mode,dominant=haptics.Mixer.Dominant,playing=playing,grounded=motor.Grounded,grass=motor.OnGrass,water=motor.InWater,speed=motor.Speed,elapsed=Time.time-began,events=haptics.Mixer.AcceptedEvents,writes=haptics.SuccessfulWrites,low=haptics.Output.low,high=haptics.Output.high };
            File.WriteAllText(Path.Combine(directory,"current.json"),JsonUtility.ToJson(receipt,true));
            File.AppendAllText(Path.Combine(directory,"frames.jsonl"),JsonUtility.ToJson(receipt)+"\n");
        }
        string Mode => haptics.Mixer.LegacyComparison ? "旧版背景对照" : !haptics.Mixer.MechanismsEnabled ? "仅冲击" : !haptics.Mixer.ImpactsEnabled ? "仅机制提示" : "路感与事件";
        void SetMode(int mode)
        {
            haptics.Stop();haptics.Mixer.LegacyComparison=mode==1;
            haptics.Mixer.ImpactsEnabled=mode!=3;haptics.Mixer.MechanismsEnabled=mode!=2;haptics.Mixer.SurfaceEnabled=mode==0;
            Replay();
        }
        void End()
        {
            CancelWarmup();uiInput?.Dispose();uiInput=null;
            playing=false;haptics.ExperienceActive=false;haptics.ExperienceDevice=null;haptics.Mixer.Reset();haptics.Stop();haptics.Observed-=OnEvent;haptics.TraceDirectory=null;haptics.Mixer.LegacyComparison=false;haptics.Mixer.ImpactsEnabled=haptics.Mixer.MechanismsEnabled=haptics.Mixer.SurfaceEnabled=true;
            Destroy(arena);Destroy(canvas.gameObject);Destroy(camera.gameObject);
            for(int i=0;i<originalCameras.Length;i++)if(originalCameras[i])originalCameras[i].enabled=cameraEnabled[i];
            initialized=false;race.enabled=originalRaceEnabled;race.ReturnFromHapticExperience();
            foreach(var m in race.racers)m.enabled=true;
        }
        void OnDestroy() { CancelWarmup();uiInput?.Dispose();uiInput=null;if(haptics) { haptics.Stop();haptics.Observed-=OnEvent; } }
        RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pos,Vector2 size)
        {
            var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=anchor;r.pivot=anchor;r.anchoredPosition=pos;r.sizeDelta=size;return r;
        }
        TMP_Text Label(string name,Transform parent,Vector2 anchor,Vector2 pos,Vector2 size,int fontSize)
        {
            var r=Rect(name,parent,anchor,pos,size);var label=r.gameObject.AddComponent<TextMeshProUGUI>();label.font=NativeChineseFont.Font;label.fontSize=fontSize;label.color=Color.white;label.raycastTarget=false;return label;
        }
        void BuildUI()
        {
            canvas=new GameObject("触觉体验",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1000;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            var panel=Rect("信息",canvas.transform,new Vector2(.5f,1),new Vector2(0,-16),new Vector2(1216,145));panel.pivot=new Vector2(.5f,1);panel.gameObject.AddComponent<Image>().color=new Color(.025f,.035f,.055f,.94f);
            title=Label("标题",panel,new Vector2(0,1),new Vector2(20,-12),new Vector2(1176,42),28);
            description=Label("说明",panel,new Vector2(0,1),new Vector2(20,-58),new Vector2(1176,35),21);
            status=Label("设备",panel,new Vector2(0,1),new Vector2(20,-102),new Vector2(1176,30),17);
            var bottom=Rect("操作",canvas.transform,new Vector2(.5f,0),new Vector2(0,14),new Vector2(1216,92));bottom.pivot=new Vector2(.5f,0);bottom.gameObject.AddComponent<Image>().color=new Color(.025f,.035f,.055f,.94f);
            progressText=Label("进度",bottom,new Vector2(0,1),new Vector2(20,-10),new Vector2(550,34),21);
            var bar=Rect("进度条",bottom,new Vector2(0,0),new Vector2(20,14),new Vector2(480,12));bar.gameObject.AddComponent<Image>().color=new Color(.2f,.24f,.3f);
            var fill=Rect("填充",bar,new Vector2(0,0),Vector2.zero,new Vector2(480,12));
            progressFill=fill.gameObject.AddComponent<Image>();progressFill.color=new Color(.35f,.8f,.7f);progressFill.sprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height),new Vector2(.5f,.5f));progressFill.type=Image.Type.Filled;progressFill.fillMethod=Image.FillMethod.Horizontal;
            string[] labels={"上一项","开始 / 重播","下一项","结束"};Action[] actions={Previous,Replay,Next,End};
            for(int i=0;i<labels.Length;i++)
            {
                var r=Rect(labels[i],bottom,new Vector2(0,0),new Vector2(540+i*160,16),new Vector2(148,54));r.gameObject.AddComponent<Image>().color=new Color(.14f,.21f,.3f);
                var button=r.gameObject.AddComponent<Button>();int captured=i;button.onClick.AddListener(()=>actions[captured]());
                var text=Label("文字",r,new Vector2(.5f,.5f),Vector2.zero,new Vector2(148,54),20);text.alignment=TextAlignmentOptions.Center;text.text=labels[i];
            }
        }
    }
    public sealed partial class NativeRace { internal void ReturnFromHapticExperience() { ResetRace(false); } }
}
