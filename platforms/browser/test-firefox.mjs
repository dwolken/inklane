// Real temporary add-on: no extension API adapters or content script injection.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { spawn, spawnSync } from 'node:child_process';
import { open, readFile, mkdir, mkdtemp, readdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
const require = createRequire(new URL('../../website/package.json', import.meta.url));
const { firefox } = require('playwright-core');
const output = resolve(process.argv[2] || '.scratch/browser-firefox');
await mkdir(output, { recursive: true });
const downloads = await mkdtemp(join(output, 'downloads-'));
const packaged = spawnSync(process.platform === 'win32' ? 'python' : 'python3', ['platforms/browser/package.py', join(output, 'package')], { encoding: 'utf8', windowsHide: true });
assert.equal(packaged.status, 0, packaged.stderr);
const source = '<!doctype html><html lang="de"><meta charset="utf-8"><title>Feldnotizen</title><style>body{font:20px system-ui;margin:220px 40px 0;background:white}p{padding:20px;background:#f4f5f9;line-height:1.6}</style><h1>Feldnotizen</h1><p id="target">Die Erklärung gehört direkt an diese Textstelle.</p><script>window.pageKeys=[];window.addEventListener("keydown",e=>pageKeys.push(e.key),true);</script></html>';
const server = createServer((q, r) => r.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Content-Security-Policy': "frame-src 'none'" }).end(source));
await new Promise(r => server.listen(0, '127.0.0.1', r));
const portServer = createServer(); await new Promise(r => portServer.listen(0, '127.0.0.1', r));
const port = portServer.address().port; await new Promise(r => portServer.close(r));
const log = await open(join(output, 'geckodriver.log'), 'w');
const driver = spawn(process.env.GECKODRIVER || 'geckodriver', ['--port', String(port), '--allow-system-access'], { stdio: ['ignore', log.fd, log.fd], windowsHide: true });
let spawnError; driver.on('error', e => spawnError = e);
const origin = 'http://127.0.0.1:' + server.address().port, base = 'http://127.0.0.1:' + port;
let session, shadow;
const elementKey = 'element-6066-11e4-a52e-4f735466cecf', shadowKey = 'shadow-6066-11e4-a52e-4f735466cecf';
async function call(path, data, method) {
  const response = await fetch(base + path, { method: method || (data === undefined ? 'GET' : 'POST'), headers: { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data), signal: AbortSignal.timeout(60000) });
  const result = await response.json();
  if (!response.ok || result.value?.error) throw Object.assign(Error(JSON.stringify(result.value)), { webdriverError: result.value?.error });
  return result.value;
}
const s = (path, data, method) => call('/session/' + session + path, data, method);
const js = (script, args = []) => s('/execute/sync', { script, args });
async function until(fn, label, timeout = 25000) {
  const end = Date.now() + timeout; let error;
  while (Date.now() < end) { try { const value = await fn(); if (value) return value; } catch (e) { error = e; } await new Promise(r => setTimeout(r, 100)); }
  throw Error('Timed out: ' + label + (error ? '\n' + error.message : ''));
}
const element = async selector => (await s('/shadow/' + shadow + '/element', { using: 'css selector', value: selector }))[elementKey];
async function click(selector) {
  // Layout renders replace marker elements. A stale reference is rejected before
  // WebDriver dispatches input, so re-query that one control within a fixed bound.
  let last;
  for (let attempt = 0; attempt < 50; attempt++) {
    try { await s('/element/' + await element(selector) + '/click', {}); return; }
    catch (error) {
      if (!['stale element reference', 'no such element'].includes(error.webdriverError)) throw error;
      last = error; await new Promise(resolve => setTimeout(resolve, 100));
    }
  }
  throw Error('Control did not settle: ' + selector + '\n' + last?.message);
}
const text = async selector => s('/element/' + await element(selector) + '/text');
const saved = () => until(async () => (await text('#status')) === 'In diesem Browser gespeichert', 'real Firefox local storage save');
async function activate() {
  await s('/moz/context', { context: 'chrome' });
  await js("document.getElementById('unified-extensions-button').click()");
  await until(() => js("return !!document.getElementById('web-review_inklane_app-BAP')"), 'Firefox add-on action');
  await js("document.getElementById('web-review_inklane_app-BAP').click()");
  await s('/moz/context', { context: 'content' });
  const host = await until(() => s('/element', { using: 'css selector', value: '#inklane-web-review' }), 'injected review UI');
  shadow = (await s('/element/' + host[elementKey] + '/shadow'))[shadowKey];
  await saved();
}
async function enterEditor() {
  // Opening a remark navigates the frame through about:blank. Re-select it
  // from the current top-level document until the private editor has loaded.
  await until(async () => {
    await s('/frame', { id: null });
    const frame = await element('#editor');
    if (!(await s('/element/' + frame + '/attribute/src'))?.startsWith('moz-extension://')) return false;
    await s('/frame', { id: { [elementKey]: frame } });
    return js("return location.protocol === 'moz-extension:' && document.readyState === 'complete' && document.body.dataset.ready === 'true'");
  }, 'secure editor frame and channel');
}
async function download(suffix, action) {
  const before = new Set(await readdir(downloads));
  await click('summary'); await click('[data-action=' + action + ']');
  const name = await until(async () => (await readdir(downloads)).find(n => !before.has(n) && n.endsWith(suffix)), suffix + ' download');
  const path = join(downloads, name);
  await until(async () => (await readFile(path)).length > 100, 'complete download');
  return path;
}
try {
  await until(async () => { if (spawnError) throw spawnError; return await call('/status'); }, 'geckodriver startup');
  const opened = await call('/session', { capabilities: { alwaysMatch: { browserName: 'firefox', 'moz:firefoxOptions': { binary: process.env.FIREFOX_PATH || firefox.executablePath(), args: ['-headless'], prefs: { 'intl.accept_languages': 'de-AT,de,en', 'browser.download.folderList': 2, 'browser.download.dir': downloads, 'browser.download.useDownloadDir': true, 'browser.helperApps.neverAsk.saveToDisk': 'application/json,application/octet-stream' } } } } });
  session = opened.sessionId;
  await s('/window/rect', { width: 1280, height: 1000 });
  await s('/url', { url: origin + '/review' });
  assert.equal(await s('/moz/addon/install', { path: join(output, 'package/inklane-web-firefox-0.1.0.zip'), temporary: true }), 'web-review@inklane.app');
  await activate();
  await click('[data-tool=note]');
  await s('/actions', { actions: [{ type: 'pointer', id: 'mouse', parameters: { pointerType: 'mouse' }, actions: [{ type: 'pointerMove', x: 150, y: 370, duration: 0 }, { type: 'pointerDown', button: 0 }, { type: 'pointerUp', button: 0 }] }] });
  await enterEditor();
  const explanation = 'Präzise Rückmeldung mit Umlauten: äöü.\nFirefox speichert diese Erklärung lokal.';
  const body = (await s('/element', { using: 'css selector', value: '#body' }))[elementKey];
  await s('/element/' + body + '/value', { text: explanation });
  await until(() => js('return document.querySelector("#body").value === arguments[0]', [explanation]), 'remark input');
  await s('/frame', { id: null });
  await saved();
  assert.deepEqual(await js('return window.pageKeys'), [], 'A webpage capture listener must not see private editor keys');
  await click('[data-action=finish]');
  const jsonPath = await download('.web-review.json', 'all');
  const review = JSON.parse(await readFile(jsonPath, 'utf8'));
  assert.equal(review.objects.length, 1); assert.equal(review.objects[0].text, explanation);
  const portablePath = await download('.inklane', 'visible');
  const bytes = await readFile(portablePath); assert.equal(bytes.subarray(0, 8).toString(), 'INKLANE1');
  const header = JSON.parse(bytes.subarray(12, 12 + bytes.readUInt32BE(8)));
  assert.equal(header.document.pages[0].items[0].text, explanation);
  assert.equal(header.document.kind, 'Image');
  assert.equal(bytes.subarray(12 + bytes.readUInt32BE(8) + 1, 12 + bytes.readUInt32BE(8) + 4).toString(), 'PNG');
  await writeFile(join(output, 'installed-firefox.png'), Buffer.from(await s('/screenshot'), 'base64'));
  const handles = await s('/window/handles');
  await click('summary'); await click('[data-action=print]');
  const report = await until(async () => (await s('/window/handles')).find(h => !handles.includes(h)), 'extension-origin report');
  await s('/window', { handle: report });
  await until(() => js('return document.body.textContent.includes(arguments[0])', [explanation]), 'complete report');
  await writeFile(join(output, 'report-firefox.pdf'), Buffer.from(await s('/print', { background: true }), 'base64'));
  await s('/window', { handle: handles[0] }); await s('/refresh', {}); await activate();
  await click('[data-object="' + review.objects[0].id + '"]'); await enterEditor();
  assert.equal(await js('return document.querySelector("#body").value'), explanation, 'Annotation reopens from real extension local storage');
  await s('/frame', { id: null });
  await s('/window/rect', { width: 430, height: 1000 });
  const narrowViewport = await js('return innerWidth');
  assert(narrowViewport <= 500, 'Firefox narrow viewport');
  const card = await element('#card');
  assert.equal(await js('const r=arguments[0].getBoundingClientRect();return r.x>=0 && r.right<=innerWidth', [{ [elementKey]: card }]), true);
  await writeFile(join(output, 'narrow-firefox.png'), Buffer.from(await s('/screenshot'), 'base64'));
  await click('[data-action=finish]');
  const before = await readdir(downloads);
  await js("history.pushState({}, '', '/different-page')");
  await click('summary'); await click('[data-action=visible]');
  await until(async () => (await text('#status')).includes('Seitenadresse'), 'changed URL capture refusal');
  assert.deepEqual(await readdir(downloads), before, 'A different SPA page must not be captured into the old review');
  const result = { browser: 'Firefox ' + opened.capabilities.browserVersion, narrowViewport, installation: 'Unmodified packaged add-on, temporary isolated profile, actual Firefox toolbar action', verified: ['German interface and Unicode remark', 'Extension-origin editor does not leak keydown to the webpage', 'Real extension local storage save and reload', 'Complete web-review JSON', 'Actual captureVisibleTab editable portable image', 'Extension report and Firefox PDF output', 'Narrow viewport card', 'SPA URL change refuses screenshot export'], limitations: ['Temporary installation only; signed store publication and physical mobile devices are not tested.'] };
  await writeFile(join(output, 'results.json'), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  if (session) try { await s('/frame', { id: null }); await s('/moz/context', { context: 'chrome' }); const errors = await js("return Services.console.getMessageArray().map(m=>m.message).filter(m=>/moz-extension|Inklane|content.js|core.js/.test(m))"); console.error('Firefox extension console:', errors); } catch {}
  throw error;
} finally {
  if (session) await call('/session/' + session, undefined, 'DELETE').catch(() => {});
  driver.kill(); await log.close(); server.closeAllConnections(); await new Promise(r => server.close(r));
}
