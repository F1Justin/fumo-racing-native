"""Recreate our three-card grass meshes from vertices in the user's original.

The recipe contains source vertex indices, never positions or target mesh bytes.
Chunk membership is decided and verified by the private producer. Geometry,
normals, tangents and colors are read from the original package at installation.
"""
from __future__ import annotations

from copy import deepcopy
import math
import struct

from .files import ImportFailure
from .unity_codecs import _empty_lod_info


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def fallback_tangent(normal):
    x, y, z = normal
    vector = (-z, 0.0, x) if abs(y) < 0.9 else (y, -x, 0.0)
    length = math.sqrt(sum(v*v for v in vector))
    return tuple(f32(v/length) if length > 1e-5 else 0.0 for v in vector)+(1.0,)


def decode_mesh(reader):
    from UnityPy.helpers.MeshHelper import MeshHandler
    if reader.type.name != "Mesh":
        raise ImportFailure("草地重建来源不是模型。")
    mesh = reader.read()
    handler = MeshHandler(mesh)
    handler.process()
    # MeshHandler exposes integer storage for normalized vertex formats; Unity's
    # GetColors/GetNormals API exposes the normalized floating-point values.
    channels = mesh.m_VertexData.m_Channels or []
    for channel, name in [(0, "m_Vertices"), (1, "m_Normals"), (2, "m_Tangents"), (3, "m_Colors")]:
        values = getattr(handler, name)
        if not values or channel >= len(channels):
            continue
        form = channels[channel].format
        if form in (2, 3, 4, 5):
            divisor = {2: 255.0, 3: 127.0, 4: 65535.0, 5: 32767.0}[form]
            setattr(handler, name, [tuple(f32(max(-1.0, v/divisor)) for v in row) for row in values])
    return handler


def source_vertex(handler, index):
    position = tuple(handler.m_Vertices[index][:3])
    normal = tuple(handler.m_Normals[index][:3]) if handler.m_Normals else (0.0, 1.0, 0.0)
    tangent = tuple(handler.m_Tangents[index]) if handler.m_Tangents else fallback_tangent(normal)
    color = tuple(handler.m_Colors[index]) if handler.m_Colors else (1.0,)*4
    return position+normal+tangent+color


def vertex_key(handler, index):
    return struct.pack("<14f", *source_vertex(handler, index))


def bounds(positions, padding=0.0):
    if not positions:
        raise ImportFailure("草地重建没有顶点。")
    low = [min(v[axis] for v in positions) for axis in range(3)]
    high = [max(v[axis] for v in positions) for axis in range(3)]
    return {"m_Center": {axis: f32(f32(lo+hi)*0.5) for axis, lo, hi in zip("xyz", low, high)},
            "m_Extent": {axis: f32(f32(f32(hi-lo)*0.5)+padding) for axis, lo, hi in zip("xyz", low, high)}}


def _empty_compressed():
    floats = {"m_Vertices", "m_UV", "m_Normals", "m_Tangents", "m_FloatColors"}
    integers = {"m_Weights", "m_NormalSigns", "m_TangentSigns", "m_BoneIndices", "m_Triangles"}
    return {**{key: {"m_NumItems": 0, "m_Range": 0.0, "m_Start": 0.0, "m_Data": [], "m_BitSize": 0} for key in floats},
            **{key: {"m_NumItems": 0, "m_Data": [], "m_BitSize": 0} for key in integers}, "m_UVInfo": 0}


def generate_grass(source, schema, vertices, bounds_padding, *, decoded=None):
    """Return a complete Unity Mesh body using only source geometry and our code.

    `schema` supplies root field names, m_Name and optional m_IsReadable. Every
    other field is generated here; no target field is copied as a fallback.
    """
    mesh = decoded if decoded is not None else decode_mesh(source)
    count = len(mesh.m_Vertices or [])
    if (not isinstance(vertices, list) or not vertices or len(vertices) > count
            or any(type(i) is not int or i < 0 or i >= count for i in vertices)
            or len(set(vertices)) != len(vertices)):
        raise ImportFailure("草地顶点配方与原版模型不一致。")
    if (type(bounds_padding) not in (int, float) or not math.isfinite(bounds_padding)
            or not 0 <= bounds_padding <= 10000):
        raise ImportFailure("草地范围配方不正确。")
    positions = [mesh.m_Vertices[index][:3] for index in vertices]
    total = len(vertices)*12
    vertex_data = bytearray(total*72)
    index_format = 1 if total > 65535 else 0
    index_struct = struct.Struct("<6I" if index_format else "<6H")
    index_data = bytearray(len(vertices)*3*index_struct.size)
    cursor = 0
    for source_index in vertices:
        attributes = source_vertex(mesh, source_index)
        for card in range(3):
            for corner in range(4):
                struct.pack_into("<18f", vertex_data, cursor*72, *attributes,
                                 1.0 if corner < 2 else 0.0, float(corner % 2), float(card), 0.0)
                cursor += 1
            first = cursor-4
            index_struct.pack_into(index_data, (first//4)*index_struct.size,
                                   first, first+1, first+2, first+2, first+1, first+3)
    dimensions = (3, 3, 4, 4, 2, 2)+(0,)*8
    offsets = (0, 12, 24, 40, 56, 64)+(0,)*8
    channels = [{"stream": 0, "offset": offset, "format": 0, "dimension": dimension}
                for offset, dimension in zip(offsets, dimensions)]
    values = {
        "m_Name": schema["m_Name"],
        "m_SubMeshes": [{"firstByte": 0, "indexCount": len(vertices)*18, "topology": 0,
                         "baseVertex": 0, "firstVertex": 0, "vertexCount": total, "localAABB": bounds(positions)}],
        "m_Shapes": {"vertices": [], "shapes": [], "channels": [], "fullWeights": []},
        "m_BindPose": [], "m_BoneNameHashes": [], "m_RootBoneNameHash": 0, "m_BonesAABB": [],
        "m_VariableBoneCountWeights": {"m_Data": []}, "m_MeshCompression": 0,
        "m_IsReadable": schema.get("m_IsReadable", True) is not False,
        "m_KeepVertices": False, "m_KeepIndices": False, "m_IndexFormat": index_format,
        "m_IndexBuffer": index_data, "m_VertexData": {"m_VertexCount": total, "m_Channels": channels, "m_DataSize": vertex_data},
        "m_CompressedMesh": _empty_compressed(), "m_LocalAABB": bounds(positions, f32(bounds_padding)),
        "m_MeshUsageFlags": 0, "m_CookingOptions": 30,
        "m_BakedConvexCollisionMesh": [], "m_BakedTriangleCollisionMesh": [],
        "m_MeshMetrics[0]": 1.0, "m_MeshMetrics[1]": 1.0,
        "m_StreamData": {"offset": 0, "size": 0, "path": ""}, "m_MeshLodInfo": _empty_lod_info(1),
    }
    unknown = set(schema)-set(values)
    if unknown:
        raise ImportFailure("草地模型结构包含不支持的字段。")
    return {key: deepcopy(values[key]) for key in schema}
