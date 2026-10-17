using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        float originalWrongWayTime;
        void TickOriginalWrongWayHud()
        {
            if(progress==null || phase==Phase.Menu || progress[0].CurrentTrigger<0 || gates.Length==0)
            {originalWrongWayTime=0;hud.OriginalWrongWay=false;return;}
            int trigger=progress[0].CurrentTrigger;
            if(trigger>=gates.Length)trigger=0;
            if(Vector3.Dot(racers[0].transform.forward,gates[trigger].forward)<-.2f)
            {originalWrongWayTime+=Time.deltaTime;if(originalWrongWayTime>=.5f)hud.OriginalWrongWay=true;}
            else {originalWrongWayTime=0;hud.OriginalWrongWay=false;}
        }
    }
}
