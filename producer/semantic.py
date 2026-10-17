"""Build-time matching of original object fields; never a byte-delta fallback.

The source index is private producer state. Exported expressions contain original
object locators and property paths, not the source values themselves.
"""

from __future__ import annotations

import base64
import hashlib
import json
import math
import struct
from collections import Counter


def fingerprint(value, visit=None, path=()):
    """Typed Merkle hash; deterministic across mapping order and machines."""
    digest = hashlib.sha256()
    if value is None:
        digest.update(b"n")
        size = 1
    elif isinstance(value, bool):
        digest.update(b"t" if value else b"f")
        size = 1
    elif isinstance(value, int):
        encoded = str(value).encode("ascii")
        digest.update(b"i" + encoded)
        size = len(encoded)
    elif isinstance(value, float):
        digest.update(b"f" + struct.pack("<d", value))
        size = 8
    elif isinstance(value, str):
        encoded = value.encode("utf-8", "surrogatepass")
        digest.update(b"s" + encoded)
        size = len(encoded)
    elif isinstance(value, (bytes, bytearray)):
        digest.update(b"b" + value)
        size = len(value)
    elif isinstance(value, dict):
        digest.update(b"d")
        size = 2
        for key in sorted(value):
            child, length = fingerprint(value[key], visit, path + (key,))
            digest.update(key.encode("utf-8") + b"\0" + child)
            size += len(key) + length
    elif isinstance(value, (list, tuple)):
        digest.update(b"a")
        size = 2
        for index, item in enumerate(value):
            child, length = fingerprint(item, visit, path + (index,))
            digest.update(child)
            size += length
    else:
        raise TypeError(type(value).__name__)
    key = digest.digest()
    if visit is not None:
        visit(key, value, size, path)
    return key, size


class SourceIndex:
    def __init__(self):
        self.fields = {}
        self.locators = []
        self.by_type_name = {}
        self.statistics = Counter()
        self.errors = []

    def add(self, reader, locator):
        number = len(self.locators)
        self.locators.append(locator)
        try:
            value = reader.read_typetree()
        except Exception as error:
            self.errors.append({"locator": locator, "type": reader.type.name, "error": str(error)})
            return
        name = value.get("m_Name", "") if isinstance(value, dict) else ""
        self.by_type_name.setdefault((reader.type.name, name), []).append(number)
        self.statistics[reader.type.name] += 1

        def remember(key, node, size, field):
            # Small constants/IDs are structural metadata. Index content-bearing
            # subtrees, not each byte of a multi-megabyte vertex array.
            eligible = (isinstance(node, dict) and size >= 12 or
                        isinstance(node, (list, tuple)) and len(node) >= 2 and size >= 16 or
                        isinstance(node, (bytes, bytearray)) and len(node) >= 8 or
                        isinstance(node, str) and len(node) >= 1)
            if eligible:
                self.fields.setdefault(key, (number, list(field), size))

        fingerprint(value, remember)

    def encode(self, value, *, owner=None, context="", unmatched=None):
        if owner and context.rsplit("/", 1)[-1] in ("m_FontData", "compressedBlob") and isinstance(value, list):
            if any(type(byte) is not int or not 0 <= byte <= 255 for byte in value):
                raise ValueError("Font data is not a byte array")
            return ["b", base64.b64encode(bytes(value)).decode("ascii"), "approved:" + owner]
        key, size = fingerprint(value)
        source = self.fields.get(key)
        if source:
            return ["r", source[0], source[1]]
        if isinstance(value, (str, bytes, bytearray)) and len(value) > 256:
            try:
                decoded = json.loads(value)
                if isinstance(decoded, (dict, list)):
                    return ["j", self.encode(decoded, owner=owner, context=context + "/JSON", unmatched=unmatched),
                            "str" if isinstance(value, str) else "bytes"]
            except (ValueError, UnicodeError, TypeError):
                pass
        if isinstance(value, dict):
            return ["d", [[key, self.encode(item, owner=owner, context=context + "/" + key,
                                            unmatched=unmatched)] for key, item in value.items()]]
        if isinstance(value, (list, tuple)):
            if len(value) > 128 and all(isinstance(x, (int, float)) for x in value) and not owner:
                if unmatched is not None:
                    unmatched.append({"field": context, "kind": "numeric-array", "count": len(value), "bytes": size})
                return ["UNRESOLVED", "numeric-array", context]
            return ["a", [self.encode(item, owner=owner, context=context + "/" + str(i),
                                      unmatched=unmatched) for i, item in enumerate(value)]]
        if isinstance(value, (bytes, bytearray)):
            if not value:
                return ["b", "", "approved:empty-structural-field"]
            if owner:
                return ["b", base64.b64encode(value).decode("ascii"), "approved:" + owner]
            if unmatched is not None:
                unmatched.append({"field": context, "kind": "binary", "bytes": len(value)})
            return ["UNRESOLVED", "binary", context]
        if isinstance(value, str) and len(value) > 256 and not owner:
            if unmatched is not None:
                unmatched.append({"field": context, "kind": "text", "bytes": size})
            return ["UNRESOLVED", "text", context]
        if isinstance(value, float) and math.isinf(value) and context.rsplit("/", 1)[-1] in ("inSlope", "outSlope"):
            return ["f", "+infinity" if value > 0 else "-infinity"]
        if isinstance(value, float) and not math.isfinite(value):
            if unmatched is not None:
                unmatched.append({"field": context, "kind": "non-finite-number"})
            return ["UNRESOLVED", "non-finite-number", context]
        return ["v", value]
