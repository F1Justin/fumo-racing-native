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
from index_original import load_index
from adaptations import encode_component


def identity(bundle, serialized, path_id):
    return bundle + "/" + serialized + "/" + str(path_id)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=True, indent=2) + "\n")


def build(reference, database, output, mappings=None, approvals=None, bundles=None):
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
                    if target.type.name in ("Texture2D", "Mesh", "AudioClip") and match.get("status") == "matched":
                        source_id = locator_ids[tuple(match["source"])]
                        schema = {key: None for key in body}
                        schema["m_Name"] = body["m_Name"]
                        row["convert"] = {"source": source_id, "schema": index.encode(schema),
                                          "texturePolicy": "preserve", "readable": body.get("m_IsReadable")}
                        decisions.append({"object": ident, "action": "source-conversion", "source": source_id,
                                          "proof": match.get("proof", [])})
                    else:
                        missing = []
                        adapted = encode_component(index, target, body, ident, missing) if not owner else None
                        row["expression"] = adapted[0] if adapted else index.encode(body, owner=owner, context=ident, unmatched=missing)
                        unresolved.extend(missing)
                        if target.type.name in ("Texture2D", "Mesh", "AudioClip"):
                            # Stream metadata alone cannot rebuild a content-bearing object.
                            stream = body.get("m_StreamData", body.get("m_Resource", {}))
                            if stream.get("size", stream.get("m_Size", 0)):
                                unresolved.append({"field": ident, "kind": "unmapped-resource-stream", "bytes": stream.get("size", stream.get("m_Size", 0)),
                                                   "matchStatus": match.get("status", "not-attempted")})
                        decisions.append({"object": ident, "action": "owned-expression" if owner else "source-expression",
                                          "owner": own, "adaptation": adapted[1] if adapted else None, "unresolved": len(missing)})
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
    args = parser.parse_args()
    build(args.reference, args.database, args.output, args.mappings, args.approvals, args.bundle)
