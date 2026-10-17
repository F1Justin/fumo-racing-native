"""Stage a complete installation, then mark it ready after bound game acceptance."""
import argparse
from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat

from audit import AuditFailure, safe_name
from package_payload import ACCEPTANCE, file_identity, json_bytes, read_json, validate_recipe_dir
from runtime_inventory import validate as validate_runtime_inventory


def new_output(output, inputs):
    if output.exists():
        raise AuditFailure("Output must be a new directory")
    for source in inputs:
        source = source.resolve()
        if output == source or output.is_relative_to(source) or source.is_relative_to(output):
            raise AuditFailure("Output overlaps an input")
    output.mkdir(parents=True)


def stage(recipe_dir, runtime_dir, runtime_output, executable, reconstruction_report, runtime_receipt, licenses, output, *, runtime_file_inventory):
    extra = set()
    if reconstruction_report.parent.resolve() == recipe_dir.resolve():
        extra.add(reconstruction_report.name)
    recipe = deepcopy(validate_recipe_dir(recipe_dir, allowed_extra=extra))
    if recipe["status"] != "draft" or recipe.get("runtime", {}).get("files"):
        raise AuditFailure("Stage expects a fresh draft before runtime assembly")
    reconstruction = read_json(reconstruction_report)
    if type(reconstruction.get("unresolvedCount")) is not int or reconstruction["unresolvedCount"] != 0:
        raise AuditFailure("Resource reconstruction still has unresolved objects")
    built = read_json(runtime_receipt)
    if (built.get("role") != "code-only-player-candidate" or built.get("result") != "passed"
            or built.get("platform") != recipe["platform"]):
        raise AuditFailure("Clean runtime build receipt does not match this platform")
    if Path(built.get("player", "")).resolve() != runtime_dir.resolve():
        raise AuditFailure("Runtime build receipt is bound to a different player")
    runtime_inventory, runtime_links = validate_runtime_inventory(runtime_dir, runtime_file_inventory, runtime_receipt, recipe["platform"])
    if not licenses or any(not directory.is_dir() or directory.is_symlink() for directory in licenses):
        raise AuditFailure("Runtime license notice directories are required")
    safe_name(runtime_output)
    safe_name(executable)
    if len(PurePosixPath(runtime_output).parts) != 1 or runtime_output in ("Content", ".fumo-install.json"):
        raise AuditFailure("Choose one runtime output directory name")
    new_output(output, [recipe_dir, runtime_dir, *licenses])
    try:
        for row in recipe["files"]:
            destination = output/row["file"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(recipe_dir/row["file"], destination)
        runtime_files, links, output_names = [], [], set()
        def add_file(source, target, expected=None):
            safe_name(target)
            if target.casefold() in output_names:
                raise AuditFailure("Duplicate runtime output: " + target)
            output_names.add(target.casefold())
            identity = file_identity(source)
            if expected is not None and identity != expected:
                raise AuditFailure("Runtime file changed during assembly")
            # A partial .app inside launcher Resources would be mistaken for
            # nested executable code by macOS signing. Store content-addressed
            # files and restore their paths/links only in the installation.
            name = "runtime/files/"+identity["sha256"]
            destination = output/name
            if not destination.exists():
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, destination)
                recipe["files"].append({"file": name, **identity})
            mode = stat.S_IMODE(source.stat().st_mode)
            if mode & ~0o777:
                raise AuditFailure("Special runtime file permission rejected")
            runtime_files.append({"source": name, "output": target, "mode": mode, "executable": bool(mode & 0o111)})
        if runtime_dir.is_symlink() or not runtime_dir.is_dir():
            raise AuditFailure("Runtime input must be a real directory")
        for source in sorted(runtime_dir.rglob("*")):
            relative = source.relative_to(runtime_dir).as_posix()
            target = runtime_output+"/"+relative
            safe_name(target)
            if source.is_symlink():
                link = os.readlink(source)
                resolved = source.resolve(strict=True)
                if os.path.isabs(link) or "\\" in link or not resolved.is_relative_to(runtime_dir.resolve()):
                    raise AuditFailure("Runtime link escapes the player: " + relative)
                if runtime_links.get(relative) != link:
                    raise AuditFailure("Runtime link changed during assembly")
                if target.casefold() in output_names:
                    raise AuditFailure("Duplicate runtime link")
                output_names.add(target.casefold())
                links.append({"output": target, "target": link})
            elif source.is_file():
                if relative not in runtime_inventory:
                    raise AuditFailure("Runtime file was added during assembly")
                add_file(source, target, runtime_inventory[relative])
            elif not source.is_dir():
                raise AuditFailure("Unexpected runtime input: " + relative)
        if (set(row["output"] for row in runtime_files) != {runtime_output+"/"+name for name in runtime_inventory}
                or set(row["output"] for row in links) != {runtime_output+"/"+name for name in runtime_links}):
            raise AuditFailure("Runtime file or link was removed during assembly")
        for number, directory in enumerate(licenses):
            notice_count = 0
            for source in sorted(directory.rglob("*")):
                if source.is_symlink():
                    raise AuditFailure("License input contains a symlink")
                if source.is_file():
                    if source.suffix.casefold() not in (".txt", ".md", ".json", ".pdf", ""):
                        raise AuditFailure("Unexpected runtime license file")
                    add_file(source, "ThirdPartyNotices/"+str(number)+"/"+source.relative_to(directory).as_posix())
                    notice_count += 1
            if notice_count == 0:
                raise AuditFailure("Runtime license notice directory is empty")
        entry = runtime_output+"/"+executable
        original_executable = runtime_dir/executable
        if not original_executable.resolve(strict=True).is_relative_to(runtime_dir.resolve()) or not original_executable.is_file():
            raise AuditFailure("Runtime executable is outside the player")
        recipe["runtime"] = {"files": runtime_files, "links": links, "executable": entry}
        recipe["buildEvidence"] = {"unresolvedCount": 0,
                                   "reconstructionReportSHA256": file_identity(reconstruction_report)["sha256"],
                                   "runtimeBuildReceiptSHA256": file_identity(runtime_receipt)["sha256"],
                                   "runtimeFileInventorySHA256": file_identity(runtime_file_inventory)["sha256"]}
        recipe["files"].sort(key=lambda row: row["file"])
        (output/"recipe.json").write_bytes(json_bytes(recipe))
        validate_recipe_dir(output)
    except BaseException:
        shutil.rmtree(output)
        raise
    return {"status": "draft", "candidateRecipeSHA256": hashlib.sha256((output/"recipe.json").read_bytes()).hexdigest(),
            "contentId": recipe.get("contentId"), "platform": recipe["platform"], "files": len(recipe["files"])}


