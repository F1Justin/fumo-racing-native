#!/usr/bin/env python3
"""Fail-closed structural audit of a clean source export or reviewed release payload."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import posixpath
import re
import stat
import zipfile

FORBIDDEN_COMPONENTS = {".git", ".private", ".aws", ".ssh", ".codex", ".agents", "private", "private-reference", "local",
                        "assetripper", "assets", "library", "temp", "obj", "logs", "players", "original", "originals", "verified-original"}
FORBIDDEN_NAMES = {"release.seed", "auth.json", "credentials.json", "source-inputs.private.json", "source-asset-index.json",
                   "producer-receipt.json", ".env", "globalgamemanagers.original"}
SOURCE_SUFFIXES = {".py", ".go", ".m", ".cs", ".shader", ".cginc", ".hlsl", ".compute", ".asmdef", ".asmref", ".html", ".md", ".txt", ".json", ".yml", ".yaml", ".mod", ".sum"}
SECRET_PATTERNS = [
    re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    re.compile(rb"\bgh[pousr]_[A-Za-z0-9]{36,}\b"),
    re.compile(rb"\bgithub_pat_[A-Za-z0-9_]{50,}\b"),
    re.compile(rb"\bAKIA[A-Z0-9]{16}\b"),
    re.compile(rb"\bxox[baprs]-[A-Za-z0-9-]{20,}\b"),
]
RELEASE_CLASSES = {"project-code", "third-party-runtime", "reconstruction-recipe", "notice", "checksum"}


class AuditFailure(ValueError):
    pass


def safe_name(name: str, *, runtime_names=frozenset()):
    if not isinstance(name, str) or not name or "\\" in name or "\x00" in name or name.startswith("/"):
        raise AuditFailure("Unsafe path in export")
    path = PurePosixPath(name)
    if any(part in ("", ".", "..") for part in name.split("/")) or ":" in name:
        raise AuditFailure("Unsafe path in export: " + name)
    for part in path.parts:
        lower = part.casefold()
        if ((lower in FORBIDDEN_COMPONENTS and not (lower == "assets" and name in runtime_names))
                or lower in FORBIDDEN_NAMES or lower.endswith(".private.json") or lower.startswith(".env.")):
            raise AuditFailure("Private or original-content path rejected: " + name)
        if lower.endswith((".p12", ".pfx", ".key", ".pem", ".seed")):
            raise AuditFailure("Credential/signing file rejected: " + name)
    return path


def inspect_stream(stream, name: str):
    digest = hashlib.sha256()
    size = 0
    tail = b""
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(chunk)
        size += len(chunk)
        if any(pattern.search(tail + chunk) for pattern in SECRET_PATTERNS):
            # Never print the matched credential itself.
            raise AuditFailure("Possible credential content rejected: " + name)
        tail = chunk[-256:]
    return {"sha256": digest.hexdigest(), "bytes": size}


def inspect_zip(path: Path):
    total = 0
    seen = set()
    with zipfile.ZipFile(path) as archive:
        rows = archive.infolist()
        if len(rows) > 100000:
            raise AuditFailure("Too many archive entries: " + path.name)
        for row in rows:
            name = row.filename.rstrip("/")
            safe_name(name)
            if name.casefold() in seen:
                raise AuditFailure("Duplicate archive path: " + name)
            seen.add(name.casefold())
            if row.flag_bits & 1:
                raise AuditFailure("Encrypted archive entry cannot be audited: " + name)
            total += row.file_size
            if row.file_size > 2 * 1024**3 or total > 8 * 1024**3:
                raise AuditFailure("Archive exceeds bounded audit limits")
            if row.is_dir():
                continue
            mode = row.external_attr >> 16
            with archive.open(row) as stream:
                if stat.S_ISLNK(mode):
                    target = stream.read(4097)
                    if len(target) > 4096:
                        raise AuditFailure("Oversized archive symlink")
                    target = target.decode("utf-8")
                    resolved = posixpath.normpath(posixpath.join(posixpath.dirname(name), target))
                    if target.startswith("/") or "\\" in target or resolved == ".." or resolved.startswith("../"):
                        raise AuditFailure("Archive symlink escapes payload: " + name)
                    safe_name(resolved)
                else:
                    if PurePosixPath(name).suffix.casefold() in {".zip", ".7z", ".rar", ".dmg", ".iso"}:
                        raise AuditFailure("Nested archive needs independent review: " + name)
                    inspect_stream(stream, name)


def audit_tree(root: Path, mode="source", review=None, runtime_source=None):
    root = root.resolve()
    if not root.is_dir():
        raise AuditFailure("Audit root must be a directory")
    files = {}
    seen = set()
    runtime_names, runtime_report = set(), None
    if runtime_source:
        if mode != "source":
            raise AuditFailure("Runtime source exception applies only to source audit")
        from runtime_source import validate
        runtime_names, runtime_report = validate(root, runtime_source, AuditFailure)
    for path in sorted(root.rglob("*")):
        name = path.relative_to(root).as_posix()
        safe_name(name, runtime_names=runtime_names)
        if path.is_symlink():
            raise AuditFailure("Filesystem symlinks are not accepted by this packaging audit: " + name)
        if path.is_dir():
            continue
        if not path.is_file() or name.casefold() in seen:
            raise AuditFailure("Unexpected or duplicate file: " + name)
        seen.add(name.casefold())
        if mode == "source":
            if path.suffix.casefold() not in SOURCE_SUFFIXES and path.name != ".gitignore" and not (name in runtime_names and path.suffix == ""):
                raise AuditFailure("Non-source file rejected: " + name)
            if path.stat().st_size > 2 * 1024**2:
                raise AuditFailure("Source file exceeds size bound: " + name)
            try:
                data = path.read_bytes()
                data.decode("utf-8")
                if b"\x00" in data:
                    raise UnicodeError()
            except UnicodeError as exc:
                raise AuditFailure("Non-text content in source export: " + name) from exc
        with path.open("rb") as stream:
            files[name] = inspect_stream(stream, name)
        if mode == "release" and path.suffix.casefold() == ".zip":
            inspect_zip(path)
        elif mode == "release" and path.suffix.casefold() in {".7z", ".rar", ".dmg", ".iso"}:
            raise AuditFailure("Container type is not covered by this audit: " + name)
    if not files:
        raise AuditFailure("Empty export")
    if mode == "release":
        validate_review(review, files)
    canonical = json.dumps(files, sort_keys=True, separators=(",", ":")).encode()
    return {"schemaVersion": 1, "mode": mode, "structuralAuditPassed": True, "runtimeSource": runtime_report,
            "publicDistributionAccepted": mode == "release", "files": files,
            "treeSHA256": hashlib.sha256(canonical).hexdigest(),
            "limits": "Structural/secret/path and explicit review checks; not an independent copyright or semantic resource assessment."}


def validate_review(review, files):
    if not isinstance(review, dict) or review.get("schemaVersion") != 1:
        raise AuditFailure("A reviewed release provenance record is required")
    for key in ("sourceProvenanceReviewed", "originalPackageImportVerified", "publicDistributionAccepted"):
        if review.get(key) is not True:
            raise AuditFailure("Release gate has not passed: " + key)
    if type(review.get("originalContentObjectsBundled")) is not int or review["originalContentObjectsBundled"] != 0:
        raise AuditFailure("Original-content exclusion has not been verified")
    if not re.fullmatch(r"[0-9a-f]{64}", str(review.get("sourceInventorySHA256", ""))):
        raise AuditFailure("Reviewed source inventory hash is required")
    expected = review.get("files")
    if not isinstance(expected, dict) or set(expected) != set(files):
        raise AuditFailure("Review file list does not exactly match this payload")
    for name, actual in files.items():
        row = expected[name]
        if not isinstance(row, dict) or row.get("sha256") != actual["sha256"] or row.get("bytes") != actual["bytes"]:
            raise AuditFailure("Reviewed payload changed: " + name)
        if row.get("classification") not in RELEASE_CLASSES or not row.get("license"):
            raise AuditFailure("Unreviewed content classification/license: " + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--mode", choices=["source", "release"], default="source")
    parser.add_argument("--review", type=Path)
    parser.add_argument("--runtime-source", help="Relative directory with an exact runtime source manifest")
    parser.add_argument("--report", type=Path, help="Keep outside audited root")
    args = parser.parse_args()
    try:
        review = json.loads(args.review.read_text()) if args.review else None
        report = audit_tree(args.root, args.mode, review, args.runtime_source)
        data = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
        if args.report:
            if args.root.resolve() in args.report.resolve().parents:
                raise AuditFailure("Report must be outside audited root")
            args.report.write_text(data, encoding="utf-8")
        print(json.dumps({key: value for key, value in report.items() if key != "files"}, ensure_ascii=False))
    except (AuditFailure, OSError, ValueError, zipfile.BadZipFile) as exc:
        parser.exit(1, str(exc) + "\n")


if __name__ == "__main__":
    main()
