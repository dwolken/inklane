# Inklane 0.1.0-alpha.1

Initial private Windows 11 x64 evaluation release.

Includes PNG/JPEG folder viewing with editable JSON annotations; A4/A5 PDF notebooks with millimeter-based paper patterns; mouse/stylus ink, editable text, anchored expandable notes, selection/movement/erasing, undo/redo, local recovery, English/German UI, and system/light/dark appearance.

Extract the entire ZIP and run `Inklane.exe`. The .NET runtime is included. The executable is unsigned; this is a portable alpha, not an installed or production-certified application.

The release workflow builds and runs the persistence, WPF surface and document-lifecycle suites, starts the packaged application, and checks that it loads its own bundled runtime. The repository's validation record distinguishes automated checks from actual desktop inspection and untested devices.

Known boundaries: only Inklane notebooks reopen for PDF editing; external PDF edits are not merged. Third-party viewers vary in annotation support and do not reproduce note cards. Physical pen pressure, printer hardware, Narrator and mixed-monitor DPI transitions remain unverified. Print output uses 150 DPI rasterization. PDFsharp 6.2.4 is pinned under MIT and is no longer maintained upstream. Keep backups of important documents while evaluating this alpha.
