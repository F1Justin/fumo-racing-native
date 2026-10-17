using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed class NativeHapticCharacterization : MonoBehaviour
    {
        [Serializable] public sealed class Trial
        {
            public int id,motor,pulses;
            public string phase,label,comfort;
            public float amplitude,on,gap;
        }
        [Serializable] sealed class Session
        {
            public string identity,started,finished;
            public Trial[] trials;
            public int answered;
            public bool complete;
        }
        [Serializable] sealed class Write
        {
            public int trial,lowByte,highByte;
            public double time;
            public float commandLow,commandHigh,estimatedMotorLow=-1,estimatedMotorHigh=-1;
            public bool success;
        }
        readonly List<Trial> trials=new List<Trial>();
        INativeHapticDevice device;
        Gamepad pad;
        NativeRace race;
        Canvas canvas;
        TMP_Text title,body,status;
        string directory,identity,started;
        int index;
        bool running,awaiting,paused,finished,initialized;
        double began;
        NativeHapticOutput sent;
        int oldFrameRate,oldVSync;
        bool oldRaceEnabled;
        NativeHapticUiInput uiInput;
        int selectedFeedback=1;double nextSelection;string lastFeedback;
        Button startButton;
        readonly bool uiTest=Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-characterize-ui-self-test")>=0;
        Button[] labelButtons;
        static readonly string[] labels={"摸不到","一下","两下","连成一坨","舒服","太猛"};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-characterize")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"--haptics-characterize-ui-self-test")>=0)
                new GameObject("ERM characterization").AddComponent<NativeHapticCharacterization>();
        }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int report=Array.IndexOf(args,"--report-dir");
            directory=report>=0 && report+1<args.Length ? args[report+1] : Path.Combine(Application.persistentDataPath,"haptic-characterization-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(directory);started=DateTime.UtcNow.ToString("o");
            while(!(race=FindObjectOfType<NativeRace>()) || race.StartupSplashActive)yield return null;
            oldRaceEnabled=race.enabled;race.enabled=false;Time.timeScale=1;
            if(race.menuPresentation)race.menuPresentation.SetVisible(false);
            race.hud.SetMenuPage(NativeMenuPage.None);race.hud.SetVisible(false);
            foreach(var m in race.racers) { m.driving=false;m.enabled=false;var ai=m.GetComponent<NativeKartOriginalAI>();if(ai)ai.active=false; }
            oldFrameRate=Application.targetFrameRate;oldVSync=QualitySettings.vSyncCount;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=120;
            BuildTrials();BuildUI();uiInput=new NativeHapticUiInput();
            Connect();Resume();initialized=true;Refresh();Save();
#if ENABLE_INPUT_SYSTEM
            if(uiTest)StartCoroutine(CheckUi());
#endif
            Debug.Log("NATIVE_HAPTIC_CHARACTERIZATION_READY trials="+trials.Count);
        }
        public static List<Trial> CreateTrials()
        {
            var result=new List<Trial>();
            for(int motor=0;motor<2;motor++)
            {
                for(int i=1;i<=10;i++)result.Add(new Trial { motor=motor,phase="启动强度",amplitude=i/10f,on=.8f,pulses=1 });
                foreach(float amplitude in new[]{.5f,1f})
                    foreach(float on in new[]{.05f,.1f,.15f,.2f,.3f})result.Add(new Trial { motor=motor,phase="单脉冲",amplitude=amplitude,on=on,pulses=1 });
                foreach(float amplitude in new[]{.5f,1f})
                    for(int i=1;i<=10;i++)result.Add(new Trial { motor=motor,phase="双脉冲静默",amplitude=amplitude,on=.2f,gap=i*.05f,pulses=2 });
            }
            for(int i=0;i<result.Count;i++)result[i].id=i+1;
            return result;
        }
        void BuildTrials() => trials.AddRange(CreateTrials());
        void Connect()
        {
            if(device!=null && device.Connected)return;
#if ENABLE_INPUT_SYSTEM
            if(uiTest) { pad=InputSystem.AddDevice<Gamepad>("Haptic UI test pad");device=NativeHapticDevices.Audit();identity="UI test";return; }
#endif
            device?.Dispose();device=null;pad=null;
            foreach(var d in InputSystem.devices)if(d is Gamepad p && p.native && p.added && NativeHapticDevices.IsDS4(p)) { pad=p;break; }
            if(pad==null)return;
            device=NativeHapticDevices.Open(pad);identity=device.Name+"|"+device.Status;
            StopMotor();
        }
        void Resume()
        {
            string file=Path.Combine(directory,"session.json");if(!File.Exists(file))return;
            try
            {
                var session=JsonUtility.FromJson<Session>(File.ReadAllText(file));
                if(session.identity!=identity || session.trials==null || session.trials.Length!=trials.Count)return;
                trials.Clear();trials.AddRange(session.trials);started=session.started;
                int next=trials.FindIndex(t=>string.IsNullOrEmpty(t.label));index=next>=0 ? next : trials.Count-1;finished=session.complete;
            }
            catch(Exception e) { Debug.LogWarning("NATIVE_HAPTICS_RESUME "+e.GetType().Name); }
        }
        void Play()
        {
            if(finished || !initialized)return;
            Connect();if(device==null || !device.Connected) { Refresh();return; }
            StopMotor();running=true;awaiting=false;paused=false;began=Time.realtimeSinceStartupAsDouble;Refresh();
        }
        void SetLabel(int choice)
        {
            if(!awaiting || running || finished)return;
            trials[index].label=labels[choice];lastFeedback="第 "+(index+1)+" 项已保存："+labels[choice];Save();StopMotor();
            if(index<trials.Count-1) { index++;Play(); }
            else Complete();
        }
        void Complete()
        {
            running=false;awaiting=false;finished=true;StopMotor();Save();
            var response=DeriveResponse(trials,identity,directory);
            File.WriteAllText(Path.Combine(directory,"response-summary.json"),JsonUtility.ToJson(response,true));
            if(response.measured)File.WriteAllText(NativeHapticResponse.FileFor(identity),JsonUtility.ToJson(response,true));
            Refresh();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"complete.png"));
        }
        public static NativeHapticResponse DeriveResponse(List<Trial> trials,string identity,string directory)
        {
            var result=new NativeHapticResponse { identity=identity,evidence=directory };
            bool all=true;
            for(int motor=0;motor<2;motor++)
            {
                var rows=trials.Where(t=>t.motor==motor && !string.IsNullOrEmpty(t.label)).ToArray();
                var starts=rows.Where(t=>t.phase=="启动强度" && (t.label=="一下" || t.label=="舒服")).Select(t=>t.amplitude).ToArray();
                // Only positive discrimination labels establish perceptual timing bounds.
                var ons=rows.Where(t=>t.phase=="单脉冲" && t.label=="一下").GroupBy(t=>t.amplitude).Select(g=>g.Min(t=>t.on)).ToArray();
                var gaps=rows.Where(t=>t.phase=="双脉冲静默" && t.label=="两下").GroupBy(t=>t.amplitude).Select(g=>g.Min(t=>t.gap)).ToArray();
                all&=starts.Length>0 && ons.Length==2 && gaps.Length==2;
                float start=starts.Length>0 ? starts.Min() : 0;
                float on=ons.Length>0 ? Mathf.Max(.05f,ons.Max()) : motor==0 ? result.lowOn : result.highOn;
                float gap=gaps.Length>0 ? Mathf.Min(1,gaps.Max()+.05f) : motor==0 ? result.lowGap : result.highGap;
                if(motor==0) { result.lowStart=start;result.lowOn=on;result.lowGap=gap; }
                else { result.highStart=start;result.highOn=on;result.highGap=gap; }
            }
            result.measured=all;return result;
        }
        void Update()
        {
            if(!initialized)return;
            var k=Keyboard.current;
            string command=Path.Combine(directory,"command.txt");
            if(File.Exists(command))
            {
                string value=File.ReadAllText(command).Trim();File.Delete(command);
                if(value=="start" || value=="replay")Play();
                else if(value=="screen")ScreenCapture.CaptureScreenshot(Path.Combine(directory,"current.png"));
                else if(value=="stop")Pause();
                else if(value=="quit") { StopMotor();Application.Quit(); }
                else if(value.StartsWith("label:") && int.TryParse(value.Substring(6),out int answer) && answer>=1 && answer<=6)SetLabel(answer-1);
                else if(value=="next")Skip();
            }
            if(!running && !awaiting && !finished && ((k!=null && k.enterKey.wasPressedThisFrame) || (pad!=null && pad.buttonSouth.wasPressedThisFrame)))Play();
            else if(awaiting)
            {
                if(k!=null)
                {
                    if(k.digit1Key.wasPressedThisFrame)SetLabel(0);
                    else if(k.digit2Key.wasPressedThisFrame)SetLabel(1);
                    else if(k.digit3Key.wasPressedThisFrame)SetLabel(2);
                    else if(k.digit4Key.wasPressedThisFrame)SetLabel(3);
                    else if(k.digit5Key.wasPressedThisFrame)SetLabel(4);
                    else if(k.digit6Key.wasPressedThisFrame)SetLabel(5);
                }
                if(pad!=null)
                {
                    float direction=Mathf.Abs(pad.dpad.x.ReadValue())>.5f ? pad.dpad.x.ReadValue() : pad.leftStick.x.ReadValue();
                    if(Mathf.Abs(direction)>.55f && Time.realtimeSinceStartupAsDouble>=nextSelection)
                    {
                        selectedFeedback=(selectedFeedback+(direction>0 ? 1 : -1)+labels.Length)%labels.Length;
                        nextSelection=Time.realtimeSinceStartupAsDouble+.24;Refresh();
                    }
                    if(Mathf.Abs(direction)<=.55f)nextSelection=0;
                    if(pad.buttonSouth.wasPressedThisFrame)SetLabel(selectedFeedback);
                }
            }
            if(pad!=null)
            {
                if(pad.buttonWest.wasPressedThisFrame)Play();
                if(pad.leftShoulder.wasPressedThisFrame)Previous();
                if(pad.rightShoulder.wasPressedThisFrame)Skip();
            }
            if(k!=null)
            {
                if(k.rKey.wasPressedThisFrame)Play();
                if(k.nKey.wasPressedThisFrame)Skip();
                if(k.bKey.wasPressedThisFrame)Previous();
                if(k.escapeKey.wasPressedThisFrame)Pause();
            }
            if(!running)return;
            if((!Application.isFocused && !uiTest) || device==null || !device.Connected) { Pause();return; }
            var t=trials[index];double elapsed=Time.realtimeSinceStartupAsDouble-began;
            const double rest=1.2;
            double age=elapsed-rest,total=t.on*t.pulses+t.gap*(t.pulses-1);
            bool on=age>=0 && age<total && age%(t.on+t.gap)<t.on;
            var output=on ? new NativeHapticOutput(t.motor==0 ? t.amplitude : 0,t.motor==1 ? t.amplitude : 0) : default;
            if(output.low!=sent.low || output.high!=sent.high || (on && Time.realtimeSinceStartupAsDouble-lastWrite>.3))
                if(!Send(output))return;
            if(elapsed>=rest+total+1)
            {
                StopMotor();running=false;awaiting=true;selectedFeedback=t.pulses==2 ? 2 : 1;nextSelection=0;Refresh();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"current.png"));
            }
            else status.text=(device?.Status ?? "等待 DS4 手柄")+" · "+(age<0 ? "静默准备" : age<total ? "正在输出，请握住手柄" : "归零停转，等待标签")+" · "+elapsed.ToString("F1")+" s";
        }
        double lastWrite;
        bool Send(NativeHapticOutput output)
        {
            bool ok=device!=null && device.Connected && device.Write(output);
            File.AppendAllText(Path.Combine(directory,"writes.jsonl"),JsonUtility.ToJson(new Write { trial=index+1,time=Time.realtimeSinceStartupAsDouble,commandLow=output.low,commandHigh=output.high,
                lowByte=Mathf.RoundToInt(output.low*255),highByte=Mathf.RoundToInt(output.high*255),success=ok })+"\n");
            if(ok) { sent=output;lastWrite=Time.realtimeSinceStartupAsDouble; }
            else { running=false;paused=true;Refresh(); }
            return ok;
        }
        void StopMotor() { if(device!=null && (device.Connected || sent.low>0 || sent.high>0))Send(default);sent=default; }
        void Pause() { bool feedback=awaiting && !running;running=false;awaiting=feedback;paused=true;StopMotor();Save();Refresh(); }
        void Skip() { if(finished)return;running=false;StopMotor();trials[index].label="跳过";Save();if(index<trials.Count-1) { index++;Play(); }else Complete(); }
        void Previous() { if(index>0)index--;finished=false;running=false;awaiting=false;StopMotor();Save();Refresh(); }
        void Save()
        {
            File.WriteAllText(Path.Combine(directory,"session.json"),JsonUtility.ToJson(new Session { identity=identity,started=started,finished=finished ? DateTime.UtcNow.ToString("o") : null,
                trials=trials.ToArray(),answered=trials.Count(t=>!string.IsNullOrEmpty(t.label)),complete=finished },true));
        }
        void Refresh()
        {
            if(!title)return;
            var t=trials[index];
            title.text=finished ? "马达标定完成" : "马达标定  "+(index+1)+" / "+trials.Count+"  ·  "+(t.motor==0 ? "大马达 / Low" : "小马达 / High");
            body.text=finished ? "标签与实际指令日志已保存。\n两个马达的启动、50% 单脉冲、50% / 100% 双脉冲辨识结果齐全时，事件版自动使用实测时长与静默间隔。\n转子时间常数等待仪器测量，当前日志标为 -1。" :
                t.phase+"    命令 "+Mathf.RoundToInt(t.amplitude*100)+"%\n"+Mathf.RoundToInt(t.on*1000)+" ms ON"+(t.pulses==2 ? "  →  "+Mathf.RoundToInt(t.gap*1000)+" ms OFF  →  "+Mathf.RoundToInt(t.on*1000)+" ms ON" : "  →  OFF")+
                "\n每项先静默 1.2 秒，输出结束后停转 1 秒，再选择手感。\n手柄：方向键 / 左摇杆选择标签，A 确认，X 重播，LB 上一项，RB 跳过\n键盘：1—6 标签 · Enter 开始 · R 重播 · N 跳过 · B 返回 · Esc 暂停";
            status.text=(device?.Status ?? "等待 DS4 USB 手柄")+" · "+(finished ? "完成" : awaiting ? "选择："+labels[selectedFeedback]+" · A 确认" : running ? "运行中" : paused ? "暂停，按 A / Enter 重播当前项" : "握住手柄，按 Enter / A 开始");
            for(int i=0;i<labelButtons.Length;i++)
            { labelButtons[i].interactable=awaiting && !finished;labelButtons[i].image.color=awaiting && i==selectedFeedback ? new Color(.12f,.55f,.45f) : new Color(.16f,.26f,.34f); }
            if(!string.IsNullOrEmpty(lastFeedback))body.text+="\n"+lastFeedback;
        }
        TMP_Text Label(Transform parent,Vector2 position,Vector2 size,int font)
        {
            var g=new GameObject("文字",typeof(RectTransform));g.transform.SetParent(parent,false);var r=(RectTransform)g.transform;
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=position;r.sizeDelta=size;
            var text=g.AddComponent<TextMeshProUGUI>();text.font=NativeChineseFont.Font;text.fontSize=font;text.color=Color.white;text.raycastTarget=false;return text;
        }
        Button Button(Transform parent,string text,Vector2 pos,Vector2 size,Action click)
        {
            var g=new GameObject(text,typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(parent,false);var r=(RectTransform)g.transform;
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;g.GetComponent<Image>().color=new Color(.16f,.26f,.34f);
            var button=g.GetComponent<Button>();button.onClick.AddListener(()=>click());var label=Label(g.transform,new Vector2(0,0),size,24);label.alignment=TextAlignmentOptions.Center;label.text=text;return button;
        }
        void BuildUI()
        {
            canvas=new GameObject("马达标定面板",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1100;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            var panel=new GameObject("面板",typeof(RectTransform),typeof(Image));panel.transform.SetParent(canvas.transform,false);var r=(RectTransform)panel.transform;
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(1200,660);panel.GetComponent<Image>().color=new Color(.025f,.045f,.07f,.97f);
            title=Label(r,new Vector2(28,-20),new Vector2(1144,55),32);
            body=Label(r,new Vector2(28,-100),new Vector2(1144,270),24);
            status=Label(r,new Vector2(28,-382),new Vector2(1144,48),23);
            labelButtons=new Button[6];
            for(int i=0;i<6;i++) { int answer=i;labelButtons[i]=Button(r,(i+1)+"  "+labels[i],new Vector2(28+i*190,-446),new Vector2(176,76),()=>SetLabel(answer)); }
            startButton=Button(r,"开始 / 重播",new Vector2(28,-550),new Vector2(250,66),Play);
            Button(r,"暂停",new Vector2(302,-550),new Vector2(250,66),Pause);
            Button(r,"上一项",new Vector2(576,-550),new Vector2(250,66),Previous);
            Button(r,"保存退出",new Vector2(850,-550),new Vector2(250,66),()=>{ Pause();Application.Quit(); });
        }
#if ENABLE_INPUT_SYSTEM
        [Serializable] sealed class UiReport { public bool passed;public string[] checks; }
        IEnumerator Press(GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(button));yield return null;yield return null;
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;yield return null;
        }
        IEnumerator WaitForFeedback()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+6;
            while(!awaiting && Time.realtimeSinceStartupAsDouble<deadline)yield return null;
        }
        IEnumerator CheckUi()
        {
            var checks=new List<string>();bool passed=true;
            Action<bool,string> check=(ok,name)=>{ passed&=ok;checks.Add((ok ? "PASS " : "FAIL ")+name); };
            yield return null;
            check(uiInput.PointerReady,"pointer event system active");
            yield return Press(GamepadButton.South);check(running,"controller A starts trial");
            yield return WaitForFeedback();check(awaiting,"completed trial awaits feedback");
            Pause();check(awaiting,"focus/pause preserves completed feedback");
            uiInput.System.SetSelectedGameObject(startButton.gameObject);
            yield return Press(GamepadButton.DpadLeft);check(selectedFeedback==0,"controller moves feedback highlight");
            yield return Press(GamepadButton.South);
            check(index==1 && trials[0].label=="摸不到","controller A records selected label and advances despite replay focus");
            yield return WaitForFeedback();yield return Press(GamepadButton.DpadRight);
            yield return Press(GamepadButton.South);
            check(index==2 && trials[1].label=="两下","controller can select a different feedback label");
            yield return WaitForFeedback();
            var mouse=InputSystem.AddDevice<Mouse>("Haptic UI test mouse");
            var rect=(RectTransform)labelButtons[4].transform;
            Vector2 point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(mouse,new MouseState { position=point });yield return null;yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState { position=point,buttons=1 });yield return null;yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState { position=point });yield return null;yield return null;
            check(index==3 && trials[2].label=="舒服","mouse click records feedback and advances");
            Pause();File.WriteAllText(Path.Combine(directory,"ui-input.json"),JsonUtility.ToJson(new UiReport { passed=passed,checks=checks.ToArray() },true));
            InputSystem.RemoveDevice(mouse);Debug.Log("NATIVE_HAPTICS_UI_RESULT passed="+passed);Application.Quit(passed ? 0 : 2);
        }
#endif
        void OnApplicationFocus(bool focus) { if(!focus && initialized && !uiTest)Pause(); }
        void OnApplicationPause(bool pause) { if(pause && initialized && !uiTest)Pause(); }
        void OnDestroy()
        {
            if(!initialized)return;
            StopMotor();Save();device?.Dispose();
            Application.targetFrameRate=oldFrameRate;QualitySettings.vSyncCount=oldVSync;
            uiInput?.Dispose();
            if(race)race.enabled=oldRaceEnabled;
        }
    }
}
