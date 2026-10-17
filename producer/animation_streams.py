"""Prove that original streamed curves can replace Unity reimported coefficients.

The emitted recipe should reference the original data field; this producer helper
does not emit a copy of the source curve values. Binding order, frame times and
curve indices are exact. Only cubic coefficients may differ by a small verified
floating point amount, and the original curve remains the reconstruction source.
"""
from __future__ import annotations

from copy import deepcopy
import math
import struct


STREAM_PATH = ["m_MuscleClip", "m_Clip", "data", "m_StreamedClip", "data"]


def _float(word):
    return struct.unpack("<f", struct.pack("<I", word))[0]


def _bindings(body):
    bindings = deepcopy(body["m_ClipBindingConstant"]["genericBindings"])
    for binding in bindings:
        if binding.get("isSerializeReferenceCurve", 0) != 0:
            raise ValueError("SerializeReference animation curves are not supported")
        binding.pop("isSerializeReferenceCurve", None)
        pointer = binding.get("script", {})
        if pointer.get("m_PathID", 0):
            raise ValueError("Script-bound curve needs script identity mapping")
    return bindings


def prove_stream_replacement(source, target) -> dict | None:
    if source.type.name != "AnimationClip" or target.type.name != "AnimationClip":
        return None
    original, reference = source.read_typetree(), target.read_typetree()
    if _bindings(original) != _bindings(reference):
        return None
    for key in ("m_SampleRate", "m_Legacy", "m_Compressed", "m_WrapMode"):
        if original[key] != reference[key]:
            return None
    for key in ("m_StartTime", "m_StopTime"):
        if original["m_MuscleClip"][key] != reference["m_MuscleClip"][key]:
            return None
    a = original["m_MuscleClip"]["m_Clip"]["data"]["m_StreamedClip"]
    b = reference["m_MuscleClip"]["m_Clip"]["data"]["m_StreamedClip"]
    if a["curveCount"] != b["curveCount"] or a.get("discreteCurveCount", 0) or b.get("discreteCurveCount", 0):
        return None
    left, right = a["data"], b["data"]
    if len(left) != len(right):
        return None
    cursor, changes, maximum_relative, maximum_absolute = 0, 0, 0.0, 0.0
    curves = {}
    while cursor < len(left):
        if cursor + 2 > len(left) or left[cursor:cursor+2] != right[cursor:cursor+2]:
            return None
        count = left[cursor+1]
        time = _float(left[cursor])
        cursor += 2
        if count > (len(left) - cursor) // 5:
            return None
        for _ in range(count):
            if left[cursor] != right[cursor]:
                return None
            coefficients = []
            for coefficient in range(1, 5):
                x, y = _float(left[cursor+coefficient]), _float(right[cursor+coefficient])
                coefficients.append((x, y))
                if x == y:  # Includes equivalent +0 / -0.
                    continue
                if not (math.isfinite(x) and math.isfinite(y)):
                    return None
                # The authored outgoing slope and value stay exact. Only the
                # first two coefficients are reconstructed during reimport.
                if coefficient > 2:
                    return None
                changes += 1
                difference = abs(x-y)
                maximum_absolute = max(maximum_absolute, difference)
                maximum_relative = max(maximum_relative, difference / max(abs(x), abs(y), 1e-6))
            curves.setdefault(left[cursor], []).append((time, coefficients))
            cursor += 5
    # Prove the maximum difference of the two cubic trajectories over every
    # segment, rather than applying an arbitrary tolerance to high-order
    # coefficients. Values and outgoing slopes were required to stay exact.
    stop = min(original["m_MuscleClip"]["m_StopTime"], reference["m_MuscleClip"]["m_StopTime"])
    maximum_trajectory = 0.0
    for keys in curves.values():
        for index, (time, coefficients) in enumerate(keys):
            da, db, dc, dd = [0.0 if x == y else x-y for x, y in coefficients]
            if not any((da, db, dc, dd)):
                continue
            end = min(keys[index+1][0] if index+1 < len(keys) else stop, stop)
            if not math.isfinite(time) or end < time:
                return None
            duration = end-time
            points = [0.0, duration]
            if da:
                critical = -2*db/(3*da)
                if 0 < critical < duration:
                    points.append(critical)
            bound = max(abs(((da*t+db)*t+dc)*t+dd) for t in points)
            maximum_trajectory = max(maximum_trajectory, bound)
            if bound > 1e-4:
                return None
    return {"sourceField": STREAM_PATH, "wordCount": len(left), "curveCount": a["curveCount"],
            "changedReimportedCoefficients": changes, "maxCoefficientAbsoluteDifference": maximum_absolute,
            "maxCoefficientRelativeDifference": maximum_relative,
            "maximumCubicTrajectoryDifference": maximum_trajectory, "acceptedTrajectoryDifference": 1e-4,
            "bindingOrderFrameTimesIndicesValuesAndOutgoingSlopesExact": True}


