#!/usr/bin/env python3
"""Build a Debian development package from the locked, offline workspace."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def package(output):
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="inklane-package-") as temp:
        stage = Path(temp)
        app = stage / "usr/share/inklane"
        app.mkdir(parents=True)
        subprocess.run([sys.executable, str(ROOT / "website/build.py"), "--workspace-only",
                        "--output", str(app / "workspace")], check=True)
        shutil.copyfile(ROOT / "platforms/linux/inklane.py", app / "inklane.py")
        shutil.copyfile(ROOT / "platforms/apple/Sources/NativeSave.js", app / "NativeSave.js")
        for filename in ["THIRD-PARTY-NOTICES.md", "README.md"]:
            dest = stage / "usr/share/doc/inklane" / filename
            dest.parent.mkdir(exist_ok=True, parents=True)
            shutil.copyfile(ROOT / filename, dest)
        binary = stage / "usr/bin/inklane"
        binary.parent.mkdir(parents=True)
        binary.write_text('#!/bin/sh\nexec /usr/bin/python3 /usr/share/inklane/inklane.py "$@"\n')
        binary.chmod(0o755)
        desktop = stage / "usr/share/applications/io.github.dwolken.Inklane.desktop"
        desktop.parent.mkdir(parents=True)
        desktop.write_text("""[Desktop Entry]
Type=Application
Name=Inklane
Comment=Review documents and annotate in context
Comment[de]=Dokumente prüfen und direkt kommentieren
Exec=inklane
Icon=io.github.dwolken.Inklane
Terminal=false
Categories=Office;Graphics;
""")
        icon = stage / "usr/share/icons/hicolor/scalable/apps/io.github.dwolken.Inklane.svg"
        icon.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / "src/Inklane/Assets/inklane.svg", icon)
        control = stage / "DEBIAN/control"
        control.parent.mkdir()
        control.write_text("""Package: inklane
Version: 1.0.0~rc3+workspace1
Architecture: all
Section: editors
Priority: optional
Maintainer: Inklane project <noreply@github.com>
Depends: python3 (>= 3.10), python3-gi, gir1.2-gtk-3.0, gir1.2-webkit2-4.1 (>= 2.40)
Homepage: https://dwolken.github.io/inklane/
Description: Local document review and annotation workspace
 Draw, type and pin remarks to images and PDFs, or create a printable notebook.
""")
        subprocess.run(["dpkg-deb", "--root-owner-group", "--build", str(stage), str(output)], check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".scratch/linux/inklane.deb")
    package(parser.parse_args().output)
