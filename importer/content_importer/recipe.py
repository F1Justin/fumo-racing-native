"""Load the fixed bundled recipe and verify its complete payload allowlist."""
from __future__ import annotations

import json
import platform
import re
from pathlib import Path

from .files import ImportFailure, checked_file, relative_name, sha256


def read_json(path: Path, limit: int = 64 * 1024 * 1024):
    if path.stat().st_size > limit:
        raise ImportFailure("发行包清单过大。")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ImportFailure("发行包清单包含重复字段。")
            result[key] = value
        return result
    try:
        return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique,
                          parse_constant=lambda _: (_ for _ in ()).throw(ValueError("nonfinite")))
    except (ValueError, UnicodeError, OSError) as exc:
        raise ImportFailure("发行包清单损坏，请重新下载。") from exc


class Recipe:
    def __init__(self, directory: Path, progress=lambda *args: None, *, allow_draft=False):
        self.root = directory.resolve()
        self.data = read_json(checked_file(self.root, "recipe.json"))
        if not isinstance(self.data, dict) or self.data.get("schema") != 1:
            raise ImportFailure("发行包的资源配方版本不受支持。")
        if self.data.get("status") != "ready" and not allow_draft:
            raise ImportFailure("这是尚未完成资源重建验证的开发包，不能用于安装。")
        expected_platform = {"Darwin": "macos", "Windows": "windows", "Linux": "linux"}.get(platform.system())
        if self.data.get("platform") != expected_platform:
            raise ImportFailure("请下载适用于当前系统的发行包。")
        self.files = {}
        rows = self.data.get("files", [])
        if not isinstance(rows, list) or not rows or len(rows) > 50000:
            raise ImportFailure("发行包文件清单无效。")
        aliases = set()
        for index, row in enumerate(rows):
            name = relative_name(row["file"])
            if name.casefold() in aliases or not re.fullmatch(r"[0-9a-f]{64}", row.get("sha256", "")):
                raise ImportFailure("发行包文件清单无效。")
            aliases.add(name.casefold())
            path = checked_file(self.root, name)
            if path.stat().st_size != row.get("bytes") or sha256(path) != row["sha256"]:
                raise ImportFailure("发行包内容校验失败：" + name)
            self.files[name] = row
            progress("package", index + 1, len(rows), "正在检查下载包…")
        originals = self.data.get("originalFiles", [])
        if not isinstance(originals, list) or not originals or len(originals) > 1000:
            raise ImportFailure("发行包缺少受支持原版的校验信息。")
        for row in originals:
            relative_name(row["file"])
            if not isinstance(row.get("bytes"), int) or not 0 <= row["bytes"] <= 4 * 1024**3:
                raise ImportFailure("原版文件清单无效。")
            if not re.fullmatch(r"[0-9a-f]{64}", row.get("sha256", "")):
                raise ImportFailure("原版文件校验值无效。")

    def file(self, name: str) -> Path:
        name = relative_name(name)
        if name not in self.files:
            raise ImportFailure("配方请求了未列入校验清单的文件：" + name)
        return checked_file(self.root, name)
