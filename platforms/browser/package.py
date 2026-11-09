"""Create unpacked Chromium/Firefox builds and unsigned ZIPs for local testing."""
from pathlib import Path
import json
import shutil
import sys
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent
target = Path(sys.argv[1] if len(sys.argv) > 1 else root.parents[1] / '.scratch/browser-package').resolve()
files = ['core.js', 'content.js', 'background.js', 'report.html', 'report.js', 'help.html', 'help.js', 'pages.css', 'icon-192.png']
for name in ['chromium', 'firefox']:
    output = target / name
    output.mkdir(parents=True, exist_ok=True)
    manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))
    if name == 'firefox':
        manifest['background'] = {'scripts': ['core.js', 'background.js']}
        manifest['browser_specific_settings'] = {'gecko': {'id': 'web-review@inklane.app', 'strict_min_version': '128.0', 'data_collection_permissions': {'required': ['none']}}}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    for file in files:
        shutil.copyfile(root / file, output / file)
    with ZipFile(target / f'inklane-web-{name}-0.1.0.zip', 'w', ZIP_DEFLATED) as archive:
        for file in ['manifest.json', *files]:
            archive.write(output / file, file)
print(target)
