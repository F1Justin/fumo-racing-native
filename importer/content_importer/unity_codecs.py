"""Rebuild Unity 6 asset bodies from the player's Unity 2022 source objects.

No filesystem writes or source mutation occur in prepare_conversion(). The caller
owns source identification and resource-file packing. apply_conversion() changes
only the supplied target ObjectReader body; its path ID and serialized type remain
unchanged, so existing object references continue to resolve.

The target is a schema/reference holder, never a fallback source for asset data.
Unknown target-only fields fail closed. Verified with UnityPy 1.25.0 and Unity
6000.3.25f1 for Texture2D (DXT5 and decoded RGBA32), AudioClip (original FSB/Vorbis),
and Mesh (vertices, normals, indices). Full-content coverage is a separate gate.
"""
from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass, field
import hashlib
from typing import Any


class AssetConversionError(ValueError):
    pass


@dataclass
class AssetConversion:
    type_name: str
    target_path_id: int
    typetree: dict[str, Any]
    stream_kind: str | None = None
    stream_data: bytes = b""
    evidence: dict[str, Any] = field(default_factory=dict)


def _read_resource(source: Any, path: str, offset: int, size: int) -> bytes:
    if size < 0 or offset < 0 or (size and not path):
        raise AssetConversionError("Invalid source resource range")
    if not size:
        return b""
    from UnityPy.helpers.ResourceReader import get_resource_data
    payload = bytes(get_resource_data(path, source.assets_file, offset, size))
    if len(payload) != size:
        raise AssetConversionError("Source resource is truncated")
    return payload


def _empty_stream() -> dict[str, Any]:
    return {"offset": 0, "size": 0, "path": ""}


def _empty_lod_info(submesh_count: int) -> dict[str, Any]:
    # Unity 6 adds this field. A single level with zero ranges is the native
    # default for meshes without generated LODs, verified by the fixture build.
    return {"m_LodSelectionCurve": {"m_LodSlope": 0.0, "m_LodBias": 0.0},
            "m_NumLevels": 1,
            "m_SubMeshes": [{"m_Levels": [{"m_IndexStart": 0, "m_IndexCount": 0}]}
                            for _ in range(submesh_count)]}


