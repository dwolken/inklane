(() => {
  if (globalThis.InklaneWebSession) { globalThis.InklaneWebSession.toggle(); return; }
  const api = globalThis.browser || chrome, M = InklaneWeb;
  const de = navigator.language.toLowerCase().startsWith('de');
  const strings = {
    en: { browse: 'Browse', select: 'Select', note: 'Remark', text: 'Text', ink: 'Pen', undo: 'Undo', redo: 'Redo', close: 'Close', saved: 'Saved in this browser', saving: 'Saving…', load: 'Opening local review…', failed: 'Could not save. Export your review before leaving.', conflict: 'Another tab changed this review. Export your work or reload the saved copy.', reload: 'Reload saved copy', color: 'Color', size: 'Size', title: 'Remark title', body: 'Write your explanation…', duplicate: 'Duplicate', remove: 'Delete', export: 'Export', all: 'All annotations (.json)', visible: 'Visible area (.inklane)', print: 'Print review report', import: 'Import web review', help: 'Help', detached: 'Page content changed. Move the marker onto its intended content to reconnect it.', selectHint: 'Select a mark to move or edit it.', placeHint: 'Choose a place on the page.', drawHint: 'Draw on the page. Esc finishes.', browseHint: 'The webpage works normally. Your marks remain visible.', bad: 'This review could not be opened safely. The saved copy was not changed.', imported: 'Web review imported', wrong: 'This review belongs to a different webpage.', replace: 'Replace this page’s current annotations with the imported review? Undo can restore them.', discard: 'Reload the saved copy and discard your unsaved changes?', area: 'Visible area exported. Other annotations remain in the full JSON review.', capture: 'Keep this tab active while the visible area is captured.', changed: 'The page address changed. Export the original review as JSON, then open this page’s review.', reopen: 'Open current page review', quota: 'This review is full. Export it before adding more marks.', unsaved: 'Your latest changes are not saved. Export or retry before closing.', rebind: 'Move to reconnect', noObjects: 'No annotations yet', count: 'annotations', inspect: 'Annotation properties', width: 'Width', height: 'Height' },
    de: { browse: 'Webseite', select: 'Auswählen', note: 'Anmerkung', text: 'Text', ink: 'Stift', undo: 'Rückgängig', redo: 'Wiederholen', close: 'Schließen', saved: 'In diesem Browser gespeichert', saving: 'Wird gespeichert…', load: 'Lokale Anmerkungen werden geöffnet…', failed: 'Speichern fehlgeschlagen. Exportiere deine Anmerkungen vor dem Verlassen.', conflict: 'Ein anderer Tab hat diese Anmerkungen geändert. Exportiere deine Arbeit oder lade den gespeicherten Stand.', reload: 'Gespeicherten Stand laden', color: 'Farbe', size: 'Größe', title: 'Titel der Anmerkung', body: 'Schreibe deine Erklärung…', duplicate: 'Duplizieren', remove: 'Löschen', export: 'Exportieren', all: 'Alle Anmerkungen (.json)', visible: 'Sichtbarer Bereich (.inklane)', print: 'Anmerkungsbericht drucken', import: 'Web-Anmerkungen importieren', help: 'Hilfe', detached: 'Der Seiteninhalt hat sich geändert. Verschiebe die Markierung an die richtige Stelle, um sie neu zu verankern.', selectHint: 'Wähle eine Markierung zum Verschieben oder Bearbeiten.', placeHint: 'Wähle eine Stelle auf der Seite.', drawHint: 'Zeichne auf der Seite. Esc beendet das Zeichnen.', browseHint: 'Die Webseite ist normal bedienbar. Deine Markierungen bleiben sichtbar.', bad: 'Diese Anmerkungen konnten nicht sicher geöffnet werden. Die gespeicherte Kopie bleibt erhalten.', imported: 'Web-Anmerkungen importiert', wrong: 'Diese Anmerkungen gehören zu einer anderen Webseite.', replace: 'Aktuelle Anmerkungen dieser Seite durch den Import ersetzen? Rückgängig stellt sie wieder her.', discard: 'Gespeicherten Stand laden und ungespeicherte Änderungen verwerfen?', area: 'Sichtbarer Bereich exportiert. Die vollständige JSON-Datei enthält alle Anmerkungen.', capture: 'Lass diesen Tab während der Aufnahme des sichtbaren Bereichs aktiv.', changed: 'Die Seitenadresse hat sich geändert. Exportiere die ursprünglichen Anmerkungen als JSON und öffne danach die neue Seite.', reopen: 'Anmerkungen dieser Seite öffnen', quota: 'Diese Anmerkungssammlung ist voll. Exportiere sie, bevor du weitere Markierungen hinzufügst.', unsaved: 'Die letzten Änderungen sind nicht gespeichert. Exportiere sie oder versuche es erneut vor dem Schließen.', rebind: 'Zum Verankern verschieben', noObjects: 'Noch keine Anmerkungen', count: 'Anmerkungen', inspect: 'Eigenschaften', width: 'Breite', height: 'Höhe' }
  };
  const t = key => strings[de ? 'de' : 'en'][key];
  const host = document.createElement('div');
  host.id = 'inklane-web-review';
  host.style.cssText = 'all:initial!important;font:14px/1.35 system-ui,sans-serif!important;color:#292535!important;position:fixed!important;inset:0!important;z-index:2147483647!important;pointer-events:none!important;display:block!important;';
  const shadow = host.attachShadow({ mode: 'closed' });
  document.documentElement.append(host);
  shadow.innerHTML = `<style>
  :host{--ui-surface:#fff;--ui-fg:#272534;--ui-muted:#625e6b;--ui-border:#dcd9df;--ui-accent:#5633ff;--ui-soft:#eee9ff;--ui-hover:#efecf0;--ui-error:#a4262c;--ui-warning:#894314}
  @media(prefers-color-scheme:dark){:host{--ui-surface:#25242d;--ui-fg:#f3f0fa;--ui-muted:#bab6c7;--ui-border:#70697c;--ui-accent:#9b87ff;--ui-soft:#30284f;--ui-hover:#35323f;--ui-error:#ff9b9b;--ui-warning:#ffd19a}}
  .dock,.card{color:var(--ui-fg);color-scheme:light dark}
  :host{font-family:system-ui,sans-serif;color:var(--ui-fg);font-size:14px;color-scheme:light dark}*{box-sizing:border-box}button,input,textarea,select{font:inherit}button{border:0;border-radius:7px;background:transparent;color:inherit;cursor:pointer;padding:8px 11px;min-height:36px}button:hover{background:var(--ui-hover)}button:focus-visible,input:focus-visible,textarea:focus-visible{outline:2px solid var(--ui-accent);outline-offset:2px}button[aria-pressed=true]{background:var(--ui-soft);color:var(--ui-accent)}button:disabled{opacity:.4;cursor:default}.dock{position:absolute;top:16px;right:16px;max-width:calc(100vw - 32px);background:var(--ui-surface);border:1px solid var(--ui-border);border-radius:12px;box-shadow:0 6px 28px #20143820;pointer-events:auto;padding:8px;z-index:10}.tools,.actions,.properties{display:flex;align-items:center;gap:3px;flex-wrap:wrap}.brand{font-weight:650;padding:0 10px 0 6px;letter-spacing:-.4px}.divider{width:1px;height:23px;background:var(--ui-border);margin:0 4px}.actions{border-top:1px solid var(--ui-border);padding-top:6px;margin-top:6px}.status{font-size:11px;color:var(--ui-muted);padding:3px 7px 2px;max-width:580px}.status.error{color:var(--ui-error)}.hint{font-size:12px;color:var(--ui-muted);padding:6px 7px 2px}#color{width:30px;height:30px;padding:0;border:0;background:none}#hex{width:82px}input,textarea,select{border:1px solid var(--ui-border);border-radius:6px;background:var(--ui-surface);color:var(--ui-fg);padding:6px 8px}#size{width:61px}label{font-size:12px;color:var(--ui-muted)}details{position:relative}summary{list-style:none;cursor:pointer;padding:8px 11px;border-radius:7px}summary:hover{background:var(--ui-hover)}.menu{position:absolute;right:0;top:36px;min-width:240px;padding:6px;background:var(--ui-surface);box-shadow:0 8px 28px #20143825;border:1px solid var(--ui-border);border-radius:9px}.menu button{display:block;width:100%;text-align:left}#overlay{position:absolute;inset:0;width:100%;height:100%;overflow:visible;pointer-events:none}#marks{position:absolute;inset:0;pointer-events:none}.pin{position:absolute;display:grid;place-items:center;min-width:28px;width:28px;height:28px;min-height:28px;padding:0;border-radius:50%;background:var(--ink);color:#fff;box-shadow:0 2px 6px #0003;border:2px solid white;transform:translate(-50%,-50%);touch-action:none}.pin.selected{outline:3px solid var(--ui-accent);outline-offset:2px}.pin.detached{border-style:dashed;border-color:#ffb66b}.placed{position:absolute;white-space:pre-wrap;overflow-wrap:anywhere;overflow:hidden;padding:4px;cursor:move;touch-action:none}.placed.selected{outline:1.5px solid var(--ui-accent);background:#fff9}.card{position:absolute;right:16px;top:155px;width:340px;height:300px;max-width:calc(100vw - 32px);max-height:calc(100vh - 180px);min-width:250px;min-height:230px;resize:both;overflow:auto;pointer-events:auto;background:var(--ui-surface);border:1px solid var(--ui-border);border-radius:12px;box-shadow:0 8px 30px #20143825;padding:13px;z-index:9;display:flex;flex-direction:column;gap:9px}.card[hidden]{display:none}.card input[type=text]{width:100%;font-weight:600}.card textarea{width:100%;min-height:65px;flex:1;resize:none;line-height:1.5}.card .actions{margin-top:0}.warning{font-size:12px;color:var(--ui-warning);line-height:1.5}.props{display:flex;gap:10px}.props label{display:flex;align-items:center;gap:4px}.props input{width:65px}.badge{font-size:11px;color:var(--ui-muted);margin-left:auto}.hide{display:none!important}@media(max-width:650px){.dock{top:8px;right:8px;max-width:calc(100vw - 16px);width:calc(100vw - 16px)}.brand{width:100%;padding-bottom:3px}.tools button{padding:7px 9px}.card{right:8px;top:auto;bottom:8px;max-height:55vh;width:calc(100vw - 16px)}.hint{display:none}}@media print{.dock,.card{display:none!important}}
  .pin:hover{background:var(--ink)}
  </style><svg id="overlay" xmlns="http://www.w3.org/2000/svg"></svg><div id="marks"></div>
  <section class="dock" aria-label="Inklane"><div class="tools"><span class="brand">Inklane</span>${['browse','select','ink','text','note'].map(x => `<button data-tool="${x}" aria-pressed="${x === 'select'}">${t(x)}</button>`).join('')}<span class="divider"></span><button data-action="undo" title="${t('undo')} (Ctrl+Z)" aria-label="${t('undo')}">↶</button><button data-action="redo" title="${t('redo')} (Ctrl+Y)" aria-label="${t('redo')}">↷</button><button data-action="close" aria-label="${t('close')}" title="${t('close')}">×</button></div>
  <div class="actions"><input id="color" type="color" value="#6650b8" aria-label="${t('color')}"><input id="hex" value="#6650B8" maxlength="7" aria-label="HEX"><label for="size">${t('size')}</label><input id="size" type="number" min=".5" max="96" step=".5" value="3"><details><summary>${t('export')}</summary><div class="menu">${['all','visible','print','import'].map(x => `<button data-action="${x}">${t(x)}</button>`).join('')}</div></details><button data-action="help" title="${t('help')}" aria-label="${t('help')}">?</button><span class="badge" id="count"></span></div><div class="hint" id="hint"></div><div class="status" id="status" role="status">${t('load')}</div><button data-action="reload" class="hide" id="reload">${t('reload')}</button></section>
  <section class="card" id="card" aria-label="${t('inspect')}" hidden><iframe id="editor" title="${t('body')}" referrerpolicy="no-referrer" style="border:0;width:100%;min-height:110px;flex:1"></iframe><div id="warning" class="warning"></div><div class="props"><label>${t('width')}<input id="width" type="number" min="40" max="1500"></label><label>${t('height')}<input id="height" type="number" min="40" max="1500"></label></div><div class="actions"><button data-action="duplicate">${t('duplicate')}</button><button data-action="remove">${t('remove')}</button><button data-action="finish" style="margin-left:auto" aria-label="${t('close')}">×</button></div></section><input id="import" type="file" accept=".json,application/json" hidden>`;
  const $ = id => shadow.getElementById(id);
  const overlay = $('overlay'), marks = $('marks'), card = $('card');
  let record = M.create(location.href, document.title), revision = 0, dirty = false, generation = 0, conflict = false, ready = false, visible = true, mode = 'select', selected = null, drag = null, saveTimer, savePromise, raf;
  let editorObject=null,editorRequest=0; const editorSessions=new Map();
  let color = '#6650B8', strokeWidth = 3, fontSize = 18, history = [], future = [];
  const status = (key, error = false) => { $('status').textContent = t(key); $('status').classList.toggle('error', error); };
  const snapshot = () => JSON.stringify(record);
  function checkpoint() { history.push(snapshot()); if (history.length > 50) history.shift(); future = []; updateUndo(); }
  function updateUndo() { shadow.querySelector('[data-action=undo]').disabled = !history.length; shadow.querySelector('[data-action=redo]').disabled = !future.length; }
  function change() { dirty = true; generation++; status('saving'); clearTimeout(saveTimer); saveTimer = setTimeout(() => save(), 400); render(); }
  async function save() {
    if (savePromise) { await savePromise; if (dirty && !conflict) return save(); return !dirty; }
    if (!dirty || conflict || !ready) return !dirty;
    if (!editable()) return false;
    const at = generation, data = JSON.parse(snapshot());
    savePromise = (async () => {
      try {
        M.validate(data);
        const response = await api.runtime.sendMessage({ target: 'inklane-web', command: 'save', data, revision });
        if (response?.conflict) { conflict = true; status('conflict', true); $('reload').classList.remove('hide'); return false; }
        if (!response || response.error) throw Error(response?.error || 'save');
        revision = response.revision;
        if (generation === at) { dirty = false; status('saved'); }
        return !dirty;
      } catch { status('failed', true); return false; }
    })();
    try { return await savePromise; } finally { savePromise = null; }
  }
  async function load() {
    ready = false;
    try {
      const key = await M.keyFor(location.href), found = (await api.storage.local.get(key))[key];
      record = found?.data ? M.validate(found.data) : M.create(location.href, document.title);
      editorSessions.clear(); editorObject=null;editorRequest++;
      revision = found?.revision || 0; dirty = false; conflict = false; generation = 0; selected = null; history = []; future = [];
      $('reload').classList.add('hide'); $('reload').textContent=t('reload'); ready = true; status('saved'); render(); inspect(); updateUndo();
    } catch { status('bad', true); }
  }
  function current() { return record.objects.find(x => x.id === selected); }
  function position(obj) { const p = M.locate(obj.anchor); return drag?.type === 'move' && drag.id === obj.id ? { ...p, x: drag.origin.x + drag.dx, y: drag.origin.y + drag.dy } : p; }
  function render() {
    overlay.replaceChildren(); marks.replaceChildren();
    const ns = 'http://www.w3.org/2000/svg';
    let number = 0;
    for (const obj of record.objects) {
      const p = position(obj);
      if (obj.type === 'ink') {
        const d = obj.points.map((pt, i) => `${i ? 'L' : 'M'}${p.x + pt.x} ${p.y + pt.y}`).join(' ');
        if (selected === obj.id) { const halo = document.createElementNS(ns, 'path'); halo.setAttribute('d', d); halo.setAttribute('stroke', '#c5b6e8'); halo.setAttribute('stroke-width', obj.strokeWidth + 6); halo.setAttribute('fill', 'none'); halo.setAttribute('stroke-linecap', 'round'); overlay.append(halo); }
        const path = document.createElementNS(ns, 'path');
        for (const [k, v] of Object.entries({ d, fill: 'none', stroke: obj.color, 'stroke-width': obj.strokeWidth, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' })) path.setAttribute(k, v);
        if (p.state === 'detached') path.setAttribute('stroke-dasharray', '5 3');
        overlay.append(path);
        const hit = path.cloneNode(); hit.setAttribute('stroke', 'transparent'); hit.setAttribute('stroke-width', Math.max(14, obj.strokeWidth + 8)); hit.style.pointerEvents = mode === 'select' ? 'stroke' : 'none'; hit.dataset.object = obj.id; overlay.append(hit);
      } else {
        const el = document.createElement(obj.type === 'note' ? 'button' : 'div');
        el.dataset.object = obj.id; el.className = (obj.type === 'note' ? 'pin' : 'placed') + (selected === obj.id ? ' selected' : '') + (p.state === 'detached' ? ' detached' : '');
        el.style.left = p.x + 'px'; el.style.top = p.y + 'px'; el.style.setProperty('--ink', obj.color); el.style.pointerEvents = mode === 'select' || mode === 'browse' ? 'auto' : 'none';
        el.title = obj.title || (obj.text.slice(0, 100)) || t(obj.type);
        if (obj.type === 'note') { el.textContent = String(++number); el.setAttribute('aria-label', `${t('note')} ${number}: ${obj.title || obj.text.slice(0, 100)}`); }
        else { el.textContent = obj.text || t('text'); el.style.width = obj.width + 'px'; el.style.height = obj.height + 'px'; el.style.fontSize = obj.fontSize + 'px'; el.style.color = obj.color; el.setAttribute('role', 'button'); el.tabIndex = 0; }
        marks.append(el);
      }
    }
    if (['ink', 'text', 'note'].includes(mode)) {
      const catchArea = document.createElementNS(ns, 'rect');
      catchArea.setAttribute('width', '100%'); catchArea.setAttribute('height', '100%'); catchArea.setAttribute('fill', 'transparent'); catchArea.style.pointerEvents = 'all'; overlay.append(catchArea);
    }
    $('count').textContent = record.objects.length ? `${record.objects.length} ${t('count')}` : '';
    const obj = current();
    $('warning').textContent = obj && M.locate(obj.anchor).state === 'detached' ? t('detached') : '';
  }
  function inspect(focus = false) {
    const obj = current(); card.hidden = !obj;
    if (!obj) { editorObject=null;editorRequest++;return; }
    $('editor').classList.toggle('hide',obj.type === 'ink');
    if(obj.type !== 'ink' && editorObject !== obj.id) openEditor(obj,focus);
    $('width').value = obj.width; $('height').value = obj.height;
    card.style.width = Math.max(250, obj.type === 'note' ? obj.width : 340) + 'px'; card.style.height = Math.max(230, obj.type === 'note' ? obj.height : 300) + 'px';
    color = obj.color; $('color').value = color; $('hex').value = color;
    $('size').value = obj.type === 'ink' ? obj.strokeWidth : obj.fontSize;
    render();

  }
  async function openEditor(obj,focus) {
    editorObject=obj.id;const request=++editorRequest;$('editor').src='about:blank';
    try {
      const result=await api.runtime.sendMessage({target:'inklane-web',command:'editor-open',url:record.url,objectId:obj.id,title:obj.title,text:obj.text,focus,de});
      if(request!==editorRequest)return;if(!result?.nonce)throw Error('editor');
      editorSessions.set(result.nonce,obj.id);$('editor').src=api.runtime.getURL('editor.html')+'#'+result.nonce;
    }catch{status('failed',true);}
  }
  api.runtime.onMessage.addListener((message,sender,respond)=>{
    if(sender.id!==api.runtime.id || message?.target!=='inklane-content' || message.command!=='editor-change')return;
    if(editorSessions.get(message.nonce)!==message.objectId || !editable()){respond({error:'changed'});return;}
    const obj=record.objects.find(x=>x.id===message.objectId);if(!obj){respond({error:'removed'});return;}
    const candidate=structuredClone(record),edited=candidate.objects.find(x=>x.id===obj.id);edited.title=message.title;edited.text=message.text;
    try{M.validate(candidate);}catch{status('quota',true);respond({error:'quota'});return;}
    if(message.checkpoint)checkpoint();obj.title=message.title;obj.text=message.text;change();respond({ok:true});
  });
  function tool(value) {
    if (['ink', 'text', 'note'].includes(value)) { selected = null; inspect(); }
    mode = value;
    for (const button of shadow.querySelectorAll('[data-tool]')) button.setAttribute('aria-pressed', String(button.dataset.tool === value));
    $('hint').textContent = t(value === 'browse' ? 'browseHint' : value === 'select' ? 'selectHint' : value === 'ink' ? 'drawHint' : 'placeHint');
    $('size').value = current() ? (current().type === 'ink' ? current().strokeWidth : current().fontSize) : value === 'ink' ? strokeWidth : fontSize;
    overlay.style.cursor = value === 'ink' ? 'crosshair' : ['text','note'].includes(value) ? 'copy' : 'default';
    render();
  }
  function editable() {
    if (!ready) return false;
    if (M.cleanUrl(location.href) !== record.url) { status('changed', true); $('reload').classList.remove('hide');$('reload').textContent=t('reopen');return false; }
    return true;
  }
  function down(event) {
    if (!visible || !editable() || event.button !== 0) return;
    const id = event.target.dataset.object;
    if (id && (mode === 'select' || mode === 'browse')) {
      selected = id; inspect();
      if (mode === 'select') { const obj = current(); drag = { type: 'move', id, origin: position(obj), startX: event.clientX, startY: event.clientY, dx: 0, dy: 0 }; overlay.setPointerCapture(event.pointerId); }
      event.preventDefault(); event.stopPropagation(); return;
    }
    if (!['ink','text','note'].includes(mode)) return;
    if (record.objects.length >= M.MAX_OBJECTS) { status('quota', true); return; }
    const remainingPoints = M.MAX_POINTS - record.objects.reduce((sum, obj) => sum + (obj.points?.length || 0), 0);
    if (mode === 'ink' && remainingPoints < 2) { status('quota', true); return; }
    checkpoint();
    const obj = M.object(mode, M.anchorAt(event.clientX, event.clientY, host), color); obj.strokeWidth = strokeWidth; obj.fontSize = fontSize;
    record.objects.push(obj); selected = obj.id;
    if (mode === 'ink') {
      obj.points.push({ x: 0, y: 0, pressure: event.pressure || .5 });
      drag = { type: 'ink', id: obj.id, x: event.clientX, y: event.clientY, maxPoints: Math.min(10000, remainingPoints) };
      overlay.setPointerCapture(event.pointerId); render();
    } else { change(); tool('select'); inspect(true); }
    event.preventDefault(); event.stopPropagation();
  }
  function move(event) {
    if (!drag) return;
    if (drag.type === 'ink') {
      const obj = current(); if (!obj) return;
      if (obj.points.length >= drag.maxPoints) return;
      const last = obj.points.at(-1), x = event.clientX - drag.x, y = event.clientY - drag.y;
      if (Math.hypot(last.x - x, last.y - y) < 1) return;
      obj.points.push({ x, y, pressure: event.pressure || .5 });
    } else { drag.dx = event.clientX - drag.startX; drag.dy = event.clientY - drag.startY; }
    render(); event.preventDefault(); event.stopPropagation();
  }
  function up(event) {
    if (!drag) return;
    const obj = record.objects.find(x => x.id === drag.id);
    if (drag.type === 'move' && obj && Math.hypot(drag.dx, drag.dy) > 2) {
      checkpoint(); obj.anchor = M.anchorAt(drag.origin.x + drag.dx, drag.origin.y + drag.dy, host); drag = null; change(); inspect();
    } else if (drag.type === 'ink') { if (obj.points.length === 1) obj.points.push({ ...obj.points[0], x: .1 }); drag = null; change(); }
    else { drag = null; render(); }
    if (overlay.hasPointerCapture(event.pointerId)) overlay.releasePointerCapture(event.pointerId);
  }
  overlay.addEventListener('pointerdown', down); marks.addEventListener('pointerdown', down);
  overlay.addEventListener('pointermove', move); overlay.addEventListener('pointerup', up); overlay.addEventListener('pointercancel', up);
  marks.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.dataset.object) { selected = e.target.dataset.object; inspect(true); e.preventDefault(); } });
  function remove() { if (!editable() || !current()) return; checkpoint(); record.objects = record.objects.filter(x => x.id !== selected); selected = null; change(); inspect(); }
  function duplicate() {
    if(!editable())return;const obj=current();if(!obj)return;
    const candidate=structuredClone(record),copy=structuredClone(obj),p=position(obj);copy.id=crypto.randomUUID();copy.anchor=M.anchorAt(p.x+20,p.y+20,host);candidate.objects.push(copy);
    try{M.validate(candidate);}catch{status('quota',true);return;}
    checkpoint();record=candidate;selected=copy.id;change();inspect();
  }
  function undo(redo = false) { if(!editable())return;const from = redo ? future : history, to = redo ? history : future; if (!from.length) return; to.push(snapshot()); record = JSON.parse(from.pop()); selected = null; change(); inspect(); updateUndo(); }
  function download(blob, extension) { const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href = url; link.download = (record.title || 'Inklane review').replace(/[\\/:*?"<>|\u0000-\u001f]/g, '-').slice(0, 100) + extension; link.click(); setTimeout(() => URL.revokeObjectURL(url), 60000); }
  async function visibleExport() {
    if (!editable()) return;
    const captureUrl=record.url, captureGeneration=generation;
    const vw = innerWidth, vh = innerHeight, sx = scrollX, sy = scrollY, positions = Object.fromEntries(record.objects.map(x => [x.id, position(x)]));
    const capturedRecord = structuredClone(record);
    try {
      host.style.setProperty('display', 'none', 'important');
      await new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)));
      const response = await api.runtime.sendMessage({ target: 'inklane-web', command: 'capture',url:captureUrl });
      if (!response?.dataUrl || response.error || vw !== innerWidth || vh !== innerHeight || sx !== scrollX || sy !== scrollY || M.cleanUrl(location.href)!==captureUrl || generation!==captureGeneration || record.objects.some(x=>{const now=position(x),before=positions[x.id];return !before || Math.abs(now.x-before.x)>.5 || Math.abs(now.y-before.y)>.5 || now.state!==before.state;})) throw Error('capture');
      const bytes = Uint8Array.from(atob(response.dataUrl.split(',')[1]), c => c.charCodeAt(0)), header = new DataView(bytes.buffer);
      const width = header.getUint32(16), height = header.getUint32(20);
      if (width * height > 32000000 || bytes.length > 64000000) throw Error('size');
      const result = M.portable(capturedRecord, bytes, width, height, vw, vh, positions);
      download(await result.create(), '.inklane'); status('area');
    } catch { status('capture', true); }
    finally { host.style.setProperty('display', visible ? 'block' : 'none', 'important'); }
  }
  shadow.addEventListener('click', async e => {
    const button = e.target.closest('button'); if (!button) return;
    if (button.dataset.tool) { if (editable()) tool(button.dataset.tool); return; }
    try {
      if(!['all','help','reload','close','finish'].includes(button.dataset.action) && !editable()){shadow.querySelector('details').open=false;return;}
      switch (button.dataset.action) {
        case 'undo': undo(); break; case 'redo': undo(true); break; case 'remove': remove(); break; case 'duplicate': duplicate(); break;
        case 'finish': selected = null; inspect(); render(); break;
        case 'close': if (await save()) { visible = false; host.style.setProperty('display', 'none', 'important'); } else status('unsaved', true); break;
        case 'all': download(new Blob([JSON.stringify(record, null, 2)], { type: 'application/json' }), '.web-review.json'); break;
        case 'visible': await visibleExport(); break;
        case 'print': { const result = await api.runtime.sendMessage({ target: 'inklane-web', command: 'report', data: structuredClone(record) }); if (result?.error) throw Error(result.error); break; }
        case 'import': $('import').click(); break;
        case 'help': window.open(api.runtime.getURL('help.html'), '_blank', 'noopener'); break;
        case 'reload': if (!dirty || confirm(t('discard'))) await load(); break;
      }
    } catch { status('failed', true); }
    shadow.querySelector('details').open = false;
    e.stopPropagation();
  });
  function recolor(value) { if (!editable())return;if (!/^#[\da-f]{6}$/i.test(value)) return; color = value.toUpperCase(); $('color').value = color; $('hex').value = color; const obj = current(); if (obj && obj.color !== color) { checkpoint(); obj.color = color; change(); } }
  $('color').addEventListener('change', () => recolor($('color').value)); $('hex').addEventListener('change', () => { recolor($('hex').value); $('hex').value = color; });
  $('size').addEventListener('change', () => { if(!editable())return;const obj = current(), isInk = obj ? obj.type === 'ink' : mode === 'ink', value = Math.max(isInk ? .5 : 8, Math.min(isInk ? 24 : 96, +$('size').value || 3)); if (isInk) strokeWidth = value; else fontSize = value; $('size').value = value; if (obj) { checkpoint(); obj[isInk ? 'strokeWidth' : 'fontSize'] = value; change(); } });
  for (const id of ['width','height']) $(id).addEventListener('change', () => { if(!editable())return;const obj = current(); if (!obj) return; checkpoint(); obj[id] = Math.max(40, Math.min(1500, +$(id).value || 100)); change(); inspect(); });
  let resizing = false;
  card.addEventListener('pointerdown', e => { const r = card.getBoundingClientRect(); if (e.clientX > r.right - 20 && e.clientY > r.bottom - 20 && current()?.type === 'note') { checkpoint(); resizing = true; } });
  document.addEventListener('pointerup', () => { if (!resizing) return; resizing = false;if(!editable())return; const obj = current(); if (obj?.type === 'note') { const r = card.getBoundingClientRect(); obj.width = Math.round(r.width); obj.height = Math.round(r.height); change(); } }, true);
  $('import').addEventListener('change', async () => {
    const file = $('import').files?.[0]; $('import').value = ''; if (!file || !editable()) return;
    try { if (file.size > M.MAX_BYTES) throw Error('size'); const data = M.validate(JSON.parse(await file.text())); if (data.url !== M.cleanUrl(location.href)) { status('wrong', true); return; } if (record.objects.length && !confirm(t('replace'))) return; checkpoint(); record = data; selected = null; change(); inspect(); }
    catch { status('bad', true); }
  });
  setInterval(()=>{if(ready)editable();},1000);
  shadow.addEventListener('keydown', e => e.stopPropagation());
  document.addEventListener('keydown', e => {
    if (!visible || !ready) return;
    if (['INPUT','TEXTAREA','SELECT'].includes(shadow.activeElement?.tagName) || ['INPUT','TEXTAREA','SELECT'].includes(e.target.tagName) || e.target.isContentEditable) return;
    if (e.key === 'Escape') { selected = null; tool('select'); inspect(); }
    else if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'z') { undo(e.shiftKey); e.preventDefault(); }
    else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { undo(true); e.preventDefault(); }
    else if (selected && (e.key === 'Delete' || e.key === 'Backspace')) { remove(); e.preventDefault(); }
    else if (selected && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') { duplicate(); e.preventDefault(); }
  }, true);
  window.addEventListener('beforeunload', e => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
  function schedule() { if (!visible || raf) return; raf = requestAnimationFrame(() => { raf = 0; render(); }); }
  window.addEventListener('scroll', schedule, { passive: true, capture: true }); window.addEventListener('resize', schedule, { passive: true });
  const observer = new MutationObserver(schedule); observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  const resize = new ResizeObserver(schedule); resize.observe(document.documentElement);
  globalThis.InklaneWebSession = { toggle: async () => {
    if (M.cleanUrl(location.href) !== record.url) { if (!await save()) { status('unsaved', true); return; } await load(); }
    visible = !visible; if (visible) { host.style.setProperty('display', 'block', 'important'); render(); } else if (await save()) host.style.setProperty('display', 'none', 'important'); else { visible = true; status('unsaved', true); }
  } };
  tool('select'); load();
})();
