"""Private proof of generated grass against original mesh vertex references.

Only original object locators, vertex indices and an authored bounds expansion
enter the recipe. Reference geometry is used for comparison, never serialized.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"importer"))
from content_importer.grass import bounds, decode_mesh, f32, generate_grass, vertex_key
from content_importer.source import OriginalStore


def poskey(position):
    return struct.pack("<3f", *position[:3])


def plain(value):
    if isinstance(value, (bytes, bytearray)):
        return list(value)
    if isinstance(value, dict):
        return {key: plain(item) for key, item in value.items()}
    if isinstance(value, (tuple, list)):
        return [plain(item) for item in value]
    return value


def same_bounds(left, right):
    return all(abs(left[part][axis]-right[part][axis]) <= 2e-6*max(1, abs(right[part][axis]))
               for part in ("m_Center", "m_Extent") for axis in "xyz")


def match_grass(source, target, decoded_source=None):
    src = decoded_source or decode_mesh(source)
    dst = decode_mesh(target)
    target_body = target.read_typetree()
    if not dst.m_Vertices or len(dst.m_Vertices) % 12:
        raise ValueError("Target is not a complete three-card mesh")
    lookup = defaultdict(list)
    for index in range(len(src.m_Vertices or [])):
        lookup[vertex_key(src, index)].append(index)
    used = defaultdict(int)
    indices = []
    for index in range(0, len(dst.m_Vertices), 12):
        key = vertex_key(dst, index)
        candidates = lookup.get(key, [])
        if used[key] >= len(candidates):
            raise ValueError("Target vertex attributes are absent from this original mesh")
        indices.append(candidates[used[key]])
        used[key] += 1
    raw_bounds = bounds([src.m_Vertices[index] for index in indices])
    padding = [target_body["m_LocalAABB"]["m_Extent"][axis]-raw_bounds["m_Extent"][axis] for axis in "xyz"]
    if max(padding)-min(padding) > 2e-5 or min(padding) < -1e-6:
        raise ValueError("Bounds expansion differs across axes")
    expansion = f32(round(sum(padding)/3, 6))
    schema = {key: None for key in target_body}
    schema.update(m_Name=target_body["m_Name"], m_IsReadable=target_body["m_IsReadable"])
    settings = {key: target_body[key] for key in ("m_IsReadable", "m_KeepVertices", "m_KeepIndices")}
    schema.update(settings)
    rebuilt = generate_grass(source, schema, indices, expansion, decoded=src)
    # Verify every field; float bounds are subject to harmless float32 reduction
    # ordering, but all geometry and indices must match byte for byte.
    for key in target_body:
        expected, actual = plain(target_body[key]), plain(rebuilt[key])
        if key == "m_LocalAABB":
            equal = same_bounds(actual, expected)
        elif key == "m_SubMeshes":
            equal = len(actual) == len(expected) == 1
            if equal:
                equal = same_bounds(actual[0]["localAABB"], expected[0]["localAABB"])
                equal = equal and {k:v for k,v in actual[0].items() if k != "localAABB"} == {k:v for k,v in expected[0].items() if k != "localAABB"}
        else:
            equal = actual == expected
        if not equal:
            raise ValueError("Generated target field mismatch: " + key)
    payload = bytes(rebuilt["m_VertexData"]["m_DataSize"])
    return {"vertices": indices, "boundsPadding": expansion, "settings": settings,
            "proof": ["all-vertex-bytes-identical", "all-index-bytes-identical", "all-fields-compared", "bounds-float32-tolerance"],
            "vertexBytesSHA256": hashlib.sha256(payload).hexdigest(), "vertexCount": len(dst.m_Vertices)}


def audit(original, reference, mapping_file, output):
    import UnityPy
    rows = [json.loads(line) for line in mapping_file.open()]
    grass = [row for row in rows if row.get("type_name") == "Mesh" and (row.get("asset_path") or "").startswith("Assets/NativeGenerated/Grass/")]
    env = UnityPy.Environment()
    targets = {}
    wanted = {tuple(row["target"]) for row in grass}
    first_positions = defaultdict(list)
    for name in sorted({row["target"][0] for row in grass}):
        bundle = env.load_file(str(reference/name))
        for file in bundle.get_assets():
            for reader in file.objects.values():
                key = (name, file.name, reader.path_id)
                if key in wanted:
                    targets[key] = reader
                    mesh = decode_mesh(reader)
                    first_positions[poskey(mesh.m_Vertices[0])].append(key)
    inputs = ["data.unity3d"]+[str(path.relative_to(original)) for path in sorted(original.rglob("*.bundle"))]
    store = OriginalStore(original, inputs)
    matched, errors = {}, defaultdict(list)
    sources = [r for r in store.objects() if r.type.name == "Mesh"]
    # Every source is examined; names are only a performance hint.
    sources.sort(key=lambda r: ("grass" not in r.peek_name().casefold(), r.peek_name()))
    for position, reader in enumerate(sources):
        if len(matched) == len(targets):
            break
        candidate_keys = set()
        try:
            mesh = decode_mesh(reader)
            candidate_keys = {key for vertex in mesh.m_Vertices or [] for key in first_positions.get(poskey(vertex), []) if key not in matched}
            for key in candidate_keys:
                try:
                    result = match_grass(reader, targets[key], mesh)
                    matched[key] = {"target": list(key), "source": store.reader_locations[id(reader)], **result}
                except (ValueError, KeyError, AssertionError) as exc:
                    errors[key].append(str(exc))
        except Exception as exc:
            print(json.dumps({"sourceMeshReadError": reader.peek_name(), "error": str(exc)}), flush=True)
        if position % 50 == 0 or candidate_keys:
            print(json.dumps({"sourceMeshes": position+1, "matched": len(matched), "targets": len(targets)}), flush=True)
    output.mkdir(parents=True, exist_ok=False)
    with (output/"grass-mapping.jsonl").open("w") as file:
        for key, result in sorted(matched.items()):
            file.write(json.dumps(result, separators=(",", ":"))+"\n")
    missing = [{"target": list(key), "errors": errors[key][-5:]} for key in sorted(wanted-set(matched))]
    summary = {"targets": len(targets), "matched": len(matched), "unmatched": missing,
               "targetGeometryDistributed": False, "publicDistributionAccepted": False}
    (output/"grass-audit.private.json").write_text(json.dumps(summary, indent=2)+"\n")
    print(json.dumps(summary), flush=True)
    return 0 if not missing else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--mapping", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    raise SystemExit(audit(args.original.resolve(), args.reference.resolve(), args.mapping.resolve(), args.output.resolve()))
