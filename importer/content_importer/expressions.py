"""Small data-only recipe language. No eval, Python source, shell or plugins."""

from __future__ import annotations

import base64
import json
import math

from .files import InvalidPackage as ImportFailure

MAX_NODES = 8_000_000
MAX_DEPTH = 128


class Evaluator:
    def __init__(self, originals, locators):
        self.originals = originals
        self.locators = locators
        self.nodes = 0
        self.source_reads = 0

    def evaluate(self, expression, depth=0):
        self.nodes += 1
        if self.nodes > MAX_NODES or depth > MAX_DEPTH:
            raise ImportFailure("资源配方超出处理限制。")
        if not isinstance(expression, list) or not expression:
            raise ImportFailure("资源配方格式无效。")
        op = expression[0]
        if op == "r" and len(expression) == 3:
            index, field = expression[1:]
            if not isinstance(index, int) or isinstance(index, bool) or not 0 <= index < len(self.locators):
                raise ImportFailure("原资源引用超出范围。")
            self.source_reads += 1
            return self.originals.value(self.locators[index], field)
        if op == "v" and len(expression) == 2:
            value = expression[1]
            if value is not None and not isinstance(value, (str, int, float, bool)):
                raise ImportFailure("配方常量类型不正确。")
            if isinstance(value, str) and len(value) > 1024 * 1024:
                raise ImportFailure("配方常量过大。")
            if isinstance(value, float) and not math.isfinite(value):
                raise ImportFailure("配方数值无效。")
            return value
        if op == "f" and len(expression) == 2 and expression[1] in ("+infinity", "-infinity"):
            # Unity uses infinite animation tangents for constant/step curves.
            return float("inf") if expression[1] == "+infinity" else float("-inf")
        if op == "d" and len(expression) == 2:
            result = {}
            if not isinstance(expression[1], list):
                raise ImportFailure("资源配方字段无效。")
            for entry in expression[1]:
                if not isinstance(entry, list) or len(entry) != 2 or not isinstance(entry[0], str) or entry[0] in result:
                    raise ImportFailure("资源配方字段重复或无效。")
                result[entry[0]] = self.evaluate(entry[1], depth + 1)
            return result
        if op == "a" and len(expression) == 2 and isinstance(expression[1], list):
            return [self.evaluate(item, depth + 1) for item in expression[1]]
        if op == "b" and len(expression) == 3:
            # The producer must bind every literal blob to an approved owned or
            # redistributable input. The installer does not infer ownership.
            if not isinstance(expression[2], str) or not expression[2].startswith("approved:"):
                raise ImportFailure("二进制内容缺少来源声明。")
            try:
                return base64.b64decode(expression[1], validate=True)
            except (ValueError, TypeError) as exc:
                raise ImportFailure("配方二进制编码无效。") from exc
        if op == "j" and len(expression) == 3 and expression[2] in ("str", "bytes"):
            value = self.evaluate(expression[1], depth + 1)
            encoded = json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
            return encoded.encode("utf-8") if expression[2] == "bytes" else encoded
        if op == "q" and len(expression) == 4 and expression[3] in ("vertices", "indices"):
            from .sprite_geometry import full_rect_buffers
            values = self.evaluate(expression[1], depth + 1)
            return full_rect_buffers(values["rect"], values["pivot"], values["pixelsPerUnit"],
                                     arithmetic=expression[2])[expression[3]]
        if op == "t" and len(expression) == 4:
            values = self.evaluate(expression[1], depth + 1)
            width, frames = expression[2:]
            if (not isinstance(values, list) or type(width) is not int or not 1 <= width <= 65536
                or type(frames) is not int or not 1 <= frames <= 8 or len(values) < width
                or len(values) % width or len(values) + width * frames > 2_000_000):
                raise ImportFailure("Dense animation padding parameters are invalid.")
            return values + values[-width:] * frames
        if op == "p" and len(expression) == 3 and isinstance(expression[2], list):
            value = self.evaluate(expression[1], depth + 1)
            for change in expression[2]:
                if not isinstance(change, list) or len(change) != 2:
                    raise ImportFailure("资源字段重建步骤无效。")
                field, replacement = change
                if not isinstance(field, list) or not field or len(field) > 128:
                    raise ImportFailure("资源字段重建路径无效。")
                target = value
                try:
                    for part in field[:-1]:
                        target = target[part]
                    part = field[-1]
                    if isinstance(target, list):
                        if type(part) is not int or not 0 <= part < len(target):
                            raise IndexError(part)
                    elif not isinstance(target, dict) or not isinstance(part, str) or part not in target:
                        raise KeyError(part)
                    target[part] = self.evaluate(replacement, depth + 1)
                except (KeyError, TypeError, IndexError) as exc:
                    raise ImportFailure("资源字段重建路径与结构不符。") from exc
            return value
        raise ImportFailure("发行包需要的资源配方版本不受支持。")
