using System;
namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        public NativeMainPromptSource.State ExtrasConfirmPromptState { get; private set; }
        public NativeMainPromptSource.State ExtrasCancelPromptState { get; private set; }
        public NativeMainPromptSource.State OptionsConfirmPromptState { get; private set; }
        public NativeMainPromptSource.State OptionsCancelPromptState { get; private set; }
        bool SourceSchemeOriginalPrompts(string prefix)
        {
            if (prefix == "menu2/Canvas/ExtrasMenu/")
            {
                ExtrasConfirmPromptState = WriteSourceSchemePrompt(prefix + "Prompts/AText", 1715, 1693, "PROMPT_SELECT", "Select", NativeInputBindings.Action.Ok, "aa3b5e27-73fb-4641-ab88-df45fa6b86e1");
                ExtrasCancelPromptState = WriteSourceSchemePrompt(prefix + "Prompts/BText", 1691, 1714, "PROMPT_GOBACK", "Go Back", NativeInputBindings.Action.Cancel, "a1ae49c6-7f31-41dd-b840-52f881e36bf6");
                return true;
            }
            if (prefix == "menu2/Canvas/OptionsMenu/")
            {
                OptionsConfirmPromptState = WriteSourceSchemePrompt(prefix + "Prompts/AText", 1701, 1689, "PROMPT_SELECT", "Select", NativeInputBindings.Action.Ok, "aa3b5e27-73fb-4641-ab88-df45fa6b86e1");
                OptionsCancelPromptState = WriteSourceSchemePrompt(prefix + "Prompts/BText", 1697, 1711, "PROMPT_GOBACK", "Go Back", NativeInputBindings.Action.Cancel, "a1ae49c6-7f31-41dd-b840-52f881e36bf6");
                return true;
            }
            return false;
        }
        NativeMainPromptSource.State WriteSourceSchemePrompt(string path, long textId, long iconId, string phrase, string fallback, NativeInputBindings.Action action, string actionId)
        {
            var textSource = authoredUi.Definition(path); var iconSource = authoredUi.Definition(path + "/icon");
            string iconGuid = action == NativeInputBindings.Action.Ok ? "b28f9ef90db8c3a44bc4258f9c537967" : "db68a2743b01d71418042c56775c0270";
            if (!authoredUi.PrefabAuthored && (textSource.id != textId || textSource.scene != "menu2" || textSource.scene + "/" + textSource.path != path
                || !textSource.hasText || textSource.text != phrase || iconSource.id != iconId || iconSource.scene != "menu2"
                || iconSource.scene + "/" + iconSource.path != path + "/icon" || !iconSource.image || !iconSource.imageEnabled
                || iconSource.spriteId != 21300000 || iconSource.spriteGuid != iconGuid))
                throw new InvalidOperationException("Original authored prompt identity mismatch: " + path);
            var state = NativeMainPromptSource.Read(action); state.sourcePath = path;
            if (state.closed && state.actionId != actionId) { state.closed = false; state.unknownReason = "source-authored-prompt-action-identity-mismatch"; }
            string caption = NativeLocalization.Get(phrase, fallback);
            if (state.closed)
            {
                authoredUi.SetSprite(path + "/icon", state.sprite);
                authoredUi.SetActive(path + "/icon", state.spriteFound);
                if (!state.spriteFound) caption = "<color=red>" + state.displayBinding + "</color> " + caption;
            }
            else
            {
                authoredUi.SetSprite(path + "/icon", iconSource.sprite);
                authoredUi.SetActive(path + "/icon", iconSource.active);
            }
            authoredUi.SetText(path, caption);
            return state;
        }
    }
}
