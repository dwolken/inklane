"""Build both static product pages using the published Windows installer."""

import argparse
from html import escape
import json
import os
from pathlib import Path
import re
from shutil import copyfile
from string import Template
from time import sleep
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from build_workspace import build_workspace

SOURCE = Path(__file__).resolve().parent
ROOT = SOURCE.parent
SITE = "https://dwolken.github.io/inklane/"
REPO = "https://github.com/dwolken/inklane"
API = "https://api.github.com/repos/dwolken/inklane/releases?per_page=100"
TAG = re.compile(r"v(\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?)")
TRANSIENT_DOWNLOAD_ERRORS = {429, 500, 502, 503, 504}


def choose_release(releases):
    # Prefer a public stable build. Until one exists, use the newest published installer.
    candidates = sorted((r for r in releases if r["published_at"] and not r["draft"]),
                        key=lambda r: r["published_at"], reverse=True)
    for release in sorted(candidates, key=lambda r: r["prerelease"]):
        tag = release["tag_name"]
        match = TAG.fullmatch(tag)
        if not match or release["html_url"] != f"{REPO}/releases/tag/{tag}":
            continue
        version = match[1]
        filename = f"Inklane-Setup-{version}-x64.exe"
        asset = next((a for a in release["assets"] if a["name"] == filename
                      and a["state"] == "uploaded" and a["size"] > 0
                      and a["browser_download_url"] == f"{REPO}/releases/download/{tag}/{filename}"), None)
        if asset:
            return release, asset, version
    raise ValueError("No published Inklane release contains a matching Windows installer.")


def verify_installer_download(asset, token=None):
    # Check the same public, unauthenticated route a visitor will use.
    for delay in (0, 2, 6):
        if delay:
            sleep(delay)
        try:
            with urlopen(Request(asset["browser_download_url"], method="HEAD"), timeout=20) as response:
                length = response.headers.get("Content-Length")
                if response.status != 200 or (length and int(length) != asset["size"]):
                    raise ValueError("The published installer failed the public download check.")
                return
        except HTTPError as error:
            if error.code not in TRANSIENT_DOWNLOAD_ERRORS:
                raise
        except (URLError, TimeoutError):
            pass

    # A failing gateway can affect the browser URL while GitHub still serves the
    # exact uploaded binary through its release asset endpoint.
    asset_url = f"https://api.github.com/repos/dwolken/inklane/releases/assets/{int(asset['id'])}"
    request = Request(asset_url, headers={"Accept": "application/octet-stream",
                                          "Range": "bytes=0-1", "User-Agent": "Inklane-Pages"})
    if token:
        request.add_unredirected_header("Authorization", f"Bearer {token}")
    with urlopen(request, timeout=30) as response:
        size_matches = (response.status == 200 and response.headers.get("Content-Length") == str(asset["size"])
                        or response.status == 206 and response.headers.get("Content-Range") == f"bytes 0-1/{asset['size']}")
        if not size_matches or response.read(2) != b"MZ":
            raise ValueError("GitHub's release asset endpoint did not serve the expected Windows installer.")
    print("The public download probe timed out or returned a temporary error. Verified the binary through GitHub's release asset API.")


def published_installer():
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "Inklane-Pages"}
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    with urlopen(Request(API, headers=headers), timeout=30) as response:
        release, asset, version = choose_release(json.load(response))
    verify_installer_download(asset, token)
    return release, asset, version


def icon(paths, size=18):
    return (f'<svg aria-hidden="true" focusable="false" width="{size}" height="{size}" '
            'viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" '
            f'stroke-linecap="round" stroke-linejoin="round"><path d="{paths}"/></svg>')


def build(output):
    release, asset, version = published_installer()
    installer = asset["browser_download_url"]
    assets = output / "assets"
    assets.mkdir(exist_ok=True, parents=True)
    for name in ("site.css", "language.js", "demo.js"):
        copyfile(SOURCE / name, assets / name)
    build_workspace(output)
    copyfile(ROOT / "brand/motion/inklane-sting.mp4", assets / "inklane-sting.mp4")
    for name in ("inklane.svg", "inklane.ico"):
        copyfile(ROOT / "src/Inklane/Assets" / name, assets / name)
    for name in ("notebook.png", "paper.png", "image.png"):
        copyfile(ROOT / "docs/screenshots" / name, assets / name)
    template = Template((SOURCE / "page.html").read_text(encoding="utf-8"))
    strings = {lang: json.loads((SOURCE / f"{lang}.json").read_text(encoding="utf-8"))
               for lang in ("en", "de")}
    if strings["en"].keys() != strings["de"].keys():
        raise ValueError("The two website translations have different keys.")
    for lang, copy in strings.items():
        german = lang == "de"
        dest = output / "de" if german else output
        dest.mkdir(exist_ok=True)
        canonical = SITE + ("de/" if german else "")
        data = {
            "@context": "https://schema.org", "@type": "SoftwareApplication", "name": "Inklane",
            "url": canonical, "description": copy["description"], "operatingSystem": "Windows 11 x64",
            "applicationCategory": "ProductivityApplication", "softwareVersion": version,
            "downloadUrl": installer, "screenshot": SITE + "assets/notebook.png",
            "inLanguage": ["en", "de"],
        }
        status = "stable"
        if release["prerelease"]:
            status = "rc" if "-rc." in version else "preview"
        values = {k: escape(v, quote=True) for k, v in copy.items()}
        values.update(
            lang=lang, base="../" if german else "./", site=SITE, canonical=canonical,
            locale="de_DE" if german else "en_US", other_url="../" if german else "./de/",
            other_lang="en" if german else "de", other_label="English" if german else "Deutsch",
            repo=REPO, installer=installer, release_url=release["html_url"],
            version=version, status=values[status],
            signing=values["unsigned" if re.search(r"\bunsigned\b", release["body"] or "", re.I)
                           else "signing_unknown"],
            structured_data=json.dumps(data, ensure_ascii=False).replace("<", "\\u003c"),
            expand_icon=icon("M14 4h6v6m0-6-7 7M10 20H4v-6m0 6 7-7", 16),
            arrow_icon=icon("M5 12h14m-6-6 6 6-6 6", 16),
            download_icon=icon("M12 3v12m-5-5 5 5 5-5M4 16v4h16v-4"),
        )
        (dest / "index.html").write_text(template.substitute(values), encoding="utf-8")
    (output / "sitemap.xml").write_text(
        '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
        f'  <url><loc>{SITE}</loc></url>\n  <url><loc>{SITE}de/</loc></url>\n</urlset>\n',
        encoding="utf-8")
    (output / "robots.txt").write_text(f"User-agent: *\nAllow: /inklane/\nSitemap: {SITE}sitemap.xml\n",
                                       encoding="utf-8")
    print(f"Built English and German pages for {release['tag_name']} ({asset['size']:,} bytes) in {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".scratch/website/inklane")
    parser.add_argument("--workspace-only", action="store_true", help="Build the offline app without fetching release metadata.")
    args = parser.parse_args()
    (build_workspace if args.workspace_only else build)(args.output)
