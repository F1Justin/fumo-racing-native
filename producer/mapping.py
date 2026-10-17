"""Private build-time provenance matching; names only narrow the search.

No source asset data is written to this module or a report. A successful match
requires content evidence, and different equally plausible sources remain
ambiguous. This does not grant redistribution rights for a matched asset.
"""
from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, field
import hashlib
from pathlib import Path
import re
import struct
from typing import Any


SUPPORTED_TYPES = frozenset({"Texture2D", "Mesh", "AudioClip", "Font",
                             "AnimationClip", "Sprite", "Shader"})


def digest(value: Any) -> str:
    """Unambiguous structural digest, independent of dictionary insertion order."""
    result = hashlib.sha256()
    def visit(item):
        if item is None:
            result.update(b"n")
        elif isinstance(item, bool):
            result.update(b"t" if item else b"f")
        elif isinstance(item, int):
            encoded = str(item).encode()
            result.update(b"i" + struct.pack("<Q", len(encoded)) + encoded)
        elif isinstance(item, float):
            result.update(b"d" + struct.pack("<d", item))
        elif isinstance(item, (bytes, bytearray, memoryview)):
            result.update(b"b" + struct.pack("<Q", len(item)) + item)
        elif isinstance(item, str):
            encoded = item.encode("utf-8", "surrogatepass")
            result.update(b"s" + struct.pack("<Q", len(encoded)) + encoded)
        elif isinstance(item, dict):
            result.update(b"o" + struct.pack("<Q", len(item)))
            for key in sorted(item):
                visit(key)
                visit(item[key])
        elif isinstance(item, (tuple, list)):
            result.update(b"a" + struct.pack("<Q", len(item)))
            for child in item:
                visit(child)
        else:
            raise TypeError(f"Unsupported fingerprint value: {type(item).__name__}")
    visit(value)
    return result.hexdigest()


def stream_bytes(reader, tree=None) -> bytes:
    """Return logical asset bytes, not a resource filename/offset fingerprint."""
    tree = tree or reader.read_typetree()
    typ = reader.type.name
    if typ == "AudioClip":
        resource = tree.get("m_Resource", {})
        path, offset, size = (resource.get("m_Source", ""),
                              resource.get("m_Offset", 0), resource.get("m_Size", 0))
    else:
        stream = tree.get("m_StreamData", {})
        path, offset, size = stream.get("path", ""), stream.get("offset", 0), stream.get("size", 0)
    if size:
        from UnityPy.helpers.ResourceReader import get_resource_data
        value = bytes(get_resource_data(path, reader.assets_file, offset, size))
        if len(value) != size:
            raise ValueError("Truncated asset resource")
        return value
    if typ == "Texture2D":
        return bytes(tree.get("image data", b""))
    if typ == "Mesh":
        return bytes(tree["m_VertexData"].get("m_DataSize", b""))
    return bytes(tree.get("m_AudioData", b""))


def name_of(reader) -> str:
    name = reader.peek_name() or ""
    if not name and reader.type.name == "Shader":
        name = reader.read_typetree().get("m_ParsedForm", {}).get("m_Name", "")
    return name


def _pointer(reader, pointer):
    if not pointer.get("m_PathID"):
        return None
    from UnityPy.classes import PPtr
    return PPtr(m_FileID=pointer["m_FileID"], m_PathID=pointer["m_PathID"],
                assetsfile=reader.assets_file).deref()


def _without_pointers(value):
    """Reject live PPtrs; do not silently erase a dependency for a match."""
    if isinstance(value, dict):
        if set(value) == {"m_FileID", "m_PathID"}:
            if value["m_PathID"]:
                raise ValueError("Live pointer requires dependency provenance")
            return None
        return {key: _without_pointers(child) for key, child in value.items()}
    if isinstance(value, (list, tuple)):
        return [_without_pointers(child) for child in value]
    return value