def finalize(candidate, verification, output):
    recipe = deepcopy(validate_recipe_dir(candidate))
    if recipe["status"] != "draft" or not recipe.get("runtime", {}).get("files"):
        raise AuditFailure("Acceptance requires an assembled draft installation")
    if (candidate/"recipe.json").read_bytes() != json_bytes(recipe):
        raise AuditFailure("Candidate must use the canonical assembly format")
    tested = read_json(verification)
    candidate_hash = hashlib.sha256((candidate/"recipe.json").read_bytes()).hexdigest()
    if (tested.get("schemaVersion") != 1 or tested.get("candidateRecipeSHA256") != candidate_hash
            or tested.get("contentId") != recipe.get("contentId") or tested.get("platform") != recipe.get("platform")):
        raise AuditFailure("Game verification is not bound to this candidate")
    keys = ("originalPackageImportVerified", "playableGameVerified", "sourceProvenanceReviewed")
    if any(tested.get(key) is not True for key in keys):
        raise AuditFailure("Original import, game play or source review is incomplete")
    for key in ("unresolvedCount", "originalContentObjectsBundled"):
        if type(tested.get(key)) is not int or tested[key] != 0:
            raise AuditFailure("Acceptance failed: " + key)
    acceptance = {key: tested[key] for key in ("schemaVersion", "candidateRecipeSHA256", "contentId", "platform",
                  *keys, "unresolvedCount", "originalContentObjectsBundled", "sourceInventorySHA256")}
    acceptance["verificationReceiptSHA256"] = file_identity(verification)["sha256"]
    new_output(output, [candidate])
    try:
        for row in recipe["files"]:
            destination = output/row["file"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(candidate/row["file"], destination)
        (output/ACCEPTANCE).write_bytes(json_bytes(acceptance))
        # Keep the tested order; removing this final row restores candidate bytes.
        recipe["files"].append({"file": ACCEPTANCE, **file_identity(output/ACCEPTANCE)})
        recipe["status"], recipe["acceptance"] = "ready", ACCEPTANCE
        (output/"recipe.json").write_bytes(json_bytes(recipe))
        validate_recipe_dir(output, require_ready=True)
    except BaseException:
        shutil.rmtree(output)
        raise
    return {"status": "ready", "contentId": recipe.get("contentId"), "files": len(recipe["files"])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    draft = commands.add_parser("stage")
    draft.add_argument("--recipe-dir", type=Path, required=True)
    draft.add_argument("--runtime-dir", type=Path, required=True)
    draft.add_argument("--runtime-output", default="Game.app")
    draft.add_argument("--runtime-executable", required=True, help="Path relative to runtime-dir")
    draft.add_argument("--reconstruction-report", type=Path, required=True)
    draft.add_argument("--runtime-receipt", type=Path, required=True)
    draft.add_argument("--runtime-file-inventory", type=Path, required=True)
    draft.add_argument("--licenses", type=Path, action="append", required=True)
    draft.add_argument("--output", type=Path, required=True)
    ready = commands.add_parser("finalize")
    ready.add_argument("--recipe-dir", type=Path, required=True)
    ready.add_argument("--verification", type=Path, required=True)
    ready.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "stage":
        result = stage(args.recipe_dir.resolve(), args.runtime_dir.resolve(), args.runtime_output, args.runtime_executable,
                       args.reconstruction_report.resolve(), args.runtime_receipt.resolve(), [p.resolve() for p in args.licenses], args.output.resolve(),
                       runtime_file_inventory=args.runtime_file_inventory.resolve())
    else:
        result = finalize(args.recipe_dir.resolve(), args.verification.resolve(), args.output.resolve())
    print(json.dumps(result))


if __name__ == "__main__":
    main()
