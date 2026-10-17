from __future__ import annotations

import json
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from content_importer.expressions import Evaluator
from content_importer.files import ImportFailure, extract_zip, relative_name, sha256, snapshot_original
from content_importer.install import MARKER, install
from content_importer.recipe import Recipe, read_json


class InputSafety(unittest.TestCase):
    def test_unsafe_paths(self):
        for name in ("../escape", "/absolute", "A/../x", "C:/x", "a\\b", "a//b", "CON.txt", "NUL", "a.", "a ", "a|b"):
            with self.subTest(name=name), self.assertRaises(ImportFailure):
                relative_name(name)

    def test_zip_validated_before_writing(self):
        for variant in ("escape", "alias", "symlink"):
            with self.subTest(variant=variant), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                with zipfile.ZipFile(root / "original.zip", "w") as archive:
                    archive.writestr("safe/file", b"ok")
                    if variant == "escape":
                        archive.writestr("../escape", b"bad")
                    elif variant == "alias":
                        archive.writestr("SAFE/FILE", b"bad")
                    else:
                        entry = zipfile.ZipInfo("link")
                        entry.create_system = 3
                        entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                        archive.writestr(entry, "../escape")
                with self.assertRaises(ImportFailure):
                    extract_zip(root / "original.zip", root / "out")
                self.assertFalse((root / "out").exists())

    def test_original_version_hash_checked_on_copy(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            data = root / "original"
            data.mkdir()
            (data / "data.unity3d").write_bytes(b"wrong version")
            with self.assertRaises(ImportFailure):
                snapshot_original(data, root / "copy", [{"file": "data.unity3d", "bytes": 13,
                    "sha256": "0" * 64}], lambda *args: None)
            self.assertEqual((data / "data.unity3d").read_bytes(), b"wrong version")

    def test_draft_and_duplicate_keys_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "recipe.json").write_text('{"schema":1,"status":"draft"}')
            with self.assertRaises(ImportFailure):
                Recipe(root)
            (root / "recipe.json").write_text('{"schema":1,"schema":1}')
            with self.assertRaises(ImportFailure):
                read_json(root / "recipe.json")

    def test_unresolved_and_code_operations_rejected(self):
        evaluator = Evaluator(None, [])
        for expression in (["UNRESOLVED", "blob"], ["exec", "something"], ["r", -1, []],
                           ["b", "YQ==", "unknown"], ["d", [["x", ["v", 1]], ["x", ["v", 2]]]]):
            with self.subTest(expression=expression), self.assertRaises(ImportFailure):
                evaluator.evaluate(expression)


class TransactionSafety(unittest.TestCase):
    def setup_fixture(self, root):
        source = root / "original"
        data = source / "TouhouFumoRacing_Data"
        data.mkdir(parents=True)
        (data / "data.unity3d").write_bytes(b"synthetic fixture")
        output = root / "installed"
        output.mkdir()
        (output / MARKER).write_text(json.dumps({"installer": "fumo-content-importer-v1"}))
        (output / "old-game").write_bytes(b"previous installation")
        recipe = type("FixtureRecipe", (), {"data": {"originalFiles": [{"file": "data.unity3d",
            "bytes": 17, "sha256": sha256(data / "data.unity3d")}]}})()
        return source, output, recipe

    def test_failed_rebuild_keeps_old_install(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, output, recipe = self.setup_fixture(root)
            with patch("content_importer.install.Recipe", return_value=recipe), \
                 patch("content_importer.install.OriginalStore"), \
                 patch("content_importer.install.copy_runtime", return_value="Game/game"), \
                 patch("content_importer.install.rebuild_content", side_effect=ImportFailure("unmapped object")):
                with self.assertRaises(ImportFailure):
                    install(source, output, root, lambda *args: None)
            self.assertEqual((output / "old-game").read_bytes(), b"previous installation")
            self.assertFalse(output.with_name("installed.previous").exists())
            self.assertFalse(list(root.glob(".fumo-import-*")))

    def test_failed_commit_restores_old_install(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, output, recipe = self.setup_fixture(root)
            original_rename = Path.rename
            def fail_new_commit(path, target):
                if path.name == "installation":
                    raise OSError("simulated interrupted filesystem commit")
                return original_rename(path, target)
            with patch("content_importer.install.Recipe", return_value=recipe), \
                 patch("content_importer.install.OriginalStore"), \
                 patch("content_importer.install.copy_runtime", return_value="Game/game"), \
                 patch("content_importer.install.rebuild_content", return_value={"contentId": "fixture"}), \
                 patch.object(Path, "rename", fail_new_commit):
                with self.assertRaises(OSError):
                    install(source, output, root, lambda *args: None)
            self.assertEqual((output / "old-game").read_bytes(), b"previous installation")
            self.assertFalse(output.with_name("installed.previous").exists())

    def test_unowned_destination_is_never_replaced(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, output, _ = self.setup_fixture(root)
            (output / MARKER).unlink()
            with self.assertRaises(ImportFailure):
                install(source, output, root, lambda *args: None)
            self.assertTrue((output / "old-game").exists())


if __name__ == "__main__":
    unittest.main()
