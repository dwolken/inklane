const api=globalThis.browser || chrome, nonce=location.hash.slice(1), title=document.getElementById('title'), body=document.getElementById('body'), status=document.getElementById('status');
let ready=false,checkpoint=true,sequence=0,de=false;
for(const field of [title,body]){
  field.addEventListener('focus',()=>checkpoint=true);
  field.addEventListener('input',()=>{
    if(!ready)return;const at=++sequence;
    // Dispatch every change before the frame can be removed by selection. The
    // background serializes this session, retaining the last typed character.
    const message={target:'inklane-web',command:'editor-change',nonce,title:title.value,text:body.value,checkpoint};checkpoint=false;
    api.runtime.sendMessage(message).then(r=>{if(at!==sequence)return;if(!r?.ok)throw Error(r?.error);status.textContent='';})
      .catch(()=>{if(at===sequence)status.textContent=de?'Änderung nicht gespeichert. Kopiere deinen Text, bevor du den Editor schließt.':'Change not saved. Copy your text before closing the editor.';});
  });
}
(async()=>{try{const r=await api.runtime.sendMessage({target:'inklane-web',command:'editor-load',nonce});if(r?.error || typeof r?.text!=='string')throw Error('session');de=r.de;document.documentElement.lang=de?'de':'en';title.placeholder=de?'Titel der Anmerkung':'Remark title';body.placeholder=de?'Schreibe deine Erklärung…':'Write your explanation…';title.setAttribute('aria-label',title.placeholder);body.setAttribute('aria-label',body.placeholder);title.value=r.title;body.value=r.text;ready=true;title.disabled=body.disabled=false;document.body.dataset.ready='true';if(r.focus){body.focus();body.setSelectionRange(body.value.length,body.value.length);}}catch{title.disabled=body.disabled=true;status.textContent='Open this editor from Inklane on the page.';}})();
