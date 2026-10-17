using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;

namespace Fumo.NativePlayable
{
    // TMP uses BaseInput for IME, while the authored menus use Input System for actions.
    [DefaultExecutionOrder(-1000)]
    public sealed class NativeTextInputBridge : BaseInput
    {
        static NativeTextInputBridge instance;
        Keyboard keyboard;
        string composition = "", previousLegacy = "";
        int imeFrame = -1;
        bool waitForRelease;
        IMECompositionMode mode;
        Vector2 cursor;
        TMP_InputField resumeField;
        int resumePosition, resumeSelection;
        static readonly bool traceInput = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--profile-input-trace") >= 0;
        public static bool BlocksMenuInput => instance && instance.Guarded;
        public static bool Restoring(TMP_InputField field) => instance && instance.resumeField == field;
        bool Guarded
        {
            get
            {
                ObserveLegacy();
                if (composition.Length > 0 || Input.compositionString.Length > 0 || imeFrame == Time.frameCount) return true;
                if (!waitForRelease) return false;
                if (keyboard != null && (keyboard.spaceKey.isPressed || keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed
                    || keyboard.escapeKey.isPressed || keyboard.tabKey.isPressed)) return true;
                waitForRelease = false; return false;
            }
        }
        public static void Attach(InputSystemUIInputModule module)
        {
            var bridge = module.GetComponent<NativeTextInputBridge>();
            if (!bridge) bridge = module.gameObject.AddComponent<NativeTextInputBridge>();
            module.inputOverride = bridge;
        }
        protected override void OnEnable()
        { base.OnEnable(); instance = this; ConnectKeyboard(); Canvas.willRenderCanvases += AnchorImeWindow; }
        protected override void OnDisable()
        {
            Canvas.willRenderCanvases -= AnchorImeWindow;
            DisconnectKeyboard(); if (instance == this) instance = null; base.OnDisable();
        }
        void ConnectKeyboard()
        {
            if (keyboard == Keyboard.current) return;
            DisconnectKeyboard(); keyboard = Keyboard.current;
            composition = "";
            if (keyboard == null) return;
            keyboard.onIMECompositionChange += CompositionChanged;
            keyboard.onTextInput += TextCommitted;
            // IME commands target a device; a replacement keyboard needs the active field's state.
            keyboard.SetIMEEnabled(mode == IMECompositionMode.On);
            keyboard.SetIMECursorPosition(cursor);
        }
        void DisconnectKeyboard()
        {
            if (keyboard == null) return;
            keyboard.onIMECompositionChange -= CompositionChanged;
            keyboard.onTextInput -= TextCommitted;
            keyboard = null;
        }
        void CompositionChanged(IMECompositionString value)
        {
            string next = value.ToString();
            if (traceInput) Debug.Log("PROFILE_IME frame=" + Time.frameCount + " before=" + composition.Length + " after=" + next.Length);
            // Native keyboards can report an empty composition for ordinary text keys.
            // Only an active composition ending should guard its submit/cancel shortcut.
            if (next.Length == 0 && composition.Length == 0) return;
            composition = next; imeFrame = Time.frameCount; waitForRelease = true;
        }
        void TextCommitted(char character)
        {
            if (composition.Length == 0 && imeFrame != Time.frameCount) return;
            composition = ""; imeFrame = Time.frameCount; waitForRelease = true;
        }
        void ObserveLegacy()
        {
            string value = Input.compositionString;
            if (value != previousLegacy)
            { previousLegacy = value; imeFrame = Time.frameCount; waitForRelease = true; }
        }
        void Update()
        {
            ConnectKeyboard(); ObserveLegacy();
            if (traceInput && keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
                Debug.Log("PROFILE_KEY frame=" + Time.frameCount + " space=" + keyboard.spaceKey.wasPressedThisFrame
                    + " ctrl=" + keyboard.ctrlKey.isPressed + " shift=" + keyboard.shiftKey.isPressed + " alt=" + keyboard.altKey.isPressed
                    + " meta=" + (keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed) + " guarded=" + Guarded
                    + " legacyComposition=" + Input.compositionString.Length);
        }
        void AnchorImeWindow()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            var field = selected ? selected.GetComponent<TMP_InputField>() : null;
            if (!field || !field.isFocused || !field.textComponent || !field.textComponent.canvas) return;
            var text = field.textComponent; var info = text.textInfo;
            Vector3 local = new Vector3(text.rectTransform.rect.xMin, text.rectTransform.rect.yMin, 0);
            if (info.characterCount > 0)
            {
                int index = 0;
                while (index + 1 < info.characterCount && info.characterInfo[index].index < field.stringPosition) index++;
                var character = info.characterInfo[index];
                local = new Vector3(character.index < field.stringPosition ? character.xAdvance : character.origin, character.descender, 0);
            }
            var canvas = text.canvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(local));
            // TMP's text offset includes horizontal scrolling; clamp the native window anchor to its visible viewport.
            if (field.textViewport)
            {
                var corners = new Vector3[4]; field.textViewport.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                point.x = Mathf.Clamp(point.x, min.x, max.x); point.y = Mathf.Clamp(point.y, min.y, max.y);
            }
            compositionCursorPos = new Vector2(point.x, Screen.height - point.y);
        }
        void LateUpdate()
        {
            if (!resumeField) return;
            if (!resumeField.gameObject.activeInHierarchy) { resumeField = null; return; }
            if (!resumeField.isFocused) { resumeField.Select(); resumeField.ActivateInputField(); return; }
            resumeField.selectionStringAnchorPosition = Mathf.Clamp(resumePosition, 0, resumeField.text.Length);
            resumeField.selectionStringFocusPosition = Mathf.Clamp(resumeSelection, 0, resumeField.text.Length);
            resumeField = null;
        }
        public static void KeepEditingAfterIme(TMP_InputField field)
        {
            if (!instance || !field || !BlocksMenuInput || !field.gameObject.activeInHierarchy) return;
            // A genuine pointer/tab focus change owns deselection; only recover TMP's premature IME termination.
            var system = EventSystem.current;
            if (!system || system.alreadySelecting || system.currentSelectedGameObject != field.gameObject) return;
            instance.resumeField = field;
            instance.resumePosition = field.selectionStringAnchorPosition;
            instance.resumeSelection = field.selectionStringFocusPosition;
            field.ActivateInputField();
        }
        public override string compositionString => composition.Length > 0 ? composition : Input.compositionString;
        public override Vector2 compositionCursorPos
        {
            get => cursor;
            set { cursor = value; Input.compositionCursorPos = value; keyboard?.SetIMECursorPosition(value); }
        }
        public override IMECompositionMode imeCompositionMode
        {
            get => mode;
            set
            {
                mode = value; Input.imeCompositionMode = value;
                keyboard?.SetIMEEnabled(value == IMECompositionMode.On);
                if (value != IMECompositionMode.On) composition = "";
            }
        }
    }
}
