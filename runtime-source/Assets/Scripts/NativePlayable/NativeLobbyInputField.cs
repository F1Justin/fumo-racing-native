using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fumo.NativePlayable
{
    // Text geometry stays authored on TMP; only its generated caret follows it.
    public sealed class NativeLobbyInputField : TMP_InputField
    {
        TMP_SelectionCaret selectionCaret;
        readonly List<Vector3> vertices = new List<Vector3>(4);

        protected override void OnEnable()
        {
            base.OnEnable();
            Canvas.willRenderCanvases += SynchronizeCaretTransform;
        }

        protected override void OnDisable()
        {
            Canvas.willRenderCanvases -= SynchronizeCaretTransform;
            base.OnDisable();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            SynchronizeCaretTransform();
            // Desktop TMP skips its transform synchronization in LateUpdate.
            // Rebuild after text/layout changes even when the user hasn't typed.
            if (isFocused) CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);
        }

        void SynchronizeCaretTransform()
        {
            if (!Application.isPlaying || !textComponent || !textComponent.transform.parent) return;
            var textRect = textComponent.rectTransform;
            if (!selectionCaret || selectionCaret.transform.parent != textRect.parent)
            {
                selectionCaret = null;
                foreach (Transform child in textRect.parent)
                    if (child.TryGetComponent<TMP_SelectionCaret>(out var caret)) { selectionCaret = caret; break; }
            }
            if (!selectionCaret) return;

            var caretRect = selectionCaret.rectTransform;
            caretRect.localRotation = textRect.localRotation;
            caretRect.localScale = textRect.localScale;
            caretRect.anchorMin = textRect.anchorMin;
            caretRect.anchorMax = textRect.anchorMax;
            caretRect.pivot = textRect.pivot;
            caretRect.sizeDelta = textRect.sizeDelta;
            caretRect.anchoredPosition3D = textRect.anchoredPosition3D;
        }

        public override void Rebuild(CanvasUpdate update)
        {
            base.Rebuild(update);
            if (update != CanvasUpdate.LatePreRender || !textComponent || !selectionCaret || mesh.vertexCount != 4) return;

            // TMP's Geometry alignment draws the caret around local Y=0,
            // while glyphs include the RectTransform pivot and TMP margins.
            // Selection highlights already use glyph coordinates and need no offset.
            var info = textComponent.textInfo;
            bool geometryCaret = textComponent.verticalAlignment == VerticalAlignmentOptions.Geometry
                && selectionAnchorPosition == selectionFocusPosition && info.characterCount > 0;
            if (!geometryCaret) return;
            int index = Mathf.Clamp(caretPosition, 0, info.characterCount - 1);
            int line = info.characterInfo[index].lineNumber;
            if (index != info.lineInfo[line].firstCharacterIndex) index--;
            var character = info.characterInfo[index];
            mesh.GetVertices(vertices);
            float offset = (character.ascender + character.descender) * .5f;
            for (int i = 0; i < vertices.Count; i++) vertices[i] += new Vector3(0, offset);
            mesh.SetVertices(vertices);
            mesh.RecalculateBounds();
            selectionCaret.canvasRenderer.SetMesh(mesh);

            if (!isFocused) return;
            var screen = RectTransformUtility.WorldToScreenPoint(
                textComponent.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : textComponent.canvas.worldCamera,
                selectionCaret.rectTransform.TransformPoint(vertices[0]));
            screen.y = Screen.height - screen.y;
            var input = EventSystem.current && EventSystem.current.currentInputModule
                ? EventSystem.current.currentInputModule.input : null;
            if (input) input.compositionCursorPos = screen;
            else Input.compositionCursorPos = screen;
        }
    }
}
