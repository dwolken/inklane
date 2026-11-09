import './background.test.mjs';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { mkdtemp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
const require = createRequire(new URL('../../website/package.json', import.meta.url));
const { chromium } = require('playwright-core');
const root = resolve('platforms/browser'), output = resolve(process.argv[2] || '.scratch/browser-tests');
await mkdir(output, { recursive: true });
const profile = await mkdtemp(join(output, 'profile-'));
const fixture = `<!doctype html><html><meta charset="utf-8"><title>Field study review</title><style>body{margin:0;font:20px/1.6 system-ui;background:#fbfaf7;color:#333}main{width:760px;margin:150px 80px}h1{font-size:42px}p{padding:20px 30px;background:#fff;border:1px solid #dedbd3}#spacer{height:40px}footer{margin-top:1200px;padding:60px}button{padding:15px}</style><main><h1>Field study</h1><div id="spacer"></div><p id="target">The meadow supports native plants. Review the sampling method and identify the missing observations.</p><p id="second">Keep each explanation beside the material it describes.</p><button id="dynamic">Add context above</button></main><footer>End of the field notes</footer><script>window.capturedKeys=[];document.addEventListener('keydown',e=>capturedKeys.push(e.key),true);document.querySelector('#dynamic').onclick=()=>document.querySelector('#spacer').style.height='190px';</script></html>`;
const server = createServer((req, res) => res.writeHead(200, { 'Content-Type': 'text/html', 'Content-Security-Policy': "frame-src 'none'" }).end(fixture));
await new Promise(done => server.listen(0, '127.0.0.1', done));
const origin = `http://127.0.0.1:${server.address().port}`;
const context = await chromium.launchPersistentContext(profile, { executablePath: process.env.BROWSER_PATH || undefined, channel: process.env.BROWSER_PATH ? undefined : 'chromium', headless: true, ignoreDefaultArgs: ['--disable-extensions'], args: ['--enable-unsafe-extension-debugging'], viewport: { width: 1280, height: 900 }, acceptDownloads: true, locale: 'en-US' });
const results = [], errors = [];
try {
  const browser = await context.browser().newBrowserCDPSession();
  const { id } = await browser.send('Extensions.loadUnpacked', { path: root });
  const worker = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker'); await worker.evaluate(() => typeof InklaneWeb);
  const page = context.pages()[0]; page.on('pageerror', error => errors.push(error.message));
  await page.goto(origin + '/study?utm_source=test&token=secret&chapter=2');
  const session = await context.newCDPSession(page);
  async function activate() {
    const { targetInfos } = await browser.send('Target.getTargets', { filter: [{ type: 'tab' }] }); const targetInfo = targetInfos.find(x => x.url === page.url()); assert(targetInfo, JSON.stringify(targetInfos));
    await browser.send('Extensions.triggerAction', { id, targetId: targetInfo.targetId });
    await page.waitForSelector('#inklane-web-review');
  }
  await activate();
  let shadowId;
  async function connectShadow() {
    const { root: doc } = await session.send('DOM.getDocument', { depth: -1, pierce: true });
    function find(node) { if (node.attributes?.includes('inklane-web-review')) return node; for (const n of [...(node.children || []), ...(node.shadowRoots || [])]) { const found = find(n); if (found) return found; } }
    const host = find(doc); assert(host?.shadowRoots?.length);
    shadowId = (await session.send('DOM.resolveNode', { backendNodeId: host.shadowRoots[0].backendNodeId })).object.objectId;
  }
  await connectShadow();
  async function editorFrame() {
    for(let i=0;i<100;i++){const frame=page.frames().find(f=>f.url().includes('/editor.html#'));if(frame && await frame.locator('body[data-ready=true]').count())return frame;await new Promise(r=>setTimeout(r,40));}
    throw Error('Secure editor did not load');
  }
  async function ui(selector, operation = 'rect') {
    if(['#title','#body'].includes(selector))return(await editorFrame()).locator(selector).evaluate((e,op)=>{if(op==='focus'){e.focus();return true;}return op==='value'?e.value:e.textContent;},operation);
    const response = await session.send('Runtime.callFunctionOn', { objectId: shadowId, functionDeclaration: `function(selector,operation){const e=this.querySelector(selector);if(!e)return null;if(operation==='text')return e.textContent;if(operation==='value')return e.value;if(operation==='focus'){e.focus();return true;}const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height};}`, arguments: [{ value: selector }, { value: operation }], returnByValue: true });
    return response.result.value;
  }
  async function click(selector) { const r = await ui(selector); assert(r && r.width && r.height, selector); await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2); }
  async function fill(selector, value) { await ui(selector, 'focus'); await page.keyboard.press('Control+A'); await page.keyboard.insertText(value); }
  async function stored() { const { data } = await session.send('Extensions.getStorageItems', { id, storageArea: 'local' }); return Object.values(data).find(x => x.data?.format === 'inklane.web-review'); }
  async function waitSaved(test) { for (let i = 0; i < 100; i++) { const value = await stored(); if (value && test(value.data)) return value; await new Promise(r => setTimeout(r, 80)); } throw Error('Storage condition timed out: ' + await ui('#status', 'text')); }
  for (let i = 0; i < 60 && !String(await ui('#status', 'text')).includes('Saved'); i++) await new Promise(r => setTimeout(r, 50));
  await click('[data-tool=note]'); await page.mouse.click(310, 340);
  await fill('#title', 'Sampling method');
  const explanation = 'Use three independent plots.\n' + 'Keep every observation in the returned report. '.repeat(170);
  await fill('#body', explanation); await click('[data-tool=select]');
  let saved = await waitSaved(d => d.objects.length === 1 && d.objects[0].text === explanation);
  assert.equal(saved.data.url, origin + '/study?chapter=2');
  const noteId = saved.data.objects[0].id;
  assert.equal(saved.data.objects[0].anchor.selector, '#target');
  await click(`[data-object="${noteId}"]`);
  await page.evaluate(()=>window.capturedKeys=[]);await ui('#body','focus');await page.keyboard.type('PRIVATEKEYTEST');
  assert.deepEqual(await page.evaluate(()=>window.capturedKeys),[],'Page capture listener must not observe editor keystrokes');
  await fill('#body',explanation);await waitSaved(d=>d.objects[0].text===explanation);await page.screenshot({path:join(output,'secure-editor.png')});await click('[data-action=finish]');
  results.push('Actual Chromium action activation, extension-origin editor rejects page key logging, long remark creation, local save and URL cleanup');
  const before = await ui(`[data-object="${noteId}"]`);
  await page.evaluate(() => document.querySelector('#spacer').style.height = '190px');
  await new Promise(r => setTimeout(r, 150));
  const after = await ui(`[data-object="${noteId}"]`);
  assert(Math.abs(after.y - before.y - 150) < 1);
  await page.evaluate(() => window.scrollTo(0, 180)); await new Promise(r => setTimeout(r, 100));
  const scrolled = await ui(`[data-object="${noteId}"]`); assert(Math.abs(scrolled.y - after.y + 180) < 1);
  results.push('Anchor follows DOM layout shift and page scroll');
  await click('[data-tool=text]'); await page.mouse.click(300, 490); await fill('#body', 'A precise label');
  await fill('#hex', '#D95B27'); await page.keyboard.press('Tab');
  await fill('#size', '24'); await page.keyboard.press('Tab');
  await fill('#width', '180'); await page.keyboard.press('Tab');
  await waitSaved(d => d.objects.length === 2 && d.objects[1].color === '#D95B27' && d.objects[1].fontSize === 24 && d.objects[1].width === 180);
  await click('[data-tool=ink]'); await page.mouse.move(230, 260); await page.mouse.down(); await page.mouse.move(280, 240, { steps: 8 }); await page.mouse.move(330, 280, { steps: 8 }); await page.mouse.up();
  await waitSaved(d => d.objects.length === 3 && d.objects[2].points.length > 10);
  results.push('Text post-formatting and wrapping width, pen drawing');
  await click('[data-tool=select]'); await click(`[data-object="${noteId}"]`);
  await click('[data-action=duplicate]'); await waitSaved(d => d.objects.length === 4);
  await click('[data-action=remove]'); await waitSaved(d => d.objects.length === 3);
  await click('[data-action=undo]'); await waitSaved(d => d.objects.length === 4);
  await click('[data-action=redo]'); await waitSaved(d => d.objects.length === 3);
  results.push('Duplicate, delete, undo and redo saved objects');
  await page.reload(); await activate(); await connectShadow();
  for (let i = 0; i < 60 && !(await ui(`[data-object="${noteId}"]`)); i++) await new Promise(r => setTimeout(r, 50));
  assert(await ui(`[data-object="${noteId}"]`));
  await page.evaluate(() => document.querySelector('#target').textContent = 'This is a different revision of the document.');
  await new Promise(r => setTimeout(r, 100));
  await click(`[data-object="${noteId}"]`); assert((await ui('#warning', 'text')).includes('changed'));
  results.push('Reopen from extension storage; source revision visibly detaches anchor');
  // Reconnect by dragging the marker onto the revised paragraph.
  let pin = await ui(`[data-object="${noteId}"]`);
  await page.mouse.move(pin.x + pin.width / 2, pin.y + pin.height / 2); await page.mouse.down(); const targetBox = await page.locator('#target').boundingBox(); await page.mouse.move(targetBox.x + 60, targetBox.y + 35, { steps: 6 }); await page.mouse.up();
  await waitSaved(d => d.objects[0].anchor.quote === 'This is a different revision of the document.');
  assert.equal(await ui('#warning', 'text'), '');
  await click('[data-action=finish]');
  await click('summary'); const downloaded = page.waitForEvent('download'); await click('[data-action=visible]');
  const portableDownload = await downloaded; const portablePath = join(output, 'visible.inklane'); await portableDownload.saveAs(portablePath);
  const bytes = await readFile(portablePath); assert.equal(bytes.subarray(0, 8).toString(), 'INKLANE1');
  const length = bytes.readUInt32BE(8), envelope = JSON.parse(bytes.subarray(12, 12 + length));
  assert.equal(envelope.document.kind, 'Image'); assert.equal(envelope.document.pages[0].items.find(x => x.id === noteId).text, explanation);
  const { validateDocument } = await import('../../website/editor/model.js');
  await validateDocument(envelope.document);
  assert(bytes.subarray(12 + length).subarray(1, 4).toString() === 'PNG');
  await writeFile(join(output, 'captured-source.png'), bytes.subarray(12 + length));
  const colored = await page.evaluate(async dataUrl => { const img = new Image(); img.src = dataUrl; await img.decode(); const c = document.createElement('canvas'); c.width=img.width;c.height=img.height;const ctx=c.getContext('2d');ctx.drawImage(img,0,0);const p=ctx.getImageData(290,480,250,70).data;let n=0;for(let i=0;i<p.length;i+=4)if(Math.abs(p[i]-251)+Math.abs(p[i+1]-250)+Math.abs(p[i+2]-247)>30)n++;return n;}, 'data:image/png;base64,'+bytes.subarray(12+length).toString('base64'));
  assert.equal(colored, 0, 'Editable marks must not be baked into the captured source');
  results.push('Real activeTab screenshot capture and standard INKLANE1 portable image export preserving long note text');
  await click('summary'); const jsonDownloadPromise = page.waitForEvent('download'); await click('[data-action=all]'); const jsonDownload = await jsonDownloadPromise; await jsonDownload.saveAs(join(output, 'review.web-review.json'));
  const full = JSON.parse(await readFile(join(output, 'review.web-review.json'))); assert.equal(full.objects.length, 3);
  await click(`[data-object="${noteId}"]`); await click('[data-action=duplicate]'); await waitSaved(d => d.objects.length === 4);
  await click('summary'); const choose = page.waitForEvent('filechooser'); await click('[data-action=import]');
  page.once('dialog', dialog => dialog.accept()); await (await choose).setFiles(join(output, 'review.web-review.json'));
  await waitSaved(d => d.objects.length === 3 && d.objects[0].text === explanation);
  await click('[data-action=undo]'); await waitSaved(d => d.objects.length === 4);
  await click('[data-action=redo]'); await waitSaved(d => d.objects.length === 3);
  results.push('Exported JSON imports through file chooser; replacement is confirmed and undoable');
  const reportPromise = context.waitForEvent('page'); await click('summary'); await click('[data-action=print]');
  const report = await reportPromise; await report.waitForSelector('article');
  assert.equal(await report.locator('article .body').first().textContent(), explanation);
  await report.pdf({ path: join(output, 'report.pdf'), format: 'A4', printBackground: true });
  assert.equal(await report.locator('article').count(), 3);
  results.push('Full JSON export and complete printable report rendered to PDF');
  await page.bringToFront(); await page.screenshot({ path: join(output, 'workspace.png') });
  const storedCopy = await stored();
  const races = await worker.evaluate(async ({ url, copy }) => {
    const tab = (await chrome.tabs.query({})).find(x => x.url === url);
    const result = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: async copy => {
      const request = () => chrome.runtime.sendMessage({ target: 'inklane-web', command: 'save', data: copy.data, revision: copy.revision });
      return Promise.all([request(), request()]);
    }, args: [copy] });
    return result[0].result;
  }, { url: page.url(), copy: storedCopy });
  assert.equal(races.filter(x => x.conflict).length, 1); assert.equal(races.filter(x => x.revision).length, 1);
  results.push('Concurrent saves serialize; stale revision is rejected without overwriting');
  const originalReview=await stored();let downloads=0;const onDownload=()=>downloads++;page.on('download',onDownload);
  await page.evaluate(()=>{history.pushState({},'', '/different-document');document.querySelector('h1').textContent='A different document';});
  await click('summary');await click('[data-action=visible]');await new Promise(r=>setTimeout(r,500));assert.equal(downloads,0);assert((await ui('#status','text')).includes('address changed'));
  const refused=await worker.evaluate(async({url,copy})=>{const tab=(await chrome.tabs.query({})).find(t=>t.url===url);return(await chrome.scripting.executeScript({target:{tabId:tab.id},func:copy=>chrome.runtime.sendMessage({target:'inklane-web',command:'save',data:copy.data,revision:copy.revision}),args:[copy]}))[0].result;},{url:page.url(),copy:originalReview});
  assert.equal(refused.error,'changed');await click('[data-action=reload]');for(let i=0;i<50 && await ui('#count','text');i++)await new Promise(r=>setTimeout(r,30));assert.equal(await ui('#count','text'),'');page.off('download',onDownload);
  results.push('Same-origin SPA navigation rejects wrong-source capture and save; explicit reopen loads current review');
  await page.goto(origin+'/quota');
  const quota=await worker.evaluate(async url=>{const data=InklaneWeb.create(url,'Quota review'),obj=InklaneWeb.object('ink',{selector:'',quote:'',tag:'',rx:0,ry:0,x:300,y:300});obj.points=Array.from({length:60000},(_,i)=>({x:i%2?100:0,y:0,pressure:0}));data.objects=[obj];InklaneWeb.validate(data);const key=await InklaneWeb.keyFor(url);await chrome.storage.local.set({[key]:{data,revision:1}});return{key,data,revision:1};},page.url());
  await activate();await connectShadow();await new Promise(r=>setTimeout(r,100));await click('[data-tool=select]');await page.mouse.click(350,300);await click('[data-action=duplicate]');
  assert((await ui('#status','text')).includes('full'));const remaining=await worker.evaluate(async key=>(await chrome.storage.local.get(key))[key],quota.key);assert.equal(remaining.data.objects.length,1);assert.equal(remaining.revision,1);
  results.push('Duplicate preflights total point and byte limits without changing valid saved work');
  const help=await context.newPage();await help.goto('chrome-extension://'+id+'/help.html');help.once('dialog',d=>d.accept());await help.locator('.review').filter({hasText:'Quota review'}).getByRole('button',{name:'Delete',exact:true}).click();
  await help.waitForFunction(()=>!document.querySelector('#saved').textContent.includes('Quota review'));
  const tombstone=await worker.evaluate(async key=>(await chrome.storage.local.get(key))[key],quota.key);assert.equal(tombstone.deleted,true);assert.equal(tombstone.revision,2);
  const stale=await worker.evaluate(async({url,copy})=>{const tab=(await chrome.tabs.query({})).find(t=>t.url===url);return(await chrome.scripting.executeScript({target:{tabId:tab.id},func:copy=>chrome.runtime.sendMessage({target:'inklane-web',command:'save',data:copy.data,revision:copy.revision}),args:[copy]}))[0].result;},{url:page.url(),copy:quota});assert.equal(stale.conflict,true);
  results.push('Help deletion keeps a revision tombstone and rejects stale open-tab recreation');
  assert.deepEqual(errors, []);
  await writeFile(join(output, 'results.json'), JSON.stringify({ browser: await context.browser().version(), results, errors }, null, 2));
  console.log(JSON.stringify({ results, errors }, null, 2));
} finally { await context.close(); server.closeAllConnections(); await new Promise(done => server.close(done)); }
