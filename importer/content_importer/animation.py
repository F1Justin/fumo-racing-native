"""Rebuild animation array layouts using only the user's original curve data."""
from __future__ import annotations


def remap_stream(words: list[int], permutation: list[int]) -> list[int]:
    """Reorder scalar streamed curves without changing their authored samples."""
    if sorted(permutation) != list(range(len(permutation))):
        raise ValueError("Animation curve mapping is not a permutation")
    result, cursor = [], 0
    while cursor < len(words):
        if cursor + 2 > len(words):
            raise ValueError("Truncated animation frame")
        time, count = words[cursor:cursor+2]
        cursor += 2
        if count > (len(words)-cursor)//5:
            raise ValueError("Truncated animation keys")
        keys = []
        for _ in range(count):
            index = words[cursor]
            if not 0 <= index < len(permutation):
                raise ValueError("Animation curve index outside mapping")
            keys.append([permutation[index], *words[cursor+1:cursor+5]])
            cursor += 5
        if len({key[0] for key in keys}) != len(keys):
            raise ValueError("Duplicate animation curve in one frame")
        result.extend((time, count))
        for key in sorted(keys, key=lambda item: item[0]):
            result.extend(key)
    return result


def pad_dense_last_frame(samples: list[float], curve_count: int, append_frames: int) -> list[float]:
    if curve_count <= 0 or not samples or len(samples) % curve_count or not 0 <= append_frames <= 4:
        raise ValueError("Invalid dense animation tail padding")
    return [*samples, *(samples[-curve_count:] * append_frames)]
