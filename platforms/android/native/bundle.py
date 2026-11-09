"""Prepare the Android assets using the same workspace builder as the website."""
from pathlib import Path
from shutil import copyfile
import subprocess
import sys

here = Path(__file__).resolve().parent
root = here.parents[2]
dest = Path(sys.argv[1]).resolve()
subprocess.run([sys.executable, str(root / 'website/build.py'), '--workspace-only', '--output', str(dest)], check=True)
page = dest / 'editor/index.html'
markup = page.read_text(encoding='utf-8')
entry = '<script type="module" src="boot.js"></script>'
if markup.count(entry) != 1:
    raise ValueError('Cannot locate the workspace entry point.')
page.write_text(markup.replace(entry, '<script src="android-files.js"></script>\n    ' + entry), encoding='utf-8')
copyfile(here / 'android-files.js', dest / 'editor/android-files.js')
