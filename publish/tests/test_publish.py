import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit import AuditFailure, audit_tree
from export_public import export


class PublishingTests(unittest.TestCase):
    def test_export_allowlist_excludes_original_and_history(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source"
            (source / "launcher").mkdir(parents=True)
            (source / "launcher" / "go.mod").write_text("module example\n")
            (source / "Assets").mkdir()
            (source / "Assets" / "original.png").write_bytes(b"original")
            (source / ".git").mkdir()
            (source / ".git" / "private-history").write_text("history")
            policy = root / "allowlist.json"
            policy.write_text(json.dumps({"schemaVersion": 1, "patterns": ["launcher/go.mod"]}))
            report = export(source, root / "public", policy)
            self.assertTrue(report["structuralAuditPassed"])
            self.assertFalse((root / "public" / "Assets").exists())
            self.assertFalse((root / "public" / ".git").exists())
            with self.assertRaises(AuditFailure):
                export(source, root / "public", policy)

    def test_private_path_and_token_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "private-reference").mkdir()
            (root / "private-reference" / "resource.json").write_text("{}")
            with self.assertRaises(AuditFailure):
                audit_tree(root)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "source.py").write_text("token=" + "ghp_" + "a"*36)
            with self.assertRaises(AuditFailure):
                audit_tree(root)

    def test_release_needs_explicit_complete_matching_review(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            data = b"synthetic executable fixture, not a game"
            (root / "launcher.exe").write_bytes(data)
            with self.assertRaises(AuditFailure):
                audit_tree(root, "release")
            review = {"schemaVersion": 1, "sourceProvenanceReviewed": True,
                      "originalPackageImportVerified": True, "publicDistributionAccepted": True,
                      "originalContentObjectsBundled": 0, "sourceInventorySHA256": "0"*64,
                      "files": {"launcher.exe": {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                                                  "classification": "project-code", "license": "test-fixture"}}}
            self.assertTrue(audit_tree(root, "release", review)["structuralAuditPassed"])
            (root / "launcher.exe").write_bytes(data + b"changed")
            with self.assertRaises(AuditFailure):
                audit_tree(root, "release", review)

    def test_zip_cannot_hide_original_or_traversal(self):
        for forbidden in ["Assets/original.asset", "../escape", "private-reference/content.bin"]:
            with self.subTest(path=forbidden), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                with zipfile.ZipFile(root / "payload.zip", "w") as archive:
                    archive.writestr(forbidden, "blocked")
                with self.assertRaises(AuditFailure):
                    audit_tree(root, "release", {})

    def test_source_export_rejects_binary_disguised_as_code(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "code.py").write_bytes(b"text\x00binary")
            with self.assertRaises(AuditFailure):
                audit_tree(root)


if __name__ == "__main__":
    unittest.main()
