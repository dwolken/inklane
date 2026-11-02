// CACHE_SETTINGS is replaced by the build with a fingerprint of the public app assets.
const { revision, files, maps } = CACHE_SETTINGS;
const scope = self.registration.scope;
const prefix = `inklane-workspace:${scope}:`;
const cacheName = prefix + revision;
const absolute = path => new URL(path, scope).href;
const paths = new Set([...files, ...maps].map(absolute));

self.addEventListener('install', event => event.waitUntil((async () => {
  const cache = await caches.open(cacheName);
  try {
    // The 168 small character maps arrive in a single transfer on mobile.
    // Cache only shipped assets. User-selected documents never pass through here.
    const bundle = await fetch(absolute('offline-maps.json'), { cache: 'reload' });
    if (!bundle.ok) throw Error('Missing PDF maps');
    const contents = await bundle.json();
    for (const path of maps) {
      const data = Uint8Array.from(atob(contents[path]), ch => ch.charCodeAt(0));
      await cache.put(absolute(path), new Response(data, { headers: { 'Content-Type': 'application/octet-stream' } }));
    }
    await cache.addAll(files.map(path => new Request(absolute(path), { cache: 'reload' })));
  } catch (error) { await caches.delete(cacheName); throw error; }
  // Let an open editor finish with its own revision before activating an update.
})()));

self.addEventListener('activate', event => event.waitUntil((async () => {
  for (const key of await caches.keys()) if (key.startsWith(prefix) && key !== cacheName) await caches.delete(key);
  await self.clients.claim();
})()));

self.addEventListener('fetch', event => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  let key = url.href;
  if (req.mode === 'navigate' && (url.origin + url.pathname === scope || url.origin + url.pathname === absolute('index.html'))) {
    key = absolute('index.html'); // language and sample options remain in the address bar
  }
  if (!paths.has(key)) return;
  event.respondWith(caches.open(cacheName).then(async cache => await cache.match(key) || fetch(req)));
});
