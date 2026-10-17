#!/usr/bin/env python3
"""Produce a DRAFT data-only recipe; never publishes or silently copies payloads.

Original-content binary fields must be source references or verified conversion
pairs. Independently authored/redistributable binary fields require an explicit
per-object approval with evidence. Final release approval is a separate gate.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import UnityPy
from UnityPy.files import SerializedFile
from UnityPy.streams import EndianBinaryReader

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "importer"))
from content_importer.files import sha256
from content_importer.templates import neutralize_object
from content_importer.sprite_geometry import prove_full_rect
from index_original import load_index
from adaptations import encode_component, transparent_unused_texture


def identity(bundle, serialized, path_id):
    return bundle + "/" + serialized + "/" + str(path_id)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=True, indent=2) + "\n")


def build(reference, database, output, mappings=None, approvals=None, bundles=None, grass_mappings=None, replacements=None):
    if output.exists():
        raise FileExistsError("Recipe output must be a new directory")
    output.mkdir(parents=True)
    (output / "templates").mkdir()
    (output / "objects").mkdir()
    index, metadata = load_index(database)
    locator_ids = {tuple(locator): number for number, locator in enumerate(index.locators)}
    mapping = {}
    if mappings:
        if mappings.suffix == ".jsonl":
            for line in mappings.open():
                match = json.loads(line)
                mapping[identity(*match["target"])] = match
        else:
            mapping = json.loads(mappings.read_text())
    approved = json.loads(approvals.read_text()) if approvals else {}
    grass_matches = {}
    if grass_mappings:
        for line in grass_mappings.open():
            match = json.loads(line)
            if match.get("status") == "matched" or "all-vertex-bytes-identical" in match.get("proof", []):
                grass_matches[identity(*match["target"])] = match
    replacement_map = json.loads(replacements.read_text()) if replacements else {}
    replacement_environments = {}
    manifest = json.loads((reference / "install-manifest.json").read_text())
    names = bundles or [row["file"] for row in manifest["bundles"]]
    counts = Counter()
    unresolved, decisions, specs = [], [], []
    started = time.monotonic()
    for bundle_name in names:
        print(json.dumps({"phase": "bundle", "bundle": bundle_name}), flush=True)
        env = UnityPy.load(str(reference / bundle_name))
        bundle = next(iter(env.files.values()))
        template_name = "templates/" + bundle_name
        objects_name = "objects/" + bundle_name + ".jsonl"
        with (output / objects_name).open("w") as instructions:
            for serialized_name, file in bundle.files.items():
                if not isinstance(file, SerializedFile):
                    continue
                for target in file.objects.values():
                    ident = identity(bundle_name, serialized_name, target.path_id)
                    row = {"file": serialized_name, "pathID": target.path_id, "type": target.type.name}
                    counts[target.type.name] += 1
                    body = target.read_typetree()
                    match = mapping.get(ident, {})
                    own = approved.get(ident)
                    owner = None
                    if own:
                        if not all(own.get(key) for key in ("category", "evidence", "sourceSha256")):
                            raise ValueError("Object approval requires category, evidence and sourceSha256: " + ident)
                        if own["category"] not in ("authored", "third-party-redistributable", "engine-generated"):
                            raise ValueError("Unsupported approval category: " + ident)
                        owner = own["category"] + ":" + own["sourceSha256"]
                    if ident in replacement_map:
                        replacement = replacement_map[ident]
                        if not owner:
                            raise ValueError("Replacement object requires source approval: " + ident)
                        container = replacement["bundle"]
                        if container not in replacement_environments:
                            replacement_environments[container] = UnityPy.load(container)
                        source = next(o for o in replacement_environments[container].objects
                                      if o.assets_file.name == replacement["file"] and o.path_id == replacement["pathID"])
                        if source.type.name != target.type.name:
                            raise ValueError("Replacement type changed: " + ident)
                        body = source.read_typetree()
                    if own and own.get("generator"):
                        if own["generator"] != "transparent-unused-texture" or target.type.name != "Texture2D":
                            raise ValueError("Unsupported approved content generator: " + ident)
                        body = transparent_unused_texture(body)
                    if owner and target.type.name == "Texture2D" and body.get("m_StreamData", {}).get("size"):
                        from mapping import stream_bytes
                        body["image data"] = stream_bytes(target)
                        body["m_StreamData"] = {"offset": 0, "size": 0, "path": ""}
                    if ident in grass_matches:
                        grass = grass_matches[ident]
                        schema = {key: None for key in body}
                        schema.update(m_Name=body["m_Name"], m_IsReadable=body["m_IsReadable"])
                        schema.update(grass.get("settings", {}))
                        row["grass"] = {"source": locator_ids[tuple(grass["source"])], "schema": index.encode(schema),
                                        "vertices": grass["vertices"], "boundsPadding": grass["boundsPadding"]}
                        decisions.append({"object": ident, "action": "generated-grass", "source": row["grass"]["source"],
                                          "vertices": len(grass["vertices"])})
                    elif target.type.name in ("Texture2D", "Mesh", "AudioClip") and match.get("status") == "matched":
                        source_id = locator_ids[tuple(match["source"])]
                        schema = {key: None for key in body}
                        schema["m_Name"] = body["m_Name"]
                        row["convert"] = {"source": source_id, "schema": index.encode(schema),
                                          "texturePolicy": "preserve", "readable": True if target.type.name == "Mesh" else body.get("m_IsReadable")}
                        decisions.append({"object": ident, "action": "source-conversion", "source": source_id,
                                          "proof": match.get("proof", [])})
                    elif (target.type.name == "Sprite" and match.get("status") == "matched"
                          and match.get("adaptation", {}).get("kind") == "sprite-source-geometry"):
                        source_id = locator_ids[tuple(match["source"])]
                        keep_empty_physics_shape = not body["m_PhysicsShape"]
                        body["m_RD"]["m_VertexData"] = {}
                        body["m_RD"]["m_IndexBuffer"] = b""
                        body["m_RD"]["m_SubMeshes"] = []
                        body["m_PhysicsShape"] = []
                        missing = []
                        row["sprite"] = {"source": source_id, "keepEmptyPhysicsShape": keep_empty_physics_shape,
                                         "schema": index.encode(body, context=ident, unmatched=missing)}
                        unresolved.extend(missing)
                        decisions.append({"object": ident, "action": "source-sprite-geometry", "source": source_id,
                                          "proof": match["adaptation"], "unresolved": len(missing)})
                    else:
                        missing = []
                        patches = []
                        if target.type.name == "Sprite":
                            quad = prove_full_rect(body)
                            if quad:
                                parameters = index.encode({k: quad[k] for k in ("rect", "pivot", "pixelsPerUnit")})
                                body["m_RD"]["m_VertexData"]["m_DataSize"] = b""
                                body["m_RD"]["m_IndexBuffer"] = b""
                                patches = [ [["m_RD", "m_VertexData", "m_DataSize"], ["q", parameters, quad["arithmetic"], "vertices"]],
                                            [["m_RD", "m_IndexBuffer"], ["q", parameters, quad["arithmetic"], "indices"]] ]
                        if (target.type.name == "AnimationClip" and match.get("status") == "matched"
                            and match.get("adaptation", {}).get("kind") == "animation-stream-source"):
                            field = ["m_MuscleClip", "m_Clip", "data", "m_StreamedClip", "data"]
                            if match["adaptation"].get("sourceField") != field:
                                raise ValueError("Unsupported animation field adaptation: " + ident)
                            body["m_MuscleClip"]["m_Clip"]["data"]["m_StreamedClip"]["data"] = []
                            source_id = locator_ids[tuple(match["source"])]
                            patches.append([field, ["r", source_id, field]])
                            padding = match["adaptation"].get("densePadding")
                            if padding:
                                dense = ["m_MuscleClip", "m_Clip", "data", "m_DenseClip", "m_SampleArray"]
                                if padding.get("sourceField") != dense or padding.get("targetField") != dense:
                                    raise ValueError("Unsupported dense animation adaptation: " + ident)
                                body["m_MuscleClip"]["m_Clip"]["data"]["m_DenseClip"]["m_SampleArray"] = []
                                patches.append([dense, ["t", ["r", source_id, dense], padding["curveCount"], padding["appendFrames"]]])
                        adapted = encode_component(index, target, body, ident, missing) if not owner else None
                        row["expression"] = adapted[0] if adapted else index.encode(body, owner=owner, context=ident, unmatched=missing)
                        if patches:
                            row["expression"] = ["p", row["expression"], patches]
                        unresolved.extend(missing)
                        if target.type.name in ("Texture2D", "Mesh", "AudioClip"):
                            # Stream metadata alone cannot rebuild a content-bearing object.
                            stream = body.get("m_StreamData", body.get("m_Resource", {}))
                            if stream.get("size", stream.get("m_Size", 0)):
                                unresolved.append({"field": ident, "kind": "unmapped-resource-stream", "bytes": stream.get("size", stream.get("m_Size", 0)),
                                                   "matchStatus": match.get("status", "not-attempted")})
                        decisions.append({"object": ident, "action": "owned-expression" if owner else "source-expression",
                                          "owner": own, "adaptation": adapted[1] if adapted else match.get("adaptation"), "unresolved": len(missing)})
                    instructions.write(json.dumps(row, ensure_ascii=True, separators=(",", ":")) + "\n")
                    neutralize_object(target)
                    if sum(counts.values()) % 2000 == 0:
                        print(json.dumps({"objects": sum(counts.values()), "unresolved": len(unresolved),
                                          "elapsed": round(time.monotonic() - started)}), flush=True)
            for name, file in list(bundle.files.items()):
                if not isinstance(file, SerializedFile):
                    empty = EndianBinaryReader(b"")
                    empty.flags = file.flags
                    bundle.files[name] = empty
            (output / template_name).write_bytes(bundle.save(packer="lz4"))
        specs.append({"template": template_name, "objects": objects_name, "output": bundle_name})
    write_json(output / "sources.json", index.locators)
    shutil.copyfile(reference / manifest["catalogFile"], output / "catalog.json")
    files = [{"file": str(path.relative_to(output)), "sha256": sha256(path), "bytes": path.stat().st_size}
             for path in sorted(output.rglob("*")) if path.is_file()]
    recipe = {"schema": 1, "status": "draft", "platform": manifest["platform"], "engine": manifest["engineVersion"],
              "contentId": manifest["contentId"], "originalFiles": metadata["originalFiles"], "sourceObjects": "sources.json",
              "files": files, "bundles": specs, "catalog": "catalog.json", "scenes": manifest["scenes"],
              "initialScene": manifest["initialScene"], "runtime": {"files": [], "links": [], "executable": ""}}
    write_json(output / "recipe.json", recipe)
    report = {"schema": 1, "status": "blocked" if unresolved else "reconstruction-candidate",
              "publicReleaseApproved": False, "sourceStatistics": metadata["statistics"], "sourceReadErrors": metadata["errors"],
              "targetCounts": dict(counts), "unresolvedCount": len(unresolved), "unresolved": unresolved,
              "decisions": decisions, "missingGates": ["runtime-provenance", "real-import-runtime-validation", "release-review"]}
    write_json(output / "audit.private.json", report)
    print(json.dumps({"status": report["status"], "objects": sum(counts.values()), "unresolved": len(unresolved),
                      "output": str(output), "seconds": round(time.monotonic() - started)}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mappings", type=Path)
    parser.add_argument("--approvals", type=Path)
    parser.add_argument("--bundle", action="append")
    parser.add_argument("--grass-mappings", type=Path)
    parser.add_argument("--replacements", type=Path)
    args = parser.parse_args()
    build(args.reference, args.database, args.output, args.mappings, args.approvals, args.bundle, args.grass_mappings, args.replacements)
