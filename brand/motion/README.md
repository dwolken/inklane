# Inklane motion mark

[Watch the film](inklane-sting.mp4) · [Open the editable vector animation](inklane-sting.svg)

The camera opens inside the diagonal fold of the [Inklane mark](../../src/Inklane/Assets/inklane.svg). Two faces turn around the shared edge as the camera pulls back. The film settles on the original mark and wordmark.

The MP4 runs for 3.6 seconds at 1920 × 1080 and 60 fps. Its original stereo score combines a textured brush with two resonances. The standalone SVG is silent and shows the final lockup immediately when the viewer requests reduced motion.

## Edit and render

Edit the fold, camera keys and typography in [inklane-sting.svg](inklane-sting.svg). The camera uses the same 48-unit coordinate space as the mark. Edit the sound timing and voicing in [score.mjs](score.mjs). No external audio samples are needed.

Install Node.js, Microsoft Edge or Chrome, and FFmpeg with the libx264 encoder. From the repository root run

```powershell
npm ci --prefix website --ignore-scripts
node brand/motion/render.mjs
```

The renderer seeks each SVG timeline at 60 fps, synthesizes the score at 48 kHz and encodes the MP4. An optional argument sets the output path. Use `BROWSER_PATH` or `FFMPEG_PATH` to select a local executable. The Windows master uses Segoe UI Variable Display. Other systems use the local font fallback.
