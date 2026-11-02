import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';

const adb = (...args) => execFileSync('adb', args, { encoding: 'utf8', timeout: 30000 }).trim();
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
socket.onmessage = ({data}) => {
  const msg = JSON.parse(data);
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
  await command('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x,y});
  await command('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x,y});
}
async function type(sel,value) {
  await js(find(sel)+'.focus();'+find(sel)+'.select()');
  await command('Input.insertText',{text:value});
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
const saveButton = n => /^save$/i.test(n.text) && n.enabled==='true';
const fresh = mkdtempSync(join(tmpdir(),'inklane-android-'));
const title = 'AndroidReview';
try {
  await waitFor(()=>visible('#new-notebook'), 'document home');
  assert.equal(await js('isSecureContext && typeof inklaneSaveCopy === "function"'),true);
  await tap('#new-notebook');
  await type('#book-title',title);
  await js('document.querySelector("#paper-form").requestSubmit()');
  await waitFor(()=>visible('#sheet'), 'new notebook');
  await tap('[data-tool="note"]');
  await tap('#sheet',.48,.19);
  await type('#note-title','Door clearance');
  await type('#note-body','Leave room for the handle.');
  await tap('#close-card');
  await tap('#save');
  await waitFor(async()=>nodes().some(saveButton),'system create picker');
  adb('shell','input','keyevent','BACK');
  await waitFor(async()=>await js('document.hasFocus() && !document.querySelector("#save").disabled'),'workspace after cancelled save');
  await tap('#save');
  await systemTap(saveButton,'system Save');
  // DocumentsUI creates an empty entry before our activity receives its URI.
  // Wait for the editor to confirm the native write before inspecting that entry.
  await waitFor(()=>js('document.hasFocus() && !document.querySelector("#save").disabled && document.querySelector("#state").textContent.toLowerCase().includes("saved")'), 'editor confirmation of native write');
  const deviceFile=await waitFor(async()=>adb('shell','find','/sdcard/Download','-size','+12c','-name',title+'.inklane'), 'committed notebook');
  adb('pull',deviceFile,join(fresh,'saved.inklane'));
  const bytes=readFileSync(join(fresh,'saved.inklane'));
  const meta=JSON.parse(bytes.subarray(12,12+bytes.readUInt32BE(8)).toString('utf8'));
  assert.equal(meta.document.title,title);
  const [note]=meta.document.pages[0].items.filter(item => item.isNote);
  assert.equal(note.title,'Door clearance');
  assert.equal(note.text,'Leave room for the handle.');
  assert.ok(note.x>0 && note.y>0,'anchor coordinates persisted');
  await tap('#more summary');
  await tap('#home');
  await waitFor(()=>visible('#new-notebook'),'document home before reopening');
  await tap('#start');
  await systemTap(n=>n.text===title+'.inklane','saved document in system picker');
  await waitFor(()=>js('document.querySelector("#name").textContent === "'+title+'" && document.querySelectorAll(".marker").length>0'), 'reopened annotation');
  await tap('.marker');
  assert.equal(await js(find('#note-body')+'.value'),'Leave room for the handle.');
  console.log('Android offline notebook, anchored remark, cancelled save, SAF file write and reopen passed.');
} catch (error) {
  console.error('Workspace state:', await js('JSON.stringify({title:document.querySelector("#name")?.textContent,state:document.querySelector("#state")?.textContent,notice:document.querySelector("#message")?.textContent,dialog:document.querySelector("dialog[open]")?.id})').catch(()=>'unavailable'));
  throw error;
} finally {
  socket.close();
  adb('forward','--remove','tcp:'+port);
}
