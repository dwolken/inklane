# Architecture and format decisions

## Windows desktop and editing

Inklane targets Windows 11 with .NET 10 and WPF. WPF's built-in `InkCanvas` provides mouse/stylus input, pressure samples, selection and stroke erasing. Current stable WinUI releases do not include `InkCanvas` or `InkToolbar`; choosing WPF avoids an experimental dependency or a replacement ink engine. The application uses native Windows dialogs and WPF's DPI-independent layout. Pen pressure is implemented; physical tablet behavior requires separate hardware testing.

Sources reviewed on 2026-11-01:

- [Microsoft: WPF InkCanvas API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.inkcanvas)
- [Microsoft: stable WinUI feature support](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/what-is-supported)
- [Microsoft: WPF .NET 10 changes and Fluent styling limitations](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)

The editor and persistence share plain mutable models. All content coordinates use device-independent pixels (96 per inch); zoom and pan affect the view only. Paper spacing and margins are stored in millimeters. Conversion is `mm * 96 / 25.4` for the editor and `mm * 72 / 25.4` for PDF. Pattern lines begin at the chosen margin and stop at the opposite margin.

## PDF and editable data

PDFsharp-WPF **6.2.4** is explicitly pinned. Its downloaded NuGet manifest and tagged source license are MIT. Version 6.5 and later changed to AGPL/commercial licensing, and upstream states that the 6.2 branch will receive no further updates. Upgrading therefore requires a fresh maintenance and licensing decision. This is a known dependency limitation of the alpha, not an assumption that all PDFsharp versions have the same license.

- [PDFsharp-WPF 6.2.4 package and license](https://www.nuget.org/packages/PDFsharp-WPF/6.2.4)
- [Tagged 6.2.4 license](https://github.com/empira/PDFsharp/blob/v6.2.4/LICENSE)
- [Current PDFsharp licensing](https://docs.pdfsharp.net/General/License/License.html)
- [Upstream support statement](https://www.pdfsharp.com/)
- [Adobe PDF 1.7 reference, annotations section](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.7old.pdf)

Generated PDFs use physical page dimensions and vector paper patterns. Handwriting is represented by standard `/Ink` annotations. Typed text uses `/FreeText`, and contextual notes use `/Text` with full Unicode `/Contents`. Each annotation has a printable appearance stream; strokes and text are not drawn a second time in the page content. Stroke appearances use the same WPF pressure-dependent outline as the editor. Text appearances embed the Segoe UI glyphs used by PDFsharp. Note appearances show a small marker; full note content remains accessible as annotation data.

The complete versioned model is both an `inklane.json` attachment and a private catalog stream. Inklane reopens this model, retaining pressure, object IDs, note expansion state and editable text. Other readers may show or edit standard annotations, but their behavior and presentation vary. Third-party edits are not merged into the embedded model. Keep third-party-edited PDFs separate.

Before editable reopening, Inklane checks page count/dimensions and a versioned content fingerprint. The fingerprint follows PDFsharp's object graph through complete page dictionaries, annotations (including properties Inklane does not edit), appearance streams, fonts, images, resource dictionaries, inherited page properties and other catalog content such as attachments and document viewing settings. It sorts dictionary keys, resolves references without object numbers, frames scalar/stream values, and detects cycles. Stream bytes and filter parameters are included without implementing another PDF decoder. Numeric values are normalized to a millionth of a PDF point to tolerate insignificant serializer rounding. Informational document metadata and regenerated XMP timestamps are excluded; edits to such metadata are not preserved when Inklane rebuilds a notebook. Content changes cause a safe refusal to reopen for editing. This is a consistency safeguard, not a cryptographic signature or a guarantee about arbitrary PDF applications. Early development PDFs without fingerprint version 2 are rejected.

General PDF import/editing is deliberately unsupported in this alpha. A PDF without Inklane's versioned data is rejected rather than rasterized or reconstructed. Encrypted PDFs are rejected, including files that open without a user password, so saving cannot silently remove their security settings. Image PDF export is self-contained for viewing; its editable image workspace still depends on the original image and its JSON sidecar. Windows.Data.Pdf was considered for future foreign-PDF rendering but is not required by the implemented notebook workflow.

## Persistence and boundaries

PNG/JPEG sources are read-only inputs. JSON sidecars hold image identity (SHA-256) and all annotations; the store refuses writing annotation data over the source image. PDF export checks the source hash. Writes use a unique temporary file in the destination directory, flush it to disk, then atomically replace or move it. A failed write leaves the existing destination intact. Read-only and locked destinations produce errors.

Models are validated before saving and after loading. Limits include 1,000 pages, two million ink points, 32 MiB JSON and 128 MiB PDFs. PDF graph verification is bounded to two million visits, depth 128 and 512 MiB of referenced data (shared resources can be visited more than once). Numeric values must be finite; object identifiers must be unique. Unsupported schema versions are rejected. These are bounded desktop-document safeguards, not a promise to handle every arbitrary or malicious PDF.

## Validation

The executable regression harness checks full JSON/PDF round trips, independent A4/A5 point values, actual emitted grid operators for 5/10 mm spacing, standard annotation appearances and contents, deep cloning, atomic replacement, locked/read-only failures, original-image protection, image identity changes, malformed models and foreign/externally changed PDFs. Graph regressions modify fonts, font/image streams, page and inherited rotation, extra annotation properties, cyclic annotation references and catalog settings. An unchanged rewrite remains editable; independent pypdf verification also confirmed reopening after object renumbering. The lifecycle suite runs the actual window and filesystem with a narrow user-interaction boundary for deterministic cancel/save choices. Test output belongs under `.scratch/`. Poppler is used for independent visual rendering during development; it is not an application dependency.
