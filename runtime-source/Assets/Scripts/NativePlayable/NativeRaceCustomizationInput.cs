using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        internal const float CustomInputRepeatDelay = .5f, CustomInputRepeatRate = .1f;
        void StepCustomizationAxis(int axis, int vertical, int horizontal, float now)
        {
            if (axis != 0 && (axis != customHeldAxis || now >= customRepeatAt))
            {
                if (customDropdownOpen)
                {
                    int count = customDropdownRow == 3 ? 3 : 2;
                    int next = Mathf.Clamp(customDropdownOption + (vertical != 0 ? vertical : horizontal), 0, count - 1);
                    if (next != customDropdownOption) { customDropdownOption = next; hud.PlayCustomizationSound(0); }
                }
                else if (vertical != 0) hud.NavigateMenu(NativeMenuPage.Custom, vertical);
                else AdjustCustom(hud.SelectedMenuIndex, horizontal);
                customRepeatAt = now + (axis != customHeldAxis ? CustomInputRepeatDelay : CustomInputRepeatRate);
            }
            customHeldAxis = axis;
        }
    }
}
