#!/usr/bin/env python3
"""Produce local source lineage evidence; no copyright clearance is inferred."""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import re
import tarfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
ACTIVE = ROOT / 'native-macos/UnityProject'
ORIGINAL = ROOT / 'assetripper/fumoracing/ExportedProject/Assets/Scripts/Assembly-CSharp'
EXTENSIONS = {'.cs', '.shader', '.cginc', '.hlsl', '.compute', '.asmdef', '.asmref'}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def normalized(data):
    # Diagnostic only: ignore whitespace/comments to detect trivial unchanged copies.
    text = data.decode('utf-8', errors='replace')
    text = re.sub(r'/\*.*?\*/|//[^\n]*', '', text, flags=re.S)
    return digest(re.sub(r'\s+', '', text).encode())


def package_shader_sources():
    exact = defaultdict(list)
    archives = sorted((ACTIVE / 'Library/PackageCache').glob('com.unity.ugui@*/Package Resources/TMP Essential Resources.unitypackage'))
    for archive in archives:
        with tarfile.open(archive, 'r:gz') as package:
            members = {p.name: p for p in package.getmembers() if p.isfile()}
            for name, member in members.items():
                if not name.endswith('/pathname'):
                    continue
                path = package.extractfile(member).read().decode().strip()
                asset = members.get(name.rsplit('/', 1)[0] + '/asset')
                if asset and Path(path).suffix in EXTENSIONS:
                    exact[digest(package.extractfile(asset).read())].append({'package': str(archive.relative_to(ROOT)), 'path': path})
    return exact


def scan(project):
    original = defaultdict(list)
    stripped = defaultdict(list)
    for path in ORIGINAL.rglob('*.cs'):
        data = path.read_bytes()
        original[digest(data)].append(str(path.relative_to(ROOT)))
        stripped[normalized(data)].append(str(path.relative_to(ROOT)))
    third_party = package_shader_sources()
    rows = []
    for path in sorted((project / 'Assets').rglob('*')):
        if not path.is_file() or path.suffix not in EXTENSIONS or '/Editor/' in str(path):
            continue
        data = path.read_bytes()
        text = data.decode('utf-8')
        relative = str(path.relative_to(project))
        row = {'path': relative, 'sha256': digest(data), 'bytes': len(data), 'findings': []}
        if row['sha256'] in original or normalized(data) in stripped:
            row['findings'].append({'kind': 'decompiled-original-match', 'sources': original[row['sha256']] or stripped[normalized(data)]})
        if 'DummyShaderTextExporter' in text:
            row['findings'].append({'kind': 'extracted-shader-stub'})
        for match in re.finditer(r'@?"(?:""|\\.|[^"\\])*"', text, re.S):
            literal = match.group()
            reason = None
            if len(literal) > 1024 and ('{' in literal or '[' in literal):
                reason = 'large-structured-string'
            if re.search(r'[A-Za-z0-9+/]{512,}={0,2}', literal):
                reason = 'base64-like-payload'
            if reason:
                row['findings'].append({'kind': reason, 'line': text.count('\n', 0, match.start()) + 1, 'characters': len(literal)})
        for match in re.finditer(r'(?:new\s+)?byte\s*\[\s*\]\s*[^;]*?\{([^}]+)\}', text):
            if match.group(1).count(',') > 255:
                row['findings'].append({'kind': 'large-byte-initializer', 'line': text.count('\n', 0, match.start()) + 1})
        if row['sha256'] in third_party:
            row['lineage'] = 'unity-package-exact-match'
            row['upstream'] = third_party[row['sha256']]
        elif '/TextMesh Pro/' in relative or '/UIShaders/TMP' in relative:
            row['lineage'] = 'textmeshpro-derived-review-required'
        elif path.name == 'RestoredMountainWater.shader':
            row['lineage'] = 'nvjob-and-unity-mit-derived'
            row['licenses'] = ['assetripper/fumoracing/ExportedProject/Assets/Scripts/NativePlayable/license_nvjob.txt',
                               'assetripper/fumoracing/ExportedProject/Assets/Scripts/NativePlayable/license_unity.txt']
        elif path.name in ('NativeContent.cs', 'NativeContentBootstrap.cs') or 'Independently authored compatibility pass' in text:
            row['lineage'] = 'distribution-overlay-authored-source'
        elif relative.startswith('Assets/Scripts/NativePlayable/') or relative in (
                'Assets/Shaders/UI/UIMovingPattern.shader', 'Assets/Resources/NativeSettings/post.shader',
                'Assets/Resources/NativeSettings/particles.shader'):
            row['lineage'] = 'project-reimplementation-source-owner-authorized'
        else:
            row['lineage'] = 'unclassified-review-required'
        shader = re.search(r'^\s*Shader\s+"([^"]+)"', text, re.M)
        if shader:
            row['shaderName'] = shader.group(1)
        rows.append(row)
    packages = []
    for path in sorted((ACTIVE / 'Library/PackageCache').glob('*/package.json')):
        data = json.loads(path.read_text())
        if data.get('name') not in json.loads((project / 'Packages/manifest.json').read_text()).get('dependencies', {}):
            continue
        licenses = list(path.parent.glob('LICENSE*')) + list(path.parent.glob('Third Party Notices*'))
        packages.append({'name': data.get('name'), 'version': data.get('version'), 'licenses': [
            {'path': str(p.relative_to(ROOT)), 'sha256': digest(p.read_bytes())} for p in licenses if p.is_file() and p.suffix != '.meta']})
    return {'schema': 1, 'scope': 'PRIVATE source review evidence', 'project': str(project),
            'sourceProvenanceReviewed': False, 'publicDistributionAccepted': False,
            'summary': {'sourceFiles': len(rows), 'lineages': dict(Counter(r['lineage'] for r in rows)),
                        'findingCount': sum(len(r['findings']) for r in rows), 'originalScriptsCompared': sum(map(len, original.values()))},
            'limitations': ['Hash/token matching cannot prove independent authorship or detect all derivative code.',
                            'Literal scanning flags common embedded payload forms; it is not a complete semantic media audit.',
                            'Project reimplementation classification records owner authorization, not a legal opinion.',
                            'Bundle reconstruction data and compiled player packed-asset audit are separate checks.'],
            'files': rows, 'packages': packages}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = scan(args.project.resolve())
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report['summary'], indent=2))


if __name__ == '__main__':
    main()
