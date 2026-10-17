"""Generate a whole-texture Sprite quad without retaining imported Tight outlines.

The caller must separately prove the referenced Texture2D's original-package or
reviewed authored provenance. These functions generate geometry only; they never
copy texture pixels, imported vertex/index buffers, or physics contours.
"""
from __future__ import annotations

import math
import struct

from .sprite_geometry import full_rect_buffers


def _f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def validate_full_texture_sprite(tree: dict, width: int, height: int) -> None:
    """Reject atlases, cropped/rotated sprites and nontrivial vertex transforms."""
    render = tree["m_RD"]
    if tree.get("m_Bones") or tree.get("m_IsPolygon"):
        raise ValueError("Skinned or polygon sprites are not whole-texture sprites")
    if tree.get("m_SpriteAtlas", {}).get("m_PathID", 0):
        raise ValueError("Packed atlas sprites need a separate generator")
    if render.get("settingsRaw") not in (0, 64) or render.get("secondaryTextures"):
        raise ValueError("Packed, rotated or secondary-texture sprite is unsupported")
    if render.get("alphaTexture", {}).get("m_PathID", 0):
        raise ValueError("Separate alpha texture is unsupported")
    rect, pivot, ppu = tree["m_Rect"], tree["m_Pivot"], tree["m_PixelsToUnits"]
    expected_rect = {"x": 0, "y": 0, "width": width, "height": height}
    if width <= 0 or height <= 0 or any(float(rect[k]) != float(v) for k, v in expected_rect.items()):
        raise ValueError("Sprite must cover the entire referenced texture")
    # This check reads only scalar metadata. The generator itself validates the
    # rectangle/pivot/PPU and creates its own two streams and triangles.
    full_rect_buffers(rect, pivot, ppu)
    expected_uv = {"x": ppu, "y": _f32(width * pivot["x"]),
                   "z": ppu, "w": _f32(height * pivot["y"])}
    if any(not math.isfinite(float(render["uvTransform"][k])) or
           abs(float(render["uvTransform"][k]) - float(v)) > 0.0001
           for k, v in expected_uv.items()):
        raise ValueError("Sprite UV transform does not map the full source texture")


def full_rect_sprite_geometry(rect: dict, pivot: dict, pixels_per_unit: float) -> dict:
    """Return generated Unity 6 streams/submesh plus an empty physics shape.

    Texture pointers, uvTransform and other Sprite metadata are left to the
    caller. Only rect, pivot and pixels-per-unit affect these output bytes.
    """
    buffers = full_rect_buffers(rect, pivot, pixels_per_unit)
    positions = struct.unpack("<12f", buffers["vertices"][:48])
    left, top, right, bottom = positions[0], positions[1], positions[3], positions[7]
    channels = [{"stream": 0, "offset": 0, "format": 0, "dimension": 0}
                for _ in range(14)]
    channels[0] = {"stream": 0, "offset": 0, "format": 0, "dimension": 3}
    channels[4] = {"stream": 1, "offset": 0, "format": 0, "dimension": 2}
    bounds = {"m_Center": {"x": _f32((left + right) / 2),
                           "y": _f32((bottom + top) / 2), "z": 0.0},
              "m_Extent": {"x": _f32((right - left) / 2),
                           "y": _f32((top - bottom) / 2), "z": 0.0}}
    return {"vertexData": {"m_VertexCount": 4, "m_Channels": channels,
                            "m_DataSize": buffers["vertices"]},
            "indexBuffer": buffers["indices"],
            "subMeshes": [{"firstByte": 0, "indexCount": 6, "topology": 0,
                            "baseVertex": 0, "firstVertex": 0, "vertexCount": 4,
                            "localAABB": bounds}],
            "physicsShape": []}
