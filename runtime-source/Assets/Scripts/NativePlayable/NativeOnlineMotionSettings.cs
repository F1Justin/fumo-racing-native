using UnityEngine;
namespace Fumo.NativePlayable
{
    [CreateAssetMenu(menuName = "Fumo/Online Motion Settings")]
    public sealed class NativeOnlineMotionSettings : ScriptableObject
    {
        public float focusWidth = 40, selectorOffset = 16f / 1.8f;
        public float menuFocusInSeconds = .2f, menuFocusOutSeconds = .15f, menuSelectorSeconds = .15f;
        public float mainWheelDegreesPerSecond = -90, optionsWheelDegreesPerSecond = -65, raceWheelDegreesPerSecond = -360;
        public AnimationCurve menuFocusIn = new AnimationCurve(new Keyframe(0, 0, 1.9287574f, 1.9287574f),
            new Keyframe(.5058899f, .9103776f, .4229123f, .4229123f), new Keyframe(1, 1, 0, 0));
        public AnimationCurve menuFocusOut = new AnimationCurve(new Keyframe(0, 0, 0, 0), new Keyframe(1, 1, 2, 2));
        public AnimationCurve menuSelectorCurve = new AnimationCurve(new Keyframe(0, 0, 1.1909018f, 1.1909018f),
            new Keyframe(.58855224f, .79717046f, 1.0621257f, 1.0621257f), new Keyframe(1, 1, 0, 0));
        public AnimationCurve countdownScaleX, countdownScaleY, countdownScaleZ, countdownGreen, countdownBlue, countdownRed;
        public AnimationCurve rankScaleX, rankScaleY, rankRotation;
        public TextAsset hudAnimations;
    }
}
