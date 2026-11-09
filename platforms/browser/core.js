/* Shared by the isolated content script, extension pages and local checks. */
(() => {
  if (globalThis.InklaneWeb) return;
  const MAX_BYTES = 2 * 1024 * 1024, MAX_OBJECTS = 500, MAX_POINTS = 100000;
  const text = element => (element?.textContent || element?.getAttribute?.('alt') || '').replace(/\s+/g, ' ').trim().slice(0, 280);
  const finite = (n, min = -1e7, max = 1e7) => Number.isFinite(n) && n >= min && n <= max;
  function cleanUrl(value) {
    const url = new URL(value);
    if (!['http:', 'https:'].includes(url.protocol)) throw Error('page');
    url.username = ''; url.password = ''; url.hash = '';
    const keys=[];url.searchParams.forEach((_,key)=>keys.push(key));
    for (const key of keys) {
      if (/^(utm_|fbclid$|gclid$|msclkid$|token$|access_token$|auth$|authorization$|api_?key$|password$|passwd$|session|code$|sig(nature)?$)/i.test(key)) url.searchParams.delete(key);
    }
    url.searchParams.sort();
    return url.href;
  }
  async function keyFor(url) {
    const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(cleanUrl(url)));
    return 'review:' + [...new Uint8Array(bytes)].map(n => n.toString(16).padStart(2, '0')).join('');
  }
  function selectorFor(element) {
    const parts = [];
    for (let node = element; node && node !== document.documentElement && parts.length < 8; node = node.parentElement) {
      if (node.id && document.querySelectorAll('#' + CSS.escape(node.id)).length === 1) { parts.unshift('#' + CSS.escape(node.id)); break; }
      const tag = node.localName;
      const siblings = node.parentElement ? [...node.parentElement.children].filter(x => x.localName === tag) : [node];
      parts.unshift(tag + (siblings.length > 1 ? `:nth-of-type(${siblings.indexOf(node) + 1})` : ''));
    }
    return parts.join(' > ');
  }
  function anchorAt(x, y, host) {
    let element = document.elementsFromPoint(x, y).find(el => el !== host && !host?.contains(el));
    element = element?.closest('p,h1,h2,h3,h4,h5,h6,li,td,th,figcaption,figure,img,article,section,main') || element || document.body;
    if (['html', 'body'].includes(element.localName)) return { selector: '', quote: '', tag: '', rx: 0, ry: 0, x: x + scrollX, y: y + scrollY };
    const box = element.getBoundingClientRect();
    return { selector: selectorFor(element), quote: text(element), tag: element.localName, rx: (x - box.left) / Math.max(1, box.width), ry: (y - box.top) / Math.max(1, box.height), x: x + scrollX, y: y + scrollY };
  }
  function locate(anchor) {
    if (!anchor.selector) return { x: anchor.x - scrollX, y: anchor.y - scrollY, state: 'position' };
    let element;
    try { element = document.querySelector(anchor.selector); } catch { /* Invalid selectors are detached safely. */ }
    if (!element || (anchor.quote && text(element) !== anchor.quote)) {
      const matches = anchor.quote && /^[a-z][a-z0-9-]*$/.test(anchor.tag) ? [...document.querySelectorAll(anchor.tag)].slice(0, 3000).filter(el => text(el) === anchor.quote) : [];
      element = matches.length === 1 ? matches[0] : null;
    }
    if (!element) return { x: anchor.x - scrollX, y: anchor.y - scrollY, state: 'detached' };
    const box = element.getBoundingClientRect();
    return { x: box.left + anchor.rx * box.width, y: box.top + anchor.ry * box.height, state: box.width && box.height ? 'attached' : 'detached' };
  }
  function validate(value) {
    if (!value || value.format !== 'inklane.web-review' || value.version !== 1 || !Array.isArray(value.objects) || value.objects.length > MAX_OBJECTS || JSON.stringify(value).length > MAX_BYTES) throw Error('invalid');
    if (cleanUrl(value.url) !== value.url || typeof value.title !== 'string' || value.title.length > 300) throw Error('invalid');
    const ids = new Set(); let points = 0;
    for (const obj of value.objects) {
      if (!obj || !['note', 'text', 'ink'].includes(obj.type) || typeof obj.id !== 'string' || obj.id.length > 128 || ids.has(obj.id)) throw Error('invalid');
      ids.add(obj.id);
      if (!/^#[\da-f]{6}$/i.test(obj.color) || !finite(obj.width, 40, 1500) || !finite(obj.height, 40, 1500) || !finite(obj.fontSize, 8, 96) || !finite(obj.strokeWidth, .5, 24)) throw Error('invalid');
      if (typeof obj.title !== 'string' || obj.title.length > 200 || /[\u0000-\u001f\u007f-\u009f]/.test(obj.title) || typeof obj.text !== 'string' || obj.text.length > 50000) throw Error('invalid');
      const a = obj.anchor;
      if (!a || typeof a.selector !== 'string' || a.selector.length > 1024 || typeof a.quote !== 'string' || a.quote.length > 280 || typeof a.tag !== 'string' || a.tag.length > 50 || ![a.rx, a.ry, a.x, a.y].every(n => finite(n))) throw Error('invalid');
      if (obj.type === 'ink') {
        if (!Array.isArray(obj.points) || obj.points.length < 1) throw Error('invalid');
        points += obj.points.length;
        if (points > MAX_POINTS || !obj.points.every(p => finite(p.x) && finite(p.y) && finite(p.pressure, 0, 1))) throw Error('invalid');
      }
    }
    return value;
  }
  function create(url, title) { return { format: 'inklane.web-review', version: 1, url: cleanUrl(url), title: title.slice(0, 300), objects: [] }; }
  function object(type, anchor, color = '#6650B8') {
    return { id: crypto.randomUUID(), type, anchor, color, width: type === 'note' ? 340 : 240, height: type === 'note' ? 300 : 100, fontSize: 18, strokeWidth: 3, title: '', text: '', ...(type === 'ink' ? { points: [] } : {}) };
  }
  const escape = value => String(value).replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]);
  function portable(record, sourceBytes, sourceWidth, sourceHeight, viewportWidth, viewportHeight, positions) {
    const sx = sourceWidth / viewportWidth, sy = sourceHeight / viewportHeight;
    const items = [], ink = [];
    let excluded = 0;
    for (const obj of record.objects) {
      const p = positions[obj.id];
      const inside=pt=>pt.x>=0 && pt.y>=0 && pt.x<=viewportWidth && pt.y<=viewportHeight;
      function crosses(a,b){let start=0,end=1;const dx=b.x-a.x,dy=b.y-a.y;for(const [direction,distance] of [[-dx,a.x],[dx,viewportWidth-a.x],[-dy,a.y],[dy,viewportHeight-a.y]]){if(direction===0){if(distance<0)return false;continue;}const t=distance/direction;if(direction<0)start=Math.max(start,t);else end=Math.min(end,t);if(start>end)return false;}return true;}
      const visible = p && (obj.type === 'ink' ? obj.points.some((pt,i)=>{const at={x:p.x+pt.x,y:p.y+pt.y};return inside(at) || i>0 && crosses({x:p.x+obj.points[i-1].x,y:p.y+obj.points[i-1].y},at);})
        : obj.type === 'text' ? p.x<viewportWidth && p.y<viewportHeight && p.x+obj.width>0 && p.y+obj.height>0
        : p.x>-14 && p.y>-14 && p.x<viewportWidth+14 && p.y<viewportHeight+14);
      if (!visible) { excluded++; continue; }
      if (obj.type === 'ink') ink.push({ id: obj.id, color: obj.color, width: obj.strokeWidth * sx, points: obj.points.map(pt => ({ x: (p.x + pt.x) * sx, y: (p.y + pt.y) * sy, pressure: pt.pressure })) });
      else items.push({ id: obj.id, title: obj.title, text: obj.text, x: p.x * sx, y: p.y * sy, width: obj.width * sx, height: obj.height * sy, fontSize: obj.fontSize * sx, color: obj.color, isNote: obj.type === 'note', isExpanded: false });
    }
    return { excluded, create: async () => {
      const hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', sourceBytes))].map(n => n.toString(16).padStart(2, '0')).join('').toUpperCase();
      const document = { schemaVersion: 2, kind: 'Image', title: record.title.slice(0, 200).replace(/[\u0000-\u001f\u007f-\u009f]/g, ''), sourceImagePath: 'webpage.png', sourceImageSha256: hash, pages: [{ width: sourceWidth, height: sourceHeight, pattern: 'Blank', spacingMm: 5, marginMm: 0, images: [], items, ink }] };
      const metadata = new TextEncoder().encode(JSON.stringify({ format: 'inklane.workspace', version: 1, sourceName: 'webpage.png', document }));
      const header = new Uint8Array(12); header.set(new TextEncoder().encode('INKLANE1')); new DataView(header.buffer).setUint32(8, metadata.length);
      return new Blob([header, metadata, sourceBytes], { type: 'application/vnd.inklane.workspace' });
    } };
  }
  globalThis.InklaneWeb = { MAX_BYTES, MAX_OBJECTS, MAX_POINTS, cleanUrl, keyFor, anchorAt, locate, validate, create, object, escape, portable };
})();
