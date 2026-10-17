#!/usr/bin/env python3
"""Export explicitly audited runtime source and notices; never export game assets."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil

HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parents[2]
ALLOWED_EXTENSIONS = {'.cs', '.shader', '.cginc', '.hlsl', '.compute', '.asmdef', '.asmref'}
ALLOWED_LINEAGES = {
    'project-reimplementation-source-owner-authorized', 'distribution-overlay-authored-source',
    'unity-package-exact-match', 'textmeshpro-derived-review-required', 'nvjob-and-unity-mit-derived',
}
EXPLICIT_PATHS = {
    'Assets/Resources/NativeSettings/post.shader', 'Assets/Resources/NativeSettings/particles.shader',
    'Assets/Shaders/UI/UIMovingPattern.shader', 'Assets/Shaders/GrassShader/Shader/GGG_Shader.shader',
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative(value):
    path = PurePosixPath(value)
    if path.is_absolute() or '..' in path.parts or '\\' in value or ':' in value:
        raise ValueError('Invalid manifest path')
    return path


def export(project, audit_path, output, repository):
    audit = json.loads(audit_path.read_text())
    if audit.get('schema') != 1 or Path(audit['project']).resolve() != project:
        raise ValueError('Audit is not bound to this project')
    if audit['summary']['originalScriptsCompared'] < 1 or audit['summary']['findingCount'] != 0:
        raise ValueError('Original script comparison or embedded-data audit has unresolved findings')
    if output.exists() or output.is_relative_to(project) or project.is_relative_to(output):
        raise ValueError('Output must be a new directory outside the private project')
    copies = []
    inventory = []
    for row in audit['files']:
        value = row['path']; path = relative(value)
        if path.suffix not in ALLOWED_EXTENSIONS or '/Editor/' in value:
            raise ValueError('Non-runtime source in source manifest: ' + value)
        if not (value.startswith('Assets/Scripts/NativePlayable/') or value.startswith('Assets/TextMesh Pro/Shaders/')
                or value in EXPLICIT_PATHS):
            raise ValueError('Source path is outside the explicit runtime allowlist: ' + value)
        source = project / path
        if source.is_symlink() or not source.is_file() or sha(source) != row['sha256']:
            raise ValueError('Source changed after audit: ' + value)
        if row['lineage'] not in ALLOWED_LINEAGES or row['findings']:
            raise ValueError('Unresolved source lineage: ' + value)
        if 'DummyShaderTextExporter' in source.read_text():
            raise ValueError('Extracted shader stub is not public runtime source')
        copies.append((source, Path(value)))
        inventory.append({key: row[key] for key in ('path', 'sha256', 'bytes', 'lineage')})
    # Exact set equality prevents a stale report from silently omitting new code.
    actual = {str(p.relative_to(project)) for p in (project / 'Assets').rglob('*')
              if p.is_file() and p.suffix in ALLOWED_EXTENSIONS and '/Editor/' not in p.as_posix()}
    if actual != {row['path'] for row in inventory}:
        raise ValueError('Runtime source inventory and audit differ')
    license_rows = []
    for package in audit['packages']:
        for item in package['licenses']:
            source = Path(item['path'])
            if not source.is_absolute(): source = repository / source
            if not source.is_file() or sha(source) != item['sha256']:
                raise ValueError('Package notice changed after audit: ' + str(source))
            destination = Path('ThirdPartyNotices') / package['name'] / source.name
            copies.append((source, destination))
            license_rows.append({'path': str(destination), 'sha256': item['sha256'], 'bytes': source.stat().st_size,
                                 'package': package['name'], 'version': package['version']})
    for filename in ('license_nvjob.txt', 'license_unity.txt'):
        source = repository / 'assetripper/fumoracing/ExportedProject/Assets/Scripts/NativePlayable' / filename
        if not source.is_file(): raise ValueError('Missing water shader MIT notice: ' + str(source))
        destination = Path('ThirdPartyNotices/NVJOB') / filename
        copies.append((source, destination))
        license_rows.append({'path': str(destination), 'sha256': sha(source), 'bytes': source.stat().st_size,
                             'package': 'NVJOB Simple Water Shaders / Unity', 'version': '1.6-derived'})
    package_manifest = project / 'Packages/manifest.json'
    copies.append((package_manifest, Path('Packages/manifest.json')))
    output.mkdir(parents=True)
    for source, destination in copies:
        target = output / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    manifest = {'schema': 1, 'kind': 'runtime-source-only', 'gameAssetsIncluded': False,
                'independenceProvenByStaticAudit': False, 'sourceAuditSHA256': sha(audit_path),
                'files': inventory, 'notices': license_rows,
                'packageManifestSHA256': sha(package_manifest)}
    (output / 'README.md').write_text(
        '# Runtime source\n\n'
        'This directory contains the project runtime C#, shaders and required third-party notices. '
        'Source paths preserve the private authoring project layout; no game textures, meshes, audio, fonts, prefabs, scenes, Resources data, editor exports or original decompiled scripts are included.\n\n'
        'This is not a standalone Unity project. Players use the launcher and import their own original game package. '
        'The producer requires the private complete authoring project and a locked Unity editor to create reference data. '
        'Do not publish those reference bundles or the authoring Assets.\n\n'
        'Project reimplementation source is included under the project owner\'s authorization. '
        'Unity/TMP-derived shaders and the NVJOB water shader retain their upstream notices in ThirdPartyNotices. '
        'Static matching and payload scans do not prove independent authorship or constitute legal clearance.\n', encoding='utf-8')
    manifest['metadataFiles'] = [{'path': value, 'sha256': sha(output / value), 'bytes': (output / value).stat().st_size}
                                 for value in ('README.md', 'Packages/manifest.json')]
    # Write last: the manifest binds every other file in the export exactly.
    (output / 'source-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    return {'sourceFiles': len(inventory), 'noticeFiles': len(license_rows), 'output': str(output), 'gameAssetsIncluded': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--audit', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--repository-root', type=Path, default=REPOSITORY,
                        help='Private repository containing recorded package notices and NVJOB license files')
    args = parser.parse_args()
    print(json.dumps(export(args.project.resolve(), args.audit.resolve(), args.output.resolve(), args.repository_root.resolve()), indent=2))


if __name__ == '__main__':
    main()
