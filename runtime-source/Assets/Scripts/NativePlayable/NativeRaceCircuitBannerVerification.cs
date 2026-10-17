using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class CircuitBannerReport
        {
            public bool passed,accepted=false,physicalInputAccepted=false,renderEvidenceAvailable=false;
            public string error,assemblySHA256,fixtureType="NativeInjected source HoverCircuit consumer and ordinary scaled-clock actual RectTransform/Image writers";
            public string pending="Original/native visual pixels and physical input replay pending";
            public List<string> checks=new List<string>();public List<CircuitBannerSample> samples=new List<CircuitBannerSample>();
        }
        [Serializable] sealed class CircuitBannerSample { public int count,spriteId;public float elapsed,x,alpha,expectedX,expectedAlpha;public bool activeInHierarchy; }
        [Serializable] sealed class CircuitBannerReference { public CircuitBannerReferenceClip[] clips; }
        [Serializable] sealed class CircuitBannerReferenceClip { public float length;public CircuitBannerReferenceCurve[] curves; }
        [Serializable] sealed class CircuitBannerReferenceCurve { public string attribute;public CircuitBannerReferenceKey[] keys; }
        [Serializable] sealed class CircuitBannerReferenceKey { public float time,value,inSlope,outSlope;public bool infiniteIn,infiniteOut; }
        static float CircuitBannerReferenceValue(CircuitBannerReferenceCurve curve,float time)
        {
            var keys=curve.keys;if(time<=keys[0].time)return keys[0].value;
            for(int i=1;i<keys.Length;i++)if(time<keys[i].time)
            {
                var a=keys[i-1];var b=keys[i];if(a.infiniteOut||b.infiniteIn)return a.value;
                float d=b.time-a.time,u=(time-a.time)/d,u2=u*u,u3=u2*u;
                return (2*u3-3*u2+1)*a.value+(u3-2*u2+u)*d*a.outSlope+(-2*u3+3*u2)*b.value+(u3-u2)*d*b.inSlope;
            }
            return keys[keys.Length-1].value;
        }
        static void CircuitBannerRequire(CircuitBannerReport report,bool condition,string name)
        {report.checks.Add((condition?"PASS ":"FAIL ")+name);if(!condition)throw new InvalidOperationException(name);}
        internal IEnumerator CheckCircuitBannerMotionNonrendered()
        {
            var report=new CircuitBannerReport();var stack=new Stack<IEnumerator>();stack.Push(CircuitBannerMotionCore(report));
            while(stack.Count>0)
            {
                bool next;object value;
                try{next=stack.Peek().MoveNext();value=next?stack.Peek().Current:null;}
                catch(Exception error){report.error=error.ToString();break;}
                if(!next){stack.Pop();if(stack.Count==0)report.passed=true;continue;}
                if(value is IEnumerator nested&&!(value is CustomYieldInstruction)){stack.Push(nested);continue;}yield return value;
            }
            foreach(var routine in stack)(routine as IDisposable)?.Dispose();
            Directory.CreateDirectory(reportDirectory);File.WriteAllText(Path.Combine(reportDirectory,"circuit-banner-motion-report.json"),JsonUtility.ToJson(report,true));
            Application.Quit(report.passed?0:2);
        }
        IEnumerator CircuitBannerMotionCore(CircuitBannerReport report)
        {
            report.assemblySHA256=ProfileSha256(File.ReadAllBytes(typeof(NativeRace).Assembly.Location));phase=Phase.Menu;
            CircuitBannerRequire(report,Time.timeScale==1&&Time.captureFramerate==0,"ordinary scaled animation clock");
            hud.SetMenuPage(NativeMenuPage.Tracks);hud.HoverOriginalCircuitBanner(0);
            var ui=(NativeAuthoredUi)typeof(NativeRaceHud).GetField("authoredUi",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(hud);
            CircuitBannerRequire(report,ui!=null&&hud.PendingOriginalCircuitBannerEvents==0,"production hover obtains actual authored nodes through safe lazy initialization");
            ui.Show("selection","CupSelection");ui.SetActive("selection/Canvas/CupSelection/maskbanner",true);
            hud.SetMenuPage(NativeMenuPage.Tracks);
            var reference=JsonUtility.FromJson<CircuitBannerReference>(NativeContent.Load<TextAsset>("NativeVerification/CircuitBannerSource").text).clips.Single();
            CircuitBannerRequire(report,reference.curves.Length==2,"original exactly two animated properties");
            int before=hud.OriginalCircuitBannerPlayCount;hud.HoverOriginalCircuitBanner(0);
            CircuitBannerRequire(report,hud.OriginalCircuitBannerPlayCount==before+1,"injected source hover starts select exactly once");
            foreach(float at in new[]{0f,.125f,.25f,.5f,.6f,.6666667f,.8f})
            {
                while(Time.time-hud.OriginalCircuitBannerStarted<at)yield return null;
                hud.TickOriginalCircuitBannerMotion();RecordCircuitBannerSample(report,reference,0);
            }
            int count=hud.OriginalCircuitBannerPlayCount;hud.HoverOriginalCircuitBanner(0);
            CircuitBannerRequire(report,hud.OriginalCircuitBannerPlayCount==count+1,"same circuit source hover replays completed select");
            RecordCircuitBannerSample(report,reference,0);
            while(Time.time-hud.OriginalCircuitBannerStarted<.2f)yield return null;
            hud.TickOriginalCircuitBannerMotion();RecordCircuitBannerSample(report,reference,0);
            count=hud.OriginalCircuitBannerPlayCount;hud.HoverOriginalCircuitBanner(0);
            CircuitBannerRequire(report,hud.OriginalCircuitBannerPlayCount==count+1,"same circuit source hover restarts active select");
            RecordCircuitBannerSample(report,reference,0);
            hud.NavigateOriginalChoice(NativeMenuPage.Tracks,Vector2.right,vehicleRoster,selectableCharacters);
            CircuitBannerRequire(report,hud.SelectedMenuIndex==1,"production circuit keyboard consumer selects next source circuit");
            RecordCircuitBannerSample(report,reference,1);
        }
        void RecordCircuitBannerSample(CircuitBannerReport report,CircuitBannerReferenceClip reference,int track)
        {
            var node=hud.OriginalCircuitBannerNode;var image=node.GetComponent<Image>();float elapsed=Time.time-hud.OriginalCircuitBannerStarted;
            float x=CircuitBannerReferenceValue(reference.curves.Single(curve=>curve.attribute=="m_AnchoredPosition.x"),elapsed);
            float alpha=CircuitBannerReferenceValue(reference.curves.Single(curve=>curve.attribute=="m_Color.a"),elapsed);
            report.samples.Add(new CircuitBannerSample{count=hud.OriginalCircuitBannerPlayCount,spriteId=image.sprite.GetInstanceID(),elapsed=elapsed,x=node.anchoredPosition.x,alpha=image.color.a,expectedX=x,expectedAlpha=alpha,activeInHierarchy=node.gameObject.activeInHierarchy});
            CircuitBannerRequire(report,node.gameObject.activeInHierarchy&&image.sprite==hud.TrackBannerAt(track),"actual source banner hierarchy and sprite binding");
            CircuitBannerRequire(report,Mathf.Abs(node.anchoredPosition.x-x)<.0002f&&Mathf.Abs(image.color.a-alpha)<.000002f,"actual RectTransform/Image source key trajectory");
        }
    }
}
