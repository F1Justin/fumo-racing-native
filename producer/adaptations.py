"""Narrow content transformations for the authored native runtime's components."""
from __future__ import annotations

from copy import deepcopy


LIGHTING_MAP_FIELDS = frozenset({"indices", "probeOcclusionIndices", "occlusionMaskChannels",
                                "bakeTypes", "mixedLightingModes", "isBaked"})


def script_class(reader, body):
    if reader.type.name != "MonoBehaviour":
        return None
    from UnityPy.classes import PPtr
    pointer = body.get("m_Script", {})
    if not pointer.get("m_PathID"):
        return None
    try:
        script = PPtr(m_FileID=pointer["m_FileID"], m_PathID=pointer["m_PathID"], assetsfile=reader.assets_file).deref()
        if script is None or script.type.name != "MonoScript":
            return None
        return script.read_typetree().get("m_ClassName")
    except (KeyError, ValueError, FileNotFoundError):
        return None


def encode_component(index, reader, body, context, unmatched):
    """Return an explicit adaptation or None for ordinary field matching.

    Passive editor source captures are removed. Camera recordings were created
    with NativeCameraLab and are new authored data. Lighting maps contain only
    bounded integer indices/flags needed to bind locally reconstructed assets;
    pixel data, light-probe coefficients and geometry are never covered by that
    rule. The original resource objects still require their normal source proof.
    """
    name = script_class(reader, body)
    if name not in ("NativeMenuPostProcessingSource", "NativeBroadcastAuthoredCamera", "NativeBakedLighting", "NativeMenuPresentation"):
        return None
    body = deepcopy(body)
    evidence = []
    if name == "NativeMenuPostProcessingSource":
        body["layerJson"] = ""
        body["volumes"] = []
        evidence.append("Removed passive editor capture; live PostProcessVolume assets are rebuilt separately.")
    fields = []
    for key, value in body.items():
        owner = None
        if name == "NativeBroadcastAuthoredCamera" and key in ("rawFlightSamples", "rawFlightTimes"):
            owner = "authored-camera-recording:NativeCameraLab"
            evidence.append("NativeCameraLab recording field: " + key)
        structural = name == "NativeBakedLighting" and key in LIGHTING_MAP_FIELDS or name == "NativeMenuPresentation" and key == "raceLightEnabled"
        if structural:
            if not isinstance(value, list) or len(value) > 100000 or any(not isinstance(x, (int, bool)) or not -1 <= x <= 65535 for x in value):
                raise ValueError("Unexpected nonstructural lighting metadata: " + context + "/" + key)
            owner = "reconstruction-metadata:bounded-lighting-indices"
            evidence.append("Bounded lighting index/flag mapping: " + key)
        fields.append([key, index.encode(value, owner=owner, context=context + "/" + key, unmatched=unmatched)])
    return ["d", fields], {"component": name, "evidence": evidence}
