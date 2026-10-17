using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        internal Transform InsanityPlayerHudCanvas => transforms["HUD/Canvas/TimeBG"].GetComponentInParent<Canvas>(true).transform;
    }
    public sealed partial class NativeRaceHud
    {
        internal Transform PrepareInsanityPlayerHudCanvas()
        {
            EnsureAuthoredMenus();
            if (authoredUi == null) throw new System.InvalidOperationException("Original authored HUD unavailable");
            BindLocalViewCanvases();
            return authoredUi.InsanityPlayerHudCanvas;
        }
    }
    public sealed partial class NativeRace
    {
        void BindNaturalInsanityConsumer()
        {
            var router = itemWorld.GetComponent<NativeInsanityHudRouter>();
            if (!router) router = itemWorld.gameObject.AddComponent<NativeInsanityHudRouter>();
            int primary=Net && Net.Open ? Net.LocalRacerIndex : 0;
            if(primary<0||primary>=racers.Length)return;
            router.Bind(new[]{racers[primary]}, new[]{hud.PrepareInsanityPlayerHudCanvas()});
        }
    }
}
