using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        readonly System.Collections.Generic.Queue<int> pendingCircuitBannerSelect=new System.Collections.Generic.Queue<int>();
        public int PendingOriginalCircuitBannerEvents=>pendingCircuitBannerSelect.Count;
        bool EnsureOriginalCircuitBannerUi()
        {
            EnsureAuthoredMenus();
            return authoredUi!=null;
        }
        public void HoverOriginalCircuitBanner(int index)
        {
            if(!EnsureOriginalCircuitBannerUi()){pendingCircuitBannerSelect.Enqueue(index);return;}
            authoredUi.PlayOriginalCircuitBannerSelect(TrackBannerAt(index));
            originalTrackBannerImage=authoredUi.CircuitBannerNode.GetComponent<UnityEngine.UI.Image>();
            originalTrackBannerSelection=index;
        }
        public void TickOriginalCircuitBannerMotion()
        {
            if(MenuPage!=NativeMenuPage.Tracks||!EnsureOriginalCircuitBannerUi())return;
            while(pendingCircuitBannerSelect.Count>0)HoverOriginalCircuitBanner(pendingCircuitBannerSelect.Dequeue());
            if(originalTrackBannerSelection!=SelectedMenuIndex)HoverOriginalCircuitBanner(SelectedMenuIndex);
            authoredUi.TickOriginalCircuitBannerSelect();
        }
        public RectTransform OriginalCircuitBannerNode=>authoredUi.CircuitBannerNode;
        public int OriginalCircuitBannerPlayCount=>authoredUi.CircuitBannerPlayCount;
        public float OriginalCircuitBannerStarted=>authoredUi.CircuitBannerStarted;
    }
}
