// The embedded example and bundled native apps don't need a second offline copy.
if (!globalThis.inklaneNativeHost && !globalThis.inklaneSaveCopy && window.top === window &&
    !new URL(location.href).searchParams.has('embed')) setup();

function setup() {
  const menu = document.querySelector('#more .menu');
  if (!menu) return;
  const words = {
    menu: ['Offline access…','Offline verwenden…'], title: ['Use Inklane offline','Inklane offline verwenden'],
    preparing: ['Preparing the workspace for offline use…','Der Arbeitsbereich wird für die Offline-Nutzung vorbereitet…'],
    ready: ['This workspace is available offline on this device.','Dieser Arbeitsbereich ist auf diesem Gerät offline verfügbar.'],
    unavailable: ['Offline storage is unavailable in this browser. Save a copy of your work to a file.','Dieser Browser kann den Arbeitsbereich nicht offline speichern. Sichere deine Arbeit in einer Datei.'],
    browser: ['To install, use the install command in your browser menu.','Zum Installieren wähle die Installationsoption im Browsermenü.'],
    ios: ['In Safari, choose Share → Add to Home Screen.','Wähle in Safari „Teilen“ → „Zum Home-Bildschirm“.'],
    mac: ['In Safari, choose File → Add to Dock.','Wähle in Safari „Ablage“ → „Zum Dock hinzufügen“.'],
    install: ['Install Inklane','Inklane installieren'], close: ['Done','Fertig'],
  };
  let availability='preparing', installPrompt=null;
  const el=(tag)=>document.createElement(tag);
  const launch=el('button'), dialog=el('dialog'), title=el('h2'), status=el('p'), guide=el('p'),
        actions=el('div'), install=el('button'), close=el('button');
  dialog.id='offline-access'; title.id='offline-heading'; dialog.setAttribute('aria-labelledby',title.id);
  status.setAttribute('role','status'); actions.className='relink-actions'; install.className='primary';
  actions.append(install,close); dialog.append(title,status,guide,actions); document.body.append(dialog);
  menu.insertBefore(launch,document.querySelector('#language'));
  const ua=navigator.userAgent, iOS=/iP(ad|hone|od)/.test(ua)||(navigator.platform==='MacIntel'&&navigator.maxTouchPoints>1);
  const instruction=iOS?'ios':/Macintosh/.test(ua)&&/Version\/.+Safari\//.test(ua)?'mac':'browser';
  function update() {
    const t=k=>words[k][document.documentElement.lang==='de'?1:0];
    launch.textContent=t('menu'); title.textContent=t('title'); status.textContent=t(availability);
    guide.textContent=t(instruction); install.textContent=t('install'); close.textContent=t('close');
    install.hidden=!installPrompt; guide.hidden=!!installPrompt || matchMedia('(display-mode: standalone)').matches || navigator.standalone===true;
  }
  new MutationObserver(update).observe(document.documentElement,{attributeFilter:['lang']});
  launch.onclick=()=>{ document.querySelector('#more').open=false; update(); dialog.showModal(); close.focus(); };
  close.onclick=()=>dialog.close();
  addEventListener('beforeinstallprompt',e=>{e.preventDefault();installPrompt=e;update();});
  addEventListener('appinstalled',()=>{installPrompt=null;update();});
  install.onclick=async()=>{const prompt=installPrompt;if(!prompt)return;installPrompt=null;try{await prompt.prompt();}finally{update();}};
  function set(value){availability=value;update();}
  update();
  if (!('serviceWorker' in navigator)) {set('unavailable');return;}
  async function register() {
    try {
      const registration=await navigator.serviceWorker.register(new URL('./sw.js',import.meta.url),{scope:'./',updateViaCache:'none'});
      if(registration.active) set('ready');
      function observe(){
        const worker=registration.installing;
        worker?.addEventListener('statechange',()=>{if(worker.state==='redundant'&&!registration.active)set('unavailable');});
      }
      observe();registration.addEventListener('updatefound',observe);
      await navigator.serviceWorker.ready; set('ready');
    } catch {set('unavailable');}
  }
  if(document.readyState==='complete') register(); else addEventListener('load',register,{once:true});
}