def _animation_body(reader):
    from copy import deepcopy
    body = deepcopy(reader.read_typetree())
    for key in ("m_Name", "m_Bounds", "m_MuscleClipSize"):
        body.pop(key, None)
    bindings = body.get("m_ClipBindingConstant", {}).get("genericBindings", [])
    for binding in bindings:
        if binding.get("isSerializeReferenceCurve") == 0:
            binding.pop("isSerializeReferenceCurve")
    stream = body.get("m_MuscleClip", {}).get("m_Clip", {}).get("data", {}).get("m_StreamedClip")
    if stream is not None:
        if stream.get("discreteCurveCount") == 0:
            stream.pop("discreteCurveCount")
        words = stream["data"]
        frames, cursor = [], 0
        while cursor < len(words):
            if cursor + 2 > len(words):
                raise ValueError("Truncated streamed animation frame")
            time = struct.unpack("<f", struct.pack("<I", words[cursor]))[0]
            count = words[cursor + 1]
            cursor += 2
            if count > (len(words) - cursor) // 5:
                raise ValueError("Invalid streamed animation key count")
            keys = []
            for _ in range(count):
                keys.append({"index": words[cursor], "coefficients": words[cursor+1:cursor+5]})
                cursor += 5
            frames.append({"time": time, "keys": keys})
        stream["data"] = frames
    return _without_pointers(body)


def compare_animation(source, target) -> dict | None:
    """Compare typed curve data; allow <= 4 ULP in recomputed tangent coefficients.

    Stream indices/counts and frame times remain exact. This avoids treating an
    integer word difference as harmless floating point noise. The source curves
    can be retained by a recipe even when Unity reimport recomputed the tangents.
    """
    left, right = _animation_body(source), _animation_body(target)
    rounding = []
    def equal(a, b, path=()):
        if type(a) is not type(b):
            return False
        if isinstance(a, dict):
            return a.keys() == b.keys() and all(equal(a[key], b[key], path + (key,)) for key in a)
        if isinstance(a, list):
            return len(a) == len(b) and all(equal(x, y, path + (index,)) for index, (x, y) in enumerate(zip(a, b)))
        if a == b:
            return True
        if isinstance(a, int) and len(path) > 1 and path[-2] == "coefficients":
            # Same sign prevents wraparound and sign-bit differences. The
            # streamed words encode IEEE-754 float coefficients at this point.
            if (a >> 31) == (b >> 31) and abs(a - b) <= 4 and (a & 0x7F800000) != 0x7F800000:
                rounding.append(abs(a - b))
                return True
        return False
    if equal(left, right):
        return {"rounded_coefficient_count": len(rounding), "max_coefficient_ulp": max(rounding, default=0)}
    return None


