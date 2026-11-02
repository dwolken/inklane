# Inklane 1.0.0-rc.3 build notes

This Windows candidate adds review of existing PDF pages and Office documents. Open a PDF, pin an explanation or draw a correction, and save the editable annotations beside the original. Word and PowerPoint reviews use a fixed PDF edition. Select an export from the authoring app or create a new copy with an existing standard LibreOffice installation. Some files, including documents containing links, macros or fields, require an author-exported PDF.

**More actions → Export feedback** writes a report with the source filename, SHA-256, page dimensions and precise annotations. It includes the contents of collapsed notes and the sampled geometry of handwriting. Keep the editable sidecar to resume reviewing. External PDF markup requires this build. Earlier Inklane notebook PDFs and image sidecars remain supported.

The Windows setup is an unsigned, self-contained x64 application for Windows 11. See the [validation record](validation.md) for the scope of local testing.
