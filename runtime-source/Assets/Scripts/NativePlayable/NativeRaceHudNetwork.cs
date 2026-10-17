using UnityEngine;
using TMPro;

namespace Fumo.NativePlayable
{
    public sealed partial class NativeRaceHud
    {
        readonly Rect[] networkMenuRects = new Rect[5];
        readonly float[] networkMenuAmounts = new float[5];
        public MenuFocusSnapshot NetworkMenuFocus { get; private set; }
        public float BeginNetworkFrame(string page)
        {
            return BeginOnlinePage(page);
        }
        public void EndNetworkFrame() => EndOnlinePage();
        public void HideNetworkWheel()=>authoredUi?.Hide();
        public void NetworkOpacity(float value)
        { if (OnlinePageView && OnlinePageView.TryGetComponent<CanvasGroup>(out var group)) group.alpha = value; }
        public void NetworkVisible(bool visible) { if (!visible) HideOnlinePages(); }
        public void NetworkLabel(string key, string text, bool localize = true)
        { OnlineElement(key)?.Text(text, localize); }
        public void NetworkFill(string key) => OnlineElement(key);
        public void NetworkPanel(string key) => OnlineElement(key);
        public void NetworkPicture(string key) => OnlineElement(key);
        public void NetworkSprite(string key, Sprite sprite)
        { var element = OnlineElement(key); if (element && element.image && element.bindImage) element.image.sprite = sprite; }
        public bool NetworkButton(string key, string label, bool focused, bool enabled = true, string actionKey = null)
        {
            var element = OnlineElement(key);
            if (!element) return false;
            element.Hovered = () => { if (!SuppressPointerInput) OnlineHover?.Invoke(actionKey ?? key); };
            return element.PresentButton(label, focused, enabled) && !SuppressPointerInput;
        }
        const string OnlineWheelRoot = "menu2/Canvas/OptionsMenu/";
        static readonly string[] OnlineWheelNames = { "VideoButton", "AudioButton", "InputButton", "LanguageButton", "OnlineExtraButton" };
        string OnlineWheelKey(int index) => OnlineWheelRoot + "LowerOptionsRoot/LowerOptionsPivot/Mask/Pivot/" + OnlineWheelNames[index];
        string[] networkWheelCaptions={"Host Game","Join Game"};
        public void ConfigureNetworkWheel(string[] captions)=>networkWheelCaptions=captions;
        void SetOnlineWheelCaptions()
        {
            for (int i = 0; i < (usingOnlineAuthoring ? OnlineWheelNames.Length : 4); i++)
            {
                authoredUi.SetActive(OnlineWheelKey(i),i<networkWheelCaptions.Length);
                if(i>=networkWheelCaptions.Length)continue;
                string key = OnlineWheelKey(i) + "/Text";
                authoredUi.BindLocalizedText(key, networkWheelCaptions[i], networkWheelCaptions[i]);
                authoredUi.SetTextMaximumSize(key, NativeLocalization.IsChinese ? 72 : 48);
            }
        }
        void RestoreOptionsWheelCaptions()
        {
            for (int i = 0; i < 4; i++)
            {
                string key = OnlineWheelKey(i) + "/Text";
                var definition = authoredUi.Definition(key);
                authoredUi.SetActive(OnlineWheelKey(i),true);
                authoredUi.BindLocalizedText(key, i == 2 ? "Profile" : definition.phrase, i == 2 ? "Profile" : definition.text);
                authoredUi.ResetTextLayout(key);
            }
        }
        public Rect NetworkWheelButton(int index, bool focused, bool enabled, out bool pressed)
        {
            if (index == 0)
            {
                authoredUi.Show("menu2", "OptionsMenu", "AssistButton");
                SetOnlineWheelCaptions();
            }
            float amount = networkMenuAmounts[index] = ButtonAmount(index, focused);
            string key = OnlineWheelKey(index);
            var definition = authoredUi.Definition(key);
            authoredUi.AssignOriginalSprite(key, NativeMenuSpriteData.At(focused));
            authoredUi.Size(key, new Vector2(definition.size.x + (authoredUi.Motion ? authoredUi.Motion.focusWidth : 40) * amount, definition.size.y));
            authoredUi.BindLocalizedText(key + "/Text", networkWheelCaptions[index], networkWheelCaptions[index]);
            var rect = networkMenuRects[index] = authoredUi.RectAt(key);
            pressed = enabled && !SuppressPointerInput && PointerButton(rect);
            return rect;
        }
        public void NetworkWheelForeground(int selected)
        {
            const string pivot = OnlineWheelRoot + "LowerOptionsRoot/LowerOptionsPivot/SelectorBG/SelectorPivot";
            authoredUi.Offset(pivot, Vector2.zero);
            float y = SelectorPosition(networkMenuRects[selected].center.y + (authoredUi.Motion ? authoredUi.Motion.selectorOffset : 16f / 1.8f));
            authoredUi.Offset(pivot, new Vector2(0, y - authoredUi.RectAt(pivot).center.y));
            float angle = RenderSourceWheelAngle(OptionsSourceWheel, sourceOptionsWheelAngle);
            if (Event.current.type == EventType.Repaint)
                NetworkMenuFocus = new MenuFocusSnapshot { page = "Online", index = selected,
                    selectorVisible = true, selectorY = authoredUi.RectAt(pivot).center.y, wheelAngle = angle,
                    buttonAmounts = (float[])networkMenuAmounts.Clone(), buttonRects = (Rect[])networkMenuRects.Clone() };
        }
        public bool NetworkWheelPrompts(out Rect back)
        {
            LocalizeOriginalPrompts(OnlineWheelRoot);
            back = authoredUi.RectAt(OnlineWheelRoot + "Prompts/BText");
            return !SuppressPointerInput && PointerButton(back);
        }
        public bool NetworkPrompt(string binding, Sprite key, string caption)
        {
            var element = OnlineElement(binding);
            if (!element) return false;
            var state = NativeMainPromptSource.Read(key == keyCancel ? NativeInputBindings.Action.Cancel : NativeInputBindings.Action.Ok);
            key = state.sprite;
            if (!state.spriteFound && !string.IsNullOrEmpty(state.displayBinding)) caption = "[" + state.displayBinding + "] " + caption;
            if (element.image && element.bindImage) element.image.sprite = key;
            element.Text(caption, true);
            return !SuppressPointerInput && element.ConsumeClick();
        }
        public void NetworkSound(bool confirm) => PlaySound(confirm ? menuConfirmSound : menuMoveSound);
        public void NetworkBackSound() => PlaySound(menuCancelSound);
        public void NetworkSelectionContext()
        {
            authoredUi.SetActive(CharacterPanel + "Prompts/XText (1)", true);
        }
    }
}