def prepare_conversion(source: Any, target: Any, *, texture_policy: str = "preserve",
                       preserve_target_name: bool = True,
                       make_readable: bool | None = None) -> AssetConversion:
    """Return a new body and optional stream from a confirmed source/target pair.

    texture_policy='preserve' retains the original GPU encoding and mip chain.
    'rgba32' decodes the top mip locally and deliberately writes a one-mip RGBA32
    texture; it is a compatibility fallback, not a quality-equivalent default.
    Audio needs an output resource path in apply_conversion(). Texture/Mesh can
    either be inlined or assigned an output resource range by the caller.
    """
    typ = source.type.name
    if typ != target.type.name or typ not in ("Texture2D", "AudioClip", "Mesh"):
        raise AssetConversionError("Expected matching Texture2D, AudioClip or Mesh objects")
    sd, td = source.read_typetree(), target.read_typetree()
    allowed_added = {"Texture2D": {"m_IgnoreMipmapLimit", "m_MipmapLimitGroupName"},
                     "Mesh": {"m_MeshLodInfo"}, "AudioClip": set()}[typ]
    missing = set(td) - set(sd) - allowed_added
    if missing:
        raise AssetConversionError(f"Unexplained {typ} target fields: {sorted(missing)}")
    # Copy source values, including nested content; never retain target payloads.
    out = {key: deepcopy(sd[key]) for key in td if key in sd}
    if preserve_target_name:
        out["m_Name"] = td["m_Name"]
    evidence = {"type": typ, "source_path_id": source.path_id,
                "source_file": source.assets_file.name, "source_name": sd.get("m_Name"),
                "target_path_id": target.path_id,
                "source_only_fields": sorted(set(sd) - set(td)),
                "target_only_fields": sorted(set(td) - set(sd))}
    converted = AssetConversion(typ, target.path_id, out, evidence=evidence)
    if typ == "Texture2D":
        raw = source.read()
        stream = sd.get("m_StreamData", {})
        if sd.get("image data"):
            payload = bytes(sd["image data"])
        elif stream.get("size", 0):
            payload = _read_resource(source, stream["path"], stream["offset"], stream["size"])
        else:
            payload = b""  # Dynamic/empty font atlas; do not invent image content.
        if texture_policy == "rgba32" and payload:
            from PIL import Image
            pixels = raw.image.convert("RGBA")
            # Unity's raw texture rows run bottom-to-top; UnityPy images are top-down.
            payload = pixels.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
            out.update(m_TextureFormat=4, m_MipCount=1, m_MipsStripped=0,
                       m_CompleteImageSize=len(payload), m_IsPreProcessed=False)
        elif texture_policy not in ("preserve", "rgba32"):
            raise AssetConversionError(f"Unsupported texture policy: {texture_policy}")
        if "m_IgnoreMipmapLimit" in td:
            out["m_IgnoreMipmapLimit"] = sd.get("m_IgnoreMipmapLimit", sd.get("m_IgnoreMasterTextureLimit", False))
        if "m_MipmapLimitGroupName" in td:
            out["m_MipmapLimitGroupName"] = sd.get("m_MipmapLimitGroupName", "")
        out["image data"] = b""
        out["m_StreamData"] = _empty_stream()
        if make_readable is not None:
            out["m_IsReadable"] = make_readable
        converted.stream_kind, converted.stream_data = "texture", payload
        evidence.update(width=out["m_Width"], height=out["m_Height"],
                        texture_format=out["m_TextureFormat"], mip_count=out["m_MipCount"],
                        texture_policy=texture_policy)
    elif typ == "AudioClip":
        resource = sd["m_Resource"]
        payload = _read_resource(source, resource["m_Source"], resource["m_Offset"], resource["m_Size"])
        if not payload:
            raise AssetConversionError("Empty AudioClip resource")
        if payload[:4] != b"FSB5":
            raise AssetConversionError("Unsupported AudioClip encoding; expected original FSB5")
        out["m_Resource"] = {"m_Source": "", "m_Offset": 0, "m_Size": 0}
        converted.stream_kind, converted.stream_data = "audio", payload
        evidence.update(channels=out["m_Channels"], frequency=out["m_Frequency"],
                        compression=out["m_CompressionFormat"])
    else:
        stream = sd.get("m_StreamData", {})
        if stream.get("size", 0):
            payload = _read_resource(source, stream["path"], stream["offset"], stream["size"])
        else:
            payload = bytes(sd["m_VertexData"]["m_DataSize"])
        out["m_VertexData"]["m_DataSize"] = b""
        out["m_StreamData"] = _empty_stream()
        if "m_MeshLodInfo" in td:
            out["m_MeshLodInfo"] = deepcopy(sd.get("m_MeshLodInfo", _empty_lod_info(len(sd["m_SubMeshes"]))))
        # Old PhysX cooked collision blobs are engine/platform-specific. Unity
        # recooks from the retained mesh geometry, rather than consuming those blobs.
        for key in ("m_BakedConvexCollisionMesh", "m_BakedTriangleCollisionMesh"):
            if key in out:
                out[key] = []
        if make_readable is not None:
            out["m_IsReadable"] = make_readable
            if make_readable:
                out["m_KeepVertices"] = out["m_KeepIndices"] = True
        converted.stream_kind, converted.stream_data = "mesh", payload
        evidence.update(vertex_count=out["m_VertexData"]["m_VertexCount"],
                        index_bytes=len(out["m_IndexBuffer"]), submeshes=len(out["m_SubMeshes"]))
    evidence.update(stream_bytes=len(converted.stream_data),
                    stream_sha256=hashlib.sha256(converted.stream_data).hexdigest())
    return converted


def apply_conversion(target: Any, converted: AssetConversion, *,
                     resource_path: str | None = None, offset: int = 0) -> None:
    """Serialize the converted body into target, optionally assigning a stream.

    The caller must append converted.stream_data at exactly offset in resource_path.
    For bundles use archive:/<CAB>/<CAB>.resource or .resS as appropriate. This
    function does not write that stream or change the ObjectReader's identity.
    """
    if target.path_id != converted.target_path_id or target.type.name != converted.type_name:
        raise AssetConversionError("Conversion target identity changed")
    if offset < 0 or (resource_path is None and offset):
        raise AssetConversionError("Invalid output resource offset")
    body, payload = deepcopy(converted.typetree), converted.stream_data
    if converted.stream_kind == "audio":
        if not resource_path:
            raise AssetConversionError("AudioClip requires an output .resource path")
        body["m_Resource"] = {"m_Source": resource_path, "m_Offset": offset, "m_Size": len(payload)}
    elif converted.stream_kind in ("texture", "mesh"):
        if resource_path and payload:
            body["m_StreamData"] = {"path": resource_path, "offset": offset, "size": len(payload)}
        elif converted.stream_kind == "texture":
            body["image data"] = payload
        else:
            body["m_VertexData"]["m_DataSize"] = payload
    target.save_typetree(body)
    if target.path_id != converted.target_path_id:
        raise AssetConversionError("Target path ID changed unexpectedly")
