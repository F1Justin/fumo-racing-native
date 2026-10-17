"""Bounded input discovery and extraction. Never execute the original game."""

from __future__ import annotations

import hashlib
import os
import shutil
import stat
import zipfile
from pathlib import Path, PurePosixPath

MAX_SOURCE_BYTES = 4 * 1024**3
MAX_FILES = 50000


class ImportFailure(Exception):
    def __init__(self, diagnostic, public_message=None):
        super().__init__(diagnostic)
        self.public_message = public_message or diagnostic


class InvalidPackage(ImportFailure):
    def __init__(self, diagnostic, public_message=None):
        super().__init__(diagnostic, public_message or "安装包校验失败。请重新下载最新发行包后重试；若仍失败，请保留日志并反馈。")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def relative_name(value: str) -> str:
    if not isinstance(value, str) or not value or "\\" in value or ":" in value or "\0" in value:
        raise ImportFailure("安装清单中包含不安全的路径。")
    if value.startswith("/") or any(part in ("", ".", "..") for part in value.split("/")):
        raise ImportFailure("安装清单中包含不安全的路径。")
    if any(part.rstrip(" .") != part for part in value.split("/")):
        raise ImportFailure("文件名在目标系统上不安全。")
    reserved = {"CON", "PRN", "AUX", "NUL", *("COM" + str(i) for i in range(1, 10)),
                *("LPT" + str(i) for i in range(1, 10))}
    if any(part.split(".")[0].upper() in reserved or any(ord(c) < 32 or c in '<>"|?*' for c in part)
           for part in value.split("/")):
        raise ImportFailure("文件名在目标系统上不安全。")
    return PurePosixPath(value).as_posix()


def inside(path: Path, parent: Path) -> bool:
    try:
        path.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def checked_file(root: Path, name: str) -> Path:
    result = root / relative_name(name)
    if not inside(result, root) or result.is_symlink() or not result.is_file():
        raise ImportFailure("缺少必需的文件，或文件指向安装目录之外：" + name)
    return result


def extract_zip(source: Path, destination: Path, *, max_bytes=MAX_SOURCE_BYTES) -> None:
    """Validate every entry before writing; reject links and case aliases."""
    try:
        archive = zipfile.ZipFile(source)
    except (OSError, zipfile.BadZipFile) as exc:
        raise ImportFailure("无法读取 ZIP，请选择完整的原版下载文件。") from exc
    with archive:
        entries = archive.infolist()
        if len(entries) > MAX_FILES or sum(x.file_size for x in entries) > max_bytes:
            raise ImportFailure("压缩包内容超出支持的大小。")
        seen = set()
        for entry in entries:
            name = relative_name(entry.filename.rstrip("/"))
            folded = name.casefold()
            if folded in seen:
                raise ImportFailure("压缩包中有重复或大小写冲突的文件。")
            seen.add(folded)
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode) or stat.S_IFMT(mode) not in (0, stat.S_IFREG, stat.S_IFDIR):
                raise ImportFailure("压缩包包含链接或不支持的文件类型。")
            if entry.flag_bits & 1:
                raise ImportFailure("请使用没有密码保护的官方原版 ZIP。")
        destination.mkdir(parents=True, exist_ok=False)
        written = 0
        try:
            for entry in entries:
                path = destination / relative_name(entry.filename.rstrip("/"))
                if entry.is_dir():
                    path.mkdir(parents=True, exist_ok=True)
                    continue
                path.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as src, path.open("xb") as dst:
                    file_written = 0
                    for block in iter(lambda: src.read(1024 * 1024), b""):
                        written += len(block)
                        file_written += len(block)
                        if written > max_bytes or file_written > entry.file_size:
                            raise ImportFailure("解压内容超过声明大小。")
                        dst.write(block)
                if file_written != entry.file_size:
                    raise ImportFailure("原版 ZIP 不完整，请重新下载。")
        except (OSError, zipfile.BadZipFile, RuntimeError) as exc:
            raise ImportFailure("解压失败，请检查下载文件与磁盘空间。") from exc


def locate_data(source: Path) -> Path:
    """Accept a data directory, its installation, or one enclosing ZIP folder."""
    if not source.is_dir() or source.is_symlink():
        raise ImportFailure("请选择原版 ZIP，或包含游戏文件的文件夹。")
    candidates = []
    frontier = [(source, 0)]
    visited = 0
    while frontier:
        parent, depth = frontier.pop()
        visited += 1
        if visited > MAX_FILES:
            raise ImportFailure("所选目录过大，请直接选择原版游戏文件夹。")
        if (parent / "data.unity3d").is_file() and parent.name == "TouhouFumoRacing_Data":
            candidates.append(parent)
            continue
        if depth >= 3:
            continue
        for child in parent.iterdir():
            if child.is_dir() and not child.is_symlink() and not child.name.startswith("."):
                frontier.append((child, depth + 1))
    if len(candidates) != 1:
        raise ImportFailure("未找到唯一的原版 0.2.1 数据目录，请选择原版游戏文件夹。")
    return candidates[0]


def snapshot_original(data: Path, destination: Path, required: list[dict], progress) -> dict:
    """Copy only supported inputs and verify the copies, closing the read/write race."""
    if not required:
        raise ImportFailure("该发行包尚未配置受支持的原版内容。")
    destination.mkdir(parents=True, exist_ok=False)
    identities = {}
    total = sum(int(row["bytes"]) for row in required)
    completed = 0
    seen = set()
    for row in required:
        name = relative_name(row["file"])
        if name.casefold() in seen:
            raise ImportFailure("原包清单中有重复的路径。")
        seen.add(name.casefold())
        try:
            path = checked_file(data, name)
        except ImportFailure as exc:
            raise ImportFailure(str(exc), "原版游戏文件不完整。请重新下载完整的 0.2.1 ZIP 后导入。") from exc
        if path.stat().st_size != row["bytes"]:
            raise ImportFailure("Original size mismatch: " + name,
                                "原版游戏版本不匹配或文件不完整。请使用完整的 0.2.1 ZIP。")
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        actual = sha256(target)
        accepted = row.get("sha256s", [row.get("sha256")])
        if actual not in accepted:
            raise ImportFailure("Original hash mismatch: " + name,
                                "原版游戏文件与支持的版本不一致。请使用未经修改的官方 0.2.1 ZIP。")
        identities[name] = actual
        completed += row["bytes"]
        progress("validate", completed, total, "正在校验原版游戏…")
    return identities


class InstallLock:
    """OS process lock; automatically released if the importer is terminated."""

    def __init__(self, path: Path):
        self.path = path
        self.stream = None

    def __enter__(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        if self.path.is_symlink():
            raise ImportFailure("安装锁路径不安全。")
        self.stream = self.path.open("a+b")
        try:
            if os.name == "nt":
                import msvcrt
                self.stream.seek(0)
                if not self.stream.read(1):
                    self.stream.write(b"0")
                    self.stream.flush()
                self.stream.seek(0)
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as exc:
            self.stream.close()
            raise ImportFailure("另一个导入任务正在使用这个安装位置。") from exc
        return self

    def __exit__(self, *args):
        if self.stream:
            self.stream.close()
