"""Produce a private mapping coverage report for completed reference bundles."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "importer"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from content_importer.source import OriginalStore
from producer.mapping import SourceObjectIndex, SUPPORTED_TYPES, name_of


def audit(original: Path, reference: Path, project: Path, output: Path, *, ffmpeg=None):
    import UnityPy
    original = original.resolve()
    inputs = [str(path.relative_to(original)) for path in sorted(original.rglob("*.bundle"))]
    if (original / "data.unity3d").is_file():
        inputs.append("data.unity3d")
    store = OriginalStore(original, inputs)
    index = SourceObjectIndex(store)
    asset_index = json.loads((reference / "source-asset-index.json").read_text())["assets"]
    path_by_lower = {asset["path"].casefold(): asset["path"] for asset in asset_index}
    by_object = defaultdict(set)
    for asset in asset_index:
        for obj in asset["objects"]:
            by_object[(asset["bundle"], obj["type"].split(".")[-1], obj["name"])].add(asset["path"])
    environment = UnityPy.Environment()
    bundles = []
    for path in sorted((reference / "Content").glob("*.bundle")):
        bundles.append((path.name, environment.load_file(str(path))))
    output.mkdir(parents=True, exist_ok=True)
    summary = {"scope": "Private provenance audit; not a distributable content package",
               "source_inventory": index.inventory(), "coverage": {}, "errors": []}
    counts = Counter()
    with (output / "mapping.jsonl").open("w") as report:
        for bundle_name, bundle in bundles:
            roots = {}
            for serialized in bundle.get_assets():
                for path, pointer in serialized.container.items():
                    try:
                        roots[id(pointer.deref())] = path_by_lower.get(path.casefold(), path)
                    except (ValueError, KeyError, FileNotFoundError):
                        pass
            for serialized in bundle.get_assets():
                for target in serialized.objects.values():
                    typ = target.type.name
                    if typ not in SUPPORTED_TYPES:
                        continue
                    paths = sorted(by_object[(bundle_name, typ, name_of(target))])
                    asset_path = roots.get(id(target)) or (paths[0] if len(paths) == 1 else None)
                    source_path = project / asset_path if asset_path else None
                    if source_path is not None and not source_path.is_file():
                        source_path = None
                    try:
                        result = index.match(target, exported_asset=source_path, audio_ffmpeg=ffmpeg).as_dict()
                    except Exception as exc:
                        result = {"status": "error", "type_name": typ, "name": name_of(target),
                                  "errors": [f"{type(exc).__name__}: {exc}"]}
                    result.update(target=[bundle_name, serialized.name, target.path_id],
                                  asset_path=asset_path, asset_path_candidates=paths if not asset_path else [])
                    counts[(typ, result["status"])] += 1
                    report.write(json.dumps(result, ensure_ascii=False) + "\n")
                    report.flush()
            print(json.dumps({"bundle": bundle_name, "coverage": {f"{k[0]}:{k[1]}": v for k, v in sorted(counts.items())}}), flush=True)
    summary["coverage"] = {f"{k[0]}:{k[1]}": v for k, v in sorted(counts.items())}
    (output / "summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n")
    print(json.dumps(summary, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", required=True, type=Path)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--ffmpeg", type=Path, help="Optional private audio provenance decoder")
    arguments = parser.parse_args()
    audit(arguments.original, arguments.reference, arguments.project, arguments.output, ffmpeg=arguments.ffmpeg)
