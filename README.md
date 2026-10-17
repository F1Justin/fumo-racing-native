# Fumo Racing Native

A native edition of Touhou Fumo Racing with a local importer for the player's original HonkyHood game package.

The distribution contains the launcher, native runtime and reconstruction recipes. Original game resources are read from the player's own copy and assembled locally. The importer does not execute the original Windows game or upload its contents.

## Status

Implementation and full-game validation are in progress. A playable release will appear under GitHub Releases after original-package import passes end-to-end validation. Source archives are development tools, not player downloads.

The intended player flow is: download the release for your system → open the launcher → select the official original ZIP or installed game folder → import once → play. The supported source version is Touhou Fumo Racing 0.2.1. Obtain it from [HonkyHood's official page](https://honkyhood.itch.io/touhou-fumo-racing).

## Components

| Directory | Purpose |
|---|---|
| `launcher/` | Native macOS window, platform file selection, progress, installation recovery and game launch |
| `importer/` | Original-file verification, local resource conversion, staged installation and rollback |
| `producer/` | Build-time source matching and data-only recipe generation |
| `unity/` | External-content loader and isolated runtime build tooling |
| `publish/` | Source allowlist, payload audit and draft-release workflow template |

Players use a bundled importer executable. Python, Go and the Unity Editor are development dependencies only.

## Development checks

```sh
cd launcher && go test -race ./...
PYTHONPATH=importer python3 -m unittest discover -s importer/tests -v
python3 -m unittest discover -s publish/tests -v
```

See [third-party notices](publish/THIRD-PARTY-NOTICES.md) and the [source license notice](publish/SOURCE-LICENSE-NOTICE.txt). This community project is not an official HonkyHood release.
