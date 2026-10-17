#!/usr/bin/env python3
"""Collect installed Python dependency identities and upstream license files."""
import argparse
import hashlib
import importlib.metadata as metadata
import json
from pathlib import Path
import re
import shutil
import sys
import sysconfig

from packaging.requirements import Requirement


def collect(output: Path, roots):
    output.mkdir(parents=True, exist_ok=False)
    queue = list(roots)
    found = {}
    while queue:
        distribution = metadata.distribution(queue.pop())
        name = distribution.metadata["Name"]
        key = name.casefold().replace("_", "-")
        if key in found:
            continue
        dependencies = []
        for value in distribution.requires or []:
            requirement = Requirement(value)
            if requirement.marker is None or requirement.marker.evaluate({"extra": ""}):
                queue.append(requirement.name)
                dependencies.append(str(requirement))
        licenses = []
        for relative in distribution.files or []:
            parts = Path(str(relative)).parts
            basename = parts[-1].casefold()
            if not any(".dist-info" in part for part in parts):
                continue
            if not (basename.startswith(("license", "licence", "copying", "notice")) or "licenses" in parts):
                continue
            original = Path(distribution.locate_file(relative))
            if not original.is_file() or original.stat().st_size > 2 * 1024**2:
                continue
            target = output / re.sub(r"[^a-zA-Z0-9._-]", "_", name) / (str(len(licenses)) + "-" + original.name)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
            licenses.append({"file": target.relative_to(output).as_posix(), "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
        found[key] = {"name": name, "version": distribution.version,
                      "licenseExpression": distribution.metadata.get("License-Expression"),
                      "metadataLicense": distribution.metadata.get("License"), "licenseFiles": licenses,
                      "dependencies": dependencies,
                      "homepage": distribution.metadata.get("Home-page"),
                      "reviewStatus": "metadata-collected-not-a-rights-approval"}
    python_licenses = [Path(sys.base_prefix)/"LICENSE.txt", Path(sys.base_prefix)/"LICENSE", Path(sysconfig.get_path("stdlib"))/"LICENSE.txt"]
    license_path = next((path for path in python_licenses if path.is_file()), None)
    if license_path:
        shutil.copyfile(license_path, output / "Python-LICENSE.txt")
    report = {"schemaVersion": 1, "pythonVersion": sys.version.split()[0], "pythonLicenseCopied": bool(license_path),
              "rootPackages": list(roots), "packages": [found[key] for key in sorted(found)],
              "publicDistributionAccepted": False,
              "scope": "Installed declared dependency graph; actual frozen-module/binary inventory must also be reviewed."}
    (output / "dependency-inventory.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--root", action="append", default=[])
    args = parser.parse_args()
    result = collect(args.output, args.root or ["UnityPy", "PyInstaller"])
    print(json.dumps({"packages": len(result["packages"]), "pythonLicenseCopied": result["pythonLicenseCopied"], "publicDistributionAccepted": False}))


if __name__ == "__main__":
    main()
