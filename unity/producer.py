#!/usr/bin/env python3
"""Build an isolated assetless Player and PRIVATE reference bundles; never publish."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid

HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parents[2]
NATIVE = REPOSITORY / 'native-macos'
SOURCE = NATIVE / 'UnityProject'
EDITOR = Path('/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity')
KEEP = {'.cs', '.shader', '.cginc', '.hlsl', '.compute', '.asmdef', '.asmref'}
EXCLUDED_SHELL_INPUTS = {'Assets/Shaders/GrassShader/Shader/GGG_Shader.shader'}
SHADER_REPLACEMENTS = {'Assets/Shaders/GrassShader/Shader/GGG_Shader.shader': 'GrandGeoGrassCompatibility.shader'}


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def clone(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['/bin/cp', '-cR', str(source), str(target)], check=True)


def input_hashes(project):
    return {str(p.relative_to(project)): sha(p) for folder in ('Assets', 'Packages', 'ProjectSettings')
            for p in (project / folder).rglob('*') if p.is_file()}


def copy_overlay(project):
    manifest_path = project / 'Packages/manifest.json'
    manifest = json.loads(manifest_path.read_text())
    manifest.setdefault('dependencies', {})['com.unity.modules.assetbundle'] = '1.0.0'
    # Authoring assistants/test packages have build callbacks that inject machine
    # test metadata and unrelated runtime assemblies. Game code does not use them.
    if (project / '.fumo-distribution-shell').is_file():
        for package in ('com.unity.ai.assistant', 'com.unity.ai.inference', 'com.unity.pipeline'):
            manifest['dependencies'].pop(package, None)
        # Some embedded package test asmdefs need NUnit during Editor compile;
        # the base framework has no performance-report build injection callback.
        manifest['dependencies']['com.unity.test-framework'] = '1.6.0'
    write_json(manifest_path, manifest)
    race = project / 'Assets/Scripts/NativePlayable/NativeRace.cs'
    if race.exists():
        # Apply to prepared workspaces as well, including the existing route
        # switching test's availability check before its ChangeTrack call.
        race.write_text(race.read_text().replace('Application.CanStreamedLevelBeLoaded(', 'NativeContent.CanLoadScene('))
    for folder in ('Runtime', 'Editor'):
        for source in sorted((HERE / folder).glob('*.cs')):
            destination = project / 'Assets' / ('Scripts/NativePlayable' if folder == 'Runtime' else 'Editor') / source.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            meta = destination.with_suffix(destination.suffix + '.meta')
            if not meta.exists():
                identity = uuid.uuid5(uuid.NAMESPACE_URL, 'thfr-distribution-unity/' + folder + '/' + source.name).hex
                meta.write_text('fileFormatVersion: 2\nguid: ' + identity + '\n')
    for relative, replacement in SHADER_REPLACEMENTS.items():
        target = project / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(HERE / 'Shaders' / replacement, target)
        meta = target.with_suffix(target.suffix + '.meta')
        if not meta.exists():
            shutil.copyfile((SOURCE / relative).with_suffix(target.suffix + '.meta'), meta)


def sanitize_shell(project):
    if not (project / '.fumo-distribution-shell').is_file():
        raise ValueError('Shell marker is required')
    for path in (project / 'Assets').rglob('*'):
        if path.is_file() and path.suffix in KEEP and 'DummyShaderTextExporter' in path.read_text():
            raise ValueError('Extracted shader stub entered shell: ' + str(path))


def patch_runtime(project):
    changed = []
    count = 0
    for source in sorted((project / 'Assets/Scripts/NativePlayable').glob('*.cs')):
        if source.name in ('NativeContent.cs', 'NativeContentBootstrap.cs'):
            continue
        text = source.read_text()
        patched, matches = re.subn(r'\bResources\.(Load(?:All)?)(?=[<(])', r'NativeContent.\1', text)
        if matches:
            source.write_text(patched)
            changed.append({'file': str(source.relative_to(project)), 'calls': matches})
            count += matches
    race = project / 'Assets/Scripts/NativePlayable/NativeRace.cs'
    text = race.read_text()
    old = '!Application.CanStreamedLevelBeLoaded(scene)'
    if text.count(old) != 1:
        raise ValueError('NativeRace.ChangeTrack no longer matches the bounded scene-loading patch')
    scene_checks = text.count('Application.CanStreamedLevelBeLoaded(')
    race.write_text(text.replace('Application.CanStreamedLevelBeLoaded(', 'NativeContent.CanLoadScene('))
    if count == 0:
        raise ValueError('No Resources.Load sites found; refusing an unverified overlay')
    return {'resourceCalls': count, 'files': changed, 'sceneCheckReplacements': scene_checks}


def prepare(output, platform, version):
    output.mkdir(parents=True, exist_ok=False)
    workspace = output / 'workspace'
    content = workspace / 'content-project'
    before = input_hashes(SOURCE)
    for folder in ('Assets', 'Packages', 'ProjectSettings', 'Library'):
        if (SOURCE / folder).exists():
            clone(SOURCE / folder, content / folder)
    if input_hashes(content) != before or input_hashes(SOURCE) != before:
        raise RuntimeError('Saved source changed during APFS snapshot; no build was started')
    write_json(output / 'source-inputs.private.json', before)
    original_editors = content / 'Assets/Editor'
    if original_editors.exists():
        original_editors.rename(workspace / 'original-editor-private')
    if (content / 'Assets/Editor.meta').exists():
        (content / 'Assets/Editor.meta').unlink()
    patch = patch_runtime(content)
    copy_overlay(content)
    (content / '.fumo-distribution-content').write_text(str(SOURCE.resolve()) + '\n')
    # The reference content is never copied into this shell's Assets directory.
    shell = workspace / 'shell-project'
    for folder in ('Packages', 'ProjectSettings', 'Library'):
        if (SOURCE / folder).exists():
            clone(SOURCE / folder, shell / folder)
    kept = []
    for source in sorted((content / 'Assets').rglob('*')):
        if not source.is_file() or source.suffix.lower() not in KEEP or '/Editor/' in source.as_posix():
            continue
        relative = source.relative_to(content)
        if str(relative) in EXCLUDED_SHELL_INPUTS:
            continue
        target = shell / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        meta = source.with_suffix(source.suffix + '.meta')
        if meta.exists():
            shutil.copyfile(meta, target.with_suffix(target.suffix + '.meta'))
        kept.append({'path': str(relative), 'sha256': sha(target)})
    copy_overlay(shell)
    (shell / 'Assets/link.xml').write_text('<linker>\n  <assembly fullname="Assembly-CSharp" preserve="all"/>\n'
                                           '  <assembly fullname="Unity.TextMeshPro" preserve="all"/>\n</linker>\n')
    (shell / '.fumo-distribution-shell').write_text(str(SOURCE.resolve()) + '\n')
    receipt = {'schema': 1, 'status': 'prepared', 'platform': platform, 'source': str(SOURCE.resolve()),
               'editor': str(EDITOR), 'version': version,
               'workspace': str(workspace), 'contentProject': str(content), 'shellProject': str(shell),
               'privateReference': str(output / 'private-reference'), 'publicPlayer': str(output / 'public-player'),
               'runtimePatch': patch, 'shellSourceFiles': kept,
               'limits': ['Reference bundles contain original content and MUST remain local.',
                          'Loading these bundles does not verify original-package import or content reconstruction.',
                          'Code/shader redistribution provenance and platform signing are separate release checks.']}
    write_json(output / 'producer-receipt.json', receipt)
    return receipt


def run(output, receipt, step):
    editor = Path(receipt['editor'])
    if not editor.is_file():
        raise FileNotFoundError(editor)
    env = dict(os.environ, FUMO_DISTRIBUTION_WORKSPACE=receipt['workspace'], FUMO_DISTRIBUTION_OUTPUT=str(output),
               FUMO_DISTRIBUTION_PLATFORM=receipt['platform'], FUMO_DISTRIBUTION_VERSION=receipt['version'])
    target = {'macos': 'StandaloneOSX', 'windows': 'StandaloneWindows64', 'linux': 'StandaloneLinux64'}[receipt['platform']]
    steps = [('content', 'contentProject', 'BuildPrivateContent'), ('player', 'shellProject', 'BuildPublicPlayer')]
    for label, project_key, method in steps:
        if step not in ('all', label):
            continue
        project = Path(receipt[project_key])
        if not project.is_relative_to(Path(receipt['workspace'])) or not (project / ('.fumo-distribution-content' if label == 'content' else '.fumo-distribution-shell')).is_file():
            raise ValueError('Missing isolated project marker')
        copy_overlay(project)
        if label == 'player':
            sanitize_shell(project)
        logfile = output / (label + '-build.log')
        args = [str(editor), '-batchmode', '-quit', '-buildTarget', target, '-projectPath', str(project),
                '-executeMethod', 'NativeContentBuild.' + method, '-logFile', str(logfile)]
        print('Building', label, 'in isolated project; log:', logfile, flush=True)
        result = subprocess.run(args, env=env)
        receipt.setdefault('steps', {})[label] = {'exitCode': result.returncode, 'log': str(logfile)}
        receipt['status'] = 'failed' if result.returncode else label + '-built'
        write_json(output / 'producer-receipt.json', receipt)
        if result.returncode:
            raise RuntimeError('Unity ' + label + ' failed; inspect ' + str(logfile))
    receipt['status'] = 'reference-and-player-built' if all(receipt.get('steps', {}).get(k, {}).get('exitCode') == 0 for k in ('content', 'player')) else receipt['status']
    write_json(output / 'producer-receipt.json', receipt)


def main():
    global SOURCE, EDITOR
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New local private workspace, never a publish directory')
    parser.add_argument('--platform', choices=['macos', 'windows', 'linux'], default='macos')
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--run-prepared', choices=['content', 'player', 'all'])
    parser.add_argument('--source-project', type=Path, default=SOURCE,
                        help='Private complete authoring project; required when using the exported public repository')
    parser.add_argument('--unity-editor', type=Path, default=EDITOR)
    parser.add_argument('--version', help='Product version; defaults to private native-macos/VERSION when available')
    args = parser.parse_args()
    SOURCE = args.source_project.resolve()
    EDITOR = args.unity_editor.resolve()
    output = args.output.resolve()
    if output.is_relative_to(SOURCE) or SOURCE.is_relative_to(output):
        parser.error('Output must be isolated from the authoring project')
    if args.prepare_only and args.run_prepared:
        parser.error('Choose prepare-only or run-prepared')
    if args.run_prepared:
        receipt = json.loads((output / 'producer-receipt.json').read_text())
        SOURCE = Path(receipt['source']).resolve()
    else:
        version = args.version or ((NATIVE / 'VERSION').read_text().strip() if (NATIVE / 'VERSION').is_file() else None)
        if not version or not (SOURCE / 'Assets').is_dir():
            parser.error('Specify the private --source-project and --version; public source alone does not contain game content')
        receipt = prepare(output, args.platform, version)
    if not args.prepare_only:
        run(output, receipt, args.run_prepared or 'all')
    print(json.dumps({'status': receipt['status'], 'receipt': str(output / 'producer-receipt.json')}, indent=2))


if __name__ == '__main__':
    main()
