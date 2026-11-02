# Inklane product website

The [English site](https://dwolken.github.io/inklane/) and [German translation](https://dwolken.github.io/inklane/de/) are static HTML. Both have their own canonical URL and remain readable without JavaScript. On the neutral entry page, a German browser gets the German translation. Other languages default to English. The language switch remembers an explicit choice and preserves the current section. A direct German link stays German. Images and icons come directly from the desktop project.

From the repository root, use Python 3.9+, Node.js 22+ and an installed Edge (Windows) or Chrome (Linux).

```sh
python website/build.py
npm ci --prefix website --ignore-scripts
node website/test.mjs
node website/test-editor.mjs
python -m http.server 8000 --bind 127.0.0.1 --directory .scratch/website
```

Open [the local project URL](http://127.0.0.1:8000/inklane/). Set `BROWSER_PATH` to check another installed Chromium executable. The test loads the built site under its production base path, checks keyboard navigation, links, screenshot dimensions, translated metadata, JavaScript-disabled access and accessibility at desktop and mobile widths.

On the product page, the numbered points on the courtyard plan open editable sample notes. Choose a point again or press Escape to close it. The sample is temporary.

The [public browser editor](https://dwolken.github.io/inklane/editor/) works with local PNG and JPEG files. Open an image, place ink, text or a note, then choose **Save annotations**. Browsers with a writable file picker can save directly. Other browsers offer the JSON as a download. Verify that the download finished before closing. Open the original and its sidecar together, or open the sidecar and select the matching image. **Export feedback** downloads a portable report for a recipient. Files stay in the browser and the local file system. For development, open [the local editor](http://127.0.0.1:8000/inklane/editor/) over loopback or serve it through HTTPS for access to the browser's cryptographic APIs.

The build reads GitHub’s public release API and verifies the installer’s anonymous download URL. It prefers a published stable release with a matching x64 installer. Until one exists, it uses the most recently published matching prerelease. A failed check stops deployment. Versions and download URLs are rendered into both pages at build time.

Enable **GitHub Actions** under **Settings → Pages → Build and deployment → Source**. The Product website workflow checks pull requests, deploys changes on `main`, and refreshes after successful tagged Windows builds. Use its manual run on `main` after editing a release outside that pipeline. Deployment checks the public HTML against the tested build.
