using System;
using TMPro;
using UnityEngine;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public NativeMainPromptSource.State MainConfirmPromptState { get; private set; }
        public NativeMainPromptSource.State MainCancelPromptState { get; private set; }
        void MainSourcePrompt(Rect rect, Sprite key, string caption, string sourcePath)
        {
            if (sourcePath != "menu2/Canvas/MainMenu/Prompts/AText" && sourcePath != "menu2/Canvas/MainMenu/Prompts/BText")
                throw new ArgumentException("Original Main prompt source path required", nameof(sourcePath));
            var source = authoredUi.Definition(sourcePath);
            var textRect = new Rect(rect.x + rect.height - 4, rect.y, rect.width - rect.height + 4, rect.height);
            var state = NativeMainPromptSource.Read(sourcePath.EndsWith("/AText") ? NativeInputBindings.Action.Ok : NativeInputBindings.Action.Cancel);
            state.sourcePath = sourcePath;
            if (sourcePath.EndsWith("/AText")) MainConfirmPromptState = state; else MainCancelPromptState = state;
            var icon = state.closed ? state.sprite : authoredUi.Definition(sourcePath + "/icon").sprite;
            if (icon) SpritePicture(new Rect(rect.x, rect.y + 5, rect.height - 10, rect.height - 10), icon, sourcePath: sourcePath + "/icon");
            string textValue = NativeLocalization.TranslateText(caption);
            if (state.closed && !state.spriteFound) textValue = "<color=red>" + state.displayBinding + "</color> " + textValue;
            // Original menu coordinates use a 1080 reference height; this canvas uses 600.
            const float sourceScale = 1.8f;
            canvas.Label(textRect, textValue, source.fontSize / sourceScale, source.fontColor,
                TextAnchor.MiddleLeft, authoredUiData.fonts[source.fontIndex], (TextAlignmentOptions)((source.alignment & ~0xff) | 1),
                richText: true, autoSize: source.autoSize, minimum: source.minSize / sourceScale, maximum: source.maxSize / sourceScale,
                enableKerning: source.enableKerning, sourcePath: sourcePath);
            if (Event.current.type == EventType.Repaint && canvas.TryGetSourceGraphic(sourcePath, out var graphic) && graphic is TMP_Text text)
            {
                text.fontStyle = (FontStyles)source.fontStyle;
                text.enableWordWrapping = source.wrap;
                text.margin = source.margin / sourceScale;
                text.parseCtrlCharacters = true;
            }
        }
    }
}
