import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { readFile, mkdir, mkdtemp } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';
import { chromium, webkit } from 'playwright-core';

// Two asymmetric, visibly different pages. Page 2 has a nonzero crop origin and
// 90° rotation: fitting the media box instead would silently misplace feedback.
function reviewPdf() {
  const objects = ['<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Count 2 /Kids [3 0 R 5 0 R] >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 150] /Resources << >> /Contents 4 0 R >>',
    '1 0 0 rg 0 0 150 150 re f',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /CropBox [20 20 200 140] /Rotate 90 /Resources << >> /Contents 6 0 R >>',
    '0 0 1 rg 20 20 180 120 re f'];
  let pdf = '%PDF-1.5\n'; const offsets = ['0000000000 65535 f '];
  objects.forEach((s,i) => {
    offsets.push(String(pdf.length).padStart(10,'0')+' 00000 n ');
    if (i===3 || i===5) s = `<< /Length ${s.length} >>\nstream\n${s}\nendstream`;
    pdf += `${i+1} 0 obj\n${s}\nendobj\n`;
  });
  return Buffer.from(pdf + `xref\n0 7\n${offsets.join('\n')}\ntrailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n${pdf.length}\n%%EOF\n`);
}

const root = resolve(process.argv[2]);
const mime = {'.mjs':'text/javascript','.js':'text/javascript','.css':'text/css','.html':'text/html',
  '.png':'image/png','.svg':'image/svg+xml','.wasm':'application/wasm','.webmanifest':'application/manifest+json','.json':'application/json'};
let disconnected = false;
const server = createServer(async (req,res) => {
  if (disconnected) { req.socket.destroy(); return; }
  const path = new URL(req.url,'http://localhost').pathname;
  if (!path.startsWith('/inklane/')) return res.writeHead(404).end();
  const file = resolve(root,path.slice(9) + (path.endsWith('/')?'index.html':''));
  if (!file.startsWith(root+sep)) return res.writeHead(403).end();
  try { const bytes=await readFile(file); res.writeHead(200,{'Content-Type':mime[extname(file)]||'application/octet-stream'}).end(bytes); }
  catch { res.writeHead(404).end(); }
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const origin = `http://127.0.0.1:${server.address().port}`;
const engine = process.env.INKLANE_ENGINE || 'chromium';
const profileRoot=resolve('.scratch/platform-profiles'); await mkdir(profileRoot,{recursive:true});
const profile=await mkdtemp(resolve(profileRoot,engine+'-'));
const context=await (engine==='webkit'?webkit:chromium).launchPersistentContext(profile,{
  viewport:{width:1060,height:760},locale:'en-US',
  headless: !process.env.INKLANE_INSTALL_CHECK,
  ...(engine==='webkit'?{}:process.env.BROWSER_PATH?{executablePath:process.env.BROWSER_PATH}:{channel:process.platform==='win32'?'msedge':'chrome'})
});
try {
  const page = await context.newPage(), external=[], errors=[];
  context.on('request',r=>{const u=new URL(r.url()); if((/^https?:$/.test(u.protocol) && u.origin!==origin) || !['GET','HEAD'].includes(r.method())) external.push(r.method()+' '+u.origin);});
  page.on('pageerror',e=>errors.push(e.message));
  await page.goto(origin+'/inklane/editor/?lang=en');
  await page.locator('#start').waitFor();
  await page.waitForFunction(()=>Boolean(navigator.serviceWorker?.controller),{},{timeout:90000});
  if (engine==='chromium') {
    const session=await context.newCDPSession(page);
    const {errors:manifestErrors,data}=await session.send('Page.getAppManifest'); assert.equal(manifestErrors.length,0);
    const app=JSON.parse(data); assert.equal(app.display,'standalone'); assert.equal(app.scope,'./');
    // A relative manifest id resolves against the origin root, not the project
    // directory. The default derived from start_url keeps this installation scoped.
    const manifestId=origin+'/inklane/editor/';
    const identity=await session.send('Page.getAppId'); assert.equal(identity.appId,manifestId);
    const installability=await session.send('Page.getInstallabilityErrors'); assert.deepEqual(installability.installabilityErrors,[]);
    if (process.env.INKLANE_INSTALL_CHECK) {
      // Use a disposable CI profile and OS. Never install a test app into a developer desktop.
      if (process.env.GITHUB_ACTIONS!=='true') throw Error('Installation test requires an ephemeral Actions runner.');
      await session.send('PWA.install',{manifestId,installUrlOrBundleUrl:manifestId});
      try {
        const next=context.waitForEvent('page');
        await session.send('PWA.launch',{manifestId});
        const appWindow=await next;
        await appWindow.locator('#start').waitFor();
        assert.equal(await appWindow.evaluate(()=>matchMedia('(display-mode: standalone)').matches),true);
        await appWindow.close();
        console.log('Installed and launched the standalone desktop workspace.');
      } finally {await session.send('PWA.uninstall',{manifestId});}
    }
  }
  // Cut the origin off at the server (not just the tab). A fresh navigation and a
  // first-ever PDF open must now succeed using only the installed static resources.
  disconnected=true;
  const offline=await context.newPage(); offline.on('pageerror',e=>errors.push(e.message));
  await offline.goto(origin+'/inklane/editor/?lang=de');
  await offline.locator('#start').waitFor();
  const binary=reviewPdf();
  const pixels = await offline.evaluate(async bytes=>{
    const {openPdf}=await import('./pdf-source.js');
    const source=await openPdf(new File([new Uint8Array(bytes)],'review.pdf',{type:'application/pdf'}));
    try {
      const sample=(canvas,x,y)=>Array.from(canvas.getContext('2d').getImageData(Math.floor(x*canvas.width),Math.floor(y*canvas.height),1,1).data);
      const a=await source.render(0), b=await source.render(1);
      return {pages:source.pages,hash:source.hash,left:sample(a,.25,.5),right:sample(a,.75,.5),rotated:sample(b,.5,.5)};
    } finally { await source.dispose(); }
  }, [...binary]);
  assert.deepEqual(pixels.pages,[{width:400,height:200},{width:160,height:240}]);
  assert.equal(pixels.hash,createHash('sha256').update(binary).digest('hex').toUpperCase());
  assert.deepEqual([pixels.left,pixels.right,pixels.rotated],[[255,0,0,255],[255,255,255,255],[0,0,255,255]]);
  await offline.locator('#files').setInputFiles({name:'review.pdf',mimeType:'application/pdf',buffer:binary});
  await offline.waitForFunction(()=>document.querySelector('#name')?.textContent==='review.pdf');
  await offline.locator('#page-count').click(); await offline.locator('#page-choices button').nth(1).click();
  await offline.locator('#viewport').waitFor();
  assert.deepEqual(errors,[]); assert.deepEqual(external,[]);
  console.log(`${process.platform}/${engine}: offline navigation, first local PDF import, rotation/crop, source hash, rendered pixels and page navigation passed.`);
} finally { await context.close(); server.close(); }
