// Firefox content/UI check. The browser API adapter below is test-only; this
// verifies the shared DOM behavior, not installation or signing of the add-on.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
const require = createRequire(new URL('../../website/package.json', import.meta.url));
const { firefox } = require('playwright-core');
const output = resolve(process.argv[2] || '.scratch/browser-firefox'); await mkdir(output, { recursive: true });
const server = createServer((q, r) => r.writeHead(200, { 'Content-Type': 'text/html' }).end('<!doctype html><html lang="de"><meta charset="utf-8"><style>body{font:20px system-ui;margin:220px 40px 0;background:white}p{padding:20px;background:#f4f5f9;line-height:1.6}</style><h1>Feldnotizen</h1><p id="target">Die Erklärung gehört direkt an diese Textstelle.</p></html>'));
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = await firefox.launch({ executablePath: process.env.FIREFOX_PATH || undefined, headless: true });
try {
  const page = await browser.newPage({ locale: 'de-AT', viewport: { width: 1280, height: 900 } });
  await page.addInitScript(() => {
    const attach = Element.prototype.attachShadow;
    Element.prototype.attachShadow = function(options) { const root = attach.call(this, options); if (this.id === 'inklane-web-review') window.testShadow = root; return root; };
    globalThis.browser = { storage: { local: { get: async key => ({ [key]: JSON.parse(localStorage.getItem('saved') || 'null') }) } }, runtime: { getURL: path => '/' + path, sendMessage: async msg => { if (msg.command !== 'save') throw Error('Not exercised'); const state = { data: msg.data, revision: msg.revision + 1 }; localStorage.setItem('saved', JSON.stringify(state)); return { revision: state.revision }; } } };
  });
  const core = await readFile('platforms/browser/core.js', 'utf8'), content = await readFile('platforms/browser/content.js', 'utf8');
  async function inject() { await page.addScriptTag({ content: core }); await page.addScriptTag({ content }); await page.waitForFunction(() => window.testShadow?.querySelector('#status')?.textContent.includes('gespeichert')); }
  async function rect(selector) { return page.evaluate(s => { const r = window.testShadow.querySelector(s).getBoundingClientRect(); return { x:r.x,y:r.y,width:r.width,height:r.height }; }, selector); }
  async function click(selector) { const r = await rect(selector); await page.mouse.click(r.x+r.width/2,r.y+r.height/2); }
  await page.goto(`http://127.0.0.1:${server.address().port}/review`); await inject();
  await click('[data-tool=note]'); const box = await page.locator('#target').boundingBox(); await page.mouse.click(box.x+80, box.y+30);
  await page.keyboard.insertText('Präzise Rückmeldung mit Umlauten: äöü.');
  await page.waitForFunction(() => JSON.parse(localStorage.getItem('saved') || 'null')?.data?.objects[0]?.text?.includes('äöü'));
  const noteId = await page.evaluate(() => JSON.parse(localStorage.getItem('saved')).data.objects[0].id);
  await page.screenshot({ path: join(output, 'desktop-de.png') });
  await page.reload(); await inject();
  assert(await page.evaluate(id => !!window.testShadow.querySelector(`[data-object="${id}"]`), noteId));
  await page.setViewportSize({ width: 412, height: 915 }); await click(`[data-object="${noteId}"]`);
  const card = await rect('#card'), dock = await rect('.dock'); assert(card.x >= 0 && card.x + card.width <= 412); assert(dock.x >= 0 && dock.x + dock.width <= 412);
  assert.equal(await page.evaluate(() => getComputedStyle(window.testShadow.querySelector('.dock')).fontFamily), 'system-ui, sans-serif');
  await page.screenshot({ path: join(output, 'phone-de.png') });
  const result = { browser: await browser.version(), verified: ['German content UI', 'Remark creation and reopened local test storage', 'Phone-width card and tool layout', 'System font isolation'], limitation: 'DOM/UI test with a test browser-API adapter; Firefox add-on installation, action permission and capture APIs not verified.' };
  await writeFile(join(output, 'results.json'), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result, null, 2));
} finally { await browser.close(); server.closeAllConnections(); await new Promise(r => server.close(r)); }