def asset_signatures(reader, *, decoded_texture=False) -> dict[str, str]:
    """Content proofs usable across Unity versions, with narrow explicit scopes.

    Shader code is deliberately not identified by name or parsed interface:
    platform compilation and license provenance need an independent owner rule.
    """
    typ, body = reader.type.name, reader.read_typetree()
    found = {}
    if typ == "Texture2D":
        payload = stream_bytes(reader, body)
        shape = [body["m_Width"], body["m_Height"], body["m_TextureFormat"],
                 body.get("m_MipCount", 1)]
        if payload:
            found["texture-encoded"] = digest([shape, payload])
            if decoded_texture:
                try:
                    image = reader.read().image.convert("RGBA")
                    found["texture-pixels"] = digest([list(image.size), image.tobytes()])
                except (ValueError, NotImplementedError):
                    return found
                import numpy as np
                rgba = np.asarray(image)
                transparent = rgba[:, :, 3] == 0
                if np.any(transparent) and not np.all(transparent):
                    # Sprite import may dilate RGB under alpha=0. Preserve
                    # every visible pixel and every alpha value in the proof.
                    visible = rgba.copy()
                    visible[transparent] = 0
                    found["texture-visible-pixels"] = digest([list(image.size), visible.tobytes()])
                if body["m_TextureFormat"] in {12, 29}:
                    # DXT5nm stores normal X in alpha and Y in green. The
                    # exporter reconstructs positive Z when writing a PNG.
                    if np.all(rgba[:, :, 0] == 255):
                        x = rgba[:, :, 3].astype(np.float32) / 255 * 2 - 1
                        y = rgba[:, :, 1].astype(np.float32) / 255 * 2 - 1
                        z = (np.sqrt(np.maximum(0, 1 - x*x - y*y)) * 127.5 + 127.5).astype(np.uint8)
                        normal = np.stack((rgba[:, :, 3], rgba[:, :, 1], z,
                                           np.full(z.shape, 255, dtype=np.uint8)), axis=2)
                        found["texture-normal-unpacked"] = digest([list(image.size), normal.tobytes()])
        else:
            # An empty atlas is not image-content proof. Its owner should create
            # a documented empty texture, never choose an arbitrary empty source.
            found["empty-texture-shape"] = digest(shape)
    elif typ == "Mesh":
        vertex = body["m_VertexData"]
        payload = stream_bytes(reader, body)
        compressed_vertices = body.get("m_CompressedMesh", {}).get("m_Vertices", {}).get("m_NumItems", 0)
        if payload or body.get("m_IndexBuffer") or compressed_vertices:
            if payload or body.get("m_IndexBuffer"):
                found["mesh-buffers"] = digest([payload, bytes(body["m_IndexBuffer"])])
            geometry = {key: body[key] for key in (
                "m_SubMeshes", "m_Shapes", "m_BindPose", "m_BoneNameHashes",
                "m_RootBoneNameHash", "m_BonesAABB", "m_VariableBoneCountWeights",
                "m_IndexFormat", "m_CompressedMesh", "m_LocalAABB") if key in body}
            geometry["vertex"] = {key: value for key, value in vertex.items() if key != "m_DataSize"}
            found["mesh-geometry"] = digest([geometry, payload, bytes(body["m_IndexBuffer"])])
    elif typ == "AudioClip":
        payload = stream_bytes(reader, body)
        if payload:
            found["audio-encoded"] = digest([body.get("m_Channels"), body.get("m_Frequency"), payload])
    elif typ == "Font":
        payload = bytes(body.get("m_FontData", []))
        if payload:
            found["font-file"] = hashlib.sha256(payload).hexdigest()
    elif typ == "AnimationClip":
        # A same-name idle/run clip is not evidence. Curves, timing, bindings and
        # events must match, and a live PPtr prevents this shortcut entirely.
        keys = ("m_Legacy", "m_Compressed", "m_UseHighQualityCurve", "m_RotationCurves",
                "m_CompressedRotationCurves", "m_EulerCurves", "m_PositionCurves",
                "m_ScaleCurves", "m_FloatCurves", "m_PPtrCurves", "m_SampleRate",
                "m_WrapMode", "m_MuscleClip", "m_ClipBindingConstant",
                "m_HasGenericRootTransform", "m_HasMotionFloatCurves", "m_Events")
        found["animation-curves"] = digest(_without_pointers({key: body[key] for key in keys if key in body}))
    elif typ == "Sprite":
        render = body["m_RD"]
        textures = {}
        for key in ("texture", "alphaTexture"):
            texture = _pointer(reader, render.get(key, {"m_PathID": 0}))
            if texture is not None:
                signatures = asset_signatures(texture, decoded_texture=True)
                proof = signatures.get("texture-visible-pixels", signatures.get("texture-pixels"))
                if proof is None:
                    raise ValueError("Sprite texture has no image evidence")
                textures[key] = proof
        geometry = {key: value for key, value in render.items()
                    if key not in ("texture", "alphaTexture", "secondaryTextures")}
        # Packing may differ; exact render geometry is intentionally strict.
        fields = {key: body[key] for key in ("m_Rect", "m_Offset", "m_Border", "m_PixelsToUnits",
                 "m_Pivot", "m_Extrude", "m_IsPolygon", "m_PhysicsShape", "m_Bones") if key in body}
        found["sprite-geometry-texture"] = digest([fields, _without_pointers(geometry), textures])
    return found


