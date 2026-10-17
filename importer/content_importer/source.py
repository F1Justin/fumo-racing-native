"""Verified original Unity data. Object IDs always refer to a specific input file."""

from __future__ import annotations

from collections import OrderedDict
from copy import deepcopy
from pathlib import Path

from .files import InvalidPackage as ImportFailure, checked_file


class OriginalStore:
    def __init__(self, root: Path, input_files: list[str]):
        import UnityPy
        if UnityPy.__version__ != "1.25.0":
            raise ImportFailure("资源导入组件版本不匹配，请重新下载完整发行包。")
        self.root = root
        self.environment = UnityPy.Environment()
        self.locations = {}
        self.reader_locations = {}
        self._trees = OrderedDict()
        for name in input_files:
            path = checked_file(root, name)
            # Load only the verified Unity containers, never executables or DLLs.
            if not (name.endswith(".bundle") or name == "data.unity3d"):
                continue
            loaded = self.environment.load_file(str(path))
            if loaded is None or not hasattr(loaded, "get_assets"):
                raise ImportFailure("无法读取经过校验的原版资源容器。")
            for serialized in loaded.get_assets():
                key = (name, serialized.name)
                if key in self.locations:
                    raise ImportFailure("原版资源文件出现重复标识。")
                self.locations[key] = serialized
                for reader in serialized.objects.values():
                    self.reader_locations[id(reader)] = [name, serialized.name, reader.path_id]
        if not self.locations:
            raise ImportFailure("未能读取原版游戏资源。")

    def reader(self, locator):
        if not isinstance(locator, list) or len(locator) != 3:
            raise ImportFailure("资源配方的原版对象标识不正确。")
        try:
            return self.locations[(locator[0], locator[1])].objects[int(locator[2])]
        except (KeyError, ValueError, TypeError) as exc:
            raise ImportFailure("原包中缺少配方指定的对象。") from exc

    def objects(self):
        for file in self.locations.values():
            yield from file.objects.values()

    def tree(self, locator):
        key = tuple(locator)
        if key not in self._trees:
            self._trees[key] = self.reader(locator).read_typetree()
            # Keep scene import memory bounded without reparsing hot objects.
            while len(self._trees) > 128:
                self._trees.popitem(last=False)
        self._trees.move_to_end(key)
        return self._trees[key]

    def value(self, locator, field):
        if not isinstance(field, list) or len(field) > 128:
            raise ImportFailure("资源字段路径不正确。")
        value = self.tree(locator)
        try:
            for part in field:
                if not isinstance(part, (str, int)) or isinstance(part, bool):
                    raise KeyError(part)
                value = value[part]
        except (KeyError, IndexError, TypeError) as exc:
            raise ImportFailure("原版资源字段与支持版本不一致。") from exc
        return deepcopy(value)
