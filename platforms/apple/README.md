# Inklane for macOS and iOS

The Xcode project builds the local Inklane workspace into a macOS app or an iPhone and iPad app. The document canvas uses system WebKit. Its rendering code, PDF worker, fonts and example documents ship in the bundle. Opening the app requires no account or network connection.

Open a document with the system file chooser. Use **Save** to choose a destination in the macOS Save panel or the iOS Files picker. Cancelling leaves the edit available in the workspace. **Documents** lists recoverable local drafts. Export an editable copy to keep work independently of app storage.

## Build and run

Use Xcode with the macOS or iOS Simulator SDK, Python 3.9+ and Node.js 22+. From the repository root:

```sh
npm ci --prefix website --ignore-scripts --no-audit
python3 website/build.py --workspace-only --output platforms/apple/Workspace
open platforms/apple/Inklane.xcodeproj
```

Select the **Inklane** scheme, choose **My Mac** or an **iPhone simulator** and run. Choose **Product → Test** to exercise the bundled application and the native file export bridge. On macOS the **InklaneMacUI** scheme also drives the system Save and Open panels from a separate UI test process. The deployment targets are macOS 14 and iOS 17. Installing on an iPhone or iPad requires a development signing identity and provisioning.

WebKit loads the bundled assets from a read-only listener bound to the device’s IPv4 loopback interface. The app retains its chosen port so the WebKit storage origin stays the same on later launches. Only bundled editor assets are served. Document bytes reach the host through a bounded message bridge when the user saves a file.

**Export review PDF** prepares marked pages and a complete, paginated remarks appendix, then opens the macOS Save panel or iOS Files picker. Choose all pages or the current page. The PDF is a visible copy; keep the portable `.inklane` workspace for editing. Direct printing remains available from the browser and Windows editions. No App Store or notarized Mac package is published by this project’s CI.
