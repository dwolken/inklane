# Inklane

[Product website](https://dwolken.github.io/inklane/) · [Windows downloads](https://github.com/dwolken/inklane/releases)

Inklane is a local Windows 11 workspace for marking up images and writing in printable PDF notebooks. Draw directly on a detail, add editable text, or pin a longer observation to a numbered point. The notes panel lets you search those observations and jump back to the drawing, even on a different page.

![An Inklane notebook showing a courtyard section and its cross-page notes index](docs/screenshots/notebook.png)

## Install

Choose the **Inklane-Setup** x64 executable on the [Windows releases page](https://github.com/dwolken/inklane/releases). Run Setup and choose **Install**. Open **Inklane** from Start. The installer includes the .NET desktop runtime and installs for the current Windows user. Uninstall through **Settings → Apps → Installed apps**.

The candidate is unsigned. Windows may display the publisher as unknown. A portable ZIP and SHA-256 checksums are also available on the release page. For portable use, extract the complete folder and launch `Inklane.exe`.

## Two ways to work

**Images.** Choose **Open an image** on Home. PNG and JPEG files open at a scale that shows the whole image. Use ← and → to browse other images in the folder. Click the image counter to jump to a filename. The list follows Explorer-style numeric order and updates when files are added or removed. Save writes the editable ink, text and notes to a neighboring `.inklane.json` file. Keep the image and its sidecar together. **More actions → Export annotated PNG** creates a separate image for sharing.

![An architectural plan with editable ink and numbered image notes in the German dark workspace](docs/screenshots/image.png)

**Notebooks.** Choose **New notebook**, then select A4 or A5, orientation, and blank, ruled or squared paper. The live preview shows the result. Patterned paper supports 5 mm, 10 mm and custom spacing with adjustable margins. Click the notebook title to rename it. The title stays separate from the PDF filename and location. New pages inherit the current paper. Click the page counter to jump, or right-click it to duplicate, reorder or remove pages. These commands are also available under **More actions → Pages**. The initial writing view fills the available width up to 140%. Scroll down the sheet to continue writing. The zoom menu also offers full width, whole page and actual size.

**Anchored notes.** Choose **Note** and click a point on the document. Type in the card and collapse it to its number. Click the card heading to name it, or choose **Rename note** from its context menu. Unnamed notes keep their number. Use **Notes** in the header to search note titles and contents or return to an anchor. Reading a note leaves the saved state clean. An explicit save retains the card presentation alongside its text and location.

**Editing.** Pen draws with a mouse or supported Windows stylus. Select lets you lasso ink or move text and note handles. Text inserts or edits keyboard text. Eraser removes strokes. Pan moves the viewport. The footer shows color and size controls for the active writing tool. Home lists up to six recently opened or saved documents on this computer.

**Clipboard.** Right-click an object or use **More actions → Edit**. In Select, Ctrl+click to build a selection, or use Ctrl+A for the whole page. Copy and paste selected ink, text, pictures and notes between pages, images or Inklane windows. Click inside a text field to copy or paste just the selected words.

Paste a screenshot onto the current page, then move it in Select or resize it by its corner. From Home, Ctrl+V starts a notebook with the clipboard contents. For a flattened copy, choose **Copy annotated image** or **Copy page as image** in the Edit menu. **Copy original image** copies the source pixels without markup. Image saves continue to use a sidecar.

| Shortcut | Action |
| --- | --- |
| Ctrl+O / Ctrl+N | Open a file / create a notebook |
| Ctrl+S / Ctrl+Shift+S | Save / Save as |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste |
| Ctrl+D / Ctrl+A / Delete | Duplicate / select all / delete selected objects |
| F2 / Esc | Edit selected text or a note title / finish editing or clear selection |
| Arrow keys / Shift+arrow | Nudge selected objects / larger step |
| Left / Right | Browse folder images when the canvas has no selection |
| Shift+F10 | Open the canvas context menu |
| Alt+Left / Alt+Right | Previous / next image or page |
| Ctrl+0 / Ctrl+1 | Fit page / fit width |
| Ctrl+wheel | Zoom about the pointer |
| P / V / E / T / N / H | Pen / Select / Eraser / Text / Note / Pan |
| Ctrl+P | Print |

Tool shortcuts apply outside text entry. Appearance follows Windows by default. **More actions** contains light/dark overrides, English/German language options, export and print. Language changes take effect on the next launch.

## Files and compatibility

A notebook PDF contains standard Ink, FreeText and Text annotations, vector paper, placed pictures, and an attached JSON model for editable reopening in Inklane. Other PDF readers can display the page and may expose annotation contents. Their note presentation varies. Editable PDF reopening covers notebooks saved by Inklane, including the original alpha format released in November 2026. A title or placed image uses the extended editable format and needs RC.2 or newer to reopen in Inklane. Earlier notebooks and sidecars still open. Inklane rejects detected external changes to protected PDF content. Keep third-party edits in a separate copy.

Normal saves use atomic replacement. Inklane checks image identity and detects conflicting file changes. Local recovery snapshots follow editing pauses. Documents, recovery files and recent paths stay on this computer. Settings and recovery live in `%LOCALAPPDATA%\Inklane`. Print uses the selected Windows driver with a 150 DPI raster at the document's physical dimensions.

## Build and verify

Use Windows x64, PowerShell 7 and the .NET SDK pinned in `global.json`. From the repository root:

```powershell
./scripts/build.ps1
dotnet run --project src/Inklane
./scripts/package.ps1
```

Packaging uses locked dependencies and a hash-pinned portable NSIS compiler. It builds the self-contained application, starts it, and writes an installer, ZIP and checksums in `artifacts/`. On a machine where Inklane has no registered installation, `./scripts/package.ps1 -VerifyInstaller` also exercises installation, upgrade, launch and removal. The check guards existing installations and test documents. Windows CI runs the release checks.

The [validation record](docs/validation.md) distinguishes actual desktop inspection, numerical checks and remaining device tests. See the [changelog](CHANGELOG.md), [architecture decisions](docs/architecture.md), and [dependency notices](THIRD-PARTY-NOTICES.md).
