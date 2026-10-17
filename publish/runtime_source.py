"""Strict manifest-bound exception for reviewed runtime source under Assets/."""
import hashlib
import json
from pathlib import PurePosixPath
import re

CODE_SUFFIXES = {".cs", ".shader", ".cginc", ".hlsl", ".compute", ".asmdef", ".asmref"}
LINEAGES = {"project-reimplementation-source-owner-authorized", "distribution-overlay-authored-source",
            "unity-package-exact-match", "textmeshpro-derived-review-required", "nvjob-and-unity-mit-derived"}
EXPLICIT = {"Assets/Resources/NativeSettings/post.shader", "Assets/Resources/NativeSettings/particles.shader",
            "Assets/Shaders/UI/UIMovingPattern.shader", "Assets/Shaders/GrassShader/Shader/GGG_Shader.shader"}


def validate(root, directory, failure):
    name = PurePosixPath(directory)
    if (not directory or name.is_absolute() or any(p in ("", ".", "..") for p in directory.split("/"))
            or "\\" in directory or ":" in directory):
        raise failure("Runtime source directory must be a relative path")
    path = root/name
    manifest_path = path/"source-manifest.json"
    if path.is_symlink() or manifest_path.is_symlink() or not manifest_path.is_file() or manifest_path.stat().st_size > 2*1024**2:
        raise failure("Missing or invalid runtime source manifest")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise failure("Duplicate runtime manifest key")
            result[key] = value
        return result
    manifest = json.loads(manifest_path.read_text(), object_pairs_hook=unique)
    if (manifest.get("schema") != 1 or manifest.get("kind") != "runtime-source-only"
            or manifest.get("gameAssetsIncluded") is not False
            or not re.fullmatch(r"[0-9a-f]{64}", str(manifest.get("sourceAuditSHA256", "")))):
        raise failure("Runtime source manifest identity is invalid")
    expected, allowed = {}, set()
    for group in ("files", "notices", "metadataFiles"):
        rows = manifest.get(group)
        if not isinstance(rows, list) or not rows:
            raise failure("Incomplete runtime source manifest: " + group)
        for row in rows:
            value = row.get("path", "")
            rel = PurePosixPath(value)
            if (not value or rel.is_absolute() or "\\" in value or ":" in value
                    or any(p in ("", ".", "..") for p in value.split("/"))):
                raise failure("Unsafe runtime source manifest path")
            if value.casefold() in {key.casefold() for key in expected}:
                raise failure("Duplicate runtime source manifest path")
            if group == "files":
                if (rel.suffix not in CODE_SUFFIXES or "/Editor/" in value or row.get("lineage") not in LINEAGES
                        or not (value.startswith(("Assets/Scripts/NativePlayable/", "Assets/TextMesh Pro/Shaders/")) or value in EXPLICIT)):
                    raise failure("Runtime source path or lineage is outside the code allowlist: " + value)
            elif group == "notices":
                if not value.startswith("ThirdPartyNotices/") or rel.suffix.casefold() not in (".txt", ".md", ""):
                    raise failure("Unexpected runtime notice path: " + value)
            elif value not in ("README.md", "Packages/manifest.json"):
                raise failure("Unexpected runtime metadata: " + value)
            if (type(row.get("bytes")) is not int or not 0 <= row["bytes"] <= 2*1024**2
                    or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))):
                raise failure("Invalid runtime source file identity: " + value)
            expected[value] = row
            target = path/rel
            if target.is_symlink() or not target.is_file():
                raise failure("Missing runtime source file: " + value)
            data = target.read_bytes()
            if len(data) != row["bytes"] or hashlib.sha256(data).hexdigest() != row["sha256"]:
                raise failure("Runtime source changed after review: " + value)
            full = (name/rel).as_posix()
            allowed.add(full)
            allowed.update(p.as_posix() for p in PurePosixPath(full).parents)
    if {r["path"] for r in manifest["metadataFiles"]} != {"README.md", "Packages/manifest.json"}:
        raise failure("Runtime metadata set is incomplete")
    actual = set()
    for child in path.rglob("*"):
        if child.is_symlink():
            raise failure("Runtime source contains a symlink")
        if child.is_file():
            actual.add(child.relative_to(path).as_posix())
    if actual != set(expected)|{"source-manifest.json"}:
        raise failure("Runtime source manifest does not exactly cover the directory")
    return allowed, {"directory": directory, "sourceAuditSHA256": manifest["sourceAuditSHA256"],
                     "manifestSHA256": hashlib.sha256(manifest_path.read_bytes()).hexdigest(), "files": len(expected)}
