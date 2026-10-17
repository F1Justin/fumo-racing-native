#!/usr/bin/env python3
"""Build standalone first-install launchers. Does not copy game assets or publish."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent
TARGETS = {"macos-arm64": ("darwin", "arm64"), "macos-amd64": ("darwin", "amd64"),
           "windows-amd64": ("windows", "amd64"), "linux-amd64": ("linux", "amd64")}


def run(arguments, **kwargs):
    subprocess.run(arguments, cwd=ROOT, check=True, **kwargs)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=["all", *TARGETS], default="all")
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    parser.add_argument("--importer", type=Path, help="Frozen content-importer for the selected target")
    parser.add_argument("--recipe", type=Path, help="Reviewed recipe JSON for this release")
    parser.add_argument("--version", default="0.1.0")
    args = parser.parse_args()
    if bool(args.importer) != bool(args.recipe):
        parser.error("--importer and --recipe must be supplied together")
    if args.importer and args.target == "all":
        parser.error("Choose one target when bundling a platform-specific importer")
    if args.recipe:
        json.loads(args.recipe.read_text(encoding="utf-8"))
    if not args.version or any(c not in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz.-" for c in args.version):
        parser.error("Invalid version")
    output = args.output.resolve()
    selected_targets = list(TARGETS) if args.target == "all" else [args.target]
    for target in selected_targets:
        destination = output / target
        if destination.exists() and any(destination.iterdir()):
            parser.error("Output target is not empty; choose a fresh --output: " + str(destination))
    env = dict(os.environ, CGO_ENABLED="0", GOCACHE=str(Path(tempfile.gettempdir()) / "fumo-launcher-go-cache"))
    reports = []
    for target in selected_targets:
        goos, arch = TARGETS[target]
        package = output / target
        package.mkdir(parents=True, exist_ok=True)
        flags = "-s -w -X main.version=" + args.version
        if goos == "windows":
            flags += " -H windowsgui"
        if goos == "darwin":
            app = package / "Fumo Racing Launcher.app"
            binary_dir = app / "Contents/MacOS"
            resources = app / "Contents/Resources"
            binary_dir.mkdir(parents=True, exist_ok=True)
            resources.mkdir(parents=True, exist_ok=True)
            engine = binary_dir / "engine"
            importer_target = binary_dir / "content-importer"
            recipe_target = resources / "recipe.json"
        else:
            app = None
            engine = package / ("Fumo-Racing-Launcher.exe" if goos == "windows" else "Fumo-Racing-Launcher")
            importer_target = package / ("content-importer.exe" if goos == "windows" else "content-importer")
            recipe_target = package / "recipe.json"
        run(["go", "build", "-trimpath", "-ldflags", flags, "-o", str(engine), "./cmd/launcher"],
            env=dict(env, GOOS=goos, GOARCH=arch))
        if args.importer:
            shutil.copyfile(args.importer.resolve(), importer_target)
            importer_target.chmod(0o755)
            shutil.copyfile(args.recipe.resolve(), recipe_target)
        if app:
            # Respect the actual Go toolchain's deployment target, instead of
            # advertising the older AppKit host's target for the whole product.
            minimums = re.findall(r"\bminos\s+([0-9.]+)", subprocess.check_output(["/usr/bin/otool", "-l", str(engine)], text=True))
            if not minimums:
                raise ValueError("Cannot determine the Go engine's minimum macOS version")
            minimum_macos = max(minimums, key=lambda value: tuple(map(int, value.split("."))))
            host = binary_dir / "FumoLauncher"
            run(["/usr/bin/clang", "-fobjc-arc", "-mmacosx-version-min=" + minimum_macos, "-arch", "arm64" if arch == "arm64" else "x86_64",
                 "-framework", "Cocoa", "-framework", "UniformTypeIdentifiers", str(ROOT / "macos/host.m"), "-o", str(host)])
            info = {"CFBundleExecutable": "FumoLauncher", "CFBundleIdentifier": "com.fumoracing.launcher",
                    "CFBundleName": "Fumo Racing Launcher", "CFBundleDisplayName": "Fumo Racing Launcher",
                    "CFBundlePackageType": "APPL", "CFBundleShortVersionString": args.version,
                    "CFBundleVersion": "1", "LSMinimumSystemVersion": minimum_macos, "NSHighResolutionCapable": True,
                    "NSAppTransportSecurity": {"NSAllowsLocalNetworking": True}}
            (app / "Contents/Info.plist").write_bytes(plistlib.dumps(info))
            # Local integrity signatures only. This is not Developer ID or notarization.
            for binary in ([engine, importer_target] if args.importer else [engine]):
                run(["/usr/bin/codesign", "--force", "--sign", "-", str(binary)])
            run(["/usr/bin/codesign", "--force", "--sign", "-", str(app)])
            run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(app)])
        goroot = Path(subprocess.check_output(["go", "env", "GOROOT"], text=True).strip())
        license_path = next(path for path in [goroot / "LICENSE", goroot.parent / "LICENSE"] if path.is_file())
        shutil.copyfile(license_path, package / "Go-LICENSE.txt")
        notice = ("Fumo Racing 安装与启动工具\n\n"
                  "打开启动工具，选择你已拥有的原版 ZIP 或解压后的文件夹。\n"
                  "安装完成后点击“开始游戏”；以后打开工具即可启动上次安装。\n"
                  "默认安装在当前用户的数据目录，不会修改原版文件。\n"
                  "玩家无需自行安装 Python、Go、Node.js 或 Unity。\n"
                  "macOS 使用原生窗口；Windows/Linux 当前使用本机浏览器界面。\n"
                  "本机界面只能从当前计算机访问。Linux 文件选择需 zenity 或 kdialog，\n"
                  "若不可用，可在高级选项中填写完整路径。\n")
        if not args.importer:
            notice += "\n这是开发测试用窗口骨架，未附导入器和安装说明数据，不能安装游戏。\n"
        (package / "START-HERE.txt").write_text(notice, encoding="utf-8")
        report = {"schemaVersion": 1, "target": target, "version": args.version,
                  "importerBundled": bool(args.importer), "recipeBundled": bool(args.recipe),
                  "originalGameAssetsBundled": False, "signing": "local-ad-hoc" if app else "unsigned",
                  "notarized": False, "publicDistributionAccepted": False,
                  "realGameTested": False, "files": {}}
        if app:
            report["minimumMacOS"] = minimum_macos
        for path in sorted(package.rglob("*")):
            if path.is_file() and path.name != "build-receipt.json":
                report["files"][path.relative_to(package).as_posix()] = {"bytes": path.stat().st_size, "sha256": digest(path)}
        (package / "build-receipt.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
        reports.append({"target": target, "directory": str(package), "importerBundled": bool(args.importer)})
    print(json.dumps(reports, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
