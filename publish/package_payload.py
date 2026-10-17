"""Validate a complete installation payload before copying it into a launcher."""
from pathlib import Path
import hashlib
import json
import re

from audit import AuditFailure, inspect_stream, safe_name

ACCEPTANCE = "release-acceptance.json"


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=True, sort_keys=True, indent=2)+"\n").encode()


def read_json(path):
    if path.stat().st_size > 64*1024**2:
        raise AuditFailure("JSON document exceeds limit")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise AuditFailure("Duplicate JSON key: " + key)
            result[key] = value
        return result
    return json.loads(path.read_text(), object_pairs_hook=unique,
                      parse_constant=lambda value: (_ for _ in ()).throw(AuditFailure("Non-finite JSON number")))


def file_identity(path):
    with path.open("rb") as stream:
        return inspect_stream(stream, path.name)


def validate_acceptance(recipe, root):
    if recipe.get("acceptance") != ACCEPTANCE:
        raise AuditFailure("Ready payload needs its release acceptance record")
    receipt = read_json(root/ACCEPTANCE)
    for key in ("originalPackageImportVerified", "playableGameVerified", "sourceProvenanceReviewed"):
        if receipt.get(key) is not True:
            raise AuditFailure("Release acceptance incomplete: " + key)
    if (receipt.get("schemaVersion") != 1 or receipt.get("contentId") != recipe.get("contentId")
            or receipt.get("platform") != recipe.get("platform")
            or type(receipt.get("unresolvedCount")) is not int or receipt["unresolvedCount"] != 0
            or type(receipt.get("originalContentObjectsBundled")) is not int or receipt["originalContentObjectsBundled"] != 0):
        raise AuditFailure("Release acceptance does not match this installation")
    for key in ("candidateRecipeSHA256", "verificationReceiptSHA256", "sourceInventorySHA256"):
        if not re.fullmatch(r"[0-9a-f]{64}", str(receipt.get(key, ""))):
            raise AuditFailure("Acceptance identity is missing: " + key)
    candidate = dict(recipe)
    candidate.pop("acceptance")
    candidate["status"] = "draft"
    candidate["files"] = [row for row in recipe["files"] if row["file"] != ACCEPTANCE]
    if hashlib.sha256(json_bytes(candidate)).hexdigest() != receipt["candidateRecipeSHA256"]:
        raise AuditFailure("Ready payload differs from the tested candidate")
    return receipt


def validate_recipe_dir(directory, *, require_ready=False, allowed_extra=frozenset()):
    root = Path(directory).resolve()
    recipe_path = root/"recipe.json"
    if not root.is_dir() or recipe_path.is_symlink() or not recipe_path.is_file():
        raise AuditFailure("Missing recipe.json")
    recipe = read_json(recipe_path)
    if recipe.get("schema") != 1 or recipe.get("status") not in ("draft", "ready"):
        raise AuditFailure("Unsupported installation manifest")
    if require_ready and recipe["status"] != "ready":
        raise AuditFailure("Player packages require a ready installation with completed acceptance")
    if recipe.get("platform") not in ("macos", "windows", "linux"):
        raise AuditFailure("Unsupported installation platform")
    rows = recipe.get("files")
    if not isinstance(rows, list) or not rows or len(rows) > 50000:
        raise AuditFailure("Invalid installation file inventory")
    expected, aliases = {}, set()
    for row in rows:
        name = row["file"]
        safe_name(name)
        if name == "recipe.json" or name.casefold() in aliases:
            raise AuditFailure("Duplicate or self-referencing installation file")
        aliases.add(name.casefold())
        if type(row.get("bytes")) is not int or row["bytes"] < 0 or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", ""))):
            raise AuditFailure("Invalid installation file identity")
        expected[name] = row
    actual = set()
    for path in root.rglob("*"):
        name = path.relative_to(root).as_posix()
        if name in allowed_extra:
            continue
        safe_name(name)
        if path.is_symlink():
            raise AuditFailure("Installation payload must use explicit runtime link records: " + name)
        if path.is_dir():
            continue
        if not path.is_file():
            raise AuditFailure("Unexpected installation file: " + name)
        actual.add(name)
        if name != "recipe.json":
            row = expected.get(name)
            if row is None or file_identity(path) != {"sha256": row["sha256"], "bytes": row["bytes"]}:
                raise AuditFailure("Installation file is unlisted or changed: " + name)
    if actual != set(expected)|{"recipe.json"}:
        raise AuditFailure("Installation file inventory does not exactly cover the directory")
    if require_ready:
        validate_acceptance(recipe, root)
    return recipe
