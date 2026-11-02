# Release candidate validation

Inklane 1.0.0-rc.1 was exercised on Windows 11 at 125% display scaling. This record separates desktop observations, repeatable regression checks and device work that remains open.

## Editing candidate · RC.2

The local Release build passed 17 persistence checks, 27 WPF/clipboard checks and 19 lifecycle checks, with zero compiler warnings or errors.

- Independent WPF editor processes on a noninteractive Windows window station exchanged selected objects through the operating-system clipboard after the source process exited. The checks compare stroke pressure and shape, positions, text and note titles, new object identities, native text selection, undo and redo. The user's interactive clipboard is separate.
- Clipboard checks exercise standard text, PNG, bitmap and device-independent bitmap data, placement in a PDF notebook, copying a flattened image, editing a photo sidecar and retaining the original image hash. A Poppler render independently reproduced the inserted test image’s color samples at its saved coordinates.
- The note index, marker and card expose the same edited title. Search by title and body, cross-page context commands, and reopen are exercised on real WPF controls. The German dark image screenshot is a native 1484 × 952 capture from the running RC.2 editor at 125% Windows scaling.
- The lifecycle suite duplicates and moves pages, checks the target page across undo/redo and reopens the saved order. It renames a notebook while retaining its original filename. Real filesystem changes exercise numeric folder ordering, added screenshots, a removed current file, navigation boundaries and cancel/save decisions before switching.
- Historical alpha and RC.1 files opened in the new reader. The released RC.1 reader refused the extended titled/image model, including an extended unsaved-work snapshot. A second window recovered the titles, picture data, positions and dirty state. Damaged recoveries stayed intact while the next valid recovery remained available. Device-specific pen and printer checks listed below still apply.

## RC.1 desktop journeys

| Journey | Observed result |
| --- | --- |
| First launch | Both entry cards are visible in English and German, including on a fresh profile. Selecting **Open an image** opened the native picker with a PNG/JPEG filter. The two cards have equal hit areas. |
| Image review | Opened a plan with two saved note markers, read a note without an unsaved-change prompt, browsed to its companion JPEG and drew a stroke. Save created a sidecar. Navigating away and back recovered the stroke. The original JPEG hash matched the saved identity. |
| Image export | Used the real document menu and save dialog to export the annotated JPEG as PNG. Independent pixel comparison found the same 1440 × 1000 dimensions and differences confined to the added stroke. |
| Notebook creation | Selected A5, landscape and squared paper in the actual German dark dialog. Its preview changed to 210 × 148 mm and revealed the 5 mm spacing and margin controls. Created the notebook, wrote with the mouse, typed German text, placed and edited a multiline note, collapsed it, added a second page and saved through Windows. |
| Reading and returning to a note | Opened a three-page A4 notebook. The default editing fit was 132% on this laptop. Opening the notes panel refitted the page to 99%. From page two, selecting a page-one entry returned to the right page and expanded the anchored card. |
| Switching and recovery | Observed the unsaved-change choice on a document switch. The lifecycle suite separately exercised cancel, failed saves, source changes, conflict handling, recovery replacement, restoration and history rollback using the real file writer and window model. |
| Small window | Resized the actual German dark editor using Windows sizing commands. The active image refitted while navigation, tool labels and contextual controls remained available. Both languages were also exercised at 760 × 560 and 1200 × 880 in WPF integration checks. |
| Print | Printed the three-page journal through the Windows **Microsoft Print to PDF** driver. All pages and the section drawing appeared in the output. Anchored notes printed as markers. |

The product screenshots are lossless PNG files captured from the running window using `PrintWindow` and the physical DWM frame bounds. The captures retain the native display resolution. The fixtures contain original architectural drawings, typed observations and real Inklane annotations. Inspect the [notebook and notes index](screenshots/notebook.png), [image note](screenshots/image.png), [first launch](screenshots/start.png), and [paper setup](screenshots/paper.png).

Visual review corrected unequal start-card hit areas, undersized tool highlights, clipped German property fields, a clipped paper selector, excessive note-body padding and an unreadable marker hover state in dark mode. The review also exposed two behavioral defects: reading a note made the document dirty, and inserting a page could attach another page's zoom to the new page. These cases now have targeted checks.

## File and print measurements

Independent pypdf inspection measured the three virtual print pages at 595.27576 × 841.89001 points, or 210.00006 × 297.00009 mm. Across repeated grid intervals at 150 DPI, the printed 5 mm grid measured 5.00013 mm vertically and 5.00004 mm horizontally. The 10 mm rules measured 10.00044 mm. The corresponding source PDF operators measured 4.99999 mm and 10.00002 mm. All three pages rendered legibly. The printed PDF contains raster pages. The saved notebook contains vector paper, standard annotations and editable data.

The showcase notebook contained 156 Ink, 40 FreeText and four Text annotations across three pages, each with an appearance. The full content of all four standard notes matched the editable model. Image identity verification independently matched the plan's SHA-256 to its sidecar.

## Responsiveness and regression checks

A 150,000-point notebook exposed full-document serialization during each keystroke in the earlier editor. With the candidate, a real WPF text edit and layout measured 2.79 ms median and 4.93 ms p95 over 24 warmed edits on this host. This measures the application callback and layout. Physical input-to-display latency requires separate measurement.

A substantial save of the same notebook took 5.81 seconds. While its STA worker wrote the PDF, the window processed 70 dispatcher callbacks. The test rejected attempts to dismiss progress or reenter Save, checked that the owner was disabled, reopened the approximately 48.5 MiB result, and compared the full model including note presentation. A locked destination retained its original hash and current edits. Unlocking it allowed a successful retry.

Release compilation completed with zero warnings and errors. All **53 application checks** passed locally, comprising 14 persistence/geometry, 24 WPF surface/workspace and 15 lifecycle checks. They include protected originals, modification detection, atomic replacement, PDF annotation appearances, Unicode data, physical paper operators, pressure geometry, undo/redo, keyboard text movement, zoom intent, note navigation, paper controls and locale fallback.

`./scripts/build.ps1` runs all three suites. `./scripts/package.ps1 -VerifyInstaller` additionally tests the published executable and setup. Runs retain isolated evidence below the ignored `.scratch/` directory.

## Installation and acceptance

The real per-user installer passed 17 lifecycle checks under a non-elevated Windows token. Verification covered the Installed apps entry, the Start shortcut's normal launch, startup using the installed bundled runtime, a locked previous version, upgrade, downgrade refusal and registered uninstall. The checks preserved test notebooks, sidecars, unknown files, settings and existing file associations. They used an isolated Windows desktop and refused to claim an existing registered installation. Visual inspection of the actual German wizard caught and corrected an encoding error in its authored text.

The release workflow repeats build, application, package and installer checks on Windows, verifies the downloaded artifact checksums, verifies the expected public repository and publishes candidate assets on a version tag. See the [Windows runs](https://github.com/dwolken/inklane/actions) and [release record](https://github.com/dwolken/inklane/releases).

The candidate awaits user acceptance. [Physical pen, printer, Narrator and mixed-DPI checks](https://github.com/dwolken/inklane/issues/1) remain open. The pinned MIT PDFsharp 6.2.4 branch requires a [long-term maintenance decision](https://github.com/dwolken/inklane/issues/2). A NuGet vulnerability query including transitive dependencies reported no listed vulnerable packages on 2026-11-01. [Trusted signing and a separate clean Windows 11 machine](https://github.com/dwolken/inklane/issues/3) remain outstanding. Windows Server CI and this Windows 11 development host cover different environments from a clean consumer PC.
