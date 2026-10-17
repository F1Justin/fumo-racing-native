import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit import AuditFailure, audit_tree
from assemble_recipe import stage, finalize
from package_payload import file_identity, json_bytes, validate_recipe_dir
from runtime_inventory import validate as validate_runtime_inventory


class AssemblyTests(unittest.TestCase):
    def fixture(self, root):
        draft, runtime, licenses = root/"draft", root/"Game.app", root/"notices"
        draft.mkdir(); (runtime/"Contents/MacOS").mkdir(parents=True); licenses.mkdir()
        (draft/"instructions.json").write_text('{}')
        (runtime/"Contents/MacOS/Game").write_text('#!/bin/sh\nexit 0\n')
        (runtime/"Contents/MacOS/Game").chmod(0o755)
        (runtime/"game-link").symlink_to("Contents/MacOS/Game")
        (licenses/"LICENSE.txt").write_text("Test fixture notice only.\n")
        (licenses/"Official-notice.pdf").write_bytes(b"%PDF-1.4\nTest fixture only.\n")
        manifest = {"schema": 1, "status": "draft", "platform": "macos", "contentId": "test-only",
                    "runtime": {"files": []}, "files": [{"file": "instructions.json", **file_identity(draft/"instructions.json")}]}
        (draft/"recipe.json").write_bytes(json_bytes(manifest))
        report = draft/"audit.private.json"; report.write_text('{"unresolvedCount":0}')
        receipt = root/"runtime.json"
        receipt.write_text(json.dumps({"role": "code-only-player-candidate", "result": "passed", "platform": "macos", "player": str(runtime)}))
        inventory = root/"runtime-inventory.json"
        inventory.write_bytes(json_bytes({"schema": 1, "kind": "final-macos-code-only-player-candidate", "result": "passed",
            "packedAssetAudit": {"sha256": file_identity(receipt)["sha256"], "originalContentObjectsBundled": 0},
            "playerFiles": [{"path": "Game.app/Contents/MacOS/Game", "mode": 0o755, **file_identity(runtime/"Contents/MacOS/Game")}],
            "playerLinks": [{"path": "Game.app/game-link", "target": "Contents/MacOS/Game"}]}))
        return draft, runtime, licenses, report, receipt, inventory

    def candidate(self, root):
        draft, runtime, licenses, report, receipt, inventory = self.fixture(root)
        candidate = root/"candidate"
        info = stage(draft, runtime, "Game.app", "Contents/MacOS/Game", report, receipt, [licenses], candidate,
                     runtime_file_inventory=inventory)
        return candidate, info

    def test_stage_keeps_draft_and_flat_runtime_and_links(self):
        with tempfile.TemporaryDirectory() as d:
            candidate, info = self.candidate(Path(d))
            data = validate_recipe_dir(candidate)
            self.assertEqual(info["status"], "draft")
            self.assertFalse(any(p.suffix == ".app" for p in candidate.rglob("*")))
            self.assertEqual(data["runtime"]["links"], [{"output": "Game.app/game-link", "target": "Contents/MacOS/Game"}])
            self.assertTrue(all(r["source"].startswith("runtime/files/") for r in data["runtime"]["files"]))
            self.assertEqual(next(r for r in data["runtime"]["files"] if r["executable"])["mode"], 0o755)
            self.assertTrue(any(r["output"].endswith("Official-notice.pdf") for r in data["runtime"]["files"]))
            with self.assertRaises(AuditFailure):
                validate_recipe_dir(candidate, require_ready=True)

    def test_runtime_inventory_rejects_modified_extra_and_missing_files(self):
        for change in ("modified", "extra", "missing", "link", "mode", "receipt"):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as d:
                root = Path(d)
                _, runtime, _, _, receipt, inventory = self.fixture(root)
                validate_runtime_inventory(runtime, inventory, receipt, "macos")
                game = runtime/"Contents/MacOS/Game"
                if change == "modified": game.write_text("changed code")
                if change == "extra": (runtime/"extra").write_text("extra content")
                if change == "missing": (runtime/"game-link").unlink(); game.unlink()
                if change == "link":
                    (runtime/"game-link").unlink(); (runtime/"game-link").symlink_to("Contents")
                if change == "mode": game.chmod(0o644)
                if change == "receipt": receipt.write_text("{}")
                with self.assertRaises(AuditFailure):
                    validate_runtime_inventory(runtime, inventory, receipt, "macos")

    def test_finalize_requires_exact_game_acceptance_and_rechecks_hashes(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d); candidate, info = self.candidate(root)
            verification = root/"test-verification.json"
            review = {"schemaVersion": 1, "candidateRecipeSHA256": info["candidateRecipeSHA256"], "contentId": "test-only", "platform": "macos",
                      "originalPackageImportVerified": True, "playableGameVerified": False, "sourceProvenanceReviewed": True,
                      "unresolvedCount": 0, "originalContentObjectsBundled": 0, "sourceInventorySHA256": "a"*64}
            verification.write_bytes(json_bytes(review))
            with self.assertRaises(AuditFailure):
                finalize(candidate, verification, root/"failed")
            review["playableGameVerified"] = True
            verification.write_bytes(json_bytes(review))
            finalize(candidate, verification, root/"ready")
            data = validate_recipe_dir(root/"ready", require_ready=True)
            self.assertEqual(data["status"], "ready")
            (root/"ready/instructions.json").write_text('{"changed":true}')
            with self.assertRaises(AuditFailure):
                validate_recipe_dir(root/"ready", require_ready=True)

    def test_unlisted_private_file_cannot_enter_launcher_payload(self):
        with tempfile.TemporaryDirectory() as d:
            candidate, _ = self.candidate(Path(d))
            (candidate/"audit.private.json").write_text('{}')
            with self.assertRaises(AuditFailure):
                validate_recipe_dir(candidate)

    def test_runtime_source_manifest_exception_is_exact(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d); runtime = root/"runtime-source"
            files = {"Assets/Scripts/NativePlayable/Test.cs": "class Test {}", "ThirdPartyNotices/Test/LICENSE.txt": "fixture notice", "README.md": "fixture", "Packages/manifest.json": "{}"}
            for name, text in files.items():
                path = runtime/name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text)
            manifest = {"schema": 1, "kind": "runtime-source-only", "gameAssetsIncluded": False, "sourceAuditSHA256": "a"*64,
                        "files": [{"path": "Assets/Scripts/NativePlayable/Test.cs", "lineage": "project-reimplementation-source-owner-authorized", **file_identity(runtime/"Assets/Scripts/NativePlayable/Test.cs")}],
                        "notices": [{"path": "ThirdPartyNotices/Test/LICENSE.txt", **file_identity(runtime/"ThirdPartyNotices/Test/LICENSE.txt")}],
                        "metadataFiles": [{"path": name, **file_identity(runtime/name)} for name in ("README.md", "Packages/manifest.json")]}
            (runtime/"source-manifest.json").write_bytes(json_bytes(manifest))
            with self.assertRaises(AuditFailure): audit_tree(root)
            self.assertTrue(audit_tree(root, runtime_source="runtime-source")["structuralAuditPassed"])
            (runtime/"Assets/Scripts/NativePlayable/extra.cs").write_text("class Extra {}")
            with self.assertRaises(AuditFailure): audit_tree(root, runtime_source="runtime-source")
            (runtime/"Assets/Scripts/NativePlayable/extra.cs").unlink()
            (runtime/"Assets/Scripts/NativePlayable/Test.cs").write_text("class Changed {}")
            with self.assertRaises(AuditFailure): audit_tree(root, runtime_source="runtime-source")


if __name__ == "__main__":
    unittest.main()
