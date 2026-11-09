# Architecture and format decisions

## Windows desktop and editing

Inklane targets Windows 11 with .NET 10 and WPF. WPF's built-in `InkCanvas` provides mouse/stylus input, pressure samples, selection and stroke erasing. Current stable WinUI releases do not include `InkCanvas` or `InkToolbar`; choosing WPF avoids an experimental dependency or a replacement ink engine. The application uses native Windows dialogs and WPF's DPI-independent layout. Pen pressure is implemented; physical tablet behavior requires separate hardware testing.

Sources reviewed on 2026-11-01:

- [Microsoft: WPF InkCanvas API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.inkcanvas)
- [Microsoft: stable WinUI feature support](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/what-is-supported)
- [Microsoft: WPF .NET 10 changes and Fluent styling limitations](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)

The editor and persistence share plain mutable models. All content coordinates use device-independent pixels (96 per inch); zoom and pan affect the view only. Paper spacing and margins are stored in millimeters. Conversion is `mm * 96 / 25.4` for the editor and `mm * 72 / 25.4` for PDF. Pattern lines begin at the chosen margin and stop at the opposite margin.

## Workspace and identity

The release candidate uses one editor with distinct entry points and file semantics. A folder image keeps its original and sidecar. A notebook keeps its pages and annotations in a PDF. Home makes both tasks explicit. The header carries document actions, the rail carries direct tools, and the footer carries contextual properties and navigation. The optional notes index connects numbered markers to their document positions. These choices follow the primary/overflow distinction in [Microsoft's command-bar guidance](https://learn.microsoft.com/en-us/windows/apps/design/controls/command-bar), with markup tools and an optional navigation panel informed by [Adobe's Acrobat workspace](https://helpx.adobe.com/acrobat/desktop/get-started/learn-the-basics/workspace.html) and [Apple's Preview markup workflow](https://support.apple.com/en-sg/guide/preview/prvw11580/mac).

The Margin fold identity combines a vertical margin with a folded lane that turns toward a detail. A nib was too specific to handwriting, and a bookmark competed with the meaning of Save. The selected SVG is the editable master. The Windows icon contains eleven frames from 16 to 256 pixels. The size review used [Microsoft's app-icon guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/iconography/app-icon-design).

## PDF and editable data

PDFsharp-WPF **6.2.4** is explicitly pinned. Its downloaded NuGet manifest and tagged source license are MIT. Version 6.5 and later changed to AGPL/commercial licensing, and upstream states that the 6.2 branch will receive no further updates. Upgrading therefore requires a fresh maintenance and licensing decision.

- [PDFsharp-WPF 6.2.4 package and license](https://www.nuget.org/packages/PDFsharp-WPF/6.2.4)
- [Tagged 6.2.4 license](https://github.com/empira/PDFsharp/blob/v6.2.4/LICENSE)
- [Current PDFsharp licensing](https://docs.pdfsharp.net/General/License/License.html)
- [Upstream support statement](https://www.pdfsharp.com/)
- [Adobe PDF 1.7 reference, annotations section](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.7old.pdf)

Generated PDFs use physical page dimensions and vector paper patterns. Handwriting is represented by standard `/Ink` annotations. Typed text uses `/FreeText`, and contextual notes use `/Text` with full Unicode `/Contents`. Each annotation has a printable appearance stream; strokes and text are not drawn a second time in the page content. Stroke appearances use the same WPF pressure-dependent outline as the editor. Text appearances embed the Segoe UI glyphs used by PDFsharp. Note appearances show a small marker; full note content remains accessible as annotation data.

Named notes store their title in the standard annotation subject (`/Subj`) and their body in `/Contents`. A notebook's explicit display title is also written to PDF document information. Inklane checks it on reopen to avoid losing an external rename. A pasted picture is drawn as an ordinary PDF image beneath the ink and text. Its original PNG and placement remain editable in Inklane. [ISO 32000-1, section 12.5.6.2](https://opensource.adobe.com/dc-acrobat-sdk-docs/standards/pdfstandards/PDF32000_2008.pdf) · [PDFsharp image drawing](https://docs.pdfsharp.net/PDFsharp/Topics/Bitmap-Images/Drawing.html)

The complete versioned model is both an `inklane.json` attachment and a private catalog stream. Inklane reopens this model, retaining pressure, object IDs, note expansion state and editable text. It reads schemas 1 and 2. Writing the first explicit title or placed image uses schema 2 so older Inklane releases refuse the file before editing it. Existing schema 1 documents can still be saved as schema 1 when they use its original features. Other readers may show or edit standard annotations, but their behavior and presentation vary. Third-party edits are not merged into the embedded model. Keep third-party-edited PDFs separate.

Before editable reopening, Inklane checks page count/dimensions and a versioned content fingerprint. The fingerprint follows PDFsharp's object graph through complete page dictionaries, annotations (including properties Inklane does not edit), appearance streams, fonts, images, resource dictionaries, inherited page properties and other catalog content such as attachments and document viewing settings. It sorts dictionary keys, resolves references without object numbers, frames scalar/stream values, and detects cycles. Stream bytes and filter parameters are included without implementing another PDF decoder. Numeric values are normalized to a millionth of a PDF point to tolerate insignificant serializer rounding. Informational document metadata and regenerated XMP timestamps are excluded; edits to such metadata are not preserved when Inklane rebuilds a notebook. Content changes cause a safe refusal to reopen for editing. This is a consistency safeguard, not a cryptographic signature or a guarantee about arbitrary PDF applications. Early development PDFs without fingerprint version 2 are rejected.

## Markup on existing PDFs

The Windows editor first checks for an Inklane notebook. If the catalog identifies one, the editable model and fingerprint must pass verification. A separate path opens supported external PDFs for markup. Encrypted PDFs, including documents with an empty user password, are rejected.

The viewer gives the Windows PDF renderer an in-memory snapshot of the source. Windows supplies the displayed page dimensions after crop and rotation in 96-DPI units. Only two page rasters are cached, each at up to 192 DPI, 4,096 pixels per edge and eight million pixels. This bounds memory while keeping the page coordinates independent of the rendering scale. Page geometry and order follow the original. Windows supplies the static page appearance. Links, form filling, PDF text selection and editing of existing PDF objects are outside this workflow. [Windows PDF page API](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage)

Saving writes a separate schema 3 JSON file containing the PDF basename, SHA-256, displayed page sizes and editable markup. Opening the PDF discovers its neighboring sidecar. Opening a detached sidecar can prompt for the matching original. The hash and geometry must agree before showing markup. Source changes also block a normal save. JSON export can preserve annotations for the original revision. The renderer does not rewrite the source file or embed markup in it.

PNG export flattens the current page and visible marks. Printing renders the chosen page range one page at a time and fits oversized pages within the selected paper's printable area. An exported or printed image contains note markers. The editable sidecar retains the full note text. Image workspaces continue to use their original PNG or JPEG and accompanying JSON.

## Word and PowerPoint review copies

The development opening workflow converts supported Office files directly into a private revision-specific directory under application data. A receipt identifies the original source and verified rendering by SHA-256. The recent-file list is only navigation: its eviction does not remove or detach saved annotations. Missing LibreOffice offers an official download link and a manual-export fallback. Conversion remains local and uses the bounded, isolated renderer described below.

The development PDF handoff modifies a copy of the source PDF and adds standard annotation appearances. Coordinates are transformed from displayed crop/rotation back into PDF user space; unsupported non-unit page scaling is refused. Existing source pages and resources are retained, and full note text remains in annotation contents. An external review of an Inklane notebook strips the stale private editor marker from the handoff so it cannot falsely reopen as an unmodified editable notebook.

Office handoff edits an OOXML copy without running Office. Word receives a separate review section with annotated page images and full explanations. PowerPoint retains the original slides and appends review images with complete explanations as native comments. Original package parts remain unchanged except document/slide lists, relationships and content-type declarations needed for the appendix. These images are presentation output; Inklane data remains the editable source. This does not infer Word text-range anchors from rendered page coordinates.

DOCX and PPTX annotations belong to a fixed PDF rendering of a specific source revision. The application can pair a PDF exported by the author with its source. When LibreOffice is installed in its standard Windows location, a second option can create the PDF on this computer. Inklane records the source and PDF hashes. An updated source calls for another review copy. This preserves each mark's position even when a font, page break or slide layout changes.

Automatic conversion accepts a limited set of OOXML text, slide and embedded PNG/JPEG parts. External relationships, macros, field instructions, embedded objects and unsupported parts require an export from the authoring application. The converter takes a snapshot of the source, starts a separate headless process with a fresh temporary profile and disables macro execution. A 75-second process limit bounds conversion time. Before adopting the new PDF it checks the result with the notebook parser and the Windows renderer. It creates a new PDF and keeps the supplied Office document intact. LibreOffice is optional and is not bundled or downloaded by the application. [LibreOffice command-line parameters](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html) · [PDF export filters](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html)

## Persistence and boundaries

PNG/JPEG sources are read-only inputs. JSON sidecars hold image identity (SHA-256) and all annotations; the store refuses writing annotation data over the source image. PDF export checks the source hash. Writes use a unique temporary file in the destination directory, flush it to disk, then atomically replace or move it. A failed write leaves the existing destination intact. Read-only and locked destinations produce errors.

Models are validated before saving and after loading. Limits include 1,000 pages, two million ink points, 32 MiB JSON and 128 MiB PDFs. PDF graph verification is bounded to two million visits, depth 128 and 512 MiB of referenced data (shared resources can be visited more than once). Numeric values must be finite; object identifiers must be unique. Unsupported schema versions are rejected. These are bounded desktop-document safeguards, not a promise to handle every arbitrary or malicious PDF.

An inserted PNG may contain up to 24 million pixels and 16 MiB of encoded data, within the existing JSON size limit. The reader checks PNG dimensions and a 64-million-pixel aggregate limit before decoding embedded images.

## Validation

The executable regression harness checks full JSON/PDF round trips, independent A4/A5 point values, actual emitted grid operators for 5/10 mm spacing, standard annotation appearances and contents, deep cloning, atomic replacement, locked/read-only failures, original-image protection, image identity changes, malformed models and foreign/externally changed PDFs. Graph regressions modify fonts, font/image streams, page and inherited rotation, extra annotation properties, cyclic annotation references and catalog settings. An unchanged rewrite remains editable; independent pypdf verification also confirmed reopening after object renumbering. The lifecycle suite runs the actual window and filesystem with a narrow user-interaction boundary for deterministic cancel/save choices. Test output belongs under `.scratch/`. Poppler is used for independent visual rendering during development; it is not an application dependency.
