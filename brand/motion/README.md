# Inklane motion mark

[Play the sting](inklane-sting.mp4) or [open the editable vector animation](inklane-sting.svg) in a browser.

The amber reference point stays on the drawing as the note opens. Its heading becomes the margin, and the two reading lines form the lane and crease of the [desktop mark](../../src/Inklane/Assets/inklane.svg). The SVG plays once. With reduced motion enabled, it shows the final lockup immediately.

The MP4 runs for 4.2 seconds at 1920 × 1080 and 60 fps, with H.264 video and a quiet original AAC sound cue. The wordmark uses Segoe UI Variable Text in the Windows render. The SVG uses local system fonts.

## Render

Use Node.js, Microsoft Edge and FFmpeg with the libx264 encoder. From the repository root:

```powershell
npm ci --prefix website --ignore-scripts
node brand/motion/render.mjs
```

The renderer samples the SVG timeline one frame at a time and synthesizes the sound locally. An optional final argument sets the output MP4 path. `BROWSER_PATH` and `FFMPEG_PATH` can point to local executables. The render replaces the destination only after encoding succeeds.
