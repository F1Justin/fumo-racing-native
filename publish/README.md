# Clean source export and release gates

These tools never push the existing game project or its Git history. Start from an explicitly allowlisted export in a new empty directory. Keep original game resources, private source indexes, local build outputs and signing credentials outside it.

```sh
python3 publish/export_public.py --output /absolute/new-public-source --report /absolute/export-audit.json
python3 publish/audit.py /absolute/new-public-source --mode source
```

The source export contains new launcher/importer/producer/bootstrap tooling and documentation. It excludes the recovered Unity project, the large `Assets/Scripts/NativePlayable` tree, Unity runtime builds, original resources, private build receipts and all existing Git history. Export rules are in `allowlist.json`; adding a source file outside those rules requires an explicit edit and review. The exporter rejects existing output directories and symlinks. It does not create a Git repository or upload anything.

`SOURCE-LICENSE-NOTICE.txt` records that a project source license has not yet been selected. The exporter does not automatically apply an open-source license to third-party or recovered game code.

## Binary release audit

```sh
python3 publish/audit.py /absolute/final-release-assets --mode release --review /absolute/review.json --report /absolute/payload-audit.json
```

The separate review record must contain schemaVersion 1, `sourceProvenanceReviewed: true`, `originalPackageImportVerified: true`, `publicDistributionAccepted: true`, `originalContentObjectsBundled: 0`, and `sourceInventorySHA256` for the reviewed source inventory. Its `files` object maps every final payload relative path to exact bytes, SHA-256, a nonempty license description and one of: `project-code`, `third-party-runtime`, `reconstruction-recipe`, `notice`, `checksum`.

These fields must come from completed provenance/content/reconstruction review. Neither this tool nor a successful mock test creates an approval record. Existing producer receipts with `publicDistributionAccepted: false` cannot pass the gate. A payload change invalidates the file inventory.

The audit rejects private/original directory names, credential files, likely token/private-key content, source-export binaries, unsafe paths and unreviewable archive containers. ZIP contents also receive bounded path/secret checks. Filesystem symlinks are rejected; ZIP symlinks must resolve inside the archive. Nested archives need separate review. Text scanning cannot establish that a Unity object or binary delta contains no protected content: that determination comes from the separately reviewed reconstruction recipe and runtime inventory.

## GitHub handoff

The suggested repository is `F1Justin/fumo-racing-native`. A read-only API check returned 404 on 2026-10-17; this tooling has not created it. A maintainer can initialize only the clean export and then create the desired public repository. Never add a remote to the existing mixed game project and push it.

`templates/draft-release.yml` is a disabled template kept outside `.github/workflows`. After source review, it can be activated with a protected `release-review` environment. It downloads the same repository's `reviewed-release` build artifact, verifies the exact payload against `review.json`, and creates a **draft prerelease** from an existing tag. It does not publish, overwrite existing release assets or auto-create tags. Final publication remains a separate action.

The artifact expected by that template contains `payload/` (final asset files only), `review.json` (outside payload), and `release-notes.md`. Final assets normally include per-platform packages, SHA256SUMS and third-party notices. Enable immutable Releases and attach the complete asset set before publication; GitHub documents the draft-first process in [immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases).

```sh
python3 -m unittest discover -s publish/tests -v
```

Automated checks validate structural exclusion, tampered payload rejection and source export boundaries. They are not full-game reconstruction, runtime licensing or platform-installation acceptance.
