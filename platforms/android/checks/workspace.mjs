import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, join } from 'node:path';
import { createHash } from 'node:crypto';
import { setTimeout as sleep } from 'node:timers/promises';

const adb = (...args) => execFileSync('adb', args, { encoding: 'utf8', timeout: 30000 }).trim();
// `adb shell` rejoins arguments before the remote shell parses them. Enumerate
// the fixed test directory, then match locally so names with spaces stay intact.
const findDownload = (name, minimum = 12) => adb('shell','find','/sdcard/Download','-type','f','-size','+'+minimum+'c')
  .split('\n').find(path => path.endsWith('/'+name));
const app = 'dev.inklane.android';
const output = 'platforms/android/build/outputs/apk/debug';
const apk = readdirSync(output).find(f => f.endsWith('.apk'));
assert.ok(apk, 'Debug package exists');
adb('install','-r',join(output,apk));
adb('shell','am','start','-W','-n',app+'/.WorkspaceActivity');

async function waitFor(check, description, timeout=25000) {
  const end = Date.now()+timeout;
  while (Date.now()<end) {
    const result = await check().catch(() => false);
    if (result) return result;
    await sleep(350);
  }
  throw Error('Timed out waiting for ' + description);
}
const pid = await waitFor(async () => adb('shell','pidof',app), 'app process');
const port = adb('forward','tcp:0','localabstract:webview_devtools_remote_'+pid);
const target = await waitFor(async () => (await (await fetch('http://127.0.0.1:'+port+'/json/list')).json())
  .find(t=>t.url.includes('appassets.androidplatform.net/editor/')), 'offline WebView');
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {socket.onopen=resolve;socket.onerror=reject;});
let sequence = 0;
const pending = new Map();
const startupDiagnostics = [];
socket.onmessage = ({data}) => {
  const msg = JSON.parse(data);
  if (msg.method === 'Runtime.exceptionThrown') startupDiagnostics.push(msg.params.exceptionDetails);
  if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error')
    startupDiagnostics.push(msg.params.args.map(arg => arg.description || arg.value));
  if (msg.method === 'Log.entryAdded' && msg.params.entry.level === 'error') startupDiagnostics.push(msg.params.entry);
  if (msg.method === 'Network.loadingFailed') startupDiagnostics.push(msg.params);
  const call = pending.get(msg.id);
  if (!call) return;
  pending.delete(msg.id);
  if (msg.error) call.reject(Error(msg.error.message)); else call.resolve(msg.result);
};
function command(method, params) {
  const id=++sequence;
  return new Promise((resolve,reject) => {
    pending.set(id,{resolve,reject}); socket.send(JSON.stringify({id,method,params}));
  });
}
async function js(expression) {
  const value=await command('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
  if (value.exceptionDetails) throw Error(value.exceptionDetails.exception?.description || value.exceptionDetails.text);
  return value.result.value;
}
const find = selector => 'document.querySelector('+JSON.stringify(selector)+')';
const visible = s => js('!!'+find(s)+'?.getClientRects().length');
async function tap(sel, fx=.5, fy=.5) {
  await waitFor(()=>visible(sel),sel);
  const {x,y}=await js('(()=>{const e='+find(sel)+';e.scrollIntoView({block:"nearest"});const r=e.getBoundingClientRect();return {x:r.left+r.width*'+fx+',y:r.top+r.height*'+fy+'}})()');
  await command('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y,id:1,radiusX:1,radiusY:1,force:1}]});
  await command('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
}
async function type(sel,value) {
  await waitFor(()=>visible(sel),'visible text field '+sel);
  await js(find(sel)+'.focus();'+find(sel)+'.select()');
  await waitFor(()=>js('document.activeElement === '+find(sel)),'focused text field '+sel);
  await command('Input.insertText',{text:value});
  await waitFor(()=>js(find(sel)+'.value === '+JSON.stringify(value)),'typed value in '+sel);
}
function nodes() {
  adb('shell','uiautomator','dump','/data/local/tmp/inklane-ui.xml');
  return [...adb('shell','cat','/data/local/tmp/inklane-ui.xml').matchAll(/<node\b([^>]*)>/g)]
    .map(m=>Object.fromEntries([...m[1].matchAll(/([\w-]+)="([^"]*)"/g)].map(x=>[x[1],x[2]])));
}
async function systemTap(match, label) {
  const node=await waitFor(async()=>nodes().find(match),label);
  const [x1,y1,x2,y2]=node.bounds.match(/\d+/g).map(Number);
  adb('shell','input','tap',String((x1+x2)>>1),String((y1+y2)>>1));
}

// The OS picker was exercised above. Send deterministic fixtures to the same
// file-input handler to check WebView's image codec and the APK's PDF worker.
async function loadFixture(path, type, expectedName = basename(path)) {
  const bytes = readFileSync(path);
  const name = basename(path);
  await js(`(() => {
    const input = document.querySelector('#files'), transfer = new DataTransfer();
    const binary = atob(${JSON.stringify(bytes.toString('base64'))});
    transfer.items.add(new File([Uint8Array.from(binary, c => c.charCodeAt(0))], ${JSON.stringify(name)}, {type:${JSON.stringify(type)}}));
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', {bubbles:true}));
  })()`);
  await waitFor(()=>js('document.querySelector("#name").textContent === '+JSON.stringify(expectedName)),'open '+name);
  return bytes;
}

const saveButton = n => /^save$/i.test(n.text) && n.enabled==='true';
const fresh = mkdtempSync(join(tmpdir(),'inklane-android-'));
const title = 'AndroidReview';
try {
  await command('Runtime.enable',{});
  await command('Log.enable',{});
  await command('Network.enable',{});
  await waitFor(()=>visible('#new-notebook'), 'document home');
  await waitFor(()=>js('document.body.getAttribute("aria-busy") !== "true" && typeof document.querySelector("#new-notebook")?.onclick === "function" && !document.querySelector("#new-notebook").disabled'),
    'initialized document controls');
  assert.equal(await js('isSecureContext && typeof inklaneSaveCopy === "function"'),true);
  assert.equal(await js('globalThis.inklaneLaunchMotion === false && !document.querySelector(".brand").classList.contains("launch")'),true,
    'Android keeps the OS-native static launch and opens the usable workspace immediately');
  await waitFor(()=>js('document.querySelector(".brand img").complete && document.querySelector(".brand img").naturalWidth > 0'),'bundled brand asset');
  await js(`(() => {
    window.__reviewInputs = [];
    for (const type of ['pointerdown','pointerup','pointercancel','click','focusout']) {
      document.addEventListener(type, event => {
        __reviewInputs.push({type,target:event.target.id || event.target.tagName,pointer:event.pointerType,
          x:event.clientX,y:event.clientY,focus:document.activeElement?.id,width:innerWidth,height:innerHeight});
        if (__reviewInputs.length > 30) __reviewInputs.shift();
      }, true);
    }
  })()`);
  await tap('#new-notebook');
  await type('#book-title',title);
  await js('document.querySelector("#paper-form").requestSubmit()');
  await waitFor(()=>visible('#sheet'), 'new notebook');
  await tap('[data-tool="note"]');
  await tap('#sheet',.48,.19);
  await type('#note-title','Door clearance');
  await type('#note-body','Leave room for the handle.');
  await tap('#close-card');
  await waitFor(()=>js('document.querySelector(".marker")?.getAttribute("aria-expanded")==="false" && document.querySelector("#card").hidden'),'initial card collapsed');
  await tap('#save');
  await waitFor(async()=>nodes().some(saveButton),'system create picker');
  adb('shell','input','keyevent','BACK');
  await waitFor(async()=>await js('document.hasFocus() && !document.querySelector("#save").disabled'),'workspace after cancelled save');
  await tap('#save');
  await systemTap(saveButton,'system Save');
  // DocumentsUI creates an empty entry before our activity receives its URI.
  // Wait for the editor to confirm the native write before inspecting that entry.
  await waitFor(()=>js('document.hasFocus() && !document.querySelector("#save").disabled && document.querySelector("#state").textContent.toLowerCase().includes("saved")'), 'editor confirmation of native write');
  const deviceFile=await waitFor(async()=>findDownload(title+'.inklane'), 'committed notebook');
  adb('pull',deviceFile,join(fresh,'saved.inklane'));
  const bytes=readFileSync(join(fresh,'saved.inklane'));
  const meta=JSON.parse(bytes.subarray(12,12+bytes.readUInt32BE(8)).toString('utf8'));
  assert.equal(meta.document.title,title);
  const [note]=meta.document.pages[0].items.filter(item => item.isNote);
  assert.equal(note.title,'Door clearance');
  assert.equal(note.text,'Leave room for the handle.');
  assert.ok(note.x>0 && note.y>0,'anchor coordinates persisted');
  assert.equal(note.isExpanded,false,'The original card was collapsed before saving');
  await tap('#more summary');
  await tap('#home');
  await waitFor(()=>visible('#new-notebook'),'document home before reopening');
  await tap('#start');
  await systemTap(n=>n.text===title+'.inklane','saved document in system picker');
  await waitFor(()=>js('document.querySelector("#name").textContent === "'+title+'" && document.querySelectorAll(".marker").length>0'), 'reopened annotation');
  // The picker can still be dismissing when the reopened model first paints.
  // Wait for an interactive, unobstructed marker, then perform a touchscreen tap.
  const marker = await waitFor(()=>js(`(() => {
    const m = document.querySelector('.marker');
    if (!document.hasFocus() || !m || document.querySelector('#viewport').inert) return false;
    const r = m.getBoundingClientRect(), x = r.left+r.width/2, y = r.top+r.height/2, v = visualViewport;
    return document.elementFromPoint(x,y) === m && {
      u:(x-v.offsetLeft)/v.width, v:(y-v.offsetTop)/v.height, expanded:m.getAttribute('aria-expanded')==='true'
    };
  })()`),'interactive reopened marker');
  assert.equal(marker.expanded,note.isExpanded,'Saved note presentation survived reopening');
  // Android InputDispatcher uses physical screen pixels. WebView DevTools
  // touch points can retain the scale from before DocumentsUI covered the app.
  const targetBounds = await waitFor(async()=>{
    const appNodes = nodes().filter(n=>n.package===app && n.enabled==='true');
    const named = appNodes.find(n=>[n.text,n['content-desc']].some(label=>label?.includes(note.title)));
    const host = appNodes.find(n=>n.class === 'android.webkit.WebView');
    if (!named && !host) return false;
    const [x1,y1,x2,y2]=(named || host).bounds.match(/\d+/g).map(Number);
    return {
      x:Math.round(x1 + (x2-x1)*(named ? 0.5 : marker.u)),
      y:Math.round(y1 + (y2-y1)*(named ? 0.5 : marker.v))
    };
  },'Android accessibility bounds for the review surface');
  await js(`window.__deviceTap = []; for (const type of ['pointerdown','pointermove','pointerup','pointercancel','gotpointercapture','lostpointercapture','click']) {
    for (const capture of [true,false]) window.addEventListener(type, e=>{
      if (window.__deviceTap.length < 40) window.__deviceTap.push([
        e.type, capture ? 'before' : 'after', e.target.id || e.target.className || e.target.tagName,
        e.pointerType, e.detail, e.clientX, e.clientY, !document.querySelector('#card').hidden
      ]);
    }, {capture});
  }`);
  adb('shell','input','tap',String(targetBounds.x),String(targetBounds.y));
  await waitFor(()=>visible('#close-card'),'visible expanded note');
  // Check after the compatibility-click window, not just between pointerup and click.
  await sleep(800);
  assert.ok(await visible('#close-card'),'The note stays expanded after touch compatibility events.');
  console.log('Android marker input:', JSON.stringify(await js('window.__deviceTap')));
  assert.equal(await js(find('#note-body')+'.value'),'Leave room for the handle.');
  await tap('#close-card');
  await tap('#more summary');
  await tap('#home');
  await tap('#sample-link');
  await waitFor(()=>js('document.querySelector("#name").textContent === "Field guide.png" && !document.querySelector("#viewport").inert && document.querySelectorAll(".marker").length === 2'),'bundled field guide');
  // Browse to a named remark through the regular cross-document index. Capture
  // only the bundled example on this disposable emulator, after the picker closes.
  mkdirSync('.scratch',{recursive:true});
  const capture = () => execFileSync('adb',['exec-out','screencap','-p'],{maxBuffer:8*1024*1024});
  const screenPath = '.scratch/android-review.png';
  writeFileSync(screenPath,capture());
  await tap('#more summary');
  await tap('#index');
  await waitFor(()=>visible('#browse[open] #results button'),'sample index result');
  await js('document.querySelector("#browse #results button").click()');
  await waitFor(()=>js('!document.querySelector("#browse").open && !document.querySelector("#card").hidden && document.querySelector("#note-title").value === "Petal colour"'),'indexed sample note');
  await js('document.fonts.ready.then(()=>new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done))))');
  // Chromium's touch highlight is composed by Android after the DOM has settled.
  // Retain a real, unmodified device frame once consecutive captures agree.
  await sleep(1100);
  let still = capture();
  for (let attempt = 0; attempt < 6; attempt++) {
    await sleep(300);
    const next = capture();
    const matches = next.equals(still);
    still = next;
    if (matches) break;
  }
  writeFileSync(screenPath, still);
  await tap('#close-card');
  await loadFixture('website/assets/field-guide-en.png', 'image/png');
  assert.ok(await js('document.querySelector("#source").width') > 500);
  const pdf = await loadFixture('website/fixtures/review-pages.pdf', 'application/pdf');
  assert.equal(await js(find('#page-count')+'.textContent'),'1 / 2');
  const green = await js(`(() => {
    const c = document.querySelector('#source'), data = c.getContext('2d').getImageData(0,0,c.width,c.height).data;
    let count = 0;
    for (let i=0; i<data.length; i+=4) if (data[i+1]>data[i]*1.5 && data[i]<170) count++;
    return count;
  })()`);
  assert.ok(green > 500,'The APK worker painted the known green PDF page content.');
  await tap('#next-page');
  await waitFor(()=>js('document.querySelector("#page-count").textContent === "2 / 2"'), 'second PDF page');
  await tap('[data-tool="note"]');
  await tap('#sheet',.46,.31);
  await type('#note-title','Scale bar');
  await type('#note-body','Change the units.');
  await tap('#close-card');
  await tap('#more summary');
  await tap('#feedback');
  await systemTap(saveButton,'system feedback Save');
  const feedbackPath = await waitFor(async()=>findDownload('review-pages.pdf.feedback.json'),'written feedback report');
  adb('pull',feedbackPath,join(fresh,'feedback.json'));
  const report = JSON.parse(readFileSync(join(fresh,'feedback.json'),'utf8'));
  assert.equal(report.source.sha256,createHash('sha256').update(pdf).digest('hex'));
  assert.equal(report.pages[1].annotations[0].title,'Scale bar');
  assert.equal(report.pages[1].annotations[0].text,'Change the units.');
  await tap('#more summary'); await tap('#home');
  await waitFor(()=>visible('#new-notebook'), 'home before rich fixture');
  const rich = JSON.parse(readFileSync('tests/fixtures/rich-host-review.json','utf8'));
  const metadata = Buffer.from(JSON.stringify({format:'inklane.workspace',version:1,sourceName:rich.title,document:rich}));
  const header = Buffer.alloc(12); header.write('INKLANE1'); header.writeUInt32BE(metadata.length,8);
  const richInput = join(fresh,'Rich.inklane'); writeFileSync(richInput,Buffer.concat([header,metadata]));
  await loadFixture(richInput,'application/octet-stream',rich.title);
  await waitFor(()=>js('document.querySelectorAll("[data-shape]").length === 3 && document.querySelectorAll(".placed .katex").length === 1'),'local rich renderers');
  assert.equal(await js(readFileSync('tests/browser-rich-host.js','utf8')),true);
  await tap('#save'); await systemTap(saveButton,'system rich workspace Save');
  await waitFor(()=>js('document.hasFocus() && !document.querySelector("#save").disabled && document.querySelector("#state").textContent.toLowerCase().includes("saved")'),'rich native save');
  const richDevicePath = await waitFor(async()=>findDownload('Rich host review.inklane'),'saved rich notebook');
  const richSaved = join(fresh,'Rich saved.inklane'); adb('pull',richDevicePath,richSaved);
  const richBytes = readFileSync(richSaved), richModel = JSON.parse(richBytes.subarray(12,12+richBytes.readUInt32BE(8))).document;
  assert.equal(richModel.schemaVersion,4);
  const richText = richModel.pages[0].items.find(o=>o.id==='host-text');
  assert.deepEqual([richText.width,richText.fontSize,richText.color,richText.bold,richText.contentFormat],[210,24,'#F97316',true,'Markdown']);
  const richArrow = richModel.pages[0].shapes.find(o=>o.id==='host-arrow');
  assert.deepEqual([richArrow.startId,richArrow.endId,richArrow.groupId],['host-left','host-right','host-diagram']);
  await tap('#more summary'); await tap('#export-image'); await systemTap(saveButton,'system annotated PNG Save');
  await waitFor(()=>js('document.hasFocus() && !document.querySelector("#save").disabled'),'PNG native completion');
  const pngDevicePath = await waitFor(async()=>findDownload('Rich host review-1-annotated.png',1000),'annotated PNG');
  const pngPath = join(fresh,'Rich.png'); adb('pull',pngDevicePath,pngPath);
  const png = readFileSync(pngPath); assert.equal(png.subarray(1,4).toString(),'PNG');
  assert.deepEqual(await js(`(async()=>{const i=new Image();i.src='data:image/png;base64,${png.toString('base64')}';await i.decode();const c=document.createElement('canvas');c.width=i.width;c.height=i.height;c.getContext('2d').drawImage(i,0,0);return [...c.getContext('2d').getImageData(180,760,1,1).data];})()`),[234,179,8,255]);
  await loadFixture(richSaved,'application/octet-stream',rich.title);
  await waitFor(()=>js('document.querySelectorAll("[data-shape]").length === 3 && document.querySelectorAll(".placed .katex").length === 1'),'reopened rich workspace');
  console.log('Android schema4 rich math/code editing, text resize/recolor, grouped connector movement/undo, native save/reopen and PNG pixels passed.');
  console.log('Android notebook round trip, cancelled save, local PNG decoding, two-page PDF rendering and source-linked feedback export passed.');
} catch (error) {
  console.error('WebView startup diagnostics:', JSON.stringify(startupDiagnostics));
  console.error('Recent input events:', await js('JSON.stringify(window.__reviewInputs || [])').catch(()=>'unavailable'));
  console.error('Module startup state:', await js('JSON.stringify({ready:document.readyState,busy:document.body.getAttribute("aria-busy"),handler:typeof document.querySelector("#new-notebook")?.onclick,error:document.querySelector("#startup-error")?.textContent,resources:performance.getEntriesByType("resource").filter(r=>/\\.m?js(?:$|\\?)/.test(r.name)).map(r=>({url:r.name,duration:r.duration,size:r.decodedBodySize}))})').catch(()=>'unavailable'));
  try { mkdirSync('.scratch',{recursive:true}); writeFileSync('.scratch/android-failure.png',execFileSync('adb',['exec-out','screencap','-p'],{timeout:15000})); }
  catch (captureError) { console.error('Failure screenshot unavailable:',captureError.message); }
  console.error('Workspace state:', await js('JSON.stringify({title:document.querySelector("#name")?.textContent,state:document.querySelector("#state")?.textContent,notice:document.querySelector("#message")?.textContent,dialog:document.querySelector("dialog[open]")?.id,active:document.hasFocus(),focus:document.activeElement?.id,deviceTap:window.__deviceTap,cardHidden:document.querySelector("#card")?.hidden,expanded:document.querySelector(".marker")?.getAttribute("aria-expanded")})').catch(()=>'unavailable'));
  throw error;
} finally {
  socket.close();
  adb('forward','--remove','tcp:'+port);
}
