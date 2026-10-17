namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        void RenderOnlinePausedMenu()
        {
            OnlineButton("resume", "Resume", () => paused = false);
            OnlineButton("room", "End Race and Return to Room", () => ConfirmOnlineAction(() => Send("abort"), "End Race and Return to Room"), IsHost);
            OnlineButton("spectate", "Retire and Spectate", () => SetRoomRole(true), !requestedSpectator);
            OnlineButton("leave", "Leave Room", () => ConfirmOnlineAction(LeaveToOnlineMenu, "Leave Room"));
            if (OnlineHud.NetworkPrompt("pause-select", OnlineHud.keyConfirm, "Select"))
            {
                var selected = onlineButtons.Find(button => button.key == onlineFocus && button.enabled);
                if (selected != null) ActivateOnlineButton(selected);
            }
            if (OnlineHud.NetworkPrompt("pause-back", OnlineHud.keyCancel, "Resume")) paused = false;
        }
    }
}
