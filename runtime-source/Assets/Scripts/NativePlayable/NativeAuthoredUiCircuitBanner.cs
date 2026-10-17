using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        const string CircuitBannerKey="selection/Canvas/CupSelection/maskbanner/circuitgiantbanner";
        HudClips circuitBannerClips;
        public int CircuitBannerPlayCount { get; private set; }
        public float CircuitBannerStarted { get; private set; }
        public RectTransform CircuitBannerNode => transforms[CircuitBannerKey];
        public void PlayOriginalCircuitBannerSelect(Sprite sprite)
        {
            if(circuitBannerClips==null)
            {
                var resource=NativeContent.Load<TextAsset>("NativeVerification/CircuitBannerSource");
                if(!resource)throw new InvalidOperationException("Original circuit banner animation source missing");
                circuitBannerClips=JsonUtility.FromJson<HudClips>(resource.text);
                foreach(var clip in circuitBannerClips.clips)foreach(var binding in clip.curves)
                    binding.curve=new AnimationCurve(binding.keys.Select(k=>new Keyframe(k.time,k.value,k.infiniteIn?float.PositiveInfinity:k.inSlope,k.infiniteOut?float.PositiveInfinity:k.outSlope,k.inWeight,k.outWeight){weightedMode=(WeightedMode)k.weightedMode}).ToArray());
            }
            var node=CircuitBannerNode;node.gameObject.SetActive(false);
            node.GetComponent<Image>().sprite=sprite;node.gameObject.SetActive(true);
            CircuitBannerStarted=Time.time;CircuitBannerPlayCount++;
            TickOriginalCircuitBannerSelect();
        }
        public void TickOriginalCircuitBannerSelect()
        {
            if(circuitBannerClips==null||!CircuitBannerNode.gameObject.activeInHierarchy)return;
            DynamicClip(CircuitBannerKey,"circuitgiantbannerselect",Time.time-CircuitBannerStarted,circuitBannerClips);
        }
    }
}
