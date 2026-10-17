#!/usr/bin/env python3
"""Export only the explicit distribution source allowlist into a fresh directory."""
import argparse
import json
from pathlib import Path
import shutil
import sys

from audit import AuditFailure, audit_tree, safe_name

HERE = Path(__file__).resolve().parent


def export(source: Path, output: Path, policy: Path):
    source, output = source.resolve(), output.resolve()
    if output == source or source in output.parents:
        raise AuditFailure("Public export must be outside the distribution source tree")
    if output.exists():
        raise AuditFailure("Public export destination must not already exist")
    rules = json.loads(policy.read_text(encoding="utf-8"))
    if rules.get("schemaVersion") != 1 or not rules.get("patterns"):
        raise AuditFailure("Invalid allowlist")
    candidates = {}
    for pattern in rules["patterns"]:
        if pattern.startswith("/") or ".." in pattern.split("/") or "**" in pattern:
            raise AuditFailure("Unbounded allowlist rule rejected")
        for path in source.glob(pattern):
            if not path.is_file() or path.is_symlink():
                raise AuditFailure("Allowlisted path is not a regular file")
            relative = path.relative_to(source).as_posix()
            safe_name(relative)
            if source not in path.resolve().parents:
                raise AuditFailure("Allowlisted path escapes source")
            candidates[relative] = path
    if not candidates:
        raise AuditFailure("Nothing matched the allowlist")
    output.mkdir(parents=True)
    # A failure deliberately leaves the isolated directory for inspection. The
    # caller must not publish it without a successful audit and external gates.
    for name, path in sorted(candidates.items()):
        destination = output / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)
    (output / ".gitignore").write_text("dist/\nbuild/\n__pycache__/\n*.pyc\n.venv/\n.private/\nlocal/\n*.seed\n*.p12\n*.pfx\n.DS_Store\n", encoding="utf-8")
    if not (output / "README.md").exists():
        (output / "README.md").write_text(
            "# Fumo Racing Native distribution tools\n\n"
            "This repository contains the resource importer, launcher, and source-only distribution tooling.\n"
            "Players must supply their own original game. No original game resources or playable game package are included.\n\n"
            "Current status: development. A completed original-package reconstruction and distribution review are required before a playable release.\n\n"
            "See [launcher](launcher/README.md) and [publishing](publish/README.md).\n", encoding="utf-8")
    return audit_tree(output, "source")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=HERE.parent)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--allowlist", type=Path, default=HERE / "allowlist.json")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    try:
        report = export(args.source, args.output, args.allowlist)
        if args.report:
            if args.output.resolve() in args.report.resolve().parents:
                raise AuditFailure("Report must remain outside exported source")
            args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
        print(json.dumps({"output": str(args.output.resolve()), "files": len(report["files"]), "treeSHA256": report["treeSHA256"],
                          "structuralAuditPassed": True, "runtimeIncluded": False, "gitHistoryCopied": False,
                          "publicDistributionAccepted": False}, ensure_ascii=False))
    except (AuditFailure, OSError, ValueError) as exc:
        parser.exit(1, str(exc)+"\n")


if __name__ == "__main__":
    main()
