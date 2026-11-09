# Inklane for Android

This Android development app bundles the document workspace and PDF renderer in its APK. It opens images, PDFs and portable Inklane documents from the system file picker. Creating a notebook, editing marks and reopening a copy use the same document model as the browser. No account or network permission is required.

Save and export create a new file in a location you choose. To preserve existing documents, pick an empty destination. Android can return recent files or files from installed document providers. **Export review PDF** saves marked pages followed by complete, paginated remarks through the same system picker. The PDF is a visible copy; keep the portable `.inklane` workspace for editing. Direct printing remains available from the browser and Windows editions.

## Build

Use Android SDK 36, Java 17 or 21, Gradle 8.11.1, Node.js and Python 3. From the repository root run

```sh
npm ci --prefix website --ignore-scripts
gradle -p platforms/android assembleDebug
adb install -r platforms/android/build/outputs/apk/debug/*-debug.apk
```

On Windows, pass the APK's filename to `adb install`. The Gradle build generates its private copy of the workspace from the locked browser dependencies. Set the Gradle property `python` to select a different Python executable.

The [Android workflow](../../.github/workflows/android.yml) builds a debug package and exercises it on an API 36 emulator. It opens the app, annotates a notebook, cancels a save, writes a portable copy through Android's document picker, and reopens that file. The workflow does not distribute its package. No Google Play release is available.
