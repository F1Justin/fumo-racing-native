using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRace
    {
        internal IEnumerator CheckCharacterArrowInput(Keyboard keyboard, Action<bool, string> require)
        {
#if ENABLE_INPUT_SYSTEM
            bool pointerSuppressed = hud.SuppressPointerInput;
            hud.SuppressPointerInput = false;
            try
            {
                hud.SetMenuPage(NativeMenuPage.Characters);
                hud.SelectMenuIndex(Array.IndexOf(selectableCharacters, vehicleRoster.CharacterIndexOf("reimu")));
                while (hud.MenuTransitionActive) yield return null;
                yield return new WaitForEndOfFrame();
                require(hud.OriginalCharacterSelectionBound("reimu", out string initialDetail), initialDetail);
                var keys = new[] { Key.RightArrow, Key.LeftArrow, Key.DownArrow, Key.UpArrow };
                var characters = new[] { "marisa", "reimu", "remilia", "reimu" };
                for (int i = 0; i < keys.Length; i++)
                {
                    yield return MenuParityKey(keyboard, keys[i]);
                    string selected = vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]);
                    require(selected == characters[i], "Keyboard " + keys[i] + " selects original neighbor " + characters[i] + "; actual=" + selected);
                    require(hud.OriginalCharacterSelectionBound(selected, out string detail), detail);
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
                yield return null;
                yield return null;
                require(vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == "marisa", "Held arrow starts with one original neighbor");
                yield return new WaitForSecondsRealtime(.35f);
                require(vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == "marisa", "Source Custom Navigate started-only retains selection during initial hold");
                yield return new WaitForSecondsRealtime(1.7f);
                require(vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]) == "marisa", "Source Custom Navigate performs one step throughout two-second hold");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return new WaitForEndOfFrame();
                string heldSelected = vehicleRoster.CharacterIdAt(selectableCharacters[hud.SelectedMenuIndex]);
                require(hud.OriginalCharacterSelectionBound(heldSelected, out string heldDetail), heldDetail);
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                hud.SuppressPointerInput = pointerSuppressed;
            }
#else
            yield break;
#endif
        }
    }
}