def exported_asset_signatures(path: Path, type_name: str) -> dict[str, str]:
    """Use private exported inputs to bridge importer changes in the target.

    PNG pixels and mesh YAML buffers are compared with original *content*, not
    filenames. Audio requires a separate codec-aware proof and is not guessed.
    """
    if type_name == "Texture2D" and path.suffix.lower() in {".png", ".jpg", ".jpeg", ".tga", ".bmp"}:
        from PIL import Image
        with Image.open(path) as source:
            source = source.convert("RGBA")
            result = {"texture-pixels": digest([list(source.size), source.tobytes()])}
            metadata = path.with_suffix(path.suffix + ".meta")
            if metadata.is_file() and re.search(r"^  textureType: 1$", metadata.read_text(), re.M):
                result["texture-normal-unpacked"] = result["texture-pixels"]
            return result
    if type_name == "Mesh" and path.suffix.lower() == ".asset":
        data = path.read_text()
        index = re.search(r"^  m_IndexBuffer: ([a-fA-F0-9]*)$", data, re.M)
        vertex = re.search(r"^    _typelessdata: ([a-fA-F0-9]*)$", data, re.M)
        # Only single-object native Mesh assets are accepted by this helper.
        if data.count("--- !u!43 ") == 1 and index and vertex:
            return {"mesh-buffers": digest([bytes.fromhex(vertex[1]), bytes.fromhex(index[1])])}
    if type_name == "Font" and path.suffix.lower() in {".ttf", ".otf"}:
        return {"font-file": hashlib.sha256(path.read_bytes()).hexdigest()}
    return {}


@dataclass
class Match:
    status: str
    type_name: str
    name: str
    source: list | None = None
    proof: list[str] = field(default_factory=list)
    candidates: list[list] = field(default_factory=list)
    equivalent_sources: list[list] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    details: dict = field(default_factory=dict)
    adaptation: dict | None = None

    def as_dict(self):
        return asdict(self)


