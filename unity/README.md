# External content Player overlay

This directory contains code and producer tooling. It does not contain game assets.
The producer only changes isolated APFS snapshots; it never edits the active Unity project or publishes files.

```sh
python3 native-macos/distribution/unity/producer.py \
  --platform macos --output /private/tmp/thfr-external-content-NEW --prepare-only
python3 native-macos/distribution/unity/producer.py \
  --output /private/tmp/thfr-external-content-NEW --run-prepared all
```

The second command needs ordinary Unity licensing/socket access. It builds with Unity `6000.3.25f1`.
`--run-prepared content` and `--run-prepared player` run either phase independently.
In an exported public repository, pass `--source-project /private/authoring/UnityProject --version VERSION --unity-editor /path/to/Unity` when preparing. The public repository deliberately omits the private authoring Assets; it cannot build the complete game's reference data by itself. Players use the importer, not this Unity producer.

Output boundaries:

- `workspace/content-project`, `private-reference/Content`, `source-inputs.private.json`, and `private-reference/source-asset-index.json` contain original or derived content. **Keep all of them local.**
- `public-player/` is the assetless Player candidate. Its source Assets are limited to C#, shader/include source, the overlay, a fresh bootstrap scene and fresh TMP defaults. Source/shader licensing, signing and final payload auditing remain separate checks.
- `private-reference/Content/catalog.json` maps the original `Resources` keys to external bundle assets and lists bundle dependencies. The source index records source asset paths, GUIDs, local IDs, object types and scene hierarchy for recipe production.

The Player reads `FUMO_CONTENT_PATH`, then `--fumo-content-dir PATH` (also accepts `-fumo-content-dir PATH`), then a `Content` folder adjacent to the `.app` or Windows/Linux `Game` directory. `Content/install-manifest.json` has schema `1`, `contentId`, `bundles` (`file`, `sha256`, optional `bytes`), `scenes` (`id`, `path`, `bundle`) and `initialScene`. The generated manifest also binds `catalog.json` by SHA-256 and records the engine/platform. The importer verifies the bundled recipe's file hashes; this loader verifies the local output's integrity. These checks detect corruption, not a malicious replacement of the entire download.

`NativeContent.Load<T>` preserves ordinary built-in Resources behavior only when explicitly enabled in development. Distribution builds define `FUMO_EXTERNAL_CONTENT`, so missing content cannot silently fall back to bundled game assets. The bootstrap is code only and shows an installation error if content is missing. The launcher owns the actual import UI.

The producer removes only the `BatchingStatic` flag in reference scenes, keeps bundle type trees, disables pre-baked collision meshes, retains readable mesh vertex/index data for runtime cooking, and uses uncompressed imported texture payloads to make reconstruction easier. Nonuniformly scaled colliders require readable meshes when their original cooked collision bytes are removed. Passive editor-only postprocessing provenance captures (`layerJson`/`volumes`) are cleared; live volume components remain. All other authored scene flags and references are kept. This requires separate performance validation.

The extracted `GGG_Shader.shader` stub is replaced in the isolated projects with the newly authored `Shaders/GrandGeoGrassCompatibility.shader`, retaining only the shader identifier required by existing materials and a basic textured pass. The active authoring source remains untouched. `audit_sources.py --project PATH --output REPORT.json` compares runtime C# to the original decompiler output, flags common embedded payload forms and records Unity/TMP and NVJOB shader lineage. Its report is evidence for review, not proof of copyright clearance; third-party notices and the compiled Player asset inventory must also be checked.

After auditing the final shell source, `public-source-export.py --project SHELL_PROJECT --audit REPORT.json --output NEW_DIRECTORY` exports only the exact audited runtime C#/shader/include files, a package manifest and third-party notices. It rejects changed hashes, extra/unclassified sources, extracted shader stubs and reported embedded payloads. The output preserves `Assets/...` source paths but deliberately has no game assets or scene/prefab/meta files; it is not a standalone Unity project. From an exported repository, pass private inputs explicitly with `audit_sources.py --authoring-project ... --original-scripts ...` and `public-source-export.py --repository-root ...`.

The isolated overlay also enables the synthetic keyboard created by the existing scene-switch diagnostic, because Unity can create it disabled while the window lacks focus. This affects only the `verificationRun` fixture. Passing that diagnostic does not constitute physical human input verification.

Loading a private reference bundle proves the external content integration only. It **does not prove** that a player can reconstruct those bundles from HonkyHood's original package. Public release requires the independent importer to reproduce every original-content object locally, reject unmapped content, and pass the complete game flow with the reconstructed output.
