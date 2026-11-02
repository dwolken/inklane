import { chromium } from 'playwright-core';
import { readFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const mark = await readFile(new URL('../../src/Inklane/Assets/inklane.svg', import.meta.url), 'utf8');
const output = new URL('../editor/icons/', import.meta.url);
await mkdir(output, { recursive:true });
const browser = await chromium.launch(process.env.BROWSER_PATH ? {executablePath: process.env.BROWSER_PATH} : {channel: process.platform === 'win32' ? 'msedge' : 'chrome'});
try {
  for (const size of [180,192,512]) {
    const page = await browser.newPage({ viewport: {width:size,height:size}, deviceScaleFactor:1 });
    await page.setContent(`<style>*{box-sizing:border-box}body{margin:0;display:grid;place-items:center;height:100vh;background:#29253e}svg{width:76%;height:76%}</style>${mark}`);
    await page.screenshot({path:fileURLToPath(new URL(`app-${size}.png`,output))}); await page.close();
  }
} finally { await browser.close(); }
