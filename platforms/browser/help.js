const api = globalThis.browser || chrome, de = navigator.language.toLowerCase().startsWith('de');
document.documentElement.lang = de ? 'de' : 'en';
const text = de ? {
  title: 'Anmerkungen direkt auf Webseiten', restricted: 'Diese Seite erlaubt keine Erweiterung. Öffne eine gewöhnliche HTTP- oder HTTPS-Webseite. Browser-Einstellungen, Erweiterungs-Stores und integrierte PDF-Ansichten sind geschützt.',
  steps: ['Öffne eine Webseite und klicke auf das Inklane-Symbol in der Browser-Symbolleiste.', 'Wähle Stift, Text oder Anmerkung. Mit Auswählen kannst du vorhandene Markierungen bewegen und bearbeiten. Webseite lässt die Seite normal bedienen.', 'Deine Arbeit wird nur in diesem Browserprofil gespeichert. Klicke nach einem erneuten Öffnen der Seite auf Inklane, um sie wieder einzublenden.', 'Exportiere alle Anmerkungen als JSON für eine Sicherung mit Web-Verankerungen. Sichtbarer Bereich speichert eine Momentaufnahme mit editierbaren Markierungen als .inklane. Der Druckbericht enthält alle Anmerkungen in voller Länge.'],
  privacy: 'Ohne Konto und ohne Upload. Inklane erhält Zugriff nur auf den Tab, auf dem du es aktivierst. Es speichert Textausschnitte für die Verankerung, keine Formulareingaben. Im sichtbaren Export erscheint der sichtbare Seiteninhalt. Behalte vor dem Löschen des Browserprofils eine exportierte Kopie.',
  limits: 'Nach größeren Änderungen der Webseite können Anker gelöst sein. Verschiebe die gestrichelte Markierung an die passende Stelle. Inhalte in fremden Frames, Canvas-Flächen und geschlossenen Komponenten können nicht präzise verankert werden. JSON-Verankerungen sind webbezogen; .inklane überträgt eine feste Momentaufnahme. Hash-Routen und entfernte Tracking- oder Anmeldeparameter unterscheiden keine separaten Sammlungen.',
  saved: 'Gespeicherte Webseiten', empty: 'Noch keine gespeicherten Anmerkungen.', remove: 'Löschen', confirm: 'Diese lokal gespeicherten Anmerkungen löschen? Exportiere zuerst eine Sicherung, falls du sie behalten möchtest.', count: 'Anmerkungen'
} : {
  title: 'Put feedback directly on webpages', restricted: 'This page does not allow extensions. Open an ordinary HTTP or HTTPS webpage. Browser settings, extension stores and built-in PDF viewers are protected.',
  steps: ['Open a webpage and click the Inklane icon in the browser toolbar.', 'Choose Pen, Text or Remark. Use Select to move and edit existing marks. Browse lets the webpage work normally.', 'Your work saves only in this browser profile. After reopening a page, click Inklane to show its annotations again.', 'Export all annotations as JSON for a backup with web anchors. Visible area saves a snapshot with editable marks as .inklane. The print report contains all annotations in full.'],
  privacy: 'No account and no upload. Inklane gets access only to the tab where you activate it. It stores text excerpts for anchoring, not form values. A visible-area export includes the visible webpage content. Keep an exported copy before deleting the browser profile.',
  limits: 'Large webpage revisions can detach anchors. Move a dashed marker onto the intended content to reconnect it. Cross-origin frames, canvas surfaces and closed components cannot be precisely anchored. JSON anchors are web-specific; .inklane transfers a fixed snapshot. Hash routes and removed tracking or login parameters do not create separate review collections.',
  saved: 'Saved webpages', empty: 'No saved annotations yet.', remove: 'Delete', confirm: 'Delete these locally saved annotations? Export a backup first if you want to keep them.', count: 'annotations'
};
const root = document.getElementById('help');
root.innerHTML = `<header><h1>Inklane</h1></header>${location.search.includes('restricted') ? `<p class="notice">${text.restricted}</p>` : ''}<h2>${text.title}</h2><ol>${text.steps.map(s => `<li>${s}</li>`).join('')}</ol><p>${text.privacy}</p><p class="muted">${text.limits}</p><h2>${text.saved}</h2><div id="saved"></div>`;
async function list() {
  const saved = document.getElementById('saved'); saved.replaceChildren();
  const items = await api.storage.local.get(null);
  for (const [key, value] of Object.entries(items).filter(([key]) => key.startsWith('review:'))) {
    try {
      const data = InklaneWeb.validate(value.data), row = document.createElement('div'); row.className = 'review';
      const link = document.createElement('a'); link.href = data.url; link.textContent = data.title || data.url; link.target = '_blank'; link.rel = 'noopener';
      const count = document.createElement('small'); count.textContent = `${data.objects.length} ${text.count}`;
      const remove = document.createElement('button'); remove.textContent = text.remove;
      remove.addEventListener('click', async () => { if (confirm(text.confirm)) { await api.storage.local.remove(key); list(); } });
      row.append(link, count, remove); saved.append(row);
    } catch { /* Preserve unknown data. It is never rendered as active markup. */ }
  }
  if (!saved.children.length) { const p = document.createElement('p'); p.className = 'empty'; p.textContent = text.empty; saved.append(p); }
}
list();
