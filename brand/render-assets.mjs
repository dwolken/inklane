import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const require=createRequire(new URL('../website/package.json',import.meta.url));
const {chromium}=require('playwright-core');
const asset=name=>readFile(resolve(root,'src/Inklane/Assets',name),'utf8');

/** Export committed artwork without introducing fonts or external resources. */
export async function renderAssets(platforms=['web','apple','browser']) {
  const browser=await chromium.launch(process.env.BROWSER_PATH
    ? {executablePath:process.env.BROWSER_PATH,headless:true}
    : {channel:'msedge',headless:true});
  try {
    const [tile,small,square,foreground,background]=await Promise.all(
      ['inklane-app.svg','inklane-app-small.svg','inklane-app-square.svg','inklane-adaptive-foreground.svg','inklane-adaptive-background.svg'].map(asset));
    const maskable=background.replace('</svg>',foreground.replace(/^<svg[^>]*>/,'').replace('</svg>','')+'</svg>');
    const exports=[];
    const render=async(file,size,svg,{padding=0,opaque=false,height=size}={})=>{
      const page=await browser.newPage({viewport:{width:size,height},deviceScaleFactor:1});
      try {
        await page.setContent(`<style>html,body{margin:0;width:100%;height:100%;background:${opaque?'#111114':'transparent'}}body{display:grid;place-items:center}svg{display:block;width:${100-padding*2}%;height:${100-padding*2}%}</style>${svg}`);
        const path=resolve(root,file);await mkdir(dirname(path),{recursive:true});
        const bytes=await page.screenshot({path,omitBackground:!opaque});
        if(opaque&&bytes[25]!==2)throw Error(`Opaque icon must have RGB PNG encoding: ${file}`);
        exports.push(file);return bytes;
      }finally{await page.close()}
    };
    if(platforms.includes('web')) {
      for(const size of [180,192,512])await render(`website/editor/icons/app-${size}.png`,size,size===180?square:tile,{opaque:size===180});
      for(const size of [192,512])await render(`website/editor/icons/app-maskable-${size}.png`,size,maskable,{opaque:true});
    }
    if(platforms.includes('apple')) {
      const base='platforms/apple/Resources/Assets.xcassets/AppIcon.appiconset';
      await render(`${base}/icon-ios-1024.png`,1024,square,{opaque:true});
      for(const size of [16,32,64,128,256,512,1024])await render(`${base}/icon-${size}.png`,size,size<=24?small:tile,{padding:9});
    }
    if(platforms.includes('browser'))for(const size of [16,32,48,128,192])await render(`platforms/browser/icon-${size}.png`,size,size<=24?small:tile);
    // Optional Windows regeneration; the normal platform pass preserves the ICO master.
    if(platforms.includes('windows')) {
      for(const mode of ['light','dark'])await render(`src/Inklane/Assets/inklane-lockup-${mode}.png`,990,await asset(`inklane-lockup-${mode}.svg`),{height:256});
      const sizes=[16,20,24,32,40,48,64,96,128,192,256],images=[];
      for(const size of sizes)images.push(await render(`.scratch/brand/icon-${size}.png`,size,size<=24?small:tile));
      const header=Buffer.alloc(6+16*sizes.length);header.writeUInt16LE(1,2);header.writeUInt16LE(sizes.length,4);
      let offset=header.length;
      images.forEach((bytes,i)=>{const p=6+i*16;header[p]=header[p+1]=sizes[i]===256?0:sizes[i];header.writeUInt16LE(1,p+4);header.writeUInt16LE(32,p+6);header.writeUInt32LE(bytes.length,p+8);header.writeUInt32LE(offset,p+12);offset+=bytes.length;});
      await writeFile(resolve(root,'src/Inklane/Assets/inklane.ico'),Buffer.concat([header,...images]));
      exports.push('src/Inklane/Assets/inklane.ico');
    }
    console.log(JSON.stringify({exports},null,2));
  }finally{await browser.close()}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href) {
  const option=process.argv.find(arg=>arg.startsWith('--platforms='));
  await renderAssets(option?option.split('=')[1].split(','):undefined);
}
