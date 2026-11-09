"""Create the standalone workspace from project assets and locked PDF.js distribution files."""

import base64
from hashlib import sha256
import json
from pathlib import Path
from shutil import copyfile

SOURCE = Path(__file__).resolve().parent


def build_workspace(output):
    editor = output / "editor"
    editor.mkdir(parents=True, exist_ok=True)
    files = []

    def stage(src, path):
        target = editor / path
        target.parent.mkdir(exist_ok=True, parents=True)
        copyfile(src, target)
        files.append(path)

    for name in ("index.html", "editor.js", "editor.css", "language.js", "model.js", "pdf-source.js",
                 "documents.js", "drafts.js", "print.js", "rich-text.js", "shapes.js", "properties.js", "rich-text.css", "image-export.js", "continuous.js", "startup.js", "register-offline.js", "manifest.webmanifest"):
        stage(SOURCE / "editor" / name, name)
    for size in [180, 192, 512]:
        name = f"icons/app-{size}.png"
        stage(SOURCE / "editor" / name, name)
    stage(SOURCE.parent / "src/Inklane/Assets/inklane.svg", "../assets/inklane.svg")
    stage(SOURCE.parent / "src/Inklane/Assets/inklane-app.svg", "../assets/inklane-app.svg")
    for form in ("symbol", "lockup", "wordmark"):
        for theme in ("light", "dark"):
            name = f"inklane-{form}-{theme}.svg"
            stage(SOURCE.parent / "src/Inklane/Assets" / name, "../assets/" + name)
    for lang in ("en", "de"):
        name = f"field-guide-{lang}.png"
        stage(SOURCE / "assets" / name, "../assets/" + name)

    page = editor / "index.html"
    html = page.read_text(encoding="utf-8").replace("</head>", """
    <meta name="theme-color" media="(prefers-color-scheme: light)" content="#ffffff">
    <meta name="theme-color" media="(prefers-color-scheme: dark)" content="#1c1c21">
    <link rel="manifest" href="manifest.webmanifest">
    <link rel="apple-touch-icon" href="icons/app-180.png">
    <script type="module" src="register-offline.js"></script>
  </head>""")
    page.write_text(html, encoding="utf-8")

    dep = SOURCE / "node_modules/pdfjs-dist"
    expected = json.loads((SOURCE / "package.json").read_text())["dependencies"]["pdfjs-dist"]
    if not dep.is_dir() or json.loads((dep / "package.json").read_text())["version"] != expected:
        raise ValueError("Install the locked dependencies first: npm ci --prefix website --ignore-scripts")
    for name in ("pdf.min.mjs", "pdf.worker.min.mjs"):
        stage(dep / "legacy/build" / name, "pdf-lib/" + name)
    stage(dep / "LICENSE", "pdf-lib/LICENSE")
    for folder in ("cmaps", "standard_fonts", "iccs", "wasm"):
        for asset in sorted((dep / folder).iterdir()):
            if not asset.is_file() or "quickjs" in asset.name.lower():
                continue  # Inklane never loads PDF scripting or sandbox modules.
            stage(asset, f"pdf-lib/{folder}/{asset.name}")

    for package, source, destination in [
        ("marked", "lib/marked.esm.js", "marked.js"),
        ("dompurify", "dist/purify.es.mjs", "purify.js"),
        ("katex", "dist/katex.mjs", "katex.mjs"),
        ("katex", "dist/katex.min.css", "katex.css"),
        ("@highlightjs/cdn-assets", "es/highlight.min.js", "highlight.js")]:
        package_root = SOURCE / "node_modules" / package
        expected_version = json.loads((SOURCE / "package.json").read_text())["dependencies"][package]
        if json.loads((package_root / "package.json").read_text())["version"] != expected_version:
            raise ValueError("Install the locked rich-text dependencies with npm ci first")
        stage(package_root / source, "rich-lib/" + destination)
    for font in (SOURCE / "node_modules/katex/dist/fonts").glob("*.woff2"):
        stage(font, "rich-lib/fonts/" + font.name)
    for package, license in [("marked", "LICENSE"), ("dompurify", "LICENSE"), ("katex", "LICENSE"), ("@highlightjs/cdn-assets", "LICENSE")]:
        stage(SOURCE / "node_modules" / package / license, "rich-lib/" + package.replace("/", "-") + "-LICENSE")

    maps = [name for name in files if name.endswith(".bcmap")]
    (editor / "offline-maps.json").write_text(json.dumps({
        name: base64.b64encode((editor / name).read_bytes()).decode("ascii") for name in maps
    }, separators=(",", ":")), encoding="utf-8")
    template = (SOURCE / "offline-worker.js").read_text(encoding="utf-8")
    stamp = sha256(template.encode())
    for name in sorted(files):
        stamp.update(name.encode()); stamp.update(sha256((editor / name).read_bytes()).digest())
    settings = {"revision": stamp.hexdigest()[:24], "files": [n for n in files if n not in maps], "maps": maps}
    (editor / "sw.js").write_text(template.replace("= CACHE_SETTINGS;", "= " + json.dumps(settings) + ";"), encoding="utf-8")
