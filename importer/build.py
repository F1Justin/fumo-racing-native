#!/usr/bin/env python3
"""Freeze content-importer on the current OS; never include original resources."""
from __future__ import annotations

import argparse
import ast
import hashlib
import importlib.metadata as metadata
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import tempfile

from packaging.requirements import Requirement

ROOT = Path(__file__).resolve().parent


def check_requirements(path: Path):
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("-r "):
            check_requirements(path.parent / line[3:].strip())
            continue
        requirement = Requirement(line)
        if requirement.marker and not requirement.marker.evaluate():
            continue
        installed = metadata.version(requirement.name)
        if installed not in requirement.specifier:
            raise RuntimeError(f"Build dependency mismatch: {requirement.name} {installed}; expected {requirement.specifier}")


def sha(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024*1024), b""):
            value.update(chunk)
    return value.hexdigest()


def minimum_macos(path):
    if sys.platform != "darwin":
        return None
    result = subprocess.run(["/usr/bin/otool", "-l", str(path)], capture_output=True, text=True)
    versions = re.findall(r"\bminos\s+([0-9.]+)", result.stdout)
    versions += re.findall(r"cmd LC_VERSION_MIN_MACOSX\s+cmdsize \d+\s+version ([0-9.]+)", result.stdout)
    return max(versions, key=lambda item: tuple(map(int, item.split(".")))) if versions else None


def source_hashes():
    return {str(path.relative_to(ROOT)): sha(path) for path in sorted((ROOT/"content_importer").glob("*.py"))}


def smoke(binary: Path):
    with tempfile.TemporaryDirectory(prefix="fumo-frozen-smoke-") as temporary:
        work = Path(temporary)
        original, recipe = work/"original", work/"recipe"
        original.mkdir(); recipe.mkdir()
        (recipe/"recipe.json").write_text('{"schema":1,"status":"draft"}')
        result = subprocess.run([str(binary), "--source", str(original), "--output", str(work/"installation"),
                                 "--recipe", str(recipe), "--json-progress"], capture_output=True, text=True,
                                timeout=60, env={key: value for key, value in os.environ.items()
                                                if not key.startswith(("PYTHON", "_PYI_"))})
        try:
            events = [json.loads(line) for line in result.stdout.splitlines()]
        except ValueError as exc:
            raise RuntimeError("Frozen importer did not produce valid JSON progress") from exc
        if result.returncode != 2 or not events or events[-1].get("type") != "error" or (work/"installation").exists():
            raise RuntimeError("Frozen importer failed draft-rejection smoke: " + result.stderr[-2000:])
        return {"draftRejected": True, "exitCode": result.returncode, "installationCreated": False,
                "realOriginalPackageTested": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        parser.error("Choose a new output directory")
    check_requirements(ROOT/"requirements-build.txt")
    sources_before = source_hashes()
    output.mkdir(parents=True)
    with tempfile.TemporaryDirectory(prefix="fumo-freeze-work-") as temporary:
        work = Path(temporary)
        command = [sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean", "--onefile", "--console", "--noupx",
                   "--name", "content-importer", "--distpath", str(output), "--workpath", str(work/"work"),
                   "--specpath", str(work/"spec"), "--paths", str(ROOT), "--collect-data", "UnityPy",
                   "--hidden-import", "UnityPy.helpers.ResourceReader", "--hidden-import", "PIL.Image",
                   "--exclude-module", "fmod_toolkit", "--exclude-module", "pyfmodex",
                   "--exclude-module", "UnityPy.export.AudioClipConverter", "--exclude-module", "tkinter",
                   str(ROOT/"frozen_entry.py")]
        build_environment = dict(os.environ, PYINSTALLER_CONFIG_DIR=str(work/"cache"))
        subprocess.run(command, cwd=ROOT, check=True, env=build_environment)
        # Analysis includes absolute producer paths. Sanitize this to logical
        # archive names for the distributable inventory; never publish raw TOCs.
        toc_path = work/"work"/"content-importer"/"Analysis-00.toc"
        toc = ast.literal_eval(toc_path.read_text())
        entries = []
        def walk(value):
            if isinstance(value, (list, tuple)):
                if len(value) == 3 and all(isinstance(item, str) for item in value) and value[2] in {"BINARY", "EXTENSION", "DATA", "PYMODULE", "PYSOURCE"}:
                    name, source, kind = value
                    if re.search(r"(^|[/\\])(?:lib)?fmod[^/\\]*\.(?:dylib|dll|so)", name, re.I):
                        raise RuntimeError("An FMOD SDK binary entered the freeze")
                    if name.startswith(("fmod_toolkit", "pyfmodex", "UnityPy.export.AudioClipConverter")):
                        raise RuntimeError("An excluded audio decoder entered the freeze")
                    record = {"name": name, "kind": kind}
                    source_path = Path(source)
                    if source_path.is_file():
                        record["sha256"] = sha(source_path)
                        record["bytes"] = source_path.stat().st_size
                        if kind in {"BINARY", "EXTENSION"} and sys.platform == "darwin":
                            record["minimumMacOS"] = minimum_macos(source_path)
                    entries.append(record)
                else:
                    for child in value:
                        walk(child)
        walk(toc)
    binary = output/("content-importer.exe" if sys.platform == "win32" else "content-importer")
    if source_hashes() != sources_before:
        raise RuntimeError("Importer sources changed during freezing; rerun from a stable source snapshot")
    result = smoke(binary)
    inventory_tool = ROOT.parent/"publish"/"license_inventory.py"
    subprocess.run([sys.executable, str(inventory_tool), "--output", str(output/"THIRD-PARTY-LICENSES")], check=True)
    report = {"schemaVersion": 1, "platform": platform.system(), "architecture": platform.machine(),
              "pythonVersion": sys.version.split()[0], "pyinstallerVersion": metadata.version("PyInstaller"),
              "binary": {"file": binary.name, "bytes": binary.stat().st_size, "sha256": sha(binary)},
              "sourceFiles": sources_before,
              "freezeInputs": sorted(entries, key=lambda row: (row["name"], row["kind"])), "smoke": result,
              "originalAssetsAdded": False, "fmodSDKBundled": False,
              "publicDistributionAccepted": False, "fullReconstructionVerified": False}
    license_root = output/"THIRD-PARTY-LICENSES"
    report["licenseFiles"] = {path.relative_to(license_root).as_posix(): {"sha256": sha(path), "bytes": path.stat().st_size}
                              for path in sorted(license_root.rglob("*")) if path.is_file()}
    if sys.platform == "darwin":
        versions = [row["minimumMacOS"] for row in entries if row.get("minimumMacOS")]
        versions.append(minimum_macos(binary))
        report["minimumMacOS"] = max(filter(None, versions), key=lambda value: tuple(map(int, value.split("."))))
    (output/"importer-build-receipt.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"binary": str(binary), "bytes": binary.stat().st_size, "smoke": result,
                      "publicDistributionAccepted": False}, ensure_ascii=False))


if __name__ == "__main__":
    main()
