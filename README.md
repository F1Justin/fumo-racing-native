# Fumo Racing Native

A native edition of Touhou Fumo Racing with an importer for your own Touhou Fumo Racing 0.2.1 ZIP or extracted game folder.

Open the launcher, choose the original game, and import it once. When installation finishes, select **开始游戏**. The launcher remembers the installation for future play.

## Status

The public repository currently provides development source. Player installers will appear under [GitHub Releases](https://github.com/F1Justin/fumo-racing-native/releases) after full import and game testing.

Obtain Touhou Fumo Racing 0.2.1 from [HonkyHood's official page](https://honkyhood.itch.io/touhou-fumo-racing). The installer includes the native runtime and reads game resources from your original copy on your computer.

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

Checks cover installation state, failure recovery, input paths, content generation and source export. Full-game import and platform installation are tracked separately in release acceptance.

See [third-party notices](publish/THIRD-PARTY-NOTICES.md) and the [source license notice](publish/SOURCE-LICENSE-NOTICE.txt). This community project is not an official HonkyHood release.
