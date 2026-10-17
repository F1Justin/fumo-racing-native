# Third-party inventory scope

The public source export contains no original game assets or Unity runtime. Binary packages require a separate inventory of their actual files and upstream notices.

| Component | Current role | Inventory and notice handling |
| --- | --- | --- |
| Go runtime and standard library | Compiled launcher | `launcher/build.py` copies the toolchain's `LICENSE` into each output package. |
| Python | Frozen importer's interpreter | `license_inventory.py` records the exact Python version and copies its installed license when available; absence needs resolution before release. |
| UnityPy 1.25.0 | Original-package parsing and content serialization | Installed metadata identifies MIT; copy its upstream license and inventory every resolved dependency. |
| Pillow and codec dependencies | Decoding/encoding selected resource representations | Record pinned versions and copy installed upstream license files; test actual reconstruction with the frozen executable. |
| PyInstaller | Build freezer and embedded bootloader | The upstream license includes a distribution exception; dependency license obligations still apply. See [official license text](https://pyinstaller.org/en/stable/license.html). |
| Unity player and compiled game code | Candidate external-content runtime | Not included by this exporter. Require engine/runtime redistribution review and a separate C# provenance review. Naming a file `Native*` is not evidence that its contents are independently authored. |
| FMOD, other proprietary SDK binaries | Not a required component of raw audio extraction | Do not collect local SDKs or decoder binaries into the importer. Inspect the actual freeze inventory; installed Python metadata alone does not establish that an SDK is absent. |

`license_inventory.py` collects the installed declared dependency graph and upstream license files. Its output remains `publicDistributionAccepted: false`. It does not automatically grant a project-wide license, approve restored game code, or approve engine redistribution.

PyInstaller outputs are specific to the OS and Python architecture used to build them. Windows and Linux importers must be built and tested on their respective systems; a macOS build is not a cross-platform importer. See the [official operating-mode documentation](https://pyinstaller.org/en/stable/operating-mode.html).