class SourceObjectIndex:
    """Content matcher over OriginalStore with bounded typetree/pixel retention."""
    def __init__(self, store):
        self.store = store
        self.by_type = defaultdict(list)
        self.by_name = defaultdict(list)
        self.by_container = defaultdict(list)
        self._signatures = {}
        self._errors = {}
        self._equivalence = {}
        for reader in store.objects():
            typ = reader.type.name
            if typ in SUPPORTED_TYPES:
                self.by_type[typ].append(reader)
                self.by_name[(typ, name_of(reader))].append(reader)
        for serialized in store.locations.values():
            for path, pointer in serialized.container.items():
                try:
                    reader = pointer.deref()
                    self.by_container[path.casefold()].append(reader)
                except (ValueError, KeyError, FileNotFoundError):
                    pass

    def locator(self, reader):
        return self.store.reader_locations[id(reader)]

    def signatures(self, reader, *, pixels=False):
        key = (id(reader), pixels)
        if key not in self._signatures:
            try:
                self._signatures[key] = asset_signatures(reader, decoded_texture=pixels)
            except Exception as exc:
                self._errors[key] = f"{type(exc).__name__}: {exc}"
                self._signatures[key] = {}
        return self._signatures[key]

    def _content_equivalence(self, reader):
        """Hash all source fields, except relocated streams and the asset label."""
        key = id(reader)
        if key not in self._equivalence:
            from copy import deepcopy
            body = deepcopy(reader.read_typetree())
            body.pop("m_Name", None)
            typ = reader.type.name
            if typ in {"Texture2D", "Mesh", "AudioClip"}:
                payload = stream_bytes(reader, body)
                body.pop("m_StreamData", None)
                body.pop("m_Resource", None)
                if typ == "Texture2D":
                    body["image data"] = payload
                elif typ == "Mesh":
                    body["m_VertexData"]["m_DataSize"] = payload
                else:
                    body["source_payload"] = payload
            # Do not equate pointer-bearing bodies based on raw path IDs in
            # different files. Such duplicate matches remain ambiguous.
            try:
                self._equivalence[key] = digest(_without_pointers(body))
            except ValueError:
                self._equivalence[key] = f"unresolved-pointers:{self.locator(reader)}"
        return self._equivalence[key]

    def match(self, target, *, exported_asset: Path | None = None,
              search_all: bool = True, audio_ffmpeg: Path | None = None) -> Match:
        typ, name = target.type.name, name_of(target)
        result = Match("unmatched", typ, name)
        if typ == "Shader":
            result.status = "requires-owned-platform-shader"
            result.candidates = [self.locator(item) for item in self.by_name[(typ, name)]]
            return result
        if typ not in SUPPORTED_TYPES:
            result.status = "unsupported"
            return result
        if typ == "AnimationClip":
            return self.animation_stream_source(target)
        if typ == "Sprite":
            adapted = self.sprite_geometry_source(target)
            if adapted.status == "source-geometry-proved":
                adapted.status = "matched"
                return adapted
        proofs = {}
        try:
            proofs.update(asset_signatures(target, decoded_texture=typ == "Texture2D"))
        except Exception as exc:
            result.errors.append(f"target: {type(exc).__name__}: {exc}")
        if exported_asset is not None:
            try:
                proofs.update(exported_asset_signatures(exported_asset, typ))
            except Exception as exc:
                result.errors.append(f"export: {type(exc).__name__}: {exc}")
        proofs.pop("empty-texture-shape", None)
        named = self.by_name[(typ, name)]
        result.candidates = [self.locator(item) for item in named]
        if not proofs:
            if typ == "AudioClip":
                result.status = "requires-audio-content-proof"
            return result
        matches = []
        scopes = [named]
        if search_all:
            named_ids = {id(item) for item in named}
            scopes.append([item for item in self.by_type[typ] if id(item) not in named_ids])
        # Search beyond same-name hits as well: a different-name duplicate may
        # have distinct settings and must not become an unreported ambiguity.
        for candidates in scopes:
            for candidate in candidates:
                signature = self.signatures(candidate, pixels="texture-pixels" in proofs)
                equal = sorted(key for key, value in proofs.items() if signature.get(key) == value)
                if equal:
                    matches.append((candidate, equal))
        if not matches:
            if typ == "AudioClip" and exported_asset is not None and audio_ffmpeg is not None:
                return self.match_audio_export(target, exported_asset, audio_ffmpeg)
            if typ == "AnimationClip":
                verified = []
                for source in self.by_type[typ]:
                    try:
                        proof = compare_animation(source, target)
                        if proof is not None:
                            verified.append((source, proof))
                    except ValueError:
                        continue
                if len(verified) == 1:
                    result.status, result.source = "matched", self.locator(verified[0][0])
                    result.proof = ["animation-exact-structure-bounded-tangent-rounding"]
                    result.details = verified[0][1]
                    result.candidates = [result.source]
                elif len(verified) > 1:
                    result.status = "ambiguous"
                    result.candidates = [self.locator(source) for source, _ in verified]
            return result
        result.candidates = [self.locator(item) for item, _ in matches]
        # Stronger geometry proof outranks matching only a vertex/index subset.
        if typ == "Mesh" and any("mesh-geometry" in proof for _, proof in matches):
            matches = [(item, proof) for item, proof in matches if "mesh-geometry" in proof]
            result.candidates = [self.locator(item) for item, _ in matches]
        # A container path or exact label may select between content-confirmed
        # duplicates. Neither is ever accepted without the content proof above.
        if len(matches) > 1 and exported_asset is not None and "Assets" in exported_asset.parts:
            path = "/".join(exported_asset.parts[exported_asset.parts.index("Assets"):]).casefold()
            aliases = {id(item) for item in self.by_container[path]}
            rooted = [(item, proof + ["original-container-path"]) for item, proof in matches if id(item) in aliases]
            if rooted:
                matches = rooted
        if len(matches) > 1:
            named_matches = [(item, proof) for item, proof in matches if name_of(item) == name]
            if named_matches:
                matches = named_matches
        result.candidates = [self.locator(item) for item, _ in matches]
        if len(matches) > 1:
            equivalent = {self._content_equivalence(item) for item, _ in matches}
            if len(equivalent) != 1:
                result.status = "ambiguous"
                return result
            matches.sort(key=lambda pair: tuple(str(part) for part in self.locator(pair[0])))
            result.equivalent_sources = [self.locator(item) for item, _ in matches[1:]]
        selected, proof = matches[0]
        result.status, result.source, result.proof = "matched", self.locator(selected), proof
        return result

    def match_audio_export(self, target, exported_asset: Path, ffmpeg: Path) -> Match:
        """Developer-only audio proof, not part of the player's importer.

        AssetRipper's Ogg export and FMOD's decode differ by PCM rounding and
        terminal Vorbis padding. Accept only the complete aligned overlap with
        max error <= 8 / 32768, mean error <= 1.5 / 32768 and <= 1024 trailing
        frames. No resampling, time shift, gain fit, or perceptual-only match.
        The original FSB, not this decoded/exported file, is used by the recipe.
        """
        import io
        import json
        import subprocess
        import wave
        import numpy as np
        result = Match("requires-audio-content-proof", "AudioClip", name_of(target))
        td = target.read_typetree()
        channels, rate = td["m_Channels"], td["m_Frequency"]
        if exported_asset.suffix.lower() not in {".ogg", ".wav"}:
            return result
        probe = subprocess.run([str(ffmpeg.with_name("ffprobe")), "-v", "error",
                                "-select_streams", "a:0", "-show_entries", "stream=sample_rate,channels",
                                "-of", "json", str(exported_asset)], check=True, capture_output=True)
        streams = json.loads(probe.stdout)["streams"]
        if len(streams) != 1 or (int(streams[0]["sample_rate"]), streams[0]["channels"]) != (rate, channels):
            result.errors.append("Export audio format differs; resampling is not an identification proof")
            return result
        decoded = subprocess.run([str(ffmpeg), "-v", "error", "-i", str(exported_asset),
                                  "-f", "s16le", "-acodec", "pcm_s16le", "-"],
                                 check=True, capture_output=True).stdout
        expected = np.frombuffer(decoded, dtype="<i2").astype(np.int32)
        frames = len(expected) // channels
        verified = []
        for source in self.by_type["AudioClip"]:
            sd = source.read_typetree()
            if sd["m_Channels"] != channels or sd["m_Frequency"] != rate:
                continue
            if abs(round(sd["m_Length"] * rate) - frames) > 1024:
                continue
            result.candidates.append(self.locator(source))
            try:
                samples = source.read().samples
                if len(samples) != 1:
                    continue
                with wave.open(io.BytesIO(next(iter(samples.values())))) as audio:
                    if (audio.getsampwidth(), audio.getnchannels(), audio.getframerate()) != (2, channels, rate):
                        continue
                    actual = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.int32)
                overlap = min(len(actual), len(expected))
                if overlap < 256 or abs(len(actual) - len(expected)) > 1024 * channels:
                    continue
                a, b = actual[:overlap], expected[:overlap]
                # Silence carries no unique identification evidence.
                if np.count_nonzero(np.abs(a) > 32) < min(256, overlap // 2):
                    continue
                error = np.abs(a - b)
                maximum, mean = int(error.max()), float(error.mean())
                if maximum <= 8 and mean <= 1.5:
                    verified.append((source, {"max_error_lsb": maximum, "mean_error_lsb": mean,
                        "source_frames": len(actual) // channels, "export_frames": frames,
                        "compared_frames": overlap // channels, "frequency": rate, "channels": channels,
                        "export_sha256": hashlib.sha256(exported_asset.read_bytes()).hexdigest(),
                        "source_pcm_sha256": hashlib.sha256(actual.astype("<i2").tobytes()).hexdigest()}))
            except Exception as exc:
                result.errors.append(f"{self.locator(source)}: {type(exc).__name__}: {exc}")
        if len(verified) == 1:
            source, evidence = verified[0]
            result.status, result.source = "matched", self.locator(source)
            result.proof = ["audio-export-pcm-rounding-and-tail-padding"]
            result.details = evidence
            result.candidates = [result.source]
        elif len(verified) > 1:
            result.status = "ambiguous"
            result.candidates = [self.locator(source) for source, _ in verified]
        return result

    def inventory(self):
        return {"counts": {typ: len(items) for typ, items in sorted(self.by_type.items())},
                "duplicate_names": {typ: sum(1 for (kind, _), items in self.by_name.items()
                                                if kind == typ and len(items) > 1)
                                    for typ in sorted(self.by_type)}}

    def sprite_geometry_source(self, target) -> Match:
        """Find an original contour source with explicit texture/region evidence.

        This authorizes a source-geometry replacement, not an assertion that a
        newly triangulated reference outline is byte-identical. The private audit
        must retain this distinction, and the resulting package needs UI checks.
        """
        from content_importer.sprite_geometry import original_sprite_geometry
        result = Match("unmatched", "Sprite", name_of(target))
        body = target.read_typetree()
        if body.get("m_Bones") or body.get("m_IsPolygon") or body["m_RD"]["settingsRaw"] not in (0, 64):
            return result
        texture = _pointer(target, body["m_RD"]["texture"])
        if texture is None or _pointer(target, body["m_RD"].get("alphaTexture", {"m_PathID": 0})) is not None:
            return result
        target_signature = self.signatures(texture, pixels=True)
        proofs = []
        for source in self.by_name[("Sprite", result.name)]:
            original = source.read_typetree()
            render = original["m_RD"]
            source_texture = _pointer(source, render["texture"])
            if source_texture is None or _pointer(source, render.get("alphaTexture", {"m_PathID": 0})) is not None:
                continue
            signature = self.signatures(source_texture, pixels=True)
            content = [kind for kind in ("texture-encoded", "texture-pixels", "texture-visible-pixels")
                       if kind in signature and signature[kind] == target_signature.get(kind)]
            if not content:
                continue
            source_rect, target_rect = original["m_Rect"], body["m_Rect"]
            if any(target_rect[axis] < source_rect[axis] - 0.05 or
                   target_rect[axis] + target_rect[size] > source_rect[axis] + source_rect[size] + 0.05
                   for axis, size in (("x", "width"), ("y", "height"))):
                continue
            try:
                geometry = original_sprite_geometry(source, body["m_RD"]["uvTransform"])
            except ValueError as exc:
                result.errors.append(f"{self.locator(source)}: {exc}")
                continue
            proofs.append((source, content, geometry))
        result.candidates = [self.locator(source) for source, _, _ in proofs]
        if not proofs:
            return result
        if len({digest(geometry) for _, _, geometry in proofs}) > 1:
            result.status = "ambiguous"
            return result
        proofs.sort(key=lambda item: tuple(str(part) for part in self.locator(item[0])))
        source, content, geometry = proofs[0]
        result.status, result.source = "source-geometry-proved", self.locator(source)
        result.equivalent_sources = [self.locator(item[0]) for item in proofs[1:]]
        result.proof = ["same-sprite-name", "source-rect-covers-target", *content]
        result.details = {"operation": "original-sprite-geometry", "referenceOutlineMayDiffer": True,
            "vertices": geometry["vertexData"]["m_VertexCount"],
            "indexBytes": len(geometry["indexBuffer"]),
            "referenceVertexBytesEqual": geometry["vertexData"]["m_DataSize"] == bytes(body["m_RD"]["m_VertexData"]["m_DataSize"]),
            "referenceIndexBytesEqual": geometry["indexBuffer"] == bytes(body["m_RD"]["m_IndexBuffer"])}
        result.adaptation = {"kind": "sprite-source-geometry", "targetUVField": ["m_RD", "uvTransform"],
            "targetFields": {"vertexData": ["m_RD", "m_VertexData"], "indexBuffer": ["m_RD", "m_IndexBuffer"],
                             "subMeshes": ["m_RD", "m_SubMeshes"], "physicsShape": ["m_PhysicsShape"]},
            "preserveTargetEmptyPhysicsShape": True}
        return result

    def animation_stream_source(self, target) -> Match:
        from producer.animation_streams import prove_stream_replacement, prove_stream_remapping, prove_dense_padding, STREAM_PATH
        result = Match("unmatched", "AnimationClip", name_of(target))
        matches = []
        for source in self.by_type["AnimationClip"]:
            try:
                proof = prove_stream_replacement(source, target)
            except ValueError:
                proof = None
            if proof is None:
                proof = prove_stream_remapping(source, target)
            if proof is not None:
                matches.append((source, proof))
        named = [(source, proof) for source, proof in matches if name_of(source) == result.name]
        if named:
            matches = named
        result.candidates = [self.locator(source) for source, _ in matches]
        if len(matches) != 1:
            if matches:
                result.status = "ambiguous"
            return result
        source, proof = matches[0]
        result.status, result.source = "matched", self.locator(source)
        result.proof = ["animation-bindings-times-indices-values-slopes-exact", "cubic-trajectory-bound"]
        result.details = proof
        result.adaptation = {"kind": "animation-stream-source", "sourceField": STREAM_PATH, "targetField": STREAM_PATH, **proof}
        if "permutation" in proof:
            result.adaptation["kind"] = "animation-stream-remap"
        dense = prove_dense_padding(source, target)
        if dense:
            result.adaptation["densePadding"] = dense
        return result
