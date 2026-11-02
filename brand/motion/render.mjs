// Run after installing the website's locked browser-test dependencies.
import { createSound } from './score.mjs';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, extname, join, resolve } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const requireWebsite = createRequire(new URL('../../website/package.json', import.meta.url));
const { chromium } = requireWebsite('playwright-core');
const source = fileURLToPath(new URL('./inklane-sting.svg', import.meta.url));
const output = resolve(process.argv[2] ?? fileURLToPath(new URL('./inklane-sting.mp4', import.meta.url)));
if (extname(output).toLowerCase() !== '.mp4') throw new Error('Choose an .mp4 output path.');
const fps = 60;
const seconds = 3.6;

const temp = await mkdtemp(join(dirname(output), '.inklane-sting-'));
let browser;
try {
  const sound = join(temp, 'fold.wav');
  await writeFile(sound, createSound(seconds));
  const engine = process.env.BROWSER_PATH ? { executablePath: process.env.BROWSER_PATH } :
    { channel: process.platform === 'win32' ? 'msedge' : 'chrome' };
  browser = await chromium.launch({ ...engine, headless: true });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1, reducedMotion: 'no-preference' });
  await page.goto(pathToFileURL(source).href);
  await page.evaluate(async () => { document.querySelectorAll("svg").forEach(svg => svg.pauseAnimations()); await document.fonts.ready; });
  const encoder = spawn(process.env.FFMPEG_PATH || 'ffmpeg', [
    '-v', 'error', '-y', '-f', 'image2pipe', '-framerate', String(fps), '-c:v', 'png', '-i', 'pipe:0',
    '-i', sound, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '16', '-preset', 'medium',
    '-vf', 'scale=out_color_matrix=bt709', '-colorspace', 'bt709', '-color_trc', 'iec61966-2-1',
    '-color_primaries', 'bt709', '-c:a', 'aac', '-b:a', '224k', '-movflags', '+faststart', join(temp, 'sting.mp4'),
  ], { stdio: ['pipe', 'ignore', 'inherit'], windowsHide: true });
  const completed = once(encoder, 'close');
  // A missing encoder or broken pipe should fail the export and close the browser.
  let pipeFailure;
  encoder.stdin.on('error', error => { pipeFailure = error; });
  try {
    for (let frame = 0; frame < Math.round(fps * seconds); frame++) {
      await page.evaluate(t => document.querySelectorAll("svg").forEach(svg => svg.setCurrentTime(t)), frame / fps);
      const pixels = await page.screenshot();
      if (pipeFailure || encoder.exitCode !== null) throw pipeFailure ?? new Error('FFmpeg closed before the final frame');
      if (!encoder.stdin.write(pixels)) await once(encoder.stdin, 'drain');
    }
    encoder.stdin.end();
    const [code] = await completed;
    if (code !== 0) throw new Error('FFmpeg exited with code ' + code);
    await rename(join(temp, 'sting.mp4'), output);
  } finally {
    if (encoder.exitCode === null) encoder.kill();
  }
} finally {
  await browser?.close();
  await rm(temp, { recursive: true, force: true });
}
