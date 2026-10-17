using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Fumo.NativePlayable
{
    public sealed class NativeHapticUiInput : IDisposable
    {
        public EventSystem System { get; }
        readonly InputSystemUIInputModule module;
        readonly bool ownSystem,ownModule,oldNavigation,oldEnabled;
        public bool PointerReady => module && module.isActiveAndEnabled;
        public NativeHapticUiInput()
        {
            System=EventSystem.current;ownSystem=!System;
            if(ownSystem)System=new GameObject("Haptic pointer input",typeof(EventSystem)).GetComponent<EventSystem>();
            module=System.GetComponent<InputSystemUIInputModule>();ownModule=!module;
            if(ownModule) { module=System.gameObject.AddComponent<InputSystemUIInputModule>();module.AssignDefaultActions(); }
            oldEnabled=module.enabled;module.enabled=true;oldNavigation=System.sendNavigationEvents;
            // Controller confirmation belongs to the current test state; pointer clicks use the UI module.
            System.sendNavigationEvents=false;System.SetSelectedGameObject(null);
        }
        public void Dispose()
        {
            if(!System)return;
            System.sendNavigationEvents=oldNavigation;System.SetSelectedGameObject(null);
            if(ownSystem)UnityEngine.Object.Destroy(System.gameObject);
            else if(ownModule)UnityEngine.Object.Destroy(module);
            else module.enabled=oldEnabled;
        }
    }
}
