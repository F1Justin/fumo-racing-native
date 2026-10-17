using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        public void LocalSelectionQuit(bool visible,float fill)
        {
            SetQuitPanel("selection/Canvas/CharSelection/quitting",visible,fill);
        }
    }
    public sealed partial class NativeRaceHud
    {
        public void LocalSelectionQuit(bool visible,float fill) => authoredUi.LocalSelectionQuit(visible,fill);
        public void PlayLocalCharacterCancel() => PlaySound(menuCancelSound);
    }
}
