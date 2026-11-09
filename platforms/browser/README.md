# Inklane Web Review

A local WebExtensions development build for placing remarks, text and ink on webpages. Activate it with the browser toolbar button. There is no account, upload, server, telemetry, or browser-store release.

## Build and load

From the repository root:

```powershell
python platforms/browser/package.py
```

The output is under `.scratch/browser-package/` by default. Pass another output directory as the script argument if needed. The script creates unpacked Chromium and Firefox directories and unsigned ZIP archives.

For Chrome, Edge, Brave or Opera, open the browser's extensions page, enable developer mode, choose **Load unpacked**, and select the generated `chromium` directory. These browsers share the target extension API; the automated installed-extension workflow was run in Chromium, not individually in each branded browser.

For Firefox, open `about:debugging`, choose **This Firefox → Load Temporary Add-on**, and select the generated `firefox/manifest.json`. Temporary add-ons disappear when Firefox closes. The temporary-installed Firefox action, storage, private editor, screenshot, and report workflows were verified. Permanent Firefox distribution needs signing. Neither store publication nor signing is part of this development package.

## Working with a page

Click Inklane's browser toolbar button on an HTTP or HTTPS page. The palette provides **Browse**, **Select**, **Pen**, **Text**, and **Remark**. Select an existing object to edit, recolor, resize its text layout, duplicate or delete it. Drag a mark to move its anchor. Notes open in a resizable card with full text. Undo and redo cover annotation edits; text fields retain ordinary text keyboard behavior.

Local saves happen automatically. Reopen the same webpage and activate Inklane again to load its review. The Help screen lists saved pages and supports deleting their local reviews. Deletion shares the save queue and retains a revision tombstone, so an old tab cannot silently recreate deleted work. Concurrent saves are serialized, and a stale tab is asked to export or reload instead of overwriting a newer review.

Anchors use a DOM selector and a short exact content excerpt. They follow scrolling and element layout changes. If the selector changes, a unique matching excerpt can reconnect it. A changed or ambiguous passage leaves a dashed, detached marker at its original document position. Move that marker onto the intended content to reconnect it. A mark on blank page space uses a fixed document position.

## Export and return

| Action | Result |
| --- | --- |
| All annotations | `.web-review.json` with all annotation content and web anchors; import it on the matching page to continue. |
| Visible area | A standard portable `.inklane` image workspace containing an unmarked screenshot and separate editable visible annotations. Existing Inklane schema 2 readers can open it. Offscreen annotations remain in the full JSON review. |
| Print review report | A local report tab containing every annotation in full, its referenced excerpt, and ink geometry. Print or save as PDF with the browser. The report does not include the full original webpage. |

A visible-area export is a fixed snapshot; it does not retain live DOM anchors. It uses the browser's actual visible-tab capture, including visible page content. The tab must remain active and its page address, viewport, scroll position, and resolved annotation positions unchanged during capture. After a same-document page-address change, edits, save, and source capture are blocked. Export the original review as JSON if needed, then use **Open current page review** to switch explicitly. The extension overlay is removed from the captured source so exported marks do not appear twice.

## Permissions and boundaries

The manifest requests only `activeTab`, `scripting`, and `storage`; it has no host-permission list or automatic content scripts. Activation grants temporary tab access. Local storage belongs to the extension's browser profile; there is no sync storage. The overlay uses a closed shadow root and text-only rendering. Remark titles and bodies are edited in an extension-origin frame: webpage keyboard listeners cannot observe those keystrokes. Private text travels only through extension runtime messages, using a random session nonce bound to the tab and editor frame; it is never sent through page `postMessage`. Temporary editor sessions remain in extension session storage until expiry or browser close. Visible placed annotations remain part of the on-page review.

Stored page URLs omit username/password, fragments, common tracking parameters, and named token/session/login parameters. Other query parameters distinguish reviews. Hash-only application routes share a review collection. URL redaction is not a general secret-detection system. Anchor excerpts and screenshot content can contain private material; exports are made only on explicit user actions.

Browser-internal pages, extension stores and built-in PDF viewers may reject injection. The extension explains that restriction. Cross-origin frames, canvas content, closed shadow components, transformed documents, and major page revisions are not guaranteed to support precise anchoring. Site scripts can still change or remove the extension's host element. No extension can promise an unchanged location on every future page revision.

Limits are 500 objects, 100,000 total ink samples, and a 2 MiB character-count bound per review. Browser-profile storage has its own quota. Duplication validates the entire candidate review before changing valid work. Save failures remain visible and retain current in-memory edits for export. Export a backup before clearing extension/browser data.

API references: [Chrome activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab), [Mozilla script injection](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/scripting/executeScript), [Chrome extension storage](https://developer.chrome.com/docs/extensions/reference/api/storage).

## Verification

```powershell
npm ci --prefix website --ignore-scripts
node platforms/browser/test.mjs
node platforms/browser/test-firefox.mjs
```

The Chromium check needs a recent Playwright Chromium build supporting DevTools extension loading/action commands. Set `BROWSER_PATH` to select that executable. The Firefox check needs geckodriver 0.37.1 on PATH (or `GECKODRIVER` set to its executable); CI downloads the official release with its pinned SHA-256 digest. Firefox uses the installed Playwright Firefox binary by default; set `FIREFOX_PATH` to select another executable. Both checks create isolated browser profiles and use synthetic local webpages.

The installed Chromium test uses the production manifest with no added host permissions. It exercises the real extension action, closed shadow UI, creation/formatting, DOM reflow and scrolling, saved reopening, detached-anchor detection and reconnection, duplication/deletion/undo/redo, actual visible-tab capture, portable model validation, full JSON and PDF report output, and stale-save rejection. Additional checks cover hostile page keyboard listeners, current-URL capture/save guards after SPA navigation, duplication at capacity, private editor frame boundaries, queued delete/save races, and deletion tombstones. The captured source is checked for accidental baked-in editable text.

The Firefox 157.0 check temporarily installs the unmodified Firefox package and activates its real toolbar action through geckodriver. It verifies German Unicode input, a hostile page keyboard listener receiving no private editor keystrokes, real extension local storage and reopening, full JSON export, actual visible-tab capture with an editable portable annotation, the extension report with Firefox PDF output, and refusal to capture a different SPA page into the original review. Desktop and narrow-viewport screenshots (500 CSS px on Windows) were visually inspected. This does not verify a physical phone or signed store distribution. Output and synthetic review data stay in ignored `.scratch/` directories.
