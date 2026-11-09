if (typeof importScripts === 'function') importScripts('core.js');
const api = globalThis.browser || chrome;
const queues = new Map();
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
  if (sender.id !== api.runtime.id || message?.target !== 'inklane-web' || !sender.tab) return;
  (async () => {
    if (message.command === 'capture') {
      const active = await api.tabs.query({ active: true, windowId: sender.tab.windowId });
      if (active[0]?.id !== sender.tab.id) throw Error('tab');
      const dataUrl = await api.tabs.captureVisibleTab(sender.tab.windowId, { format: 'png' });
      const after = await api.tabs.query({ active: true, windowId: sender.tab.windowId });
      if (after[0]?.id !== sender.tab.id) throw Error('tab');
      return { dataUrl };
    }
    const data = InklaneWeb.validate(message.data);
    if (new URL(data.url).origin !== new URL(sender.url).origin) throw Error('origin');
    if (message.command === 'report') {
      const token = 'report:' + crypto.randomUUID();
      await api.storage.session.set({ [token]: data });
      await api.tabs.create({ url: api.runtime.getURL('report.html') + '#' + token });
      return { ok: true };
    }
    if (message.command === 'save') {
      const key = await InklaneWeb.keyFor(data.url);
      const before = queues.get(key) || Promise.resolve();
      const next = before.catch(() => {}).then(async () => {
        const current = (await api.storage.local.get(key))[key];
        const revision = current?.revision || 0;
        if (revision !== message.revision) return { conflict: true };
        await api.storage.local.set({ [key]: { data, revision: revision + 1 } });
        return { revision: revision + 1 };
      });
      queues.set(key, next);
      try { return await next; } finally { if (queues.get(key) === next) queues.delete(key); }
    }
    throw Error('command');
  })().then(respond, error => respond({ error: error.message || 'failed' }));
  return true;
});
