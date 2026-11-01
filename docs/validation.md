# Validation record

This document records observed results, not feature intentions. The application is an initial alpha until the core workflows have been exercised and outstanding limitations have been reviewed.

## Repeatable checks

- `./scripts/build.ps1`: locked dependency restore, Release compilation, and the persistence, WPF surface, and document lifecycle regression suites. Each run retains its fixtures in a separate `.scratch/test-runs/` directory.
- `./scripts/package.ps1`: repeats the build checks, publishes the complete Windows x64 runtime, launches that packaged `Inklane.exe` with the startup-check argument, and creates a ZIP only after a successful startup result.
- `.github/workflows/windows.yml`: runs the same commands on a Windows runner and retains the packaged output as a private repository workflow artifact. Artifact upload does not create a release. Only a `v*` tag push can start the separate prerelease job, after the Windows job passes. The release job verifies repository privacy, checks the archive's version and SHA-256, then publishes the private prerelease using `docs/release-notes.md`. Only that job has repository write permission.

The startup check establishes that the packaged process initializes. It is not a substitute for manually inspecting document editing, dialogs, and rendering. A successful build does not establish pen pressure or printer behavior.

The workflow passed local static validation with actionlint 1.7.12, and its embedded PowerShell release script passed syntax parsing. The first [private Windows Actions run](https://github.com/dwolken/inklane/actions/runs/41332573422) completed successfully on 2026-11-01, including build, regression suites, self-contained startup and artifact upload. Release publication is a separate tagged workflow.

## Acceptance evidence

Local automated checks on 2026-11-01, Windows 11 (build 26300), .NET SDK 10.0.402:

- Release compilation completed with zero warnings and zero errors.
- Persistence suite: **14/14 checks passed**. These exercise JSON/PDF round trips, complete Unicode multiline note data, A4/A5 dimensions and actual 5/10 mm grid operators, standard annotation appearances, atomic replacement and failed writes, original-image protection, malformed/encrypted inputs, and external changes to PDF resources, page rotation, or annotations.
- WPF integration suite: **21/21 checks passed**. These exercise real text/note controls, anchor preservation, note readability at different zooms, movement rollback, selection/deletion, native stroke collection and pressure rendering, clean document state, history, Fluent light/dark control templates, paper-dialog state and millimeter validation, 760 × 560 and 1200 × 880 layouts in both languages, editing/full-page fit, complete English/German/fallback strings, and note-card bounds in short or scrolled viewports.
- Document lifecycle suite: **15/15 checks passed**. It uses the real window model and filesystem, substituting user decisions to check unchanged documents, canceled switching, failed saves, same-file reopening, notebook JSON import, sidecar collisions, failed recovery replacement, stale recovery after undo, invalid input, wrong file extensions, exports over active documents, changed source images, and safe rollback at document limits.
- The final combined Release run passed **50/50 checks**, with zero warnings and zero errors.
- Self-contained package startup passed with the system German language and system appearance. The startup result confirmed a laid-out 1200 × 768 main window at 125% DPI and a 794 × 1123 page render. The loaded runtime directory was inside the package, rather than the installed development runtime.
- The generated ZIP was extracted into a separate `.scratch/` directory and its `Inklane.exe` launched from that extracted folder with English/light overrides. It passed the same startup check and loaded its own extracted runtime.

The WPF suite invokes control APIs on an STA thread. It does not simulate a physical tablet or establish that all mouse/keyboard workflows are usable. Actual visual inspection is recorded separately below. Local package startup was checked on the development host, not on a second clean Windows computer.

Tests use isolated per-run fixtures, retaining the atomic-overwrite assertions. Packaging uses the SDK's `--artifacts-path` option in `.scratch/package/` so a running development UI cannot lock its build output. The Windows RID is included in both application and Core dependency locks; the successful package run used locked restore throughout.

## Actual desktop and independent PDF checks

On the Windows 11 host at 125% display scaling:

- Launched the real application, drew with mouse input, inserted editable text, created and edited a multiline note, expanded/collapsed it, saved a notebook through the native file dialog, and reopened its editable content. Added and saved a second page and navigated between pages.
- Opened a PNG, drew and saved its sidecar, navigated to a JPEG and back in the same window, and observed the recovered stroke. An independent hash comparison confirmed the original PNG was unchanged.
- Inspected English/light and German/dark workspaces, active tools, toolbar controls, the paper dialog and its open pattern list, native file dialogs, full-page and editing fit, and expanded note cards. Automated WPF checks additionally exercised paper presets/custom input and smaller layouts; those checks are not represented as physical pointer interaction.
- Corrected a native ink color serialization failure found by mouse drawing, restored Fluent control styles, constrained initial window size to the work area, and corrected note cards extending beneath the viewport at full-page zoom. The final actual window showed the reopened note fully inside the workspace at 34% zoom. The committed [screenshot](screenshots/notebook-note-light.jpg) is a capture of this application window, not a mockup.
- Used the native Windows print dialog with **Microsoft Print to PDF** to print the two-page notebook. Independent pypdf inspection measured saved pages at 210.000144 × 297.000083 mm and printed pages at 210.000060 × 297.000087 mm. Maximum rounding error was below 0.00015 mm.
- Independent Poppler raster comparison at 150 DPI found identical ink bounds; typed text differed vertically by one pixel (0.169 mm) and was not clipped. The printed second page was blank. Print output contains raster pages; the normal saved notebook retains standard editable annotations and its JSON data.
- Independent inspection of the UI-created notebook confirmed exact typed text, the full 197-character Unicode note, and its anchor at 126.657754 × 145.046791 mm. The final format reopened successfully in the actual application.

Temporary inputs and inspection outputs remain in `.scratch/`; they are not distributed as user documents. Actual pointer inspection covered representative workflows, not every combination of page size, hardware, language, theme and window size.

## Environment-dependent checks

Physical pen pressure, tablet eraser behavior, physical printer scale, screen-reader use, and multiple physical displays/DPI settings require appropriate hardware or explicit test setups. Record them as unverified until exercised.

The private repository's SSH identity is `dwolken`; the available GitHub CLI identity is `wol230296` and could not access `dwolken/inklane` at initial inspection. Remote Git uses the verified private SSH identity; repository inspection and issues use the connected `dwolken` account. The hosted workflow uses its scoped repository token.

Remaining release work is tracked in [device/accessibility validation](https://github.com/dwolken/inklane/issues/1), [PDF dependency maintenance](https://github.com/dwolken/inklane/issues/2), and [signed distribution/clean-machine validation](https://github.com/dwolken/inklane/issues/3).

## Distribution scope

The initial distributable is a portable Windows x64 folder and ZIP. Keep all files in the folder together. It includes the .NET desktop runtime, so the target computer does not need the development SDK. The package does not register file associations or require an installer. Signing and a verified installer are outside the checks currently recorded here.
