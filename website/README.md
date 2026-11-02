# Inklane on the web

The [English](https://dwolken.github.io/inklane/) and [German](https://dwolken.github.io/inklane/de/) product pages embed the same [workspace](https://dwolken.github.io/inklane/editor/) that opens local documents. The field guide is original artwork; changes to the embedded sample stay in that session unless the visitor saves a copy. The logo film loads on demand.

## Run locally

Use Python 3.9+, Node.js 22.13+ and Chromium (Edge on Windows, Chrome on Linux). From the repository root:

```sh
npm ci --prefix website --ignore-scripts
python website/build.py --output .scratch/website/inklane
python -m http.server 8000 --bind 127.0.0.1 --directory .scratch/website
```

Open [the site](http://127.0.0.1:8000/inklane/) or [the workspace](http://127.0.0.1:8000/inklane/editor/). To build only the bundled app, pass `--workspace-only`; this needs no GitHub release lookup. Serve the result over loopback or HTTPS.

## Check and deploy

```sh
node website/test.mjs .scratch/website/inklane
node website/test-editor.mjs .scratch/website/inklane
node website/test-platform.mjs .scratch/website/inklane
```

The checks use real browser engines for phone and desktop layouts, keyboard navigation, drawing, document editing and reopening, and offline PDF rendering. Set `BROWSER_PATH` for a different Chromium executable. To run the platform check with WebKit, install the matching browser with `playwright-core install webkit` from the website package and set `INKLANE_ENGINE=webkit`.

GitHub Actions builds the Pages site from `main`. The site build selects a published Windows installer with a matching release tag, filename and size, then checks its anonymous download. The website never hosts installers. The deployed HTML is compared with the tested build.

## Local files and offline access

PDF.js reads bytes selected on the device. Its code, font resources and image decoders are served from the app origin. The service worker stores only the published application files, never reviewed documents. A new worker waits for the previous app to close before changing the active cache. Document drafts are stored separately in IndexedDB; browser eviction, private browsing and clearing site data can remove them. Use a portable file for a durable backup.
