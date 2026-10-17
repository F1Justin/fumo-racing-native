using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        NativeVehicleModuleHud moduleHud;
        public void DrawOriginalVehicleModuleBar(NativeKartMotor motor,Camera camera,bool human,bool visible)
        {
            if(!human||!motor){if(moduleHud)moduleHud.Present(motor,camera,false,false);return;}
            moduleHud=motor.GetComponent<NativeVehicleModuleHud>();
            if(!moduleHud)moduleHud=motor.gameObject.AddComponent<NativeVehicleModuleHud>();
            moduleHud.Present(motor,camera,human,visible,usingOnlineAuthoring);
        }
        public int OriginalModulePresentedMotorId => moduleHud ? moduleHud.PresentedMotorInstanceId : 0;
        public int OriginalModulePresentedCameraId => moduleHud ? moduleHud.PresentedCameraInstanceId : 0;
        public NativeVehicleModuleHud.State ReadOriginalVehicleModuleBar()=>moduleHud?moduleHud.ReadState():null;
    }
}
