using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public void FinalizeCompletedMenu2SourceTransitionForCheck()
        {
            if(!transitionPending)return;
            var state=NativeMenuTransitionPlayback.Evaluate(transitionFrom,transitionTo,
                Time.unscaledTime-transitionStarted,600,transitionWheelAngle,retainedMainWheelAngle);
            if(!state.completed)return;
            TransitionState=state;transitionPending=false;
            if(transitionFrom==NativeMenuPage.RaceModes && transitionTo==NativeMenuPage.Main)SyncSourceMainWheelAngleFromTransform();
        }
    }
}
