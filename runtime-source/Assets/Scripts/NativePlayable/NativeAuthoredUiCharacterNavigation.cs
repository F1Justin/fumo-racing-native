using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        const string CharacterGridPrefix = "selection/Canvas/CharSelection/CharacterGrid/";
        int characterNavigationPageGeneration, characterNavigationBuiltGeneration = -1;
        NativeAuthoredUiNode[] characterSlots;
        readonly Dictionary<string, Button> characterButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, string[]> characterDirectionCache = new Dictionary<string, string[]>();

        public void PrepareCharacterNavigation(IEnumerable<string> eligibleCharacters)
        {
            if (characterSlots == null)
                characterSlots = Nodes(CharacterGridPrefix).Where(n => !string.IsNullOrEmpty(n.characterId))
                    .OrderBy(n => n.selectionOrder).ToArray();
            bool entering = characterNavigationBuiltGeneration != characterNavigationPageGeneration;
            var eligible = new HashSet<string>(eligibleCharacters);
            bool dirty = entering;
            foreach (var slot in characterSlots)
            {
                string key = "selection/" + slot.path;
                var rect = transforms[key];
                rect.SetAsLastSibling();
                // Original Unlock removes the random exclusion; only a new selection load restores a hidden slot.
                bool active = eligible.Contains(slot.characterId) && (entering ? definitions[key].active : rect.gameObject.activeSelf);
                rect.gameObject.SetActive(active);
                if (!characterButtons.ContainsKey(slot.characterId))
                {
                    var button = rect.gameObject.AddComponent<Button>();
                    button.enabled = false;
                    button.transition = Selectable.Transition.None;
                    button.interactable = true;
                    button.navigation = new Navigation { mode = (Navigation.Mode)(-1) };
                    characterButtons.Add(slot.characterId, button);
                }
            }
            if (!dirty) return;
            Canvas.ForceUpdateCanvases();
            foreach (var slot in characterSlots)
                characterDirectionCache[slot.characterId] = slot.navigation.Select(path => string.IsNullOrEmpty(path)
                    ? null : characterSlots.Single(target => target.path == path).characterId).ToArray();
            // Source RefreshNavigation temporarily registers all buttons in _allSlots order, then disables all of them.
            try
            {
                foreach (var slot in characterSlots) characterButtons[slot.characterId].enabled = true;
                foreach (var slot in characterSlots)
                {
                    var button = characterButtons[slot.characterId];
                    if (!button.gameObject.activeSelf) continue;
                    var targets = new[] { button.FindSelectableOnUp(), button.FindSelectableOnRight(),
                        button.FindSelectableOnDown(), button.FindSelectableOnLeft() };
                    characterDirectionCache[slot.characterId] = targets.Select(target => target
                        ? characterButtons.FirstOrDefault(pair => pair.Value == target).Key : null).ToArray();
                }
            }
            finally
            {
                foreach (var slot in characterSlots) characterButtons[slot.characterId].enabled = false;
            }
            characterNavigationBuiltGeneration = characterNavigationPageGeneration;
        }

        public string CharacterNeighbor(string characterId, int direction)
        {
            return direction >= 0 && direction < 4 && characterDirectionCache.TryGetValue(characterId, out var targets)
                ? targets[direction] : null;
        }

        public bool CharacterSlotActive(string characterId) => characterButtons.TryGetValue(characterId, out var button) && button.gameObject.activeSelf;

        [Serializable] public sealed class CharacterNavigationSample
        {
            public string characterId, path;
            public bool activeSelf, activeInHierarchy, buttonEnabled, interactable;
            public int mode;
            public bool wrapAround;
            public Vector3 position;
            public Rect rect;
            public Quaternion rotation;
            public Matrix4x4 localToWorld;
            public string[] cachedTargets;
        }

        public CharacterNavigationSample[] CharacterNavigationSnapshot()
        {
            if (characterSlots == null) return new CharacterNavigationSample[0];
            return characterSlots.Select(slot =>
            {
                var button = characterButtons[slot.characterId];
                var rect = (RectTransform)button.transform;
                return new CharacterNavigationSample { characterId = slot.characterId, path = slot.path,
                    activeSelf = button.gameObject.activeSelf, activeInHierarchy = button.gameObject.activeInHierarchy,
                    buttonEnabled = button.enabled, interactable = button.interactable,
                    mode = (int)button.navigation.mode, wrapAround = button.navigation.wrapAround,
                    position = rect.position, rect = rect.rect, rotation = rect.rotation, localToWorld = rect.localToWorldMatrix,
                    cachedTargets = characterDirectionCache.TryGetValue(slot.characterId, out var targets) ? (string[])targets.Clone() : null };
            }).ToArray();
        }
    }
}
