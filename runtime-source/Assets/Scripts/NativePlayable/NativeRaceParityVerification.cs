using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        [Serializable] sealed class ParityPass { public bool passed; }
        IEnumerator CheckVisualParityLive()
        {
            if (menuPresentation) menuPresentation.SetVisible(false);
            hud.SetVisible(false);
            yield return NativeVisualParityVerification.Run(this, reportDirectory, trackId == "forest");
            yield return null; yield return new WaitForEndOfFrame();
            CaptureTestScreenshot("visual-track.png");
            var result = JsonUtility.FromJson<ParityPass>(File.ReadAllText(Path.Combine(reportDirectory,"visual-source-state.json")));
            Application.Quit(result.passed && errorCount == 0 ? 0 : 2);
        }
        IEnumerator CheckAudioParityLive()
        {
            yield return NativeAudioVerification.StartVerification(reportDirectory);
            Application.Quit(NativeAudioVerification.LastResult.passed && errorCount == 0 ? 0 : 2);
        }
    }
}
