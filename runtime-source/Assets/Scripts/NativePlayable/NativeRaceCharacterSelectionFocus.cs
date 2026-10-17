using System;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public event Action<int> OriginalCharacterFocusSelected;
        public void NotifyOriginalCharacterFocusSelected() => OriginalCharacterFocusSelected?.Invoke(SelectedMenuIndex);
    }
    public sealed partial class NativeRace
    {
        bool originalCharacterSelectionLoaded;
        void OnOriginalCharacterFocusSelected(int choice)
        {
            if (choice < 0 || choice >= selectableCharacters.Length) return;
            selectedCharacterIndex = selectableCharacters[choice];
            selectedSkinIndex = 0;
            hud.VehicleCharacterSelection = selectedCharacterIndex;
            var first=localSelection?.ReadBox(0);if(localSelectionActive&&first!=null)localSelection.FocusCharacter(first.InputUserId,selectedCharacterIndex);
        }
        void EnterOriginalCharacterSelection()
        {
            if (BeginLocalSelection()) return;
            if (originalCharacterSelectionLoaded) return;
            originalCharacterSelectionLoaded = true;
            hud.NotifyOriginalCharacterFocusSelected();
        }
    }
}
