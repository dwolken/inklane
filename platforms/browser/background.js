if (typeof importScripts === 'function') importScripts('core.js');
const api = globalThis.browser || chrome;
const queues = new Map();
function serial(key, action) {
  const before = queues.get(key) || Promise.resolve();
  const next = before.catch(() => {}).then(action); queues.set(key,next);
  return next.finally(() => { if (queues.get(key) === next) queues.delete(key); });
}
const ownPage = (sender,name) => sender.url?.split('#')[0].split('?')[0] === api.runtime.getURL(name);
api.action.onClicked.addListener(async tab => {
  try {
    InklaneWeb.cleanUrl(tab.url);
    await api.scripting.executeScript({ target: { tabId: tab.id }, files: ['core.js', 'content.js'] });
    await api.action.setBadgeText({ tabId: tab.id, text: '' });
  } catch (error) {
    console.warn('Inklane could not activate:', error.message);
    await api.action.setBadgeText({ tabId: tab.id, text: '!' });
    await api.tabs.create({ url: api.runtime.getURL('help.html') + '?restricted=1' });
  }
});
api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id || message?.target !== 'inklane-web') return;
  (async () => {
    if (message.command === 'delete') {
      if (!ownPage(sender,'help.html') || typeof message.key !== 'string' || !/^review:[a-f0-9]{64}$/.test(message.key)) throw Error('sender');
      return serial(message.key,async () => {
        const current = (await api.storage.local.get(message.key))[message.key];
        if ((current?.revision || 0) !== message.revision) return { conflict:true };
        await api.storage.local.set({[message.key]:{revision:(current?.revision || 0)+1,deleted:true}});
        return {ok:true};
      });
    }
    if (message.command === 'editor-load' || message.command === 'editor-change') {
      if (!sender.tab || !ownPage(sender,'editor.html') || typeof message.nonce !== 'string' || !/^[a-f0-9-]{36}$/.test(message.nonce)) throw Error('sender');
      const key = 'editor:'+message.nonce;return serial(key,async()=>{
      const session = (await api.storage.session.get(key))[key];
      if (!session || session.tabId !== sender.tab.id || session.expires < Date.now() || session.frameId != null && session.frameId !== sender.frameId) throw Error('session');
      const tab = await api.tabs.get(sender.tab.id);
      if (InklaneWeb.cleanUrl(tab.url) !== session.url) throw Error('changed');
      if (message.command === 'editor-load') {
        session.frameId = sender.frameId; await api.storage.session.set({[key]:session});
        return {title:session.title,text:session.text,focus:session.focus,de:session.de};
      }
      if (session.frameId !== sender.frameId || typeof message.title !== 'string' || message.title.length > 200 || /[\u0000-\u001f\u007f-\u009f]/.test(message.title) || typeof message.text !== 'string' || message.text.length > 50000) throw Error('invalid');
      const result = await api.tabs.sendMessage(session.tabId,{target:'inklane-content',command:'editor-change',nonce:message.nonce,objectId:session.objectId,title:message.title,text:message.text,checkpoint:!!message.checkpoint},{frameId:session.contentFrame});
      if (!result?.ok) throw Error(result?.error || 'editor');
      session.title=message.title; session.text=message.text; session.expires=Date.now()+3600000;await api.storage.session.set({[key]:session});return {ok:true};
      });
    }
    if (!sender.tab || sender.frameId !== 0) throw Error('sender');
    const url = InklaneWeb.cleanUrl((await api.tabs.get(sender.tab.id)).url);
    if(new URL(sender.url).origin!==new URL(url).origin)throw Error('changed');
    if (message.command === 'editor-open') {
      if (message.url !== url || typeof message.objectId !== 'string' || typeof message.title !== 'string' || message.title.length > 200 || typeof message.text !== 'string' || message.text.length > 50000) throw Error('invalid');
      const old=await api.storage.session.get(null), previous=Object.entries(old).filter(([key,value])=>key.startsWith('editor:') && (value.expires<Date.now() || value.tabId===sender.tab.id)).sort((a,b)=>b[1].expires-a[1].expires);
      const discard=previous.filter(([,value],i)=>value.expires<Date.now() || i>=8).map(([key])=>key);if(discard.length)await api.storage.session.remove(discard);
      const nonce=crypto.randomUUID();
      await api.storage.session.set({['editor:'+nonce]:{tabId:sender.tab.id,contentFrame:sender.frameId,objectId:message.objectId,url,title:message.title,text:message.text,focus:!!message.focus,de:!!message.de,expires:Date.now()+3600000}});
      return {nonce};
    }
    if (message.command === 'capture') {
      if (message.url !== url) throw Error('changed');
      const active = await api.tabs.query({ active: true, windowId: sender.tab.windowId });
      if (active[0]?.id !== sender.tab.id || InklaneWeb.cleanUrl(active[0].url) !== url) throw Error('tab');
      const dataUrl = await api.tabs.captureVisibleTab(sender.tab.windowId, { format: 'png' });
      const after = await api.tabs.query({ active: true, windowId: sender.tab.windowId });
      if (after[0]?.id !== sender.tab.id || InklaneWeb.cleanUrl(after[0].url) !== url) throw Error('tab');
      return { dataUrl };
    }
    const data = InklaneWeb.validate(message.data);
    if (data.url !== url) throw Error('changed');
    if (message.command === 'report') {
      const token = 'report:' + crypto.randomUUID();
      await api.storage.session.set({ [token]: data });
      await api.tabs.create({ url: api.runtime.getURL('report.html') + '#' + token });
      return { ok: true };
    }
    if (message.command === 'save') {
      const key = await InklaneWeb.keyFor(data.url);
      return serial(key,async () => {
        if(InklaneWeb.cleanUrl((await api.tabs.get(sender.tab.id)).url)!==data.url)throw Error('changed');
        const current = (await api.storage.local.get(key))[key];
        const revision = current?.revision || 0;
        if (revision !== message.revision) return { conflict: true };
        await api.storage.local.set({ [key]: { data, revision: revision + 1 } });
        return { revision: revision + 1 };
      });
    }
    throw Error('command');
  })().then(respond, error => respond({ error: error.message || 'failed' }));
  return true;
});
