using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fumo.NativePlayable
{
    public sealed class NativeMainQuitSourceState
    {
        public int HumansPressing { get; private set; }
        public float HoldingTime { get; private set; }
        public float FillAmount { get; private set; } = 1;
        public bool Enabled { get; private set; }
        public bool PanelActive { get; private set; }
        public void Press()
        {
            HumansPressing=Mathf.Clamp(HumansPressing+1,0,3);
            if(HumansPressing>0){Enabled=true;PanelActive=true;}
        }
        public void Release()
        {
            HumansPressing=Mathf.Clamp(HumansPressing-1,0,3);
            if(HumansPressing==0){HoldingTime=0;Enabled=false;PanelActive=false;}
        }
        public void Update(float deltaTime,Action exit)
        {
            if(!Enabled)return;
            HoldingTime+=deltaTime;FillAmount=Mathf.Clamp01(HoldingTime);
            if(HoldingTime>=1){exit();Enabled=false;}
        }
    }
    public sealed partial class NativeRace
    {
        NativeMainQuitSourceState mainQuitSource;
        bool mainQuitMenu2Present;
        InputAction mainQuitAction;
        bool mainQuitSourceReady, mainQuitAutoActive;
        InputAction MainQuitSourceAction()
        {
            var applied=NativeInputBindings.ActiveActionAsset(0);
            return applied==null || applied.actionMaps.Count==0?null:applied.actionMaps[0].FindAction("Cancel",false);
        }
        void SubscribeMainQuitAction(){if(mainQuitAction==null)return;mainQuitAction.started+=MainQuitCancelStarted;mainQuitAction.canceled+=MainQuitCancelCanceled;}
        void UnsubscribeMainQuitAction(){if(mainQuitAction==null)return;mainQuitAction.started-=MainQuitCancelStarted;mainQuitAction.canceled-=MainQuitCancelCanceled;}
        void MainQuitCancelStarted(InputAction.CallbackContext context){mainQuitSource.Press();PublishMainQuitSource();}
        void MainQuitCancelCanceled(InputAction.CallbackContext context){mainQuitSource.Release();PublishMainQuitSource();}
        void InitializeMainQuitSource(){mainQuitSourceReady=true;SynchronizeMainQuitSourceActive();}
        int MainQuitSourceSceneLocation()
        {
            if(phase!=Phase.Menu)return -1;
            if(settingsOpen && (settingsPage==SettingsPage.Profiles || settingsPage==SettingsPage.ProfileDevices || settingsPage==SettingsPage.EditProfile || settingsPage==SettingsPage.DeleteProfile))return -1;
            if(hud==null)return 0;
            switch(hud.MenuPage)
            {
                case NativeMenuPage.Main:case NativeMenuPage.RaceModes:case NativeMenuPage.Extras:case NativeMenuPage.Options:case NativeMenuPage.Credits:case NativeMenuPage.MusicRoom:return 1;
                case NativeMenuPage.Characters:case NativeMenuPage.Vehicles:case NativeMenuPage.Skins:case NativeMenuPage.Cups:case NativeMenuPage.Tracks:case NativeMenuPage.Custom:return -1;
                default:return 0;
            }
        }
        void SynchronizeMainQuitSceneLifetime()
        {
            int location=MainQuitSourceSceneLocation();
            if(location<0 && mainQuitMenu2Present)
            {
                DestroyMainQuitSource();mainQuitAutoActive=false;mainQuitAction=null;mainQuitSource=null;mainQuitMenu2Present=false;hud?.SetMenu2SourceScenePresent(false);
            }
            else if(location>0 && !mainQuitMenu2Present)
            {
                mainQuitSource=new NativeMainQuitSourceState();mainQuitAction=null;mainQuitMenu2Present=true;hud?.SetMenu2SourceScenePresent(true);
            }
        }
        void SynchronizeMainQuitSourceActive()
        {
            if(!mainQuitSourceReady)return;
            SynchronizeMainQuitSceneLifetime();
            bool active=isActiveAndEnabled && mainQuitMenu2Present && hud!=null && hud.MainQuitSourceParentActive;
            if(active==mainQuitAutoActive)return;
            mainQuitAutoActive=active;
            if(active){if(mainQuitAction==null)mainQuitAction=MainQuitSourceAction();SubscribeMainQuitAction();}
            else {UnsubscribeMainQuitAction();if(mainQuitAction==null)mainQuitAction=MainQuitSourceAction();}
        }
        void FixedUpdate()
        {
            SynchronizeSharedMenuInput();
            SynchronizeMainQuitSourceActive();if(!mainQuitAutoActive)return;
            var next=MainQuitSourceAction();if(ReferenceEquals(mainQuitAction,next))return;
            UnsubscribeMainQuitAction();mainQuitAction=next;SubscribeMainQuitAction();
        }
        void OnEnable(){SynchronizeMainQuitSourceActive();}
        void OnDisable(){if(mainQuitAutoActive){mainQuitAutoActive=false;UnsubscribeMainQuitAction();if(mainQuitAction==null)mainQuitAction=MainQuitSourceAction();}}
        void DestroyMainQuitSource(){if(!mainQuitSourceReady || !mainQuitMenu2Present)return;if(mainQuitAction==null)mainQuitAction=MainQuitSourceAction();UnsubscribeMainQuitAction();}
        void UpdateMainQuitSource()
        {
            SynchronizeMainQuitSourceActive();
            hud?.SetMenu2SourceOptionsParentActive(!(settingsOpen && (settingsPage==SettingsPage.Video || settingsPage==SettingsPage.Audio || settingsPage==SettingsPage.Language)));
            hud?.TickMenu2SourceWheels(Time.deltaTime);
            SynchronizeMainQuitSourceActive();
            if(isActiveAndEnabled && mainQuitMenu2Present && hud!=null && hud.MainQuitSourceParentActive)mainQuitSource.Update(Time.deltaTime,ExitMainQuitSource);
            PublishMainQuitSource();
        }
        void PublishMainQuitSource(){hud?.SetMainQuitSourceState(mainQuitSource!=null && mainQuitSource.PanelActive,mainQuitSource==null?1:mainQuitSource.FillAmount);}
        void ExitMainQuitSource(){NativeTelemetry.Event("app.quit.requested","accepted","main-quit-hold");AudioListener.volume=0;ObserveQuitSourceExit();Application.Quit();}
    }
}
