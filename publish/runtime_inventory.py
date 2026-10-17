"""Bind the runtime copied into an installation to its audited build files."""
import os
import re
import stat

from audit import AuditFailure, safe_name
from package_payload import file_identity, read_json


def validate(runtime, inventory_path, build_receipt, platform):
    inventory = read_json(inventory_path)
    kinds = {"code-only-player-file-inventory"}
    if platform == "macos":
        kinds.add("final-macos-code-only-player-candidate")
    if (inventory.get("schema") != 1 or inventory.get("result") != "passed"
            or inventory.get("kind") not in kinds
            or inventory.get("platform", platform) != platform):
        raise AuditFailure("Invalid runtime file inventory")
    packed = inventory.get("packedAssetAudit", {})
    if (packed.get("sha256") != file_identity(build_receipt)["sha256"]
            or type(packed.get("originalContentObjectsBundled")) is not int
            or packed["originalContentObjectsBundled"] != 0):
        raise AuditFailure("Runtime inventory is not bound to the clean build receipt")
    if runtime.is_symlink() or not runtime.is_dir():
        raise AuditFailure("Runtime input must be a real directory")
    prefix, names = runtime.name+"/", set()

    def relative(row):
        name = row.get("path")
        safe_name(name)
        if not name.startswith(prefix):
            raise AuditFailure("Runtime inventory names another player")
        name = name[len(prefix):]
        safe_name(name)
        if name.casefold() in names:
            raise AuditFailure("Duplicate runtime inventory path")
        names.add(name.casefold())
        return name

    rows = inventory.get("playerFiles")
    if not isinstance(rows, list) or not rows or len(rows) > 50000:
        raise AuditFailure("Invalid runtime file list")
    files = {}
    for row in rows:
        name = relative(row)
        if (type(row.get("bytes")) is not int or row["bytes"] < 0
                or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))):
            raise AuditFailure("Invalid runtime file identity")
        files[name] = {"sha256": row["sha256"], "bytes": row["bytes"]}
        if "mode" in row and (type(row["mode"]) is not int or not 0 <= row["mode"] <= 0o777):
            raise AuditFailure("Invalid runtime file mode")
    link_rows = inventory.get("playerLinks", [])
    if not isinstance(link_rows, list) or len(link_rows) > 50000:
        raise AuditFailure("Invalid runtime link list")
    links = {}
    for row in link_rows:
        name, target = relative(row), row.get("target")
        if not isinstance(target, str) or not target or os.path.isabs(target) or "\\" in target or "\0" in target:
            raise AuditFailure("Invalid runtime link target")
        links[name] = target
    actual_files, actual_links = set(), {}
    for path in runtime.rglob("*"):
        name = path.relative_to(runtime).as_posix()
        safe_name(name)
        if path.is_symlink():
            target = os.readlink(path)
            if os.path.isabs(target) or not path.resolve(strict=True).is_relative_to(runtime.resolve()):
                raise AuditFailure("Runtime link escapes the player")
            actual_links[name] = target
        elif path.is_file():
            actual_files.add(name)
            if files.get(name) != file_identity(path):
                raise AuditFailure("Runtime file is unlisted or changed: " + name)
        elif not path.is_dir():
            raise AuditFailure("Unexpected runtime filesystem entry")
    if actual_files != set(files) or actual_links != links:
        raise AuditFailure("Runtime inventory does not exactly cover files and links")
    for row in rows:
        if "mode" in row and stat.S_IMODE((runtime/row["path"][len(prefix):]).stat().st_mode) != row["mode"]:
            raise AuditFailure("Runtime file permissions changed")
    return files, links
