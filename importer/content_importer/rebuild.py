"""Reconstruct reference-compatible Unity bundles using original local objects."""
from __future__ import annotations

import json
from collections import OrderedDict
from pathlib import Path
from types import SimpleNamespace

from .expressions import Evaluator
from .files import InvalidPackage as ImportFailure, relative_name, sha256
from .unity_codecs import prepare_conversion, apply_conversion
from .templates import neutral


def rebuild_bundle(recipe, spec, originals, locators, destination: Path, progress):
    import UnityPy
    from UnityPy.files import SerializedFile
    from UnityPy.streams import EndianBinaryReader

    bundle = UnityPy.Environment().load_file(str(recipe.file(spec["template"])))
    if bundle is None or not hasattr(bundle, "files"):
        raise ImportFailure("资源配方的结构模板无效。")
    serialized = {name: file for name, file in bundle.files.items() if isinstance(file, SerializedFile)}
    expected = {(name, reader.path_id) for name, file in serialized.items() for reader in file.objects.values()}
    # Public templates are schema only; every object must be rebuilt explicitly.
    for file in serialized.values():
        for reader in file.objects.values():
            if reader.type.name == "AssetBundle":
                body = reader.read_typetree()
                if body != neutral(body):
                    raise ImportFailure("结构模板的容器头包含资源内容。")
            elif reader.byte_size:
                raise ImportFailure("结构模板包含未经重建的对象数据。")
    if any(getattr(file, "Length", 0) for file in bundle.files.values() if not isinstance(file, SerializedFile)):
        raise ImportFailure("结构模板包含原始资源内容。")
    evaluator = Evaluator(originals, locators)
    streams = {}
    decoded_meshes = OrderedDict()
    done = set()
    with recipe.file(spec["objects"]).open(encoding="utf-8") as instructions:
        for line in instructions:
            if len(line) > 64 * 1024 * 1024:
                raise ImportFailure("对象配方超出处理限制。")
            entry = json.loads(line)
            key = (entry["file"], entry["pathID"])
            if key not in expected or key in done:
                raise ImportFailure("对象配方重复或与模板不一致。")
            target = serialized[key[0]].objects[key[1]]
            if target.type.name != entry["type"]:
                raise ImportFailure("对象配方的类型与模板不一致。")
            evaluator.nodes = 0
            if "expression" in entry:
                target.save_typetree(evaluator.evaluate(entry["expression"]))
            elif "convert" in entry:
                conversion = entry["convert"]
                source_index = conversion["source"]
                if not isinstance(source_index, int) or not 0 <= source_index < len(locators):
                    raise ImportFailure("资源转换来源无效。")
                body = evaluator.evaluate(conversion["schema"])
                view = SimpleNamespace(type=target.type, path_id=target.path_id, read_typetree=lambda: body)
                converted = prepare_conversion(originals.reader(locators[source_index]), view,
                                               texture_policy=conversion.get("texturePolicy", "preserve"),
                                               make_readable=conversion.get("readable"))
                evaluator.source_reads += 1
                if converted.stream_kind == "audio":
                    stream_name = key[0] + ".resource"
                    stream = streams.setdefault(stream_name, bytearray())
                    while len(stream) % 16:
                        stream.append(0)
                    offset = len(stream)
                    stream.extend(converted.stream_data)
                    apply_conversion(target, converted, resource_path="archive:/" + key[0] + "/" + stream_name,
                                     offset=offset)
                else:
                    # Preserve CPU-readable content inside its object. A .resS
                    # rewrite can reparse in UnityPy yet leave Unity 6 without
                    # readable vertex/image data (confirmed by a native Mesh
                    # crash and invalid Texture2D.GetPixels32 in the fixture).
                    # Inline bodies passed the real Unity load/readback check.
                    apply_conversion(target, converted)
            elif "sprite" in entry:
                from .sprite_geometry import original_sprite_geometry
                conversion = entry["sprite"]
                source_index = conversion["source"]
                if type(source_index) is not int or not 0 <= source_index < len(locators):
                    raise ImportFailure("Sprite conversion source is invalid.")
                body = evaluator.evaluate(conversion["schema"])
                geometry = original_sprite_geometry(originals.reader(locators[source_index]), body["m_RD"]["uvTransform"])
                body["m_RD"]["m_VertexData"] = geometry["vertexData"]
                body["m_RD"]["m_IndexBuffer"] = geometry["indexBuffer"]
                body["m_RD"]["m_SubMeshes"] = geometry["subMeshes"]
                body["m_PhysicsShape"] = [] if conversion.get("keepEmptyPhysicsShape") else geometry["physicsShape"]
                evaluator.source_reads += 1
                target.save_typetree(body)
            elif "grass" in entry:
                from .grass import decode_mesh, generate_grass
                grass = entry["grass"]
                source_index = grass["source"]
                if type(source_index) is not int or not 0 <= source_index < len(locators):
                    raise ImportFailure("草地资源来源无效。")
                source = originals.reader(locators[source_index])
                if source_index not in decoded_meshes:
                    decoded_meshes[source_index] = decode_mesh(source)
                    while len(decoded_meshes) > 16:
                        decoded_meshes.popitem(last=False)
                decoded_meshes.move_to_end(source_index)
                body = generate_grass(source, evaluator.evaluate(grass["schema"]), grass["vertices"],
                                      grass["boundsPadding"], decoded=decoded_meshes[source_index])
                evaluator.source_reads += 1
                target.save_typetree(body)
            else:
                raise ImportFailure("对象配方缺少重建步骤。")
            done.add(key)
            if len(done) % 300 == 0:
                progress("rebuild", len(done), len(expected), "正在重建游戏内容…")
    if done != expected:
        raise ImportFailure("资源配方未覆盖全部对象，安装已停止。")
    for name, data in streams.items():
        stream = EndianBinaryReader(bytes(data))
        stream.flags = 0
        bundle.files[name] = stream
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(bundle.save(packer="lz4"))
    return {"file": relative_name(spec["output"]), "sha256": sha256(destination),
            "bytes": destination.stat().st_size, "objects": len(done), "sourceReads": evaluator.source_reads}


def rebuild_content(recipe, originals, destination: Path, progress):
    destination.mkdir(parents=True, exist_ok=False)
    locators = json.loads(recipe.file(recipe.data["sourceObjects"]).read_text())
    results = []
    specs = recipe.data["bundles"]
    for index, spec in enumerate(specs):
        name = relative_name(spec["output"])
        def bundle_progress(phase, completed, total, message):
            overall = index * 1000 + min(999, completed * 1000 // max(1, total))
            progress(phase, overall, len(specs) * 1000,
                     "正在生成游戏内容（%s/%s）…" % (index + 1, len(specs)))
        results.append(rebuild_bundle(recipe, spec, originals, locators, destination / name, bundle_progress))
        progress("rebuild", (index + 1) * 1000, len(specs) * 1000,
                 "正在生成游戏内容（%s/%s）…" % (index + 1, len(specs)))
    catalog_source = recipe.file(recipe.data["catalog"])
    catalog = destination / "catalog.json"
    catalog.write_bytes(catalog_source.read_bytes())
    manifest = {"schema": 1, "contentId": recipe.data["contentId"],
                "engineVersion": recipe.data["engine"], "platform": recipe.data["platform"],
                "bundles": results, "scenes": recipe.data["scenes"],
                "initialScene": recipe.data["initialScene"], "catalogFile": "catalog.json",
                "catalogSHA256": sha256(catalog)}
    (destination / "install-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    return manifest