DENSE_PATH = ["m_MuscleClip", "m_Clip", "data", "m_DenseClip", "m_SampleArray"]


def prove_dense_padding(source, target) -> dict | None:
    """Recognize only exact prefix preservation and repeated terminal frames."""
    from content_importer.animation import pad_dense_last_frame
    a = source.read_typetree()["m_MuscleClip"]["m_Clip"]["data"]["m_DenseClip"]
    b = target.read_typetree()["m_MuscleClip"]["m_Clip"]["data"]["m_DenseClip"]
    if a == b or not a["m_CurveCount"]:
        return None
    if any(a[key] != b[key] for key in ("m_CurveCount", "m_SampleRate", "m_BeginTime")):
        return None
    frames = b["m_FrameCount"] - a["m_FrameCount"]
    if not 1 <= frames <= 4 or len(a["m_SampleArray"]) != a["m_CurveCount"] * a["m_FrameCount"]:
        return None
    if pad_dense_last_frame(a["m_SampleArray"], a["m_CurveCount"], frames) != b["m_SampleArray"]:
        return None
    return {"sourceField": DENSE_PATH, "targetField": DENSE_PATH, "curveCount": a["m_CurveCount"],
            "appendFrames": frames, "referenceArrayExactlyReproduced": True}


def prove_stream_remapping(source, target) -> dict | None:
    """Prove the project's Image subclass and a scalar curve-index permutation.

    NativeInsanityImage is the project's empty Image subclass. This explicit
    bridge applies only to inherited Image fields; arbitrary scripts and
    multi-component Transform bindings are rejected.
    """
    import json
    from types import SimpleNamespace
    from UnityPy.classes import PPtr
    from content_importer.animation import remap_stream
    a, b = source.read_typetree(), target.read_typetree()
    original_bindings = deepcopy(a["m_ClipBindingConstant"]["genericBindings"])
    reference_bindings = deepcopy(b["m_ClipBindingConstant"]["genericBindings"])
    def canonical(reader, binding):
        binding = deepcopy(binding)
        if binding.pop("isSerializeReferenceCurve", 0) or binding["isPPtrCurve"] or binding["isIntCurve"]:
            raise ValueError("Only ordinary scalar curves can be remapped")
        pointer = binding["script"]
        if pointer["m_PathID"]:
            script = PPtr(m_FileID=pointer["m_FileID"], m_PathID=pointer["m_PathID"], assetsfile=reader.assets_file).deref().read_typetree()
            identity = (script["m_ClassName"], script["m_Namespace"], script["m_AssemblyName"].removesuffix(".dll"))
            if identity not in (("Image", "UnityEngine.UI", "UnityEngine.UI"),
                                ("NativeInsanityImage", "Fumo.NativePlayable", "Assembly-CSharp")):
                raise ValueError("Unproved script class adaptation")
            binding["script"] = "inherited-UnityEngine.UI.Image"
        else:
            binding["script"] = None
        if binding["typeID"] not in (114, 224):
            raise ValueError("Non-scalar binding class")
        return json.dumps(binding, sort_keys=True)
    try:
        left = [canonical(source, binding) for binding in original_bindings]
        right = [canonical(target, binding) for binding in reference_bindings]
    except ValueError:
        return None
    if len(set(left)) != len(left) or sorted(left) != sorted(right):
        return None
    permutation = [right.index(value) for value in left]
    clips = [body["m_MuscleClip"]["m_Clip"]["data"] for body in (a,b)]
    if any(clip["m_StreamedClip"]["curveCount"] != len(left) or clip["m_DenseClip"]["m_CurveCount"] or clip["m_ConstantClip"]["data"] for clip in clips):
        return None
    rewritten = deepcopy(a)
    rewritten["m_MuscleClip"]["m_Clip"]["data"]["m_StreamedClip"]["data"] = remap_stream(clips[0]["m_StreamedClip"]["data"], permutation)
    rewritten["m_ClipBindingConstant"]["genericBindings"] = deepcopy(reference_bindings)
    # Script identities and scalar index layout were proved above. Erase only
    # those pointers in private comparison views, never in the rebuilt asset.
    reference = deepcopy(b)
    for body in (rewritten,reference):
        for binding in body["m_ClipBindingConstant"]["genericBindings"]:
            binding["script"] = {"m_FileID": 0, "m_PathID": 0}
    def view(body):
        return SimpleNamespace(type=SimpleNamespace(name="AnimationClip"), read_typetree=lambda:body)
    proof = prove_stream_replacement(view(rewritten), view(reference))
    if proof is None:
        return None
    return {**proof, "permutation": permutation,
            "scriptBridge": "UnityEngine.UI.Image -> Fumo.NativePlayable.NativeInsanityImage : Image",
            "scalarBindingsAndAuthoredValuesExactAfterRemapping": True}
