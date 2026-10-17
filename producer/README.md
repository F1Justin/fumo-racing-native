# Reconstruction recipes

The producer compares a private Unity reference build with a locally supplied original game. It emits instructions for rebuilding content on the player's computer. Original resource bytes stay in the original game and private reference directories.

| Tool | Output |
|---|---|
| `index_original.py` | Private SQLite index of object identities and field hashes |
| `audit_mapping.py` | Texture, mesh, audio, font, sprite and animation correspondence evidence |
| `grass_mapping.py` | Original mesh vertex selections and deterministic grass generation parameters |
| `build_recipe.py` | Draft recipe, empty Unity structure templates, object instructions and a private audit |

Use each command's `--help` for required paths. Build inputs must remain local. `audit.private.json`, original containers and full reference bundles are never release files.

The recipe uses source field references, bounded structural values and explicit conversions. Texture, mesh and audio payloads come from the verified original files. Grass is generated from selected original vertices. Newly authored or separately licensed binary inputs require per-object provenance approval; an unresolved field stops release preparation. Templates contain type information and empty object bodies.

The installer supports no script execution, plugin loading or arbitrary shell steps. All input containers are verified by size and SHA-256 before parsing, and each runtime payload file belongs to the recipe's hash allowlist.

Recipe generation always writes `status: draft`. A complete import followed by game testing and provenance review is required before release assembly can mark it ready. The importer CLI rejects drafts. Private development harnesses may pass `allow_draft=True` to `content_importer.install.install` for this acceptance test.
