"""Staged, recoverable installs. Existing original directories remain read only."""
from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import tempfile

from .files import ImportFailure, InvalidPackage, InstallLock, extract_zip, inside, locate_data, relative_name, snapshot_original
from .recipe import Recipe, read_json
from .rebuild import rebuild_content
from .source import OriginalStore

MARKER = ".fumo-install.json"


def copy_runtime(recipe, destination):
    runtime = recipe.data["runtime"]
    rows = runtime["files"]
    if not rows:
        raise ImportFailure("发行包缺少游戏程序。")
    names = set()
    for row in rows:
        name = relative_name(row["output"])
        if name.casefold() in names or name.split("/")[0] in ("Content", MARKER):
            raise ImportFailure("游戏程序文件路径冲突。")
        names.add(name.casefold())
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(recipe.file(row["source"]), target)
        if "mode" in row:
            mode = row["mode"]
            if type(mode) is not int or not 0 <= mode <= 0o777 or not mode & 0o400:
                raise InvalidPackage("Invalid runtime file mode: " + name)
            target.chmod(mode)
        elif row.get("executable"):
            target.chmod(0o755)
    # Only relative links internal to the installed runtime are allowed.
    for row in runtime.get("links", []):
        name = relative_name(row["output"])
        target = destination / name
        link = row["target"]
        if name.casefold() in names or not isinstance(link, str) or os.path.isabs(link) or "\\" in link:
            raise ImportFailure("游戏程序链接无效。")
        if not inside(target.parent / link, destination):
            raise ImportFailure("游戏程序链接指向安装目录之外。")
        names.add(name.casefold())
        target.parent.mkdir(parents=True, exist_ok=True)
        target.symlink_to(link)
    executable = destination / relative_name(runtime["executable"])
    if not inside(executable, destination) or not executable.is_file():
        raise ImportFailure("游戏启动程序不存在。")
    return runtime["executable"]


def owned_install(path):
    marker = path / MARKER
    return (path.is_dir() and not path.is_symlink() and marker.is_file() and not marker.is_symlink()
            and read_json(marker, 65536).get("installer") == "fumo-content-importer-v1")


def install(source: Path, output: Path, recipe_directory: Path, progress, *, allow_draft=False):
    source = source.expanduser().absolute()
    output = output.expanduser().absolute()
    if output.is_symlink() or output == output.parent:
        raise ImportFailure("请选择普通安装目录。")
    output.parent.mkdir(parents=True, exist_ok=True)
    output = output.parent.resolve() / output.name
    if inside(output, source) or inside(source, output):
        raise ImportFailure("安装位置必须与原版游戏目录分开。")
    backup = output.with_name(output.name + ".previous")
    with InstallLock(output.with_name(output.name + ".import.lock")):
        # A terminated commit can leave the old version in the backup location.
        if not output.exists() and backup.exists():
            if not owned_install(backup):
                raise ImportFailure("安装备份位置已被其他文件占用。")
            backup.rename(output)
        if output.exists() and not owned_install(output):
            raise ImportFailure("安装目录已存在且不是本工具创建的游戏，请选择空位置。")
        if backup.exists():
            if not owned_install(backup):
                raise ImportFailure("安装备份位置已被其他文件占用。")
            shutil.rmtree(backup)
        recipe = Recipe(recipe_directory, progress, allow_draft=allow_draft)
        with tempfile.TemporaryDirectory(prefix=".fumo-import-", dir=output.parent) as temporary:
            work = Path(temporary)
            if source.is_file() and not source.is_symlink():
                progress("extract", 0, 1, "正在解压原版游戏…")
                extract_zip(source, work / "source")
                data = locate_data(work / "source")
            else:
                data = locate_data(source)
            identities = snapshot_original(data, work / "verified-original", recipe.data["originalFiles"], progress)
            originals = OriginalStore(work / "verified-original", list(identities))
            stage = work / "installation"
            stage.mkdir()
            executable = copy_runtime(recipe, stage)
            manifest = rebuild_content(recipe, originals, stage / "Content", progress)
            (stage / MARKER).write_text(json.dumps({"schema": 1, "installer": "fumo-content-importer-v1",
                "contentId": manifest["contentId"], "originals": identities,
                "executable": executable}, ensure_ascii=False, indent=2) + "\n")
            progress("install", 0, 1, "正在完成安装…")
            if output.exists():
                output.rename(backup)
            try:
                stage.rename(output)
            except BaseException:
                if backup.exists() and not output.exists():
                    backup.rename(output)
                raise
        if backup.exists():
            shutil.rmtree(backup)
        progress("install", 1, 1, "导入完成，可以开始游戏。")
        return {"type": "result", "installation": str(output), "executable": str(output / executable),
                "arguments": ["-fumo-content-dir", str(output / "Content")]}
