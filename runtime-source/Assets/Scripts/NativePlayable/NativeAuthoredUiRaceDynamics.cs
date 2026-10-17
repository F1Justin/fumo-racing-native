using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        [Serializable] sealed class HudKeys { public float time,value,inSlope,outSlope,inWeight,outWeight; public bool infiniteIn,infiniteOut; public int weightedMode; }
        [Serializable] sealed class HudCurve { public string path,attribute; public HudKeys[] keys; [NonSerialized] public AnimationCurve curve; }
        [Serializable] sealed class HudClip { public string name; public float length; public bool loop; public HudCurve[] curves; }
        [Serializable] sealed class HudClips { public HudClip[] clips; }
        HudClips dynamicClips;
        float lapAnimationStart;
        bool lapAnimationPlayed;
        float dynamicAnimationOrigin=-1;
        [Serializable] public sealed class LapEndpointTraceSample
        {
            public string clip, path, attribute, stage;
            public int frame, endFrame, axis, keyCount;
            public bool loop, isText;
            public float scaledTime, lapStart, elapsed, clipLength, sampleTime, lastKeyTime, lastKeyValue;
            public float curveEvaluate, rawAssigned, graphicBefore, graphicAfter, tmpBefore, tmpAfter;
            public float graphicEndFrame, tmpEndFrame, rendererEndFrame;
            public int curveEvaluateBits, rawAssignedBits, graphicAfterBits, tmpAfterBits, graphicEndFrameBits, tmpEndFrameBits;
            public float[] probeTimes, rawProbeValues, productionClampProbeValues;
            public int[] rawProbeBits, productionClampProbeBits;
        }
        [Serializable] sealed class LapEndpointTraceReport
        {
            public string stage;
            public bool mechanismConfirmed = false;
            public LapEndpointTraceSample[] samples;
        }
        bool lapEndpointTraceEnabled;
        readonly Dictionary<string, LapEndpointTraceSample> lapEndpointTrace = new Dictionary<string, LapEndpointTraceSample>();
        static int EndpointFloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        public void BeginLapEndpointTrace() { lapEndpointTrace.Clear(); lapEndpointTraceEnabled = true; }
        public void WriteLapEndpointTrace(string outputPath, string stage)
        {
            foreach (var pair in lapEndpointTrace)
            {
                var sample = pair.Value;
                var node = transforms[sample.path];
                var graphic = node.GetComponent<Graphic>();
                var text = node.GetComponent<TMP_Text>();
                sample.stage = stage;
                sample.endFrame = Time.frameCount;
                sample.graphicEndFrame = graphic ? graphic.color[sample.axis] : 0f;
                sample.tmpEndFrame = text ? text.color[sample.axis] : 0f;
                sample.rendererEndFrame = graphic ? graphic.canvasRenderer.GetColor()[sample.axis] : 0f;
                sample.graphicEndFrameBits = EndpointFloatBits(sample.graphicEndFrame);
                sample.tmpEndFrameBits = EndpointFloatBits(sample.tmpEndFrame);
            }
            System.IO.File.WriteAllText(outputPath, JsonUtility.ToJson(new LapEndpointTraceReport { stage = stage, samples = lapEndpointTrace.Values.ToArray() }, true));
        }
        static readonly System.Reflection.FieldInfo originalTmpFontColor = typeof(TMP_Text).GetField("m_fontColor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        void DynamicClip(string rootKey, string name, float elapsed, HudClips authoredClips=null)
        {
            if (authoredClips == null && dynamicClips == null)
            {
                dynamicClips = JsonUtility.FromJson<HudClips>((Motion && Motion.hudAnimations ? Motion.hudAnimations : NativeContent.Load<TextAsset>("NativeVerification/RaceHudDynamicSource")).text);
                foreach (var clip in dynamicClips.clips) foreach (var binding in clip.curves)
                    binding.curve = new AnimationCurve(binding.keys.Select(k => new Keyframe(k.time,k.value,
                        k.infiniteIn ? float.PositiveInfinity : k.inSlope,k.infiniteOut ? float.PositiveInfinity : k.outSlope,
                        k.inWeight,k.outWeight) { weightedMode=(WeightedMode)k.weightedMode }).ToArray());
            }
            var source = (authoredClips??dynamicClips).clips.Single(c => c.name == name);
            float time = source.loop && source.length > 0 ? elapsed % source.length : Mathf.Min(elapsed, source.length);
            foreach (var binding in source.curves)
            {
                string key = rootKey + (binding.path.Length > 0 ? "/" + binding.path : "");
                var node = transforms[key]; node.gameObject.SetActive(true);
                var endpointKey = binding.keys[binding.keys.Length - 1];
                bool terminal = !source.loop && time >= endpointKey.time;
                float v = terminal ? endpointKey.value : binding.curve.Evaluate(time); string a = binding.attribute;
                if (a.StartsWith("localScale.")) { var scale=node.localScale; int axis=a.EndsWith("x")?0:a.EndsWith("y")?1:2; scale[axis]=prefabAuthored ? definitions[key].scale[axis] * v : v; node.localScale=scale; }
                else if (a.StartsWith("m_AnchoredPosition.")) { var pos=node.anchoredPosition; int axis=a.EndsWith("x")?0:1; pos[axis]=prefabAuthored ? definitions[key].position[axis] + v - binding.keys[0].value : v; node.anchoredPosition=pos; }
                else if (a.StartsWith("m_Color.") || a.StartsWith("m_fontColor."))
                {
                    var graphic=node.GetComponent<Graphic>(); if (!graphic) continue;
                    var color=graphic.color; int axis=a.EndsWith("r")?0:a.EndsWith("g")?1:a.EndsWith("b")?2:3;
                    var tmp = node.GetComponent<TMP_Text>();
                    float graphicBefore = graphic.color[axis], tmpBefore = tmp ? tmp.color[axis] : 0f;
                    color[axis]=v;
                    if (terminal && tmp && a.StartsWith("m_fontColor.") && tmp.color[axis] != v)
                    {
                        // Authored m_fontColor endpoints write the serialized field; TMP's approximate setter can retain a near-end value.
                        originalTmpFontColor.SetValue(tmp, color);
                        tmp.havePropertiesChanged = true;
                        tmp.SetVerticesDirty();
                    }
                    else graphic.color=color;
                    if (lapEndpointTraceEnabled && name == "lapped")
                    {
                        var lastKey = binding.keys[binding.keys.Length - 1];
                        var probeTimes = new[] { 0f, 0.49999997f, 0.5f, 0.50000006f, 5.75f };
                        var rawProbes = probeTimes.Select(t => binding.curve.Evaluate(t)).ToArray();
                        var clampedProbes = probeTimes.Select(t => binding.curve.Evaluate(source.loop && source.length > 0 ? t % source.length : Mathf.Min(t, source.length))).ToArray();
                        float graphicAfter = graphic.color[axis], tmpAfter = tmp ? tmp.color[axis] : 0f;
                        lapEndpointTrace[key + "/" + a] = new LapEndpointTraceSample {
                            clip = name, path = key, attribute = a, axis = axis, frame = Time.frameCount,
                            scaledTime = Time.time, lapStart = lapAnimationStart, elapsed = elapsed, loop = source.loop,
                            clipLength = source.length, sampleTime = time, keyCount = binding.keys.Length,
                            lastKeyTime = lastKey.time, lastKeyValue = lastKey.value, isText = tmp,
                            curveEvaluate = v, rawAssigned = color[axis], graphicBefore = graphicBefore, tmpBefore = tmpBefore,
                            graphicAfter = graphicAfter, tmpAfter = tmpAfter, curveEvaluateBits = EndpointFloatBits(v),
                            rawAssignedBits = EndpointFloatBits(color[axis]), graphicAfterBits = EndpointFloatBits(graphicAfter), tmpAfterBits = EndpointFloatBits(tmpAfter),
                            probeTimes = probeTimes, rawProbeValues = rawProbes, productionClampProbeValues = clampedProbes,
                            rawProbeBits = rawProbes.Select(EndpointFloatBits).ToArray(), productionClampProbeBits = clampedProbes.Select(EndpointFloatBits).ToArray()
                        };
                    }
                }
            }
        }
        static CanvasGroup DynamicGroup(RectTransform node)
        { var g=node.GetComponent<CanvasGroup>(); return g ? g : node.gameObject.AddComponent<CanvasGroup>(); }
        [Serializable] public sealed class DynamicHudSample
        {
            public string path,text,sprite;public bool active;public float alpha,fill;
            public Color color;public Vector2 size,position;public Vector3 scale,localPosition;
        }
        public DynamicHudSample[] ReadOriginalDynamicHud()
        {
            var result=new List<DynamicHudSample>();
            foreach(var pair in transforms.Where(p=>p.Key.StartsWith("HUD/Canvas/")))
            {
                var node=pair.Value;var graphic=node.GetComponent<Graphic>();var text=node.GetComponent<TMP_Text>();var image=node.GetComponent<Image>();
                float alpha=1;foreach(var group in node.GetComponentsInParent<CanvasGroup>(true))alpha*=group.alpha;
                result.Add(new DynamicHudSample{path=pair.Key,active=node.gameObject.activeInHierarchy,alpha=alpha,text=text?text.text:"",
                    sprite=image&&image.sprite?image.sprite.name:"",color=graphic?graphic.color:Color.white,fill=image?image.fillAmount:0,
                    size=node.rect.size,position=node.anchoredPosition,scale=node.localScale,localPosition=node.localPosition});
            }
            return result.ToArray();
        }

        public void NotifyOriginalLap() { lapAnimationStart=Time.time;lapAnimationPlayed=true; }
        public void ResetOriginalDynamics() {lapAnimationPlayed=false;dynamicAnimationOrigin=-1;}
        public void DrawOriginalRaceDynamics(NativeKartMotor motor, float baseSpeed, int lap, int laps, bool finished,
            bool showSpeed, string time, float timeAlpha, string difference, Color differenceColor, bool wrong)
        {
            if(dynamicAnimationOrigin<0)dynamicAnimationOrigin=Time.time;
            foreach (string key in new[] { "TimeBG","TimeText","Time","DifferenceTime","lapsbg","lapsbg/LapsTxt","lapsbg/Laps","Balancebar","Balancebar/Dot","WrongWay" })
                SetActive("HUD/Canvas/"+key,true);
            SetText("HUD/Canvas/Time",time); SetText("HUD/Canvas/DifferenceTime",difference);
            TextColor("HUD/Canvas/DifferenceTime",differenceColor); DynamicGroup(transforms["HUD/Canvas/Time"]).alpha=timeAlpha;
            SetText("HUD/Canvas/lapsbg/Laps",lap+"/"+laps);
            DynamicClip("HUD/Canvas/lapsbg", lapAnimationPlayed?"lapped":"lapnone",lapAnimationPlayed?Time.time-lapAnimationStart:Time.time-dynamicAnimationOrigin);
            var balance=transforms["HUD/Canvas/Balancebar"]; var dot=transforms["HUD/Canvas/Balancebar/Dot"];
            DynamicGroup(balance).alpha=motor.IsWheelie?1:0;
            float start=dot.sizeDelta.y*.5f, height=balance.sizeDelta.y-dot.sizeDelta.y*.5f;
            var dotPosition=dot.localPosition; dotPosition.y=height*motor.WheelieBalance-height*.5f+start; dot.localPosition=dotPosition;
            var wrongNode=transforms["HUD/Canvas/WrongWay"]; DynamicGroup(wrongNode).alpha=wrong?1:0;
            DynamicClip("HUD/Canvas/WrongWay","wrongwaysigna",Time.time-dynamicAnimationOrigin);
            SetActive("HUD/Canvas/SpeedGroup",showSpeed);
            foreach(string key in new[] {"speedbg","SpeedText","Speed","speedbar","speedbarFill","max"}) SetActive("HUD/Canvas/SpeedGroup/"+key,true);
            Vector3 velocity=motor.DriveVelocity; if(motor.Grounded)velocity.y=0;
            float speed=velocity.magnitude, ratio=baseSpeed>0?speed/baseSpeed:0;
            SetText("HUD/Canvas/SpeedGroup/Speed",speed.ToString("0",System.Globalization.CultureInfo.InvariantCulture));
            transforms["HUD/Canvas/SpeedGroup/speedbarFill"].GetComponent<Image>().fillAmount=ratio;
            bool max=ratio>=.688f; var maxNode=transforms["HUD/Canvas/SpeedGroup/max"];DynamicGroup(maxNode).alpha=max?1:0;
            DynamicClip("HUD/Canvas/SpeedGroup/max","maxspeedanim",Time.time-dynamicAnimationOrigin);
        }
    }
}
