# Original-resource importer

The command reads the player's own original game, verifies supported source hashes, reconstructs content in a staging directory and installs the clean runtime plus external `Content`. Existing original files remain read-only. `recipe.json` must be marked ready by the verified producer; draft recipes fail before installation.

```sh
python -m content_importer --source /absolute/original.zip --output /absolute/new-install --recipe /absolute/release-recipe-directory --json-progress
```

`--recipe` is a directory containing `recipe.json` and its allowlisted payload files. stdout is UTF-8 JSONL for the launcher; failure emits `type:error` and a nonzero exit. Successful results include installation, executable and the fixed external-content argument.

## Frozen executable

Build separately on each supported OS/architecture. Use an isolated Python environment and the pinned requirements; do not install packages into a player's computer.

```sh
python -m venv /absolute/build-environment
# Activate that environment using the platform's normal activation command.
python -m pip install -r requirements-build.txt
python build.py --output /absolute/new-importer-output
```

The build produces a self-contained `content-importer[.exe]`, a sanitized frozen input inventory, upstream license files and a receipt. It explicitly excludes the optional FMOD decoder modules and refuses any FMOD SDK binary found in the freeze inventory. The resource converter copies the original FSB bytes directly instead of requiring that SDK.

The build's smoke test only proves that the frozen executable starts and rejects a draft recipe without creating an installation. A successful smoke test is not full-content reconstruction, playable-game verification, signing/notarization or permission to publish. `publicDistributionAccepted` and `fullReconstructionVerified` remain false.

On Windows, keep the importer's console-style binary so redirected JSON stdout works; the Go launcher hides its child console. macOS runtime signing and notarization apply to the final packaged launcher and helper. For Linux, build against the oldest supported userland and verify the actual desktop system. PyInstaller is not a cross-OS compiler; see [its official operating-mode documentation](https://pyinstaller.org/en/stable/operating-mode.html).
