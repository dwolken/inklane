# Inklane promotional sting

[Watch the film](inklane-sting.mp4) · [Silent version](inklane-sting-silent.mp4) · [Editable timeline](inklane-sting.html) · [Contact sheet](previews/motion-sheet.png)

The 5.2-second film introduces the pen, ink arc and enclosing frame before the outlined Inklane wordmark appears. It is intended for promotional videos and presentations. Application cold-launch feedback uses a separate brief treatment.

The frame finishes at 1.53 s and holds until 2.00 s. The composition opens from 2.00–3.40 s; overlapping letter arrivals run from 2.28–3.52 s. The complete lockup holds to 5.20 s. The original quiet contact and frame sound remains aligned to the drawing and enclosure; no external audio samples are used.

The master is 1920 × 1080 at 60 fps, H.264/yuv420p with stereo 48 kHz AAC. The silent variant contains the same video stream. Open `inklane-sting.html` directly in a browser to replay, scrub or view the dark treatment; reduced-motion preference opens the resolved still.

Edit timing and easing in `motion.mjs`, sound in `score.mjs`, and the preview template in `player.html`. Geometry and outlined lettering are in `geometry.json`. The vector identity source and licensed fonts are kept in the parent brand directory.

Render locally with the website's locked `playwright-core`, Microsoft Edge and FFmpeg:

```powershell
node brand/motion/render.mjs
```

`--previews` renders the chronological stills and contact sheet without encoding a video. No network services are used.
