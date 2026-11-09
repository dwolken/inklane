const api = globalThis.browser || chrome;
const de = navigator.language.toLowerCase().startsWith('de');
document.documentElement.lang = de ? 'de' : 'en';
document.getElementById('print').textContent = de ? 'Drucken / PDF speichern' : 'Print / Save PDF';
document.getElementById('print').addEventListener('click', () => window.print());
(async () => {
  const token = location.hash.slice(1), root = document.getElementById('report');
  try {
    if (!/^report:[\da-f-]{36}$/.test(token)) throw Error('token');
    const stored = (await api.storage.session.get(token))[token];
    const data = InklaneWeb.validate(stored);
    // The report keeps its own in-memory data; one-use handoff leaves no extra disk copy.
    await api.storage.session.remove(token);
    const esc = InklaneWeb.escape;
    root.innerHTML = `<h2>${esc(data.title)}</h2><p class="source"><a href="${esc(data.url)}">${esc(data.url)}</a></p><p class="muted">${de ? 'Vollständige Anmerkungen mit dem zugehörigen Textausschnitt. Die Webseite selbst ist nicht in diesem Bericht enthalten.' : 'Complete annotations with their referenced text. The webpage itself is not included in this report.'}</p>`;
    for (const [index, obj] of data.objects.entries()) {
      const article = document.createElement('article');
      const type = obj.type === 'note' ? de ? 'Anmerkung' : 'Remark' : obj.type === 'text' ? 'Text' : de ? 'Zeichnung' : 'Drawing';
      const heading = document.createElement('h3'); heading.textContent = `${index + 1}. ${obj.title || type}`; article.append(heading);
      if (obj.anchor.quote) { const quote = document.createElement('blockquote'); quote.textContent = obj.anchor.quote; article.append(quote); }
      if (obj.type === 'ink') {
        const xs = obj.points.map(p => p.x), ys = obj.points.map(p => p.y), left = Math.min(...xs) - 10, top = Math.min(...ys) - 10;
        const width = Math.max(30, Math.max(...xs) - left + 10), height = Math.max(30, Math.max(...ys) - top + 10);
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', `${left} ${top} ${width} ${height}`); svg.setAttribute('width', String(Math.min(650, width))); svg.setAttribute('height', String(Math.min(400, height))); svg.setAttribute('aria-label', type);
        const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', obj.points.map((p, i) => `${i ? 'L' : 'M'}${p.x} ${p.y}`).join(' ')); path.setAttribute('fill', 'none'); path.setAttribute('stroke', obj.color); path.setAttribute('stroke-width', obj.strokeWidth); path.setAttribute('stroke-linecap', 'round'); svg.append(path); article.append(svg);
      }
      if (obj.text) { const body = document.createElement('div'); body.className = 'body'; body.textContent = obj.text; article.append(body); }
      const reference = document.createElement('p'); reference.className = 'reference'; reference.textContent = `${de ? 'Position beim Anlegen' : 'Position when created'}: ${Math.round(obj.anchor.x)}, ${Math.round(obj.anchor.y)} CSS px`; article.append(reference);
      root.append(article);
    }
    document.title = `${data.title || 'Web'} — Inklane`;
    root.removeAttribute('role');
  } catch {
    root.textContent = de ? 'Dieser Bericht ist nicht mehr verfügbar. Erstelle ihn erneut über die Webseite.' : 'This report is no longer available. Create it again from the webpage.';
    document.getElementById('print').disabled = true;
  }
})();
