using UnityEngine;

namespace Fumo.NativePlayable
{
    /// <summary>
    /// CPU wall-clock callback intervals only. Camera callback spans are not GPU
    /// execution time; the inter-callback gap includes engine and platform work,
    /// presentation and frame pacing, not just timer sleep. Use FrameTimingManager
    /// and a Metal capture for GPU attribution.
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class NativeFrameProbe : MonoBehaviour
    {
        public static bool Active;

        static double loopStart, loopEnd;
        static double renderFirstStart;
        static double previousRenderEnd;
        static double previousRenderSpan;
        static double previousLoopSpan;
        static int renderFrame = -1;

        public static void Enable()
        {
            if (Active) return;
            Active = true;
            Camera.onPreRender += BeforeRender;
            Camera.onPostRender += AfterRender;
        }

        public static void Disable()
        {
            if (!Active) return;
            Active = false;
            Camera.onPreRender -= BeforeRender;
            Camera.onPostRender -= AfterRender;
        }

        /// <summary>Milliseconds the previous frame spent inside Update..LateUpdate.</summary>
        public static double PreviousLoopMilliseconds { get { return previousLoopSpan * 1000.0; } }

        /// <summary>CPU wall-clock span between camera callbacks, not hardware GPU time.</summary>
        public static double PreviousRenderMilliseconds { get { return previousRenderSpan * 1000.0; } }

        /// <summary>Milliseconds between the end of the previous frame's rendering and this frame's first Update.</summary>
        public static double WaitMilliseconds()
        {
            return previousRenderEnd > 0 ? (loopStart - previousRenderEnd) * 1000.0 : double.NaN;
        }

        void OnDestroy() { Disable(); }

        void Update()
        {
            if (!Active) return;
            double now = Time.realtimeSinceStartupAsDouble;
            loopStart = now;
            if (previousLoopSpan > 0 && WaitMilliseconds() < 0)
            {
                // A frame ran without rendering (for example a headless step);
                // fall back to the script span rather than reporting negative wait.
                previousRenderEnd = now;
            }
        }

        void LateUpdate()
        {
            if (!Active) return;
            loopEnd = Time.realtimeSinceStartupAsDouble;
            previousLoopSpan = loopEnd - loopStart;
        }

        static void BeforeRender(Camera camera)
        {
            if (!Active) return;
            if (renderFrame == Time.frameCount) return;
            renderFrame = Time.frameCount;
            renderFirstStart = Time.realtimeSinceStartupAsDouble;
        }

        static void AfterRender(Camera camera)
        {
            if (!Active) return;
            double now = Time.realtimeSinceStartupAsDouble;
            previousRenderEnd = now;
            previousRenderSpan = now - renderFirstStart;
        }
    }
}
