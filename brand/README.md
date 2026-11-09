# Inklane identity assets

The symbol joins a pen, a rising ink gesture and an open context frame. The outlined wordmark uses Manrope with optical pair spacing and a shaped lowercase l terminal. Artwork is in `src/Inklane/Assets/`; the [identity sheet](identity-sheet.png) shows the light, dark and compact applications.

The `light` SVG/PNG suffix means dark artwork for a light surface; `dark` means white artwork for a dark surface. The mono SVGs inherit `currentColor`. Symbol viewBox: `0 0 64 64`; lockup: `0 0 247.374 64`; wordmark: `0 0 170.374 64`. The light accent is `#5633FF`, the dark accent `#9B87FF`, and the icon background `#111114`.

`identity.py` is the editable vector source. It requires Python 3.10+ and **fontTools** (verified with 4.68.0). It builds SVGs, Android vector/adaptive icons, motion geometry and the standalone editable film. The bundled Manrope variable TTF is unchanged; its WOFF2 is a format conversion using fontTools and Brotli. Both retain the [SIL Open Font License](type/OFL.txt). Marketing can use the local WOFF2; outlined identity artwork needs no installed font.

```powershell
python brand/identity.py
node brand/render-assets.mjs
```

Raster export uses the website's locked `playwright-core` dependency and installed Microsoft Edge. Set `BROWSER_PATH` for another Chromium executable. This workflow was verified on Windows. `--platforms=web`, `apple`, `browser`, or a comma-separated list limits exports. The optional `windows` target regenerates transparent lockup PNGs and the ICO master; the default preserves those masters.

Platform artwork uses the same paths: iOS and touch icons are opaque squares, macOS includes outside clear space, and Android/PWA maskable icons keep the mark within the safe area. Browser 16px artwork uses the optical small-size master. The Linux launcher and standard PWA icons use a contained dark tile.

The [promotional sting](motion/README.md) includes a complete rendered MP4, a silent version, editable source and chronological previews. It is separate from the brief application cold-launch treatment.
