using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceNetwork
    {
        readonly NativeCharacterProfileState onlineCharacterProfile = new NativeCharacterProfileState(0);
        bool OnlineCharacterProfilePage => onlineChoice == OnlineChoice.Characters || onlineChoice == OnlineChoice.Vehicles || onlineChoice == OnlineChoice.Skins;

        bool HandleOnlineCharacterProfileInput(bool escape)
        {
            if (!OnlineCharacterProfilePage) return false;
            if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Use))
            {
                CompleteOnlineCharacterProfile(onlineCharacterProfile.Config(true, false, NativeInputBindings.Profiles.Length,
                    OnlineHud.MenuPage, OnlineHud.SelectedMenuIndex, ApplyOnlineCharacterProfile));
                return true;
            }
            if (!onlineCharacterProfile.Open) return false;
            if (NativeInputBindings.GetNavigateStarted(out var direction))
                CompleteOnlineCharacterProfile(onlineCharacterProfile.Navigate(NativePlayerBoxNavigation.Direction(direction), NativeInputBindings.Profiles.Length));
            if (escape || NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Cancel))
                CompleteOnlineCharacterProfile(onlineCharacterProfile.Cancel());
            else if (NativeInputBindings.GetButtonDown(NativeInputBindings.Action.Ok))
                CompleteOnlineCharacterProfile(onlineCharacterProfile.Confirm(ApplyOnlineCharacterProfile));
            return true;
        }

        bool ApplyOnlineCharacterProfile(int index)
        {
            var profiles = NativeInputBindings.Profiles;
            return index >= -1 && index < profiles.Length
                && NativeInputBindings.SelectCharacterProfile(index == -1 ? "" : profiles[index].profileId);
        }

        void CompleteOnlineCharacterProfile(NativeCharacterProfileResult result)
        {
            OnlineHud.CharacterProfileSound(result);
            if (result != NativeCharacterProfileResult.Applied && result != NativeCharacterProfileResult.Cancelled) return;
            OnlineHud.CloseCharacterProfile(onlineCharacterProfile);
            OnlineHud.SelectMenuIndex(onlineCharacterProfile.ReturnIndex);
        }

        void CloseOnlineCharacterProfile()
        {
            if (!onlineCharacterProfile.Open) return;
            onlineCharacterProfile.Cancel();
            OnlineHud.CloseCharacterProfile(onlineCharacterProfile);
        }
    }
}
