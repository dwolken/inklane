// Run after installing the website's locked browser-test dependencies.
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
const seconds = 4.2;

// A dry tap at the anchor, a short brush as the note opens, then a two-tone
// release with the fold. Generate the sound directly so no samples travel with the source.
function createSound() {
  const rate = 48000;
  const samples = Math.round(seconds * rate);
  const wav = Buffer.alloc(44 + samples * 2);
  wav.write('RIFF'); wav.writeUInt32LE(wav.length - 8, 4);
  wav.write('WAVEfmt ', 8); wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(rate, 24); wav.writeUInt32LE(rate * 2, 28);
  wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34);
  wav.write('data', 36); wav.writeUInt32LE(samples * 2, 40);
  const envelope = (age, attack, decay) =>
    age <= 0 ? 0 : (1 - Math.exp(-age / attack)) * Math.exp(-age / decay);
  let seed = 4181, fast = 0, slow = 0;
  for (let i = 0; i < samples; i++) {
    const t = i / rate;
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    const noise = seed / 2147483648 - 1;
    fast += .30 * (noise - fast);
    slow += .028 * (noise - slow);
    const tap = t - .105;
    let value = envelope(tap, .0018, .038) * (.09 * Math.sin(2 * Math.PI * 740 * tap) + .07 * (fast - slow));
    value += .18 * (fast - slow) * envelope(t - .265, .013, .095);
    value += .16 * (fast - slow) * envelope(t - 1.64, .057, .15);
    for (const [start, frequency, gain] of [[2.075, 392, .067], [2.405, 587.33, .047]]) {
      const age = t - start, angle = 2 * Math.PI * frequency * age;
      value += gain * envelope(age, .008, .19) * (Math.sin(angle) + .17 * Math.sin(2 * angle));
    }
    wav.writeInt16LE(Math.round(value * 32767), 44 + i * 2);
  }
  return wav;
}

const temp = await mkdtemp(join(dirname(output), '.inklane-sting-'));
let browser;
try {
  const sound = join(temp, 'fold.wav');
  await writeFile(sound, createSound());
  const engine = process.env.BROWSER_PATH ? { executablePath: process.env.BROWSER_PATH } :
    { channel: process.platform === 'win32' ? 'msedge' : 'chrome' };
  browser = await chromium.launch({ ...engine, headless: true });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1, reducedMotion: 'no-preference' });
  await page.goto(pathToFileURL(source).href);
  await page.evaluate(async () => { document.documentElement.pauseAnimations(); await document.fonts.ready; });
  const encoder = spawn(process.env.FFMPEG_PATH || 'ffmpeg', [
    '-v', 'error', '-y', '-f', 'image2pipe', '-framerate', String(fps), '-c:v', 'png', '-i', 'pipe:0',
    '-i', sound, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '16', '-preset', 'medium',
    '-vf', 'scale=out_color_matrix=bt709', '-colorspace', 'bt709', '-color_trc', 'iec61966-2-1',
    '-color_primaries', 'bt709', '-c:a', 'aac', '-b:a', '160k', '-movflags', '+faststart', join(temp, 'sting.mp4'),
  ], { stdio: ['pipe', 'ignore', 'inherit'], windowsHide: true });
  const completed = once(encoder, 'close');
  // A missing encoder or broken pipe should fail the export and close the browser.
  let pipeFailure;
  encoder.stdin.on('error', error => { pipeFailure = error; });
  try {
    for (let frame = 0; frame < Math.round(fps * seconds); frame++) {
      await page.evaluate(t => document.documentElement.setCurrentTime(t), frame / fps);
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
