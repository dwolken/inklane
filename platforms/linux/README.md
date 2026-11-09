# Inklane for Linux

The Linux desktop workspace uses GTK 3 and the distribution’s WebKitGTK 4.1 engine. The launcher starts the bundled editor without an internet connection. Its local HTTP endpoint only serves packaged resources on the loopback interface. An installation reuses its own port and WebKit storage so local drafts can reopen on the next launch.

Open images, PDFs or an Inklane workspace through the document picker. Save writes a new copy at a location you choose. Existing files are protected. PDF and image annotations can travel with their source as a portable workspace or alongside it as JSON. A notebook can also open in the Windows desktop editor. **Export review PDF** saves marked pages followed by complete, paginated remarks through the native file chooser. The PDF is a visible copy; keep the portable `.inklane` workspace for editing. Direct printing remains available from the browser and Windows editions.

On Ubuntu 24.04 with Node.js, npm, Python 3 and dpkg-dev installed, build from the repository root:

```sh
npm ci --prefix website --ignore-scripts
python3 platforms/linux/package.py
sudo apt install ./.scratch/linux/inklane.deb
```

Start **Inklane** from the applications menu or run `inklane`. Remove the application with `sudo apt remove inklane`. This leaves its user-owned drafts and documents in place.

The [Linux workflow](../../.github/workflows/linux.yml) builds and installs the development package on a disposable Ubuntu runner. Its GTK test opens a notebook through WebKit’s file chooser, edits a note and checks that a native file export can be reopened. It does not upload the package.
