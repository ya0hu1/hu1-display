"""Bundle an offline *build kit* for local acceptance; do not publish or run anything.

Inputs: reviewed 0.6.2 portable ZIP (dependency source), a locally built Android APK,
new output ZIP path. Windows compiles the matching Studio and pins that exact APK.
"""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = 'hu1-display-0.6.2-preview-windows-x64'
BASE_SHA = '27992cf3ad4a8384595092b77e514e0eee18a896cb7307678953943d7dee101a'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('base_zip', 'apk', 'output'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    if sha(args.base_zip.read_bytes()) != BASE_SHA:
        parser.error('Unrecognized dependency ZIP; use the reviewed 0.6.2 portable.')
    files = {}
    with zipfile.ZipFile(args.base_zip) as base:
        for item in json.loads(base.read(BASE + '/package-manifest.json'))['files']:
            path = item['path']
            if path.startswith(('tools/', 'licenses/', 'packages/VirtualDisplayDriver/')):
                data = base.read(BASE + '/' + path)
                if sha(data) != item['sha256']:
                    raise ValueError(path)
                files[path] = data
    # Explicit source/resource extensions only; no logs, tools caches, keys or local state.
    for pattern in ('windows-controller/build.ps1', 'windows-controller/src/*.cs',
                    'windows-controller/src/*.json', 'windows-controller/src/*.xaml',
                    'windows-controller/assets/*.ico', 'windows-controller/tests/*.cs',
                    'docs/android-performance.md', 'THIRD_PARTY_NOTICES.md', 'LICENSE', 'protocol/UPSTREAM_LICENSE'):
        for p in ROOT.glob(pattern):
            files[p.relative_to(ROOT).as_posix()] = p.read_bytes()
    apk = args.apk.read_bytes()
    files['packages/Hu1Display-Android.apk'] = apk
    files['Build-Studio.ps1'] = ('''$ErrorActionPreference='Stop'
$apk=Join-Path $PSScriptRoot 'packages/Hu1Display-Android.apk'
if ((Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash -ne 'APK_SHA') { throw 'Wrong preview APK' }
& (Join-Path $PSScriptRoot 'windows-controller/build.ps1') -OutputDirectory $PSScriptRoot -AndroidPreviewApk $apk
Write-Host 'Built Hu1.Display.exe. Start it when ready; no app was installed or opened.'
'''.replace('APK_SHA', sha(apk))).encode()
    args.output.parent.mkdir(exist_ok=True, parents=True)
    with zipfile.ZipFile(args.output, 'x', compression=zipfile.ZIP_DEFLATED) as target:
        for path, data in sorted(files.items()):
            target.writestr(args.output.stem + '/' + path, data)
    with zipfile.ZipFile(args.output) as check:
        if check.testzip() is not None:
            raise ValueError('ZIP verification failed')
    digest = sha(args.output.read_bytes())
    Path(str(args.output) + '.sha256').write_text(f'{digest}  {args.output.name}\n')
    print(args.output, digest)


if __name__ == '__main__':
    main()
