import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {writeFile,mkdir,mkdtemp,rename,rm} from 'node:fs/promises';
import {dirname,join} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createRequire} from 'node:module';
import {createSound} from './score.mjs';
import {MOTION} from './motion.mjs';
const root=dirname(fileURLToPath(import.meta.url));
const require=createRequire(new URL('../../website/package.json',import.meta.url));
const {chromium}=require('playwright-core');
const previewsOnly=process.argv.includes('--previews');
const fps=60,frames=Math.round(MOTION.duration*fps);
const browser=await chromium.launch(process.env.BROWSER_PATH?{executablePath:process.env.BROWSER_PATH,headless:true}:{channel:'msedge',headless:true});
const preview=join(root,'previews'),temp=await mkdtemp(join(root,'.render-'));
await mkdir(preview,{recursive:true});
try{
  const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});
  await page.goto(pathToFileURL(join(root,'inklane-sting.html')).href+'?render');
  await page.waitForFunction(()=>window.filmReady===true);
  const frame=async t=>{await page.evaluate(t=>window.renderFrame(t),t);return page.screenshot()};
  const times=[.16,.52,.90,1.30,1.68,2.05,2.45,2.85,3.40,3.52,4.39,5.19];
  for(const t of times)await writeFile(join(preview,`frame-${t.toFixed(2)}.png`),await frame(t));
  if(!previewsOnly){
    const score=join(temp,'score.wav');await writeFile(score,createSound(MOTION.duration));
    const encoder=spawn('ffmpeg',['-v','error','-y','-f','image2pipe','-framerate','60','-c:v','png','-i','pipe:0','-i',score,'-c:v','libx264','-crf','16','-preset','medium','-pix_fmt','yuv420p','-vf','scale=out_color_matrix=bt709','-colorspace','bt709','-color_trc','iec61966-2-1','-color_primaries','bt709','-c:a','aac','-b:a','192k','-movflags','+faststart',join(temp,'sting.mp4')],{stdio:['pipe','ignore','inherit'],windowsHide:true});
    const finished=once(encoder,'close');let error;encoder.stdin.on('error',e=>error=e);
    for(let i=0;i<frames;i++){if(error)throw error;const bytes=await frame(i/fps);if(!encoder.stdin.write(bytes))await once(encoder.stdin,'drain');if(i%fps===0)console.log(`Rendered ${i}/${frames}`)}
    encoder.stdin.end();const [code]=await finished;if(code!==0)throw Error(`Encoder exit ${code}`);
    await rename(join(temp,'sting.mp4'),join(root,'inklane-sting.mp4'));
    const silent=spawn('ffmpeg',['-v','error','-y','-i',join(root,'inklane-sting.mp4'),'-c:v','copy','-an','-movflags','+faststart',join(root,'inklane-sting-silent.mp4')],{stdio:'ignore',windowsHide:true});const [s]=await once(silent,'close');if(s!==0)throw Error('Silent export failed');
  }
  const board=await browser.newPage({viewport:{width:1600,height:720},deviceScaleFactor:1});await board.goto(pathToFileURL(join(root,'storyboard.html')).href);await board.screenshot({path:join(preview,'motion-sheet.png')});
  console.log(JSON.stringify({fps,duration:MOTION.duration,frames,rendered:!previewsOnly}));
}finally{
  await browser.close();if(dirname(temp)!==root||!temp.startsWith(join(root,'.render-')))throw Error('Invalid render staging directory');await rm(temp,{recursive:true,force:true});
}
