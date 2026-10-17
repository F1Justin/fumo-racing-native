using System.Collections.Generic;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeAuthoredUi
    {
        readonly List<NativeInputPrompt.Binding> inputPrompts = new List<NativeInputPrompt.Binding>();
        void BindInputPrompts()
        {
            var catalog = NativeContent.Load<NativePromptSpriteCatalog>("NativeMenu/Prompts/Catalog");
            foreach (var pair in definitions)
            {
                string phrase = string.IsNullOrEmpty(pair.Value.phrase) ? pair.Value.text : pair.Value.phrase;
                string action = NativeInputPrompt.ActionFor(pair.Key, phrase);
                if (action == null || !transforms.TryGetValue(pair.Key, out var target)) continue;
                var authored = target.GetComponent<NativeInputPrompt>();
                var binding = authored ? authored.binding : NativeInputPrompt.Bind(target, action, catalog);
                if (binding != null) inputPrompts.Add(binding);
            }
        }
        public void RefreshInputPrompts()
        {
            foreach (var prompt in inputPrompts) prompt.Present();
        }
    }
}
