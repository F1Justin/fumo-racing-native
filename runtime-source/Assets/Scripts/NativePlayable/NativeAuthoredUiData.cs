using System;
using UnityEngine;

namespace Fumo.NativePlayable
{
    [Serializable] public sealed class NativeAuthoredUiNode
    {
        public NativeAuthoredUiNode Copy() => (NativeAuthoredUiNode)MemberwiseClone();
        public bool animator, animatorEnabled, animatorApplyRootMotion, animatorKeepStateOnDisable;
        public bool animatorPreserveStateAcrossFrameHide;
        public string animatorFrameHideReason;
        public int animatorUpdateMode, animatorCullingMode;
        public RuntimeAnimatorController animatorController;
        public bool enableVertexGradient;
        public bool enableKerning = true;
        public Color gradientTopLeft = Color.white, gradientTopRight = Color.white, gradientBottomLeft = Color.white, gradientBottomRight = Color.white;
        public string materialGuid, textMaterialGuid;
        public Material imageOverride, textMaterialOverride;
        public string fontGuid, scene, path, spriteGuid, textureGuid, text, phrase, characterId;
        public string[] navigation;
        public int selectionOrder;
        public Vector4 rectMaskPadding;
        public Vector2Int rectMaskSoftness;
        public bool grid, gridEnabled, layoutElement, ignoreLayout;
        public bool horizontalLayout, horizontalLayoutEnabled, childForceExpandWidth, childForceExpandHeight;
        public bool childControlWidth, childControlHeight, childScaleWidth, childScaleHeight, reverseArrangement;
        public float layoutSpacing;
        public Vector2 cellSize, spacing;
        public int startCorner, startAxis, childAlignment, constraint, constraintCount;
        public NativeAuthoredPadding padding;
        public long id, parent, spriteId;
        public bool maskable = true;
        public bool imageEnabled = true, rawImageEnabled = true, textEnabled = true, maskEnabled = true, rectMaskEnabled = true;
        public bool active, image, rawImage, mask, showMask, rectMask, preserveAspect, autoSize, wrap, hasText;
        public int fontIndex, fontStyle, order, imageType, alignment;
        public int imageFillMethod, imageFillOrigin;
        public float imageFillAmount = 1, imagePixelsPerUnitMultiplier = 1;
        public bool imageFillClockwise = true, imageUseSpriteMesh;
        public bool canvas, canvasEnabled, canvasOverrideSorting, canvasPixelPerfect, canvasOverridePixelPerfect;
        public int canvasRenderMode, canvasSortingOrder, canvasSortingLayerId, canvasShaderChannels;
        public bool hasCanvasScaler;
        public int uiScaleMode, screenMatchMode;
        public float scaleFactor = 1, referencePixelsPerUnit = 100;
        public Vector2 referenceResolution;
        public float match;
        public Vector2 position, size, anchorMin, anchorMax, pivot;
        public Quaternion rotation;
        public Vector3 scale;
        public Color color, fontColor;
        public Vector4 margin;
        public float fontSize, minSize, maxSize;
        public Sprite sprite;
        public Texture texture;
    }
    [Serializable] public sealed class NativeAuthoredPadding { public int m_Left, m_Right, m_Top, m_Bottom; }
    [Serializable] public sealed class NativeAuthoredUiDocument { public NativeAuthoredUiNode[] nodes; public NativeUiFontData[] fonts; }
    public sealed class NativeAuthoredUiData : ScriptableObject { public NativeAuthoredUiNode[] nodes; public NativeUiFontData[] fonts; }

}
