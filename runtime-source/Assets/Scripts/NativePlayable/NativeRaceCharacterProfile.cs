using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        readonly NativeCharacterProfileState[] characterProfiles = { new NativeCharacterProfileState(0),new NativeCharacterProfileState(1),new NativeCharacterProfileState(2),new NativeCharacterProfileState(3) };
        float characterProfilePreviewPausedAt = -1;
        bool SuspendCharacterProfilePreview()
        {
            if (characterProfiles[0].Open)
            {
                if (characterProfilePreviewPausedAt < 0) characterProfilePreviewPausedAt = Time.time;
                return true;
            }
            if (characterProfilePreviewPausedAt >= 0)
            {
                previewRotationStarted += Time.time - characterProfilePreviewPausedAt;
                characterProfilePreviewPausedAt = -1;
            }
            return false;
        }
        bool CharacterProfilePage => phase == Phase.Menu && (hud.MenuPage == NativeMenuPage.Characters || hud.MenuPage == NativeMenuPage.Vehicles || hud.MenuPage == NativeMenuPage.Skins);
        bool ApplyCharacterProfile(int human, int index)
        {
            var profiles = NativeInputBindings.Profiles;
            if (index < -1 || index >= profiles.Length) return false;
            return NativeInputBindings.SelectCharacterProfile(index == -1 ? "" : profiles[index].profileId,human);
        }
        void CompleteCharacterProfile(NativeCharacterProfileState state, NativeCharacterProfileResult result)
        {
            hud.CharacterProfileSound(result);
            if (result != NativeCharacterProfileResult.Applied && result != NativeCharacterProfileResult.Cancelled) return;
            hud.CloseCharacterProfile(state); hud.SetMenuPage(state.ReturnPage); hud.SelectMenuIndex(state.ReturnIndex);
        }
        bool HandleCharacterProfileInput()
        {
            if (!CharacterProfilePage) return false;
            var state = characterProfiles[0];
            if (NativeInputBindings.GetCharacterProfileStarted(NativeInputBindings.Action.Use))
            {
                var result = state.Config(true,false,NativeInputBindings.Profiles.Length,hud.MenuPage,hud.SelectedMenuIndex,index => ApplyCharacterProfile(0,index));
                CompleteCharacterProfile(state,result); return true;
            }
            if (!state.Open) return false;
            if (NativeInputBindings.GetNavigateStarted(out var navigation))
                CompleteCharacterProfile(state,state.Navigate(NativePlayerBoxNavigation.Direction(navigation),NativeInputBindings.Profiles.Length));
            if (NativeInputBindings.GetCharacterProfileStarted(NativeInputBindings.Action.Cancel)) CompleteCharacterProfile(state,state.Cancel());
            else if (NativeInputBindings.GetCharacterProfileStarted(NativeInputBindings.Action.Ok)) CompleteCharacterProfile(state,state.Confirm(index => ApplyCharacterProfile(0,index)));
            return true;
        }
        bool DrawCharacterProfile(float width)
        {
            if (!CharacterProfilePage || !characterProfiles[0].Open) return false;
            bool pointer = hud.SuppressPointerInput; hud.SuppressPointerInput = true;
            try
            {
                if (hud.MenuPage == NativeMenuPage.Characters) hud.CharacterMenu(width,vehicleRoster,selectableCharacters);
                else hud.VehicleMenu(width,vehicleRoster);
                DrawSelectionPreview(); hud.DrawCharacterProfile(characterProfiles[0]);
            }
            finally { hud.SuppressPointerInput = pointer; }
            return true;
        }
    }
}
