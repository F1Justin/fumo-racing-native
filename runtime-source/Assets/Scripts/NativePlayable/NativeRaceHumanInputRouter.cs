using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
namespace Fumo.NativePlayable
{
    public sealed class NativeRaceHumanInputRouter : MonoBehaviour
    {
        sealed class Binding
        {
            public int Slot,Racer;
            public InputUser User;
            public bool OwnsUser;
            public uint StableOwnerId;
            public InputActionAsset Asset;
            public PlayerInput Owner;
            public InputAction Pause,Ok,Cancel,Navigate;
            public Action<InputAction.CallbackContext> PauseHandler,OkHandler,CancelHandler,ReleaseHandler,NavigateHandler;
        }
        readonly Binding[] bindings=new Binding[4];
        bool lifecycleStarted;
        void Start(){lifecycleStarted=true;if(State!=null)foreach(var h in State.Humans)h.JustStarted=false;}
        public NativeRaceStateFlow State;
        public Func<bool> AllowMenuInput;
        public event Action<int,Vector2> NavigateStarted;
        public void Bind(int slot,int racer,InputActionAsset asset) => BindInternal(slot,racer,asset,null);
        public void Bind(int slot,int racer,PlayerInput owner)
        {
            if (!owner || !owner.user.valid) throw new ArgumentException("Race requires the actual local selection owner");
            BindInternal(slot,racer,owner.actions,owner);
        }
        void BindInternal(int slot,int racer,InputActionAsset asset,PlayerInput owner)
        {
            if(bindings[slot]!=null&&bindings[slot].Asset==asset && (!owner || bindings[slot].User.id==owner.user.id && bindings[slot].Owner==owner)){bindings[slot].Racer=racer;SynchronizeUsers();return;}
            Unbind(slot);
            if(asset==null)throw new ArgumentNullException(nameof(asset));
            var b=new Binding {Slot=slot,Racer=racer,Asset=asset,Owner=owner,OwnsUser=!owner,StableOwnerId=owner?owner.user.id:0,User=owner?owner.user:InputUser.CreateUserWithoutPairedDevices()};
            if(b.OwnsUser && asset.devices.HasValue)foreach(var device in asset.devices.Value)b.User=InputUser.PerformPairingWithDevice(device,b.User);
            if(b.OwnsUser)b.User.AssociateActionsWithUser(asset);
            b.Pause=asset.FindAction("Normal/Pause",true);b.Ok=asset.FindAction("Normal/Ok",true);
            b.Cancel=asset.FindAction("Normal/Cancel",true);b.Navigate=asset.FindAction("Normal/Navigate",true);
            b.PauseHandler=context=>{SynchronizeUsers();if(AllowMenuInput!=null&&!AllowMenuInput())return;State.PauseStarted((int)b.User.index);};
            b.OkHandler=context=>{SynchronizeUsers();if(AllowMenuInput!=null&&!AllowMenuInput())return;State.OkStarted((int)b.User.index);};
            b.CancelHandler=context=>{SynchronizeUsers();if(AllowMenuInput!=null&&!AllowMenuInput())return;State.CancelStarted((int)b.User.index);};
            b.ReleaseHandler=context=>{SynchronizeUsers();if(AllowMenuInput!=null&&!AllowMenuInput())return;State.CancelCanceled((int)b.User.index);};
            b.NavigateHandler=context=>{SynchronizeUsers();if(AllowMenuInput!=null&&!AllowMenuInput())return;NavigateStarted?.Invoke(b.Slot,context.ReadValue<Vector2>());};
            b.Pause.started+=b.PauseHandler;b.Ok.started+=b.OkHandler;b.Cancel.started+=b.CancelHandler;
            b.Cancel.canceled+=b.ReleaseHandler;b.Navigate.started+=b.NavigateHandler;
            bindings[slot]=b;SynchronizeUsers();if(AllowMenuInput!=null&&!AllowMenuInput())return;State.Humans[slot].JustStarted=!lifecycleStarted;
        }
        public PlayerInput BoundPlayerInput(int slot) => bindings[slot]?.Owner;
        public InputActionAsset BoundActionAsset(int slot) => bindings[slot]?.Asset;
        public uint BoundStableUserId(int slot) => bindings[slot]?.StableOwnerId ?? 0;
        public void SynchronizeUsers()
        {for(int i=0;i<bindings.Length;i++){var b=bindings[i];if(b==null)continue;
            if(!b.OwnsUser){var service=NativeLocalSelectionOwners.Instance;var owner=service?service.FindOwner(b.StableOwnerId):null;
                if(!owner||!owner.user.valid){State.ConfigureHuman(i,-1,b.Racer,false,false);continue;}b.User=owner.user;}
            if(!b.User.valid){State.ConfigureHuman(i,-1,b.Racer,false,false);continue;}State.ConfigureHuman(i,(int)b.User.index,b.Racer,true);}}
        public void Unbind(int slot)
        {
            var b=bindings[slot];if(b==null)return;
            b.Pause.started-=b.PauseHandler;b.Ok.started-=b.OkHandler;b.Cancel.started-=b.CancelHandler;
            b.Cancel.canceled-=b.ReleaseHandler;b.Navigate.started-=b.NavigateHandler;
            if(b.OwnsUser && b.User.valid)b.User.UnpairDevicesAndRemoveUser();bindings[slot]=null;
            State.ConfigureHuman(slot,-1,-1,false,false);
            SynchronizeUsers();
        }
        void OnDestroy(){for(int i=0;i<bindings.Length;i++)Unbind(i);}
    }
}
