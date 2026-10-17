"""Generate Unity Sprite FullRect quad buffers from resolved scalar metadata.

This module contains no outlines or artwork. Tight meshes and packed/rotated
sprites are intentionally outside this generator's accepted input contract.
"""
from __future__ import annotations

import math
import struct
from copy import deepcopy


def _float32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def full_rect_buffers(rect: dict, pivot: dict, pixels_per_unit: float, *,
                      arithmetic: str = "multiply-first") -> dict[str, bytes]:
    width, height, px, py, ppu = (float(rect["width"]), float(rect["height"]),
                                float(pivot["x"]), float(pivot["y"]), float(pixels_per_unit))
    if not all(math.isfinite(value) for value in (width, height, px, py, ppu)):
        raise ValueError("Non-finite sprite geometry")
    if width <= 0 or height <= 0 or ppu <= 0 or not (0 <= px <= 1 and 0 <= py <= 1):
        raise ValueError("Invalid FullRect sprite geometry")
    def coordinate(size, origin, right):
        factor = _float32(1 - origin) if right else -origin
        if arithmetic == "multiply-first":
            return _float32(_float32(size * factor) / ppu)
        if arithmetic == "divide-first":
            return _float32(_float32(size / ppu) * factor)
        if arithmetic == "single-round":
            return _float32(size * factor / ppu)
        raise ValueError("Unknown sprite arithmetic")
    left, right = coordinate(width, px, False), coordinate(width, px, True)
    bottom, top = coordinate(height, py, False), coordinate(height, py, True)
    positions = (left, top, 0, right, top, 0, left, bottom, 0, right, bottom, 0)
    # Native Sprite data has separate streams: four float3 positions, then four
    # zero float2 UVs. Unity derives the final atlas UVs from uvTransform.
    return {"vertices": struct.pack("<12f", *positions) + bytes(32),
            "indices": struct.pack("<6H", 0, 1, 2, 2, 1, 3)}


def prove_full_rect(tree: dict) -> dict | None:
    """Return scalar generator parameters only after exact reference comparison."""
    render = tree["m_RD"]
    vertex = render["m_VertexData"]
    if tree.get("m_IsPolygon") or tree.get("m_Bones") or render["settingsRaw"] != 0:
        return None
    if vertex["m_VertexCount"] != 4 or len(render["m_SubMeshes"]) != 1:
        return None
    channels = vertex["m_Channels"]
    for index, channel in enumerate(channels):
        if index == 0:
            expected = {"stream": 0, "offset": 0, "format": 0, "dimension": 3}
        elif index == 4:
            expected = {"stream": 1, "offset": 0, "format": 0, "dimension": 2}
        else:
            if channel["dimension"] != 0:
                return None
            continue
        if channel != expected:
            return None
    for arithmetic in ("multiply-first", "divide-first", "single-round"):
        produced = full_rect_buffers(tree["m_Rect"], tree["m_Pivot"], tree["m_PixelsToUnits"], arithmetic=arithmetic)
        if produced["vertices"] == bytes(vertex["m_DataSize"]) and produced["indices"] == bytes(render["m_IndexBuffer"]):
            return {"rect": tree["m_Rect"], "pivot": tree["m_Pivot"],
                    "pixelsPerUnit": tree["m_PixelsToUnits"], "arithmetic": arithmetic}
    return None


def original_sprite_geometry(source, target_uv_transform: dict) -> dict:
    """Re-express the original tight outline in a target Sprite coordinate frame.

    Texture pixel = source uvTransform * old position; the target uvTransform
    then determines the corresponding new position. This retains the original
    contour and triangle topology rather than copying a newly triangulated
    reference asset. Texture/object pointers remain the caller's responsibility.
    """
    if source.type.name != "Sprite":
        raise ValueError("Expected original Sprite")
    body = source.read_typetree()
    render = body["m_RD"]
    if body.get("m_Bones") or body.get("m_IsPolygon") or render["settingsRaw"] not in (0, 64):
        raise ValueError("Skinned, packed, rotated or polygon Sprite needs a dedicated conversion")
    vertex = deepcopy(render["m_VertexData"])
    channels = vertex["m_Channels"]
    for index, channel in enumerate(channels):
        if index == 0:
            expected = {"stream": 0, "offset": 0, "format": 0, "dimension": 3}
        elif index == 4:
            expected = {"stream": 1, "offset": 0, "format": 0, "dimension": 2}
        else:
            if channel["dimension"]:
                raise ValueError("Unsupported Sprite vertex channel")
            continue
        if channel != expected:
            raise ValueError("Unsupported Sprite vertex format")
    count = vertex["m_VertexCount"]
    payload = bytes(vertex["m_DataSize"])
    uv_offset = (count * 12 + 15) // 16 * 16
    if len(payload) != uv_offset + count * 8 or any(payload[count*12:]):
        raise ValueError("Sprite UV stream or padding is not the expected zero stream")
    original_uv = render["uvTransform"]
    if any(not math.isfinite(float(uv[key])) for uv in (original_uv, target_uv_transform) for key in "xyzw"):
        raise ValueError("Non-finite Sprite transform")
    if not target_uv_transform["x"] or not target_uv_transform["z"]:
        raise ValueError("Singular Sprite coordinate transform")
    def transform(x, y):
        return (_float32((original_uv["x"] * x + original_uv["y"] - target_uv_transform["y"]) / target_uv_transform["x"]),
                _float32((original_uv["z"] * y + original_uv["w"] - target_uv_transform["w"]) / target_uv_transform["z"]))
    if original_uv != target_uv_transform:
        output = bytearray(payload)
        for index in range(count):
            x, y, z = struct.unpack_from("<3f", payload, index*12)
            x, y = transform(x, y)
            struct.pack_into("<3f", output, index*12, x, y, z)
        vertex["m_DataSize"] = bytes(output)
    else:
        vertex["m_DataSize"] = payload
    sub_meshes = deepcopy(render["m_SubMeshes"])
    if original_uv != target_uv_transform:
        for sub_mesh in sub_meshes:
            bounds = sub_mesh["localAABB"]
            bounds["m_Center"]["x"], bounds["m_Center"]["y"] = transform(
                bounds["m_Center"]["x"], bounds["m_Center"]["y"])
            bounds["m_Extent"]["x"] = _float32(bounds["m_Extent"]["x"] * abs(original_uv["x"] / target_uv_transform["x"]))
            bounds["m_Extent"]["y"] = _float32(bounds["m_Extent"]["y"] * abs(original_uv["z"] / target_uv_transform["z"]))
    physics = deepcopy(body["m_PhysicsShape"])
    if original_uv != target_uv_transform:
        for shape in physics:
            for point in shape:
                point["x"], point["y"] = transform(point["x"], point["y"])
    return {"vertexData": vertex, "indexBuffer": bytes(render["m_IndexBuffer"]),
            "subMeshes": sub_meshes, "physicsShape": physics}
