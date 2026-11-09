import {
  EditorError,
  JSON_LIMIT,
  digest,
  feedbackFile,
  moved,
  newDocument,
  newItem,
  openImage,
  readEnvelope,
  readSidecar,
  serialize,
  validateDocument,
} from "./model.js";
import {
  attachCanvas,
  backdropCanvas,
  createLayer,
  notebookDocument,
  notebookSource,
  openBackdrop,
  paperPage,
  portableFile,
  readPortable,
} from "./documents.js";
import { listDrafts, readDraft, removeDraft, writeDraft } from "./drafts.js";
import { switchLanguage, t, translate } from "./language.js";

import { setupProperties } from "./properties.js";
import { renderText, textStyle } from "./rich-text.js";
import { shapeVisual } from "./shapes.js";

const svgNS = "http://www.w3.org/2000/svg";
const $ = (selector) => document.querySelector(selector);
const sheet = $("#sheet"),
  viewport = $("#viewport"),
  marks = $("#marks"),
  objects = $("#objects"),
  card = $("#card");
const toolNames = ["select", "pen", "text", "note", "shape", "pan"];
const icons = {
  shape: "M3 4h11v11H3z M12 17a5 5 0 1 0 6-6",
  select: "M4 3l15 10-7 1-3 7z",
  pen: "M4 20l2-6L17 3l4 4L10 18z M14 6l4 4",
  text: "M4 8V4h16v4 M12 4v16 M8 20h8",
  note: "M4 4h16v11l-6 6H4z M14 21v-6h6 M8 9h8",
  pan: "M6 13V7q0-3 3-2v6-7q1-3 3 0v7-6q2-3 3 0v7-4q2-3 3 0v8q0 5-5 5h-2q-3 0-5-3l-4-5q1-3 4 0z",
};
for (const tool of toolNames) {
  const b = document.createElement("button");
  b.dataset.tool = tool;
  b.innerHTML =
    '<svg aria-hidden="true" viewBox="0 0 24 24"><path d="' +
    icons[tool] +
    '"/></svg><span data-i="' +
    tool +
    '"></span>';
  b.onclick = () => setTool(tool);
  $("#tools").append(b);
}
let properties = null,
  selectedGroupIds = new Set(),
  selectionIds = new Set(),
  localClipboard = null,
  notePreview = false;
let data = null,
  image = null,
  tool = "pen",
  selected = null,
  openNote = null,
  draft = null;
let zoom = 1,
  fit = true,
  gesture = null,
  busy = false,
  printing = false,
  revision = 0,
  savedRevision = 0,
  serial = 0,
  state = "ready";
let pendingSidecar = null,
  lastTap = null,
  handledSurfacePointer = null;
const strokeShapes = new WeakMap();
let undoStack = [],
  redoStack = [],
  historyWeight = 0,
  sourceFingerprint = null,
  savedHandle = null,
  timer;
let wholePage = false,
  pageIndex = 0,
  portableMode = false,
  embedded = new URLSearchParams(location.search).get("embed") === "1";
let recovery = null,
  recoverTimer = null,
  recoverQueue = Promise.resolve();
document.documentElement.classList.toggle("embedded", embedded);
const page = () => data.pages[pageIndex];
const current = () => find(selected);
const dirty = () => revision !== savedRevision || changedDraft();
const collections = ["ink", "items", "images", "shapes"];
function find(id) {
  if (!id || !data) return null;
  for (const group of collections) {
    const index = (page()[group] || []).findIndex((o) => o.id === id);
    if (index >= 0) return { group, index, obj: page()[group][index] };
  }
  return null;
}
function changedDraft() {
  if (!draft) return false;
  const original = find(draft.id)?.obj;
  return (
    original && (original.text !== draft.text || original.title !== draft.title)
  );
}
function message(key, error = false) {
  clearTimeout(timer);
  const box = $("#message");
  box.textContent = t(key);
  box.hidden = false;
  if (!error) timer = setTimeout(() => (box.hidden = true), 4200);
}
function errorMessage(error, fallback) {
  message(error instanceof EditorError ? error.message : fallback, true);
}
function updateStatus() {
  const active = Boolean(data),
    selection = current();
  const notebook = data?.kind === "Notebook";
  $("#whole-page").disabled = !active || busy;
  document.documentElement.classList.toggle("editing", active);
  $("#open").hidden = $("#save").hidden = !active;
  for (const item of $("#more .menu").children) {
    if (
      [
        "home",
        "new-sheet",
        "index",
        "feedback",
        "whole-page",
        "portable",
      ].includes(item.id)
    )
      item.hidden = !active;
  }
  $("#pages").hidden = !active || data.kind === "Image";
  $("#page-count").textContent = active
    ? pageIndex + 1 + " / " + data.pages.length
    : "";
  $("#prev-page").disabled = !active || busy || pageIndex < 1;
  $("#next-page").disabled =
    !active || busy || pageIndex >= data.pages.length - 1;
  $("#add-page").hidden = !notebook;
  $("#add-page").disabled = busy || (notebook && data.pages.length >= 1000);
  $("#new-notebook").disabled =
    $("#new-sheet").disabled =
    $("#home").disabled =
      busy;
  $("#rename-book").hidden = !notebook;
  $("#rename-book").disabled = busy;
  $("#print").hidden = !active || globalThis.inklaneNativePrinting === false;
  $("#print").disabled = busy;
  $("#download").hidden = !active || Boolean(notebook);
  $("#portable").disabled = busy || !active;
  $("#save .wide").textContent = t(
    notebook || portableMode ? "saveShort" : "save",
  );
  $("#save").setAttribute(
    "aria-label",
    t(notebook ? "saveNotebook" : portableMode ? "saveShort" : "save"),
  );
  $("#name").textContent = active ? image.name : t("workspace");
  $("#state").textContent = t(
    !active
      ? "newHint"
      : busy
        ? printing
          ? "preparingPrint"
          : "saving"
        : dirty()
          ? "changed"
          : state,
  );
  $("#feedback").disabled =
    $("#index").disabled =
    $("#save").disabled =
    $("#download").disabled =
      !active || busy;
  for (const element of [viewport, card, $("#tools"), $("footer")])
    element.inert = busy;
  $("#open").disabled = $("#start").disabled = busy;
  $("#welcome").hidden = active;
  $("#tools").hidden = viewport.hidden = $("footer").hidden = !active;
  $("#undo").disabled = !undoStack.length && !changedDraft();
  $("#redo").disabled = !redoStack.length;
  $("#edit").hidden = !selection || selection.group === "ink";
  $("#edit").textContent = t(
    selection?.group === "images" ? "sizeAction" : "edit",
  );
  $("#duplicate").hidden = $("#remove").hidden = !selection;
  $("#hint").textContent = t(
    selection && tool === "select" ? "selectHintKeyboard" : tool + "Hint",
  );
  $("#fit").textContent = Math.round(zoom * 100) + "%";
  $("#fit").setAttribute("aria-pressed", String(fit));
  $("#tools")
    .querySelectorAll("button")
    .forEach((b) =>
      b.setAttribute("aria-pressed", String(b.dataset.tool === tool)),
    );
  sheet.dataset.tool = tool;
  properties?.update();
  $("#pen-properties").hidden = !active || tool !== "pen" || Boolean(selection);
}
function setTool(next) {
  if (busy) return;
  flush();
  cancelGesture();
  tool = next;
  selected = null;
  selectionIds.clear();
  openNote = null;
  render();
  viewport.focus({ preventScroll: true });
}
function weight(obj) {
  return obj
    ? 256 +
        (obj.points?.length ?? 0) * 40 +
        (obj.text?.length ?? 0) * 2 +
        (obj.pngData?.length ?? 0) * 2
    : 0;
}
function change(group, index, before, after) {
  if (!before && !after) return;
  const items = page()[group];
  if (
    !before &&
    items.length >= (group === "ink" ? 10000 : group === "images" ? 100 : 2000)
  ) {
    message("size", true);
    return false;
  }
  const op = {
    group,
    pageIndex,
    index,
    before,
    after,
    weight: weight(before) + weight(after),
  };
  items.splice(index, before ? 1 : 0, ...(after ? [after] : []));
  record(op);
  return true;
}
function record(op) {
  op.previous = revision;
  op.next = ++serial;
  revision = op.next;
  undoStack.push(op);
  redoStack = [];
  historyWeight += op.weight;
  while (
    undoStack.length > 1 &&
    (undoStack.length > 64 || historyWeight > 16 * 1024 * 1024)
  )
    historyWeight -= undoStack.shift().weight;
  queueRecovery();
}
async function undoOrRedo(redo) {
  flush();
  cancelGesture();
  const source = redo ? redoStack : undoStack,
    target = redo ? undoStack : redoStack;
  const op = source.at(-1);
  if (!op || busy) return;
  if (op.group === "pages") {
    const pages = redo ? op.after : op.before,
      index = redo ? op.nextPage : op.previousPage;
    if (!(await showNotebookPages(pages, index))) return;
  } else if (op.group === "title") {
    data.title = image.name = redo ? op.after : op.before;
  } else if (op.pageIndex !== pageIndex && !(await switchPage(op.pageIndex)))
    return;
  source.pop();
  const from = redo ? op.before : op.after,
    to = redo ? op.after : op.before;
  if (op.group === "batch") data.pages[pageIndex] = structuredClone(to);
  if (collections.includes(op.group))
    page()[op.group].splice(op.index, from ? 1 : 0, ...(to ? [to] : []));
  revision = redo ? op.next : op.previous;
  target.push(op);
  queueRecovery();
  historyWeight += redo ? op.weight : -op.weight;
  selected = to?.id ?? null;
  selectionIds.clear();
  openNote = null;
  render();
  viewport.focus({ preventScroll: true });
}
function flush() {
  if (!draft) return;
  const field = objects.querySelector(".placed-editor");
  const original = find(draft.id);
  if (changedDraft()) {
    const height = field
      ? Math.max(
          original.obj.height,
          Math.min(100000, field.scrollHeight / zoom),
        )
      : original.obj.height;
    change("items", original.index, original.obj, {
      ...original.obj,
      title: draft.title,
      text: draft.text,
      height,
    });
  }
  draft = null;
  if (field) field.remove();
}
function removeSelected() {
  flush();
  const sel = current();
  if (!sel) return;
  const ids = new Set(selectedObjects().map((o) => o.id));
  mutatePage((p) => {
    for (const group of collections)
      p[group] = p[group].filter((o) => !ids.has(o.id));
    for (const shape of p.shapes) {
      if (ids.has(shape.startId)) shape.startId = null;
      if (ids.has(shape.endId)) shape.endId = null;
    }
  });
  selectionIds.clear();
  selected = openNote = null;
  render();
  viewport.focus({ preventScroll: true });
}
function duplicateSelected() {
  if (selectedObjects().length > 1) {
    insertObjects(selectedRecords());
    return;
  }
  flush();
  const sel = current();
  if (!sel) return;
  const copy = {
    ...moved(sel.obj, 14 / zoom, 14 / zoom, page()),
    id: crypto.randomUUID(),
  };
  try {
    const combined = { ...page(), [sel.group]: [...page()[sel.group], copy] };
    serialize({
      ...data,
      pages: data.pages.map((p, i) => (i === pageIndex ? combined : p)),
    });
  } catch (error) {
    errorMessage(error, "size");
    return;
  }
  if (!change(sel.group, page()[sel.group].length, null, copy)) return;
  selected = copy.id;
  openNote = copy.isNote ? copy.id : null;
  render();
  viewport.focus({ preventScroll: true });
}
function setNoteOpen(id) {
  flush();
  if (openNote && find(openNote)) find(openNote).obj.isExpanded = false;
  openNote = id;
  selected = id ?? selected;
  if (id) find(id).obj.isExpanded = true;
}
function closeCard() {
  setNoteOpen(null);
  render();
  viewport.focus({ preventScroll: true });
}
function editSelected() {
  const sel = current();
  if (sel?.group === "shapes") {
    properties.open();
    $("#shape-label").focus();
    return;
  }
  if (sel?.group === "images") {
    const input = $("#picture-width"),
      picture = sel.obj;
    const max = Math.max(
      picture.width,
      Math.min(
        page().width - picture.x,
        ((page().height - picture.y) * picture.width) / picture.height,
      ),
    );
    input.min = Math.max(1, Math.ceil(picture.width / picture.height));
    input.max = Math.max(Number(input.min), max);
    input.value = String(picture.width);
    const dimensions = () => {
      $("#picture-height").textContent =
        Math.round(Number(input.value)) +
        " × " +
        Math.round((Number(input.value) * picture.height) / picture.width) +
        " px";
    };
    input.oninput = dimensions;
    dimensions();
    $("#picture-size").showModal();
    input.select();
    return;
  }
  if (sel?.group !== "items") return;
  if (sel.obj.isNote) {
    setNoteOpen(sel.obj.id);
    render();
    $("#note-title").focus();
    $("#note-title").select();
  } else {
    flush();
    draft = { ...sel.obj };
    render();
    const field = $(".placed-editor");
    field.focus();
    field.select();
  }
}
function initDoc(
  next,
  source,
  fingerprint,
  loaded,
  canvas,
  index = 0,
  portable = false,
) {
  cancelGesture();
  $("#pictures").replaceChildren();
  if (image !== source) image?.dispose?.();
  clearTimeout(recoverTimer);
  recovery = { id: crypto.randomUUID(), stored: false };
  pageIndex = index;
  portableMode = portable || next.kind === "Notebook";
  for (const p of next.pages) p.shapes ||= [];
  selectionIds.clear();
  data = next;
  image = source;
  sourceFingerprint = fingerprint;
  savedHandle = null;
  revision = savedRevision = serial = 0;
  undoStack = [];
  redoStack = [];
  historyWeight = 0;
  selected = null;
  draft = null;
  openNote = page().items.find((o) => o.isNote && o.isExpanded)?.id ?? null;
  state = loaded ? "loaded" : "ready";
  tool = "pen";
  fit = true;
  wholePage = false;
  if (canvas) attachCanvas(canvas);
  $("#message").hidden = true;
  updateStatus();
  resize();
  viewport.scrollTo(0, 0);
  render();
  if (!embedded) viewport.focus({ preventScroll: true });
}
async function openFiles(files) {
  if (!files.length || busy) return;
  flush();
  cancelGesture();
  let candidate;
  try {
    busy = true;
    updateStatus();
    let next,
      source,
      fingerprint = null,
      loaded = false;
    const portable = files.length === 1 && /\.inklane$/i.test(files[0].name);
    if (portable) {
      ({ doc: next, source, fingerprint } = await readPortable(files[0]));
      candidate = source;
      loaded = true;
    } else {
      const originals = files.filter((f) => /\.(png|jpe?g|pdf)$/i.test(f.name));
      const json = files.filter((f) => /\.json$/i.test(f.name));
      if (
        originals.length + json.length !== files.length ||
        originals.length > 1 ||
        json.length > 1
      )
        throw new EditorError("files");
      const envelope = json.length ? await readEnvelope(json[0]) : null;
      const sourceName =
        envelope?.doc.sourcePdfName ?? envelope?.doc.sourceImagePath;
      if (
        !originals.length &&
        (!image?.file ||
          (envelope.doc.kind === "Pdf") !== (data.kind === "Pdf"))
      ) {
        requestSource(json[0], sourceName);
        return;
      }
      source = originals.length ? await openBackdrop(originals[0]) : image;
      if (originals.length) candidate = source;
      if (envelope) {
        try {
          if (
            (envelope.doc.kind === "Pdf") !==
            (source.mediaType === "application/pdf")
          )
            throw new EditorError("identity");
          ({ doc: next, fingerprint } = await readSidecar(
            json[0],
            source,
            envelope,
          ));
          loaded = true;
        } catch (error) {
          if (error.message === "identity") {
            if (!originals.length) {
              requestSource(json[0], sourceName);
              return;
            }
            if (envelope.doc.kind === "Pdf")
              throw new EditorError("identityPdf");
          }
          throw error;
        }
      } else next = createLayer(source);
    }
    const canvas = await backdropCanvas(next, source, 0);
    if (dirty() && !confirm(t("discard"))) return;
    await discardRecovery();
    initDoc(next, source, fingerprint, loaded, canvas, 0, portable);
    candidate = null;
  } catch (error) {
    errorMessage(error, "fileError");
  } finally {
    if (candidate !== image) candidate?.dispose?.();
    busy = false;
    render();
  }
}
async function switchPage(index) {
  if (!data || busy || index < 0 || index >= data.pages.length) return false;
  if (index === pageIndex) return true;
  flush();
  cancelGesture();
  busy = true;
  updateStatus();
  try {
    const canvas = await backdropCanvas(data, image, index);
    pageIndex = index;
    openNote = selected = draft = null;
    attachCanvas(canvas);
    resize();
    viewport.scrollTo(0, 0);
    return true;
  } catch (error) {
    errorMessage(error, "fileError");
    return false;
  } finally {
    busy = false;
    render();
  }
}

function requestSource(sidecar, sourcePath) {
  $("#message").hidden = true;
  pendingSidecar = sidecar;
  $("#source-name").textContent =
    typeof sourcePath === "string" ? sourcePath.split(/[\\/]/).at(-1) : "";
  $("#relink").showModal();
}
async function requestDownload(
  blob,
  name = image.name + ".inklane.json",
  notification = "downloaded",
) {
  if (typeof globalThis.inklaneSaveCopy === "function") {
    await globalThis.inklaneSaveCopy(blob, name);
    message("nativeSaved");
    return true;
  }
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = name;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(link.href), 60000);
  message(notification);
}
async function save(downloadOnly = false) {
  if (!data || busy || gesture) return;
  flush();
  render();
  try {
    busy = true;
    updateStatus();
    const bundle = portableMode && (!downloadOnly || data.kind === "Notebook");
    const suggestedName = bundle
      ? image.name.replace(/[<>:"/\\|?*]/g, "-") + ".inklane"
      : image.name + ".inklane.json";
    const blob = bundle ? await portableFile(data, image) : serialize(data);
    if (
      downloadOnly ||
      globalThis.inklaneSaveCopy ||
      !window.showSaveFilePicker
    ) {
      const confirmed = await requestDownload(
        blob,
        suggestedName,
        bundle ? "portableDone" : "downloaded",
      );
      if (confirmed && !downloadOnly) {
        savedRevision = revision;
        state = "saved";
        await discardRecovery();
      }
      return;
    }
    const handle =
      savedHandle ??
      (await window.showSaveFilePicker({
        suggestedName,
        id: "inklane-annotations",
        types: bundle
          ? [
              {
                description: "Inklane",
                accept: { "application/octet-stream": [".inklane"] },
              },
            ]
          : [
              {
                description: "Inklane JSON",
                accept: { "application/json": [".json"] },
              },
            ],
      }));
    if (
      !handle.name.toLowerCase().endsWith(bundle ? ".inklane" : ".inklane.json")
    )
      throw new EditorError(bundle ? "portableExtension" : "extension");
    const write = await handle.createWritable({ mode: "exclusive" });
    try {
      const existing = await handle.getFile();
      if (
        (savedHandle || existing.size) &&
        (existing.size > (bundle ? 100 * 1024 * 1024 : JSON_LIMIT) ||
          !sourceFingerprint ||
          (await digest(await existing.arrayBuffer())) !== sourceFingerprint)
      ) {
        savedHandle = null;
        throw new EditorError("conflict");
      }
      await write.write(blob);
      await write.close();
    } catch (error) {
      try {
        await write.abort();
      } catch {}
      throw error;
    }
    sourceFingerprint = await digest(await blob.arrayBuffer());
    savedHandle = handle;
    savedRevision = revision;
    state = "saved";
    await discardRecovery();
    message("saved");
  } catch (error) {
    if (error.name !== "AbortError") errorMessage(error, "saveError");
  } finally {
    busy = false;
    updateStatus();
  }
}
function resize() {
  if (!data) return;
  if (fit) {
    const padding = parseFloat(getComputedStyle($("#mat")).paddingLeft) * 2;
    zoom = Math.min(
      (viewport.clientWidth - padding) / page().width,
      data.kind === "Image" || wholePage
        ? (viewport.clientHeight - padding) / page().height
        : 1.45,
      2,
    );
    zoom = Math.max(zoom, 0.001);
  }
  sheet.style.width = page().width * zoom + "px";
  sheet.style.height = page().height * zoom + "px";

  render();
}
function zoomBy(factor, clientX, clientY) {
  if (!data || busy) return;
  const view = viewport.getBoundingClientRect(),
    rect = sheet.getBoundingClientRect();
  const cx = clientX ?? view.left + view.width / 2,
    cy = clientY ?? view.top + view.height / 2;
  const x = (cx - rect.left) / zoom,
    y = (cy - rect.top) / zoom;
  zoom = Math.min(8, Math.max(0.01, zoom * factor));
  fit = false;
  resize();
  const next = sheet.getBoundingClientRect();
  viewport.scrollLeft += next.left + x * zoom - cx;
  viewport.scrollTop += next.top + y * zoom - cy;
}
function stripPath(points) {
  return points.length
    ? "M" +
        points.map((p) => p.x + " " + p.y).join(" L") +
        (points.length === 1 ? "l.001 0" : "")
    : "";
}
function pressureOutline(points, width) {
  // Join opposing edges of the sampled pen nib, with a semicircle at each end.
  const left = [],
    right = [];
  for (let i = 0; i < points.length; i++) {
    const p = points[i],
      a = points[Math.max(0, i - 1)],
      b = points[Math.min(i + 1, points.length - 1)];
    const dx = b.x - a.x,
      dy = b.y - a.y,
      length = Math.hypot(dx, dy) || 1;
    const radius = width * Math.max(0.08, p.pressure);
    left.push(
      p.x - (dy * radius) / length + " " + (p.y + (dx * radius) / length),
    );
    right.push(
      p.x + (dy * radius) / length + " " + (p.y - (dx * radius) / length),
    );
  }
  const end = width * Math.max(0.08, points.at(-1).pressure),
    start = width * Math.max(0.08, points[0].pressure);
  return (
    "M" +
    left.join(" L") +
    " A" +
    end +
    " " +
    end +
    " 0 0 0 " +
    right.at(-1) +
    " L" +
    right.reverse().join(" L") +
    " A" +
    start +
    " " +
    start +
    " 0 0 0 " +
    left[0] +
    " Z"
  );
}
function strokeVisual(stroke) {
  let group = strokeShapes.get(stroke);
  if (!group) {
    group = document.createElementNS(svgNS, "g");
    group.dataset.id = stroke.id;
    const line = document.createElementNS(svgNS, "path");
    const pts = stroke.points,
      centre = stripPath(pts);
    const variable =
      pts.length > 1 && pts.some((p) => p.pressure !== pts[0].pressure);
    line.setAttribute(
      "d",
      variable ? pressureOutline(pts, stroke.width) : centre,
    );
    line.setAttribute("fill", variable ? stroke.color : "none");
    line.setAttribute("stroke", variable ? "none" : stroke.color);
    line.setAttribute(
      "stroke-width",
      stroke.width * Math.max(0.16, (pts[0]?.pressure ?? 0.5) * 2),
    );
    line.setAttribute("opacity", stroke.opacity ?? 1);
    line.setAttribute("stroke-linecap", "round");
    line.setAttribute("stroke-linejoin", "round");
    const hit = document.createElementNS(svgNS, "path");
    hit.setAttribute("d", centre);
    hit.setAttribute("stroke", "transparent");
    hit.setAttribute("stroke-width", "12");
    hit.setAttribute("vector-effect", "non-scaling-stroke");
    hit.setAttribute("pointer-events", "stroke");
    group.append(line, hit);
    strokeShapes.set(stroke, group);
  }
  group.classList.toggle("ink-selected", isSelected(stroke.id));
  return group;
}
function place(el, item, size = true) {
  el.style.left = item.x * zoom + "px";
  el.style.top = item.y * zoom + "px";
  if (size) {
    el.style.width = item.width * zoom + "px";
    el.style.height = item.height * zoom + "px";
  }
  el.dataset.id = item.id;
  el.style.zIndex = 1000001 + (item.layer || 0);
  el.style.opacity = item.opacity ?? 1;
  if (item.rotation && !item.isNote)
    el.style.transform = `rotate(${item.rotation}deg)`;
}
function render() {
  selectedGroupIds = new Set(selectedObjects().map((o) => o.id));
  updateStatus();
  if (!data) return;
  const previousEditor = objects.querySelector(".placed-editor");
  const editing = previousEditor && document.activeElement === previousEditor;
  const caret = editing
    ? [previousEditor.selectionStart, previousEditor.selectionEnd]
    : null;
  const pictures = $("#pictures");
  const existing = new Map(
    [...pictures.children].map((el) => [el.dataset.id, el]),
  );
  for (const picture of page().images) {
    const img = existing.get(picture.id) ?? document.createElement("img");
    if (!img.src) {
      img.className = "picture";
      img.draggable = false;
      img.src = "data:image/png;base64," + picture.pngData;
    }
    img.alt = t("picture");
    place(img, picture);
    img.classList.toggle("selected", isSelected(picture.id));
    pictures.append(img);
    existing.delete(picture.id);
  }
  for (const old of existing.values()) old.remove();
  marks.replaceChildren(
    ...page().ink.map((stroke) => svgObject(strokeVisual(stroke), stroke)),
  );
  for (const shape of page().shapes || []) {
    const visual = shapeVisual(shape, page());
    visual.dataset.shape = "true";
    visual.classList.toggle("object-selected", isSelected(shape.id));
    marks.append(svgObject(visual, shape));
  }
  objects.replaceChildren();
  let n = 0;
  for (const item of page().items) {
    const editingText = !item.isNote && draft?.id === item.id;
    const el = document.createElement(
      editingText ? "textarea" : item.isNote ? "button" : "div",
    );
    if (!editingText && !item.isNote) {
      el.tabIndex = 0;
      el.setAttribute("role", "button");
    }
    if (!editingText && !item.isNote)
      el.onkeydown = (e) => {
        if (e.key === "Enter") {
          e.preventDefault();
          selected = item.id;
          editSelected();
        }
      };
    el.className = item.isNote
      ? "marker"
      : editingText
        ? "placed-editor"
        : "placed";
    el.classList.toggle("selected", isSelected(item.id));
    place(el, item, !item.isNote);
    if (item.isNote) {
      n++;
      el.textContent = n;
      el.setAttribute("aria-label", item.title || t("nativeNote") + " " + n);
      el.setAttribute("aria-expanded", String(item.id === openNote));
      el.title = item.title || t("nativeNote") + " " + n;
      el.onclick = (e) => {
        if (busy) return;
        // The sheet already handled this physical tap or drag on pointerup.
        // A WebView can synthesize a second, positioned click with detail 0.
        const previous = handledSurfacePointer;
        if (
          previous &&
          (previous.pan || previous.id === item.id) &&
          performance.now() - previous.time < 750 &&
          (e.detail > 0 ||
            e.pointerType ||
            Math.hypot(e.clientX - previous.x, e.clientY - previous.y) < 3)
        )
          return;
        setNoteOpen(openNote === item.id ? null : item.id);
        render();
      };
    } else {
      textStyle(el, item, zoom);
      if (editingText) {
        el.setAttribute("aria-label", t("placed"));
        el.maxLength = 1000000;
        el.value = draft.text;
        el.oninput = () => {
          draft.text = el.value;
          queueRecovery();
          updateStatus();
        };
      } else {
        renderText(el, item);
        el.setAttribute("aria-label", item.text || t("placed"));
        el.onclick = (e) => {
          if (!e.detail && !busy) {
            selected = item.id;
            editSelected();
          }
        };
      }
    }
    objects.append(el);
  }
  if (editing && $(".placed-editor")) {
    const field = $(".placed-editor");
    field.focus({ preventScroll: true });
    field.setSelectionRange(...caret);
  }
  renderHandles();
  renderCard();
}
function renderCard() {
  const note = find(openNote);
  if (!note) {
    card.hidden = true;
    return;
  }
  const item = note.obj;
  const n =
    page()
      .items.filter((o) => o.isNote)
      .findIndex((o) => o.id === item.id) + 1;
  $("#badge").textContent = n;
  $("#note-title").placeholder = t("nativeNote") + " " + n;
  if (draft?.id !== item.id) {
    $("#note-title").value = item.title;
    $("#note-body").value = item.text;
  }
  card.hidden = false;
  card.style.width =
    Math.min(Math.max(210, item.width), viewport.clientWidth - 12) + "px";
  card.style.height =
    Math.min(Math.max(150, item.height), viewport.clientHeight - 12) + "px";
  $("#note-body").hidden = notePreview;
  $("#note-preview").hidden = !notePreview;
  $("#note-read").setAttribute("aria-pressed", String(notePreview));
  $("#note-edit").setAttribute("aria-pressed", String(!notePreview));
  textStyle($("#note-body"), item);
  textStyle($("#note-preview"), item);
  renderText($("#note-preview"), item);
  positionCard();
}
function positionCard() {
  if (card.hidden || !openNote) return;
  const note = find(openNote)?.obj;
  if (!note) return;
  const outer = $("#work").getBoundingClientRect(),
    view = viewport.getBoundingClientRect(),
    rect = sheet.getBoundingClientRect();
  const x = rect.left + note.x * zoom,
    y = rect.top + note.y * zoom;
  const inView =
    x + 27 >= view.left &&
    x <= view.right &&
    y + 27 >= view.top &&
    y <= view.bottom;
  card.style.visibility = inView ? "visible" : "hidden";
  delete card.dataset.desk;
  if (!inView) return;
  let left = x + 32,
    desiredTop = y;
  if (left + card.offsetWidth > view.right - 5) left = x - card.offsetWidth - 5;
  if (left < view.left + 4) {
    left = x + 13 - card.offsetWidth / 2;
    desiredTop =
      y + 31 + card.offsetHeight <= view.bottom
        ? y + 31
        : y - card.offsetHeight - 5;
  }
  const centerX = x + 13;
  if (
    view.width <= 600 &&
    view.height > view.width &&
    centerX > view.left + 8 &&
    centerX < view.right - 8
  ) {
    // On a phone a landscape page often has room for the whole note just beyond an edge.
    // Prefer the nearer edge only while a short leader can still visibly join card and marker.
    const choices = [
      {
        side: "above",
        top: rect.top - 7 - card.offsetHeight,
        distance: y - rect.top,
      },
      { side: "below", top: rect.bottom + 7, distance: rect.bottom - (y + 27) },
    ].filter(
      (choice) =>
        choice.top >= view.top + 5 &&
        choice.top + card.offsetHeight <= view.bottom - 5 &&
        choice.distance <= Math.min(140, view.height / 4),
    );
    const best = choices.sort((a, b) => a.distance - b.distance)[0];
    if (best) {
      left = centerX - card.offsetWidth / 2;
      desiredTop = best.top;
      card.dataset.desk = best.side;
      card.style.setProperty(
        "--leader-length",
        Math.max(0, best.distance + 7) + "px",
      );
    }
  }
  left = Math.max(
    view.left + 4,
    Math.min(left, view.right - card.offsetWidth - 5),
  );
  if (card.dataset.desk)
    card.style.setProperty("--leader-x", centerX - left + "px");
  const top = Math.max(
    view.top + 4,
    Math.min(desiredTop, view.bottom - card.offsetHeight - 6),
  );
  card.style.left = left - outer.left + "px";
  card.style.top = top - outer.top + "px";
}
for (const field of [$("#note-title"), $("#note-body")]) {
  field.onfocus = () => {
    if (busy) return;
    const obj = find(openNote)?.obj;
    if (obj && (!draft || draft.id !== obj.id)) {
      flush();
      draft = { ...obj };
    }
  };
  field.oninput = () => {
    if (!draft || draft.id !== openNote) draft = { ...find(openNote).obj };
    if (field.id === "note-title") {
      field.value = field.value.replace(/[\u0000-\u001f\u007f-\u009f]/g, " ");
      draft.title = field.value;
    } else draft.text = field.value;
    queueRecovery();
    updateStatus();
  };
}
function point(e) {
  const r = sheet.getBoundingClientRect();
  return {
    x: Math.max(0, Math.min(page().width - 1, (e.clientX - r.left) / zoom)),
    y: Math.max(0, Math.min(page().height - 1, (e.clientY - r.top) / zoom)),
    pressure: e.pointerType === "pen" ? e.pressure : 0.5,
  };
}
function cancelGesture() {
  if (!gesture) return;
  const g = gesture;
  gesture = null;
  if (sheet.hasPointerCapture(g.pointer))
    sheet.releasePointerCapture(g.pointer);
  g.preview?.remove();
  g.previewRoot?.remove();
  if (g.el) g.el.style.translate = "";
}
function startGesture(e) {
  if (
    busy ||
    !data ||
    gesture ||
    (e.button !== 0 && e.button !== 1) ||
    e.target.closest("textarea,a")
  )
    return;
  handledSurfacePointer = null;
  const p = point(e),
    id = e.target.closest("[data-id]")?.dataset.id;
  flush();
  let mode = e.target.dataset.resize ? "resize" : tool;
  if (e.button === 1 || tool === "pan") mode = "pan";
  else if (mode !== "resize" && id && find(id)?.obj.isNote) mode = "object";
  else if (
    mode !== "resize" &&
    (tool === "select" ||
      (id && tool === "text" && find(id)?.group === "items"))
  )
    mode = "object";
  e.preventDefault();
  viewport.focus({ preventScroll: true });
  const g = {
    pointer: e.pointerId,
    mode,
    first: p,
    startX: e.clientX,
    startY: e.clientY,
    left: viewport.scrollLeft,
    top: viewport.scrollTop,
    id,
    moved: false,
    handle: e.target.dataset.resize,
    original: id ? structuredClone(find(id)?.obj) : null,
  };
  if (mode === "text" || mode === "note") {
    setNoteOpen(null);
    const item = newItem(mode === "note", p.x, p.y, page());
    item.color = properties.defaults().color;
    item.opacity = properties.defaults().opacity;
    if (!item.isNote) {
      item.fontSize = Math.min(300, Math.max(12, 18 / zoom));
      item.width = Math.min(page().width, 240 / zoom);
      item.height = Math.min(page().height, 90 / zoom);
    }
    // Leave enough room to read the first line when inserting text near a page edge.
    if (!item.isNote) {
      item.x = Math.min(
        item.x,
        Math.max(0, page().width - Math.min(90, item.width)),
      );
      item.width = Math.min(item.width, page().width - item.x);
    }
    if (!change("items", page().items.length, null, item)) return;
    tool = "select";
    selected = item.id;
    if (item.isNote) {
      setNoteOpen(item.id);
      render();
      $("#note-body").focus();
    } else editSelected();
    return;
  }
  if (mode === "resize") {
    g.el = [...sheet.querySelectorAll("[data-id]")].find(
      (el) => el.dataset.id === id,
    );
  } else if (mode === "shape") {
    g.shape = {
      id: crypto.randomUUID(),
      x: p.x,
      y: p.y,
      width: 1,
      height: 1,
      rotation: 0,
      label: "",
      startId: null,
      endId: null,
      flipX: false,
      flipY: false,
      ...properties.defaults(),
    };
    if (["Line", "Arrow"].includes(g.shape.kind))
      g.shape.startId = nodeAt(p)?.id || null;
    g.preview = svgObject(shapeVisual(g.shape, page()), g.shape);
    marks.append(g.preview);
  } else if (mode === "object") {
    // Claim the pointer on the stable sheet before rendering replaces the
    // original marker, which touch screens can implicitly capture on contact.
    if (id) sheet.setPointerCapture(e.pointerId);
    if (id !== openNote && openNote) setNoteOpen(null);
    if (id && tool === "pen") tool = "select";
    if (e.shiftKey && id) {
      if (selected) selectionIds.add(selected);
      if (selectionIds.has(id)) selectionIds.delete(id);
      else selectionIds.add(id);
    } else if (!id || !isSelected(id)) selectionIds.clear();
    selected = id ?? null;
    render();
    if (!id) return;
    g.el = [...sheet.querySelectorAll("[data-id]")].find(
      (el) => el.dataset.id === id,
    );
  } else if (mode === "pen") {
    setNoteOpen(null);
    selected = null;
    render();
    g.remaining =
      500000 -
      page().ink.reduce((sum, stroke) => sum + stroke.points.length, 0);
    if (page().ink.length >= 10000 || g.remaining < 1) {
      message("size", true);
      return;
    }
    g.stroke = {
      id: crypto.randomUUID(),
      color: $("#ink-color").value,
      opacity: properties.defaults().opacity,
      width: Math.min(1000, Math.max(0.2, Number($("#ink-size").value) / zoom)),
      points: [p],
    };
    g.preview = document.createElementNS(svgNS, "polyline");
    g.preview.setAttribute("stroke", g.stroke.color);
    g.preview.setAttribute("stroke-width", g.stroke.width);
    g.preview.setAttribute("points", p.x + "," + p.y);
    g.previewRoot = svgObject(g.preview, g.stroke);
    marks.append(g.previewRoot);
  }
  gesture = g;
  if (!sheet.hasPointerCapture(e.pointerId))
    sheet.setPointerCapture(e.pointerId);
}
function moveGesture(e) {
  const g = gesture;
  if (!g || g.pointer !== e.pointerId) return;
  const dx = e.clientX - g.startX,
    dy = e.clientY - g.startY;
  g.moved ||= Math.hypot(dx, dy) > 3;
  if (g.mode === "pan") {
    viewport.scrollLeft = g.left - dx;
    viewport.scrollTop = g.top - dy;
  }
  if (g.mode === "resize") {
    g.resized = resized(g.original, g.handle, dx / zoom, dy / zoom);
    if (g.el && !g.original.isNote) {
      if (find(g.id)?.group === "shapes") {
        const visual = shapeVisual(g.resized, page());
        g.el.replaceChildren(...visual.childNodes);
        g.el.setAttribute("transform", visual.getAttribute("transform") || "");
      } else place(g.el, g.resized);
    }
  }
  if (g.mode === "shape") {
    const p = point(e);
    g.shape.x = Math.min(p.x, g.first.x);
    g.shape.y = Math.min(p.y, g.first.y);
    g.shape.width = Math.max(1, Math.abs(p.x - g.first.x));
    g.shape.height = Math.max(1, Math.abs(p.y - g.first.y));
    g.shape.flipX = p.x < g.first.x;
    g.shape.flipY = p.y < g.first.y;
    if (["Line", "Arrow"].includes(g.shape.kind))
      g.shape.endId = nodeAt(p)?.id || null;
    g.preview.replaceChildren(shapeVisual(g.shape, page()));
  }
  if (g.mode === "object" && g.moved && g.el) {
    const scale = g.el.namespaceURI === svgNS ? zoom : 1;
    g.el.style.translate = `${dx / scale}px ${dy / scale}px`;
  }
  if (g.mode === "pen") {
    const events = e.getCoalescedEvents?.();
    const samples = events?.length ? events : [e];
    for (const event of samples) {
      const p = point(event);
      const last = g.stroke.points.at(-1);
      if (last.x === p.x && last.y === p.y) continue;
      if (g.stroke.points.length >= g.remaining) {
        cancelGesture();
        message("size", true);
        return;
      }
      g.stroke.points.push(p);
      const v = g.preview.ownerSVGElement.createSVGPoint();
      v.x = p.x;
      v.y = p.y;
      g.preview.points.appendItem(v);
    }
  }
}
function endGesture(e) {
  const g = gesture;
  if (!g || e.pointerId !== g.pointer) return;
  moveGesture(e);
  if (!gesture) return;
  cancelGesture();
  const sel = g.mode === "object" ? find(g.id) : null;
  if (g.mode === "pan" || sel?.obj.isNote) {
    handledSurfacePointer = {
      id: g.id,
      pan: g.mode === "pan",
      x: e.clientX,
      y: e.clientY,
      time: performance.now(),
    };
  }
  if (g.mode === "resize" && g.resized && g.moved) {
    const sel = find(g.id);
    if (sel) change(sel.group, sel.index, sel.obj, g.resized);
    render();
  }
  if (g.mode === "shape" && g.moved) {
    if (g.shape.startId === g.shape.endId) g.shape.endId = null;
    change("shapes", page().shapes.length, null, g.shape);
    selected = g.shape.id;
    tool = "select";
    render();
  }
  if (g.mode === "pen") {
    change("ink", page().ink.length, null, g.stroke);
    render();
  }
  if (g.mode === "object") {
    if (!sel) return;
    if (g.moved) {
      const next = moved(
        sel.obj,
        (e.clientX - g.startX) / zoom,
        (e.clientY - g.startY) / zoom,
        page(),
      );
      if (selectedObjects().length > 1)
        mutateSelection((o) =>
          moved(
            o,
            (e.clientX - g.startX) / zoom,
            (e.clientY - g.startY) / zoom,
            page(),
          ),
        );
      else change(sel.group, sel.index, sel.obj, next);
    } else if (sel.obj.isNote) setNoteOpen(openNote === g.id ? null : g.id);
    else if (
      sel.group === "items" &&
      (tool === "text" ||
        (lastTap?.id === g.id && performance.now() - lastTap.time < 450))
    ) {
      editSelected();
      return;
    }
    lastTap = g.moved ? null : { id: g.id, time: performance.now() };
    render();
  }
}
sheet.onpointerdown = startGesture;
sheet.onpointermove = moveGesture;
sheet.onpointerup = endGesture;
sheet.onpointercancel = () => {
  cancelGesture();
  render();
};
sheet.onlostpointercapture = (e) => {
  if (
    e.target === sheet &&
    e.pointerId === gesture?.pointer &&
    !sheet.hasPointerCapture(e.pointerId)
  )
    cancelGesture();
};
sheet.ondblclick = (e) => {
  if (e.target.matches(".placed") && !busy) {
    selected = e.target.dataset.id;
    editSelected();
  }
};
viewport.onscroll = positionCard;
viewport.addEventListener(
  "wheel",
  (e) => {
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      zoomBy(e.deltaY < 0 ? 1.18 : 1 / 1.18, e.clientX, e.clientY);
    }
  },
  { passive: false },
);
new ResizeObserver(() => {
  if (data && (fit || !card.hidden)) resize();
}).observe(viewport);
new ResizeObserver(positionCard).observe(card);
window.addEventListener("beforeunload", (e) => {
  if (dirty()) {
    e.preventDefault();
    e.returnValue = "";
  }
});
window.addEventListener("dragover", (e) => {
  if (e.dataTransfer.types.includes("Files")) e.preventDefault();
});
window.addEventListener("drop", (e) => {
  if (e.dataTransfer.files.length) {
    e.preventDefault();
    openFiles([...e.dataTransfer.files]);
  }
});
$("#open").onclick = $("#start").onclick = () => $("#files").click();
$("#files").onchange = (e) => {
  const files = [...e.target.files];
  e.target.value = "";
  if (pendingSidecar && $("#relink").open) {
    if (!files.length) return;
    files.push(pendingSidecar);
    $("#relink").close();
    pendingSidecar = null;
  }
  openFiles(files);
};
$("#pick-source").onclick = () => $("#files").click();
$("#relink").onclose = () => {
  pendingSidecar = null;
};
$("#save").onclick = () => save();
$("#download").onclick = () => {
  closeMenu();
  save(true);
};
$("#feedback").onclick = async () => {
  closeMenu();
  if (!data || busy) return;
  flush();
  render();
  try {
    busy = true;
    updateStatus();
    const revision =
      data.kind === "Notebook"
        ? {
            name: image.name + ".inklane",
            mediaType: "application/vnd.inklane.workspace",
            hash: await digest(
              await (await portableFile(data, image)).arrayBuffer(),
            ),
          }
        : image;
    const blob = await feedbackFile(data, revision);
    await requestDownload(
      blob,
      image.name + ".feedback.json",
      "feedbackDownload",
    );
  } catch (error) {
    if (error.name !== "AbortError") errorMessage(error, "saveError");
  } finally {
    busy = false;
    updateStatus();
  }
};
$("#size-form").onsubmit = (e) => {
  e.preventDefault();
  const sel = current();
  if (sel?.group !== "images") return;
  const width = $("#picture-width").valueAsNumber;
  if (!$("#picture-width").reportValidity() || !Number.isFinite(width)) return;
  const height = (width * sel.obj.height) / sel.obj.width;
  if (width !== sel.obj.width)
    change("images", sel.index, sel.obj, { ...sel.obj, width, height });
  $("#picture-size").close();
  render();
  viewport.focus({ preventScroll: true });
};
$("#cancel-size").onclick = () => $("#picture-size").close();
$("#edit").onclick = editSelected;
$("#duplicate").onclick = duplicateSelected;
$("#remove").onclick = removeSelected;
$("#undo").onclick = () => undoOrRedo(false);
$("#redo").onclick = () => undoOrRedo(true);
$("#plus").onclick = () => zoomBy(1.25);
$("#minus").onclick = () => zoomBy(0.8);
$("#fit").onclick = () => {
  wholePage = false;
  fit = true;
  resize();
};
$("#close-card").onclick = closeCard;
const closeMenu = () => ($("#more").open = false);
$("#shortcuts").onclick = () => {
  closeMenu();
  $("#help").showModal();
};
$("#language").onclick = () => {
  closeMenu();
  switchLanguage();
  render();
};
document.addEventListener("click", (e) => {
  if (!$("#more").contains(e.target)) closeMenu();
});
document.addEventListener("keydown", (e) => {
  handledSurfacePointer = null;
  if (
    document.querySelector("dialog[open]") ||
    busy ||
    e.isComposing ||
    (gesture && e.key !== "Escape")
  )
    return;
  const key = e.key.toLowerCase(),
    mod = e.ctrlKey || e.metaKey,
    typing = e.target.matches("input,textarea,[contenteditable=true]");
  if (key === "escape") {
    e.preventDefault();
    if ($("#more").open) {
      closeMenu();
      $("#more summary").focus();
      return;
    }
    if (gesture) {
      cancelGesture();
      render();
      return;
    }
    if (typing) {
      flush();
      render();
    } else if (openNote) closeCard();
    else {
      selected = null;
      selectionIds.clear();
      render();
    }
    viewport.focus({ preventScroll: true });
    return;
  }
  if (mod && key === "o") {
    e.preventDefault();
    $("#files").click();
    return;
  }
  if (!data) return;
  if (mod && key === "s") {
    e.preventDefault();
    save();
    return;
  }
  if (
    typing &&
    mod &&
    ["b", "i", "u", "k"].includes(key) &&
    e.target.matches("#note-body,.placed-editor")
  ) {
    e.preventDefault();
    const obj = find(draft?.id || selected)?.obj;
    if (obj?.contentFormat === "Markdown") {
      const field = e.target,
        start = field.selectionStart,
        end = field.selectionEnd,
        part = field.value.slice(start, end);
      const token =
        key === "b" ? "**" : key === "i" ? "*" : key === "u" ? "++" : "";
      const replacement =
        key === "k"
          ? `[${part || t("linkText")}](https://)`
          : token + part + token;
      field.setRangeText(replacement, start, end, "select");
      field.dispatchEvent(new Event("input", { bubbles: true }));
    } else if (obj && key !== "k")
      applyProperties({
        [key === "b" ? "bold" : key === "i" ? "italic" : "underline"]:
          !obj[key === "b" ? "bold" : key === "i" ? "italic" : "underline"],
      });
    return;
  }
  if (typing || e.altKey || $("#more").open) return;
  if (mod && (key === "z" || key === "y")) {
    e.preventDefault();
    undoOrRedo(key === "y" || e.shiftKey);
    return;
  }
  if (mod && key === "d" && selected) {
    e.preventDefault();
    duplicateSelected();
    return;
  }
  if (mod && key === "a") {
    e.preventDefault();
    selectionIds = new Set(allObjects().map((o) => o.id));
    selected = allObjects()[0]?.id;
    render();
    return;
  }
  if (mod && key === "g") {
    e.preventDefault();
    groupSelection(!e.shiftKey);
    return;
  }
  if (mod) return;
  if (key === "f2" && selected) {
    e.preventDefault();
    editSelected();
    return;
  }
  if ((key === "delete" || key === "backspace") && selected) {
    e.preventDefault();
    removeSelected();
    return;
  }
  if (key.startsWith("arrow") && selected && viewport.contains(e.target)) {
    const sel = current();
    if (!sel) return;
    e.preventDefault();
    flush();
    const delta = e.shiftKey ? 10 : 1;
    const next = moved(
      sel.obj,
      (key === "arrowleft" ? -1 : key === "arrowright" ? 1 : 0) * delta,
      (key === "arrowup" ? -1 : key === "arrowdown" ? 1 : 0) * delta,
      page(),
    );
    if (selectedObjects().length > 1)
      mutateSelection((o) =>
        moved(
          o,
          (key === "arrowleft" ? -1 : key === "arrowright" ? 1 : 0) * delta,
          (key === "arrowup" ? -1 : key === "arrowdown" ? 1 : 0) * delta,
          page(),
        ),
      );
    else change(sel.group, sel.index, sel.obj, next);
    render();
  }
});
translate();
updateStatus();
async function reveal(id, index = pageIndex) {
  if (index !== pageIndex && !(await switchPage(index))) return;
  const selection = find(id);
  if (!selection) return;
  flush();
  tool = "select";
  selected = id;
  setNoteOpen(selection.obj.isNote ? id : null);
  selected = id;
  render();
  const origin = selection.obj.points?.[0] ?? selection.obj;
  if (Number.isFinite(origin.x)) {
    const rect = sheet.getBoundingClientRect(),
      view = viewport.getBoundingClientRect();
    viewport.scrollLeft +=
      rect.left + origin.x * zoom - view.left - view.width / 2;
    viewport.scrollTop +=
      rect.top + origin.y * zoom - view.top - view.height / 2;
  }
  viewport.focus({ preventScroll: true });
  positionCard();
}
function fillIndex() {
  const list = $("#results"),
    query = $("#search").value.trim().toLocaleLowerCase();
  const rows = document.createDocumentFragment();
  for (const [pageNumber, content] of data.pages.entries()) {
    const notes = content.items.filter((o) => o.isNote);
    let index = 0;
    for (const item of [
      ...notes,
      ...content.items.filter((o) => !o.isNote),
      ...content.ink,
      ...content.images,
    ]) {
      const label = item.isNote
        ? item.title || t("nativeNote") + " " + (index + 1)
        : item.points
          ? t("stroke")
          : item.pngData
            ? t("picture")
            : t("placed");
      if (item.isNote) index++;
      if (
        query &&
        !label.toLocaleLowerCase().includes(query) &&
        !item.text?.toLocaleLowerCase().includes(query)
      )
        continue;
      const b = document.createElement("button");
      b.textContent =
        data.pages.length > 1 ? pageNumber + 1 + " · " + label : label;
      if (item.text) {
        const detail = document.createElement("small");
        detail.textContent = item.text.slice(0, 160);
        b.append(detail);
      }
      b.onclick = () => {
        $("#browse").close();
        reveal(item.id, pageNumber);
      };
      rows.append(b);
    }
  }
  if (!rows.childElementCount) {
    const p = document.createElement("p");
    p.textContent = t("empty");
    rows.append(p);
  }
  list.replaceChildren(rows);
}
$("#index").onclick = () => {
  flush();
  closeMenu();
  $("#search").value = "";
  $("#search").placeholder = t("search");
  fillIndex();
  $("#browse").showModal();
  $("#search").focus();
};
$("#search").oninput = fillIndex;

const clipFormat = "application/x-inklane-selection+json";
function inField(e) {
  return e.target.matches?.("input,textarea,[contenteditable=true]");
}
for (const event of ["copy", "cut"]) {
  document.addEventListener(event, (e) => {
    const sel = current();
    if (
      inField(e) ||
      busy ||
      !sel ||
      !e.clipboardData ||
      document.querySelector("dialog[open]")
    )
      return;
    flush();
    const obj = find(selected)?.obj;
    if (!obj) return;
    try {
      localClipboard = selectedRecords();
      e.clipboardData.setData(
        clipFormat,
        JSON.stringify({ group: sel.group, obj, objects: localClipboard }),
      );
      e.clipboardData.setData(
        "text/plain",
        obj.isNote
          ? [obj.title, obj.text].filter(Boolean).join("\n")
          : (obj.text ?? t(sel.group === "ink" ? "stroke" : "picture")),
      );
      e.preventDefault();
      if (event === "cut") removeSelected();
    } catch {
      message("clipboard", true);
    }
  });
}
function pasteOrigin() {
  const view = viewport.getBoundingClientRect(),
    rect = sheet.getBoundingClientRect();
  return {
    x: Math.max(
      0,
      Math.min(
        page().width - 1,
        (view.left + Math.min(32, view.width / 5) - rect.left) / zoom,
      ),
    ),
    y: Math.max(
      0,
      Math.min(
        page().height - 1,
        (view.top + Math.min(32, view.height / 5) - rect.top) / zoom,
      ),
    ),
  };
}
async function pasted(e) {
  const clip = e.clipboardData;
  if (
    !data ||
    busy ||
    inField(e) ||
    !clip ||
    document.querySelector("dialog[open]")
  )
    return;
  const rich = clip.getData(clipFormat),
    text = clip.getData("text/plain");
  const png = Array.from(clip.files).find((f) => f.type === "image/png");
  if (!rich && !text && !png) return;
  e.preventDefault();
  flush();
  try {
    if (rich && rich.length <= JSON_LIMIT) {
      const bundle = JSON.parse(rich);
      if (bundle.objects) {
        await insertObjects(bundle.objects);
        return;
      }
    }
    if (
      !rich &&
      text.length <= JSON_LIMIT &&
      text.startsWith('{"format":"inklane.selection"')
    ) {
      const bundle = JSON.parse(text);
      await insertObjects(bundle.objects);
      return;
    }
    busy = true;
    updateStatus();
    let group, obj;
    const pos = pasteOrigin();
    if (rich) {
      if (rich.length > JSON_LIMIT) throw new EditorError("size");
      const incoming = JSON.parse(rich);
      if (!collections.includes(incoming.group))
        throw new EditorError("invalid");
      group = incoming.group;
      // Validate data from other tabs and applications exactly as an opened annotation file.
      const target = {
        width: page().width,
        height: page().height,
        name: "selection.png",
        hash: "0".repeat(64),
      };
      const shell = newDocument(target);
      shell.pages[0].shapes = [];
      if (group === "shapes") {
        incoming.obj.startId = null;
        incoming.obj.endId = null;
      }
      shell.pages[0][group].push(incoming.obj);
      const checked = await readSidecar(
        new File([JSON.stringify(shell)], "selection.json"),
        target,
      );
      const original = checked.doc.pages[0][group][0];
      obj = {
        ...moved(original, 14 / zoom, 14 / zoom, page()),
        id: crypto.randomUUID(),
      };
    } else if (png) {
      if (png.size > 16 * 1024 * 1024) throw new EditorError("size");
      const blobText = await new Promise((done, fail) => {
        const reader = new FileReader();
        reader.onload = () => done(reader.result);
        reader.onerror = fail;
        reader.readAsDataURL(png);
      });
      const picture = await openImage(png);
      picture.bitmap.close();
      const scale = Math.min(
        1,
        (0.8 * page().width) / picture.width,
        (0.8 * page().height) / picture.height,
      );
      group = "images";
      obj = {
        id: crypto.randomUUID(),
        pngData: blobText.split(",")[1],
        x: pos.x,
        y: pos.y,
        width: Math.max(1, picture.width * scale),
        height: Math.max(1, picture.height * scale),
      };
    } else {
      if (text.length > 1000000) throw new EditorError("size");
      group = "items";
      obj = { ...newItem(false, pos.x, pos.y, page()), text };
    }
    const combined = { ...page(), [group]: [...page()[group], obj] };
    serialize({
      ...data,
      pages: data.pages.map((p, i) => (i === pageIndex ? combined : p)),
    });
    // Check aggregate PNG limits before accepting a newly pasted or duplicated picture.
    if (group === "images")
      await validateDocument({ ...data, pages: [combined] });
    if (!change(group, page()[group].length, null, obj)) return;
    tool = "select";
    selected = obj.id;
    setNoteOpen(obj.isNote ? obj.id : null);
    selected = obj.id;
  } catch (error) {
    errorMessage(error, "clipboard");
  } finally {
    busy = false;
    render();
    viewport.focus({ preventScroll: true });
  }
}
document.addEventListener("paste", pasted);

function queueRecovery() {
  if (!data || !recovery || embedded) return;
  clearTimeout(recoverTimer);
  recoverTimer = setTimeout(
    () => captureRecovery().catch(() => message("draftError", true)),
    300,
  );
}
function captureRecovery() {
  clearTimeout(recoverTimer);
  if (!data || !recovery || embedded) return Promise.resolve();
  const entry = recovery;
  if (!dirty()) {
    // A previous edit can already be durable. Queue removal behind any write
    // in flight, so Undo → Home cannot leave the undone version in Documents.
    return (recoverQueue = recoverQueue
      .catch(() => {})
      .then(async () => {
        if (entry.stored) {
          await removeDraft(entry.id);
          entry.stored = false;
        }
      }));
  }
  const doc = { ...data, pages: [...data.pages] };
  if (changedDraft()) {
    const original = find(draft.id);
    doc.pages[pageIndex] = { ...page(), items: [...page().items] };
    doc.pages[pageIndex].items[original.index] = {
      ...original.obj,
      text: draft.text,
      title: draft.title,
    };
  }
  const snapshot = structuredClone(doc),
    source = image,
    index = pageIndex;
  const task = recoverQueue
    .catch(() => {})
    .then(async () => {
      await writeDraft(entry.id, snapshot, source, !entry.stored, index);
      entry.stored = true;
    });
  recoverQueue = task;
  return task;
}
async function discardRecovery() {
  clearTimeout(recoverTimer);
  const entry = recovery;
  recovery = null;
  if (!entry) return;
  try {
    await recoverQueue;
    if (entry.stored) await removeDraft(entry.id);
  } catch {
    /* A failed recovery write never changes the edited file. */
  }
  if (data) recovery = { id: crypto.randomUUID(), stored: false };
}
async function showRecent() {
  let entries;
  try {
    entries = await listDrafts();
  } catch {
    entries = [];
  }
  if (data) return;
  const list = $("#draft-list");
  list.replaceChildren();
  for (const entry of entries) {
    const row = document.createElement("div");
    const open = document.createElement("button"),
      remove = document.createElement("button");
    const label = document.createElement("strong"),
      last = document.createElement("small");
    label.textContent = entry.name;
    last.textContent = new Intl.DateTimeFormat(document.documentElement.lang, {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(entry.time);
    open.append(label, last);
    open.onclick = () => resume(entry);
    remove.textContent = "×";
    remove.className = "icon";
    remove.title = t("forget");
    remove.setAttribute("aria-label", t("forget") + " · " + entry.name);
    remove.onclick = async () => {
      if (!confirm(t("forgetConfirm"))) return;
      try {
        await removeDraft(entry.id);
        await showRecent();
      } catch {
        message("draftError", true);
      }
    };
    row.append(open, remove);
    list.append(row);
  }
  $("#recent").hidden = !entries.length;
}
async function resume(entry) {
  if (busy || data) return;
  busy = true;
  updateStatus();
  let candidate;
  try {
    const { doc, file } = await readDraft(entry.id);
    candidate = file ? await openBackdrop(file) : notebookSource(doc.title);
    if (
      (doc.sourceImageSha256 ?? doc.sourcePdfSha256 ?? null)?.toUpperCase() !==
      (candidate.hash ?? undefined)
    )
      throw new EditorError("invalid");
    await validateDocument(
      doc,
      doc.kind === "Pdf"
        ? candidate.pages
        : doc.kind === "Image"
          ? [candidate]
          : undefined,
    );
    const index = Math.max(0, Math.min(doc.pages.length - 1, entry.index));
    const canvas = await backdropCanvas(doc, candidate, index);
    initDoc(doc, candidate, null, true, canvas, index);
    candidate = null;
    revision = ++serial;
    await captureRecovery();
    await removeDraft(entry.id);
    message("recovered");
  } catch (error) {
    errorMessage(error, "draftError");
  } finally {
    candidate?.dispose?.();
    busy = false;
    render();
  }
}
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "hidden")
    captureRecovery().catch(() => message("draftError", true));
});
$("#home").onclick = async () => {
  closeMenu();
  if (!data || busy) return;
  flush();
  cancelGesture();
  busy = true;
  try {
    if (dirty() && embedded && !confirm(t("discard"))) return;
    await captureRecovery();
    clearTimeout(recoverTimer);
    recovery = null;
    image?.dispose?.();
    data = image = selected = draft = openNote = null;
    $("#card").hidden = true;
    $("#message").hidden = true;
    $("#source").width = $("#source").height = 1;
    objects.replaceChildren();
    marks.replaceChildren();
    $("#pictures").replaceChildren();
    revision = savedRevision = 0;
    undoStack = [];
    redoStack = [];
    render();
    await showRecent();
    $("#start").focus();
  } catch {
    message("draftError", true);
  } finally {
    busy = false;
    render();
  }
};
async function sample() {
  if (busy) return;
  busy = true;
  updateStatus();
  try {
    const language = document.documentElement.lang === "de" ? "de" : "en";
    const response = await fetch("../assets/field-guide-" + language + ".png");
    if (!response.ok) throw new EditorError("fileError");
    const source = await openBackdrop(
      new File([await response.blob()], t("sampleName"), { type: "image/png" }),
    );
    const next = createLayer(source),
      p = next.pages[0];
    p.items.push(
      {
        ...newItem(true, 312, 292, p),
        title: t("sampleTitle"),
        text: t("sampleBody"),
        isExpanded: innerWidth >= 700,
      },
      {
        ...newItem(true, 850, 445, p),
        title: t("sampleTitle2"),
        text: t("sampleBody2"),
        isExpanded: false,
      },
    );
    const canvas = await backdropCanvas(next, source, 0);
    if (dirty() && !confirm(t("discard"))) return;
    await discardRecovery();
    initDoc(next, source, null, false, canvas, 0, true);
    tool = "select";
  } catch (error) {
    errorMessage(error, "fileError");
  } finally {
    busy = false;
    render();
  }
}
$("#sample-link").onclick = sample;
$("#portable").onclick = async () => {
  closeMenu();
  if (!data || busy) return;
  flush();
  render();
  busy = true;
  updateStatus();
  try {
    await requestDownload(
      await portableFile(data, image),
      image.name.replace(/[<>:"/\\|?*]/g, "-") + ".inklane",
      "portableDone",
    );
  } catch (error) {
    if (error.name !== "AbortError") errorMessage(error, "saveError");
  } finally {
    busy = false;
    updateStatus();
  }
};
$("#prev-page").onclick = () => switchPage(pageIndex - 1);
$("#next-page").onclick = () => switchPage(pageIndex + 1);
async function showNotebookPages(pages, index) {
  if (busy) return false;
  busy = true;
  updateStatus();
  try {
    const canvas = await backdropCanvas({ ...data, pages }, image, index);
    data.pages = pages;
    pageIndex = index;
    selected = openNote = draft = null;
    attachCanvas(canvas);
    resize();
    viewport.scrollTo(0, 0);
    return true;
  } catch (error) {
    errorMessage(error, "fileError");
    return false;
  } finally {
    busy = false;
    render();
  }
}
async function arrangePage(action) {
  if (data?.kind !== "Notebook" || busy) return;
  flush();
  cancelGesture();
  const previousPage = pageIndex,
    before = data.pages,
    after = [...before];
  let index = previousPage;
  if (action === "remove") {
    if (after.length <= 1 || !confirm(t("deletePageConfirm"))) return;
    after.splice(index, 1);
    index = Math.min(index, after.length - 1);
  } else if (action === "back" || action === "forward") {
    const dest = index + (action === "back" ? -1 : 1);
    if (dest < 0 || dest >= after.length) return;
    [after[index], after[dest]] = [after[dest], after[index]];
    index = dest;
  } else {
    if (after.length >= 1000) {
      message("size", true);
      return;
    }
    const source = page(),
      next =
        action === "copy"
          ? structuredClone(source)
          : { ...source, items: [], images: [], ink: [], shapes: [] };
    if (action === "copy") {
      remapObjects(
        collections.flatMap((group) =>
          (next[group] || []).map((obj) => ({ group, obj })),
        ),
      );
      try {
        serialize({ ...data, pages: [...after, next] });
      } catch (e) {
        errorMessage(e, "size");
        return;
      }
    }
    after.splice(++index, 0, next);
  }
  $("#page-list").close();
  if (await showNotebookPages(after, index)) {
    record({
      group: "pages",
      before,
      after,
      previousPage,
      nextPage: index,
      weight:
        512 +
        Math.abs(after.length - before.length) *
          (action === "new"
            ? 256
            : collections.reduce(
                (sum, k) =>
                  sum +
                  before[previousPage][k].reduce((n, o) => n + weight(o), 0),
                0,
              )),
    });
    render();
  }
}
$("#add-page").onclick = () => arrangePage("new");
for (const b of document.querySelectorAll("[data-page-action]"))
  b.onclick = () => arrangePage(b.dataset.pageAction);
$("#page-count").onclick = () => {
  const list = $("#page-choices");
  list.replaceChildren();
  data.pages.forEach((p, i) => {
    const button = document.createElement("button");
    button.textContent = t("page") + " " + (i + 1);
    button.setAttribute("aria-current", i === pageIndex ? "page" : "false");
    button.onclick = () => {
      $("#page-list").close();
      switchPage(i);
    };
    list.append(button);
  });
  $("#page-manage").hidden = data.kind !== "Notebook";
  for (const b of document.querySelectorAll("[data-page-action]")) {
    b.disabled =
      (b.dataset.pageAction === "remove" && data.pages.length === 1) ||
      (b.dataset.pageAction === "back" && pageIndex === 0) ||
      (b.dataset.pageAction === "forward" &&
        pageIndex === data.pages.length - 1);
  }
  $("#page-list").showModal();
  list.children[pageIndex].focus();
};
const paperDialog = $("#new-document");
$("#rename-book").onclick = () => {
  closeMenu();
  if (!data || data.kind !== "Notebook" || busy) return;
  $("#rename-title").value = data.title;
  $("#rename").showModal();
  $("#rename-title").select();
};
$("#cancel-rename").onclick = () => $("#rename").close();
$("#rename-form").onsubmit = (e) => {
  e.preventDefault();
  const name = $("#rename-title")
    .value.trim()
    .replace(/[\u0000-\u001f\u007f-\u009f]/g, " ");
  if (!name) {
    $("#rename-title").focus();
    return;
  }
  if (name !== data.title) {
    const before = data.title;
    data.title = image.name = name;
    record({ group: "title", before, after: name, weight: 512 });
  }
  $("#rename").close();
  render();
};
function configurePaper() {
  const ruled = $("#pattern").value !== "Blank";
  $("#spacing-row").hidden = !ruled;
  $("#custom-spacing-row").hidden = !ruled || $("#spacing").value !== "custom";
  $("#custom-spacing").disabled = $("#custom-spacing-row").hidden;
}
$("#pattern").onchange = $("#spacing").onchange = configurePaper;
$("#new-notebook").onclick = $("#new-sheet").onclick = () => {
  if (busy) return;
  closeMenu();
  $("#book-title").value = t("untitled");
  paperDialog.showModal();
  $("#book-title").select();
};
$("#cancel-new").onclick = () => paperDialog.close();
$("#paper-form").onsubmit = async (e) => {
  e.preventDefault();
  if (busy) return;
  const title = $("#book-title")
    .value.trim()
    .replace(/[\u0000-\u001f\u007f-\u009f]/g, " ");
  if (!title) {
    $("#book-title").focus();
    return;
  }
  const spacing =
    $("#spacing").value === "custom"
      ? Number($("#custom-spacing").value)
      : Number($("#spacing").value);
  const p = paperPage(
    $("#paper-size").value,
    $("#orientation").value,
    $("#pattern").value,
    $("#pattern").value === "Blank" ? 5 : spacing,
  );
  flush();
  if (dirty() && !confirm(t("discard"))) return;
  busy = true;
  updateStatus();
  try {
    const doc = notebookDocument(title, p),
      source = notebookSource(title);
    const canvas = await backdropCanvas(doc, source, 0);
    await discardRecovery();
    paperDialog.close();
    initDoc(doc, source, null, false, canvas);
  } catch (error) {
    errorMessage(error, "fileError");
  } finally {
    busy = false;
    render();
  }
};
async function printOutput(selectedPage = null) {
  if (!data || busy || globalThis.inklaneNativePrinting === false) return;
  flush();
  printing = busy = true;
  render();
  try {
    const { printReview } = await import("./print.js");
    await printReview(data, image, strokeVisual, t, selectedPage);
  } catch (error) {
    errorMessage(error, "printFailed");
  } finally {
    printing = busy = false;
    updateStatus();
  }
}
$("#print").onclick = async () => {
  closeMenu();
  if (!data || busy || globalThis.inklaneNativePrinting === false) return;
  if (data.kind !== "Pdf" || data.pages.length === 1) {
    await printOutput();
    return;
  }
  const current = data;
  try {
    const { canPrintAll } = await import("./print.js");
    if (busy || data !== current) return;
    const all = canPrintAll(data);
    $("#print-all").textContent =
      t("allPages") + " (" + data.pages.length + ")";
    $("#print-all").disabled = !all;
    $("#print-current").textContent =
      t("currentPage") + " (" + (pageIndex + 1) + ")";
    $("#print-range").value = all ? "all" : "current";
    $("#print-limit").hidden = all;
    $("#print-options").showModal();
  } catch (error) {
    errorMessage(error, "printFailed");
  }
};
$("#print-form").onsubmit = (e) => {
  e.preventDefault();
  $("#print-options").close();
  printOutput($("#print-range").value === "current" ? pageIndex : null);
};
$("#cancel-print").onclick = () => $("#print-options").close();
showRecent();
if (
  ["field-guide", "review"].includes(
    new URLSearchParams(location.search).get("sample"),
  )
)
  sample();

$("#whole-page").onclick = () => {
  closeMenu();
  wholePage = true;
  fit = true;
  resize();
};
$("#keep-sample").onclick = () => $("#portable").click();

function allObjects() {
  return data ? collections.flatMap((group) => page()[group] || []) : [];
}
function isSelected(id) {
  return id === selected || selectionIds.has(id) || selectedGroupIds.has(id);
}
function selectedObjects() {
  const group = current()?.obj.groupId;
  return allObjects().filter(
    (o) =>
      o.id === selected ||
      selectionIds.has(o.id) ||
      Boolean(group && o.groupId === group),
  );
}
function selectedRecords() {
  return collections.flatMap((group) =>
    (page()[group] || [])
      .filter((o) => isSelected(o.id))
      .map((obj) => ({ group, obj: structuredClone(obj) })),
  );
}
function mutatePage(action) {
  flush();
  const before = structuredClone(page()),
    after = structuredClone(before);
  action(after);
  serialize({
    ...data,
    pages: data.pages.map((p, i) => (i === pageIndex ? after : p)),
  });
  data.pages[pageIndex] = after;
  record({
    group: "batch",
    pageIndex,
    before,
    after,
    weight: JSON.stringify(before).length + JSON.stringify(after).length,
  });
}
function mutateSelection(action) {
  const ids = new Set(selectedObjects().map((o) => o.id));
  mutatePage((p) => {
    for (const group of collections)
      p[group] = p[group].map((o) => (ids.has(o.id) ? action(o, group) : o));
  });
}
function applyProperties(values) {
  if (!current()) return;
  flush();
  mutateSelection((o, group) => {
    const allowed =
      group === "items"
        ? [
            "color",
            "opacity",
            "width",
            "height",
            "rotation",
            "fontFamily",
            "fontSize",
            "bold",
            "italic",
            "underline",
            "alignment",
            "contentFormat",
          ]
        : group === "shapes"
          ? [
              "color",
              "opacity",
              "width",
              "height",
              "rotation",
              "fillColor",
              "strokeWidth",
              "kind",
              "label",
            ]
          : group === "images"
            ? ["opacity", "width", "height", "rotation"]
            : ["color", "opacity"];
    return {
      ...o,
      ...Object.fromEntries(
        Object.entries(values).filter(([key]) => allowed.includes(key)),
      ),
    };
  });
  render();
}
function svgObject(visual, item) {
  const svg = document.createElementNS(svgNS, "svg");
  svg.setAttribute("viewBox", `0 0 ${page().width} ${page().height}`);
  svg.classList.add("object-vector");
  svg.style.zIndex = 1000001 + (item.layer || 0);
  svg.append(visual);
  return svg;
}
function renderHandles() {
  const sel = current();
  if (
    !sel ||
    sel.group === "ink" ||
    sel.obj.isNote ||
    draft ||
    selectedObjects().length > 1
  )
    return;
  const o = sel.obj;
  for (const [key, rx, ry] of [
    ["nw", 0, 0],
    ["n", 0.5, 0],
    ["ne", 1, 0],
    ["e", 1, 0.5],
    ["se", 1, 1],
    ["s", 0.5, 1],
    ["sw", 0, 1],
    ["w", 0, 0.5],
  ]) {
    const handle = document.createElement("button");
    handle.className = "resize-handle";
    handle.dataset.resize = key;
    handle.dataset.id = o.id;
    handle.setAttribute("aria-label", t("resize") + " " + key.toUpperCase());
    handle.tabIndex = -1;
    const angle = ((o.rotation || 0) * Math.PI) / 180,
      dx = o.width * (rx - 0.5),
      dy = o.height * (ry - 0.5);
    handle.style.left =
      (o.x + o.width / 2 + dx * Math.cos(angle) - dy * Math.sin(angle)) * zoom +
      "px";
    handle.style.top =
      (o.y + o.height / 2 + dx * Math.sin(angle) + dy * Math.cos(angle)) *
        zoom +
      "px";
    objects.append(handle);
  }
}
function resized(original, handle, dx, dy) {
  const angle = ((original.rotation || 0) * Math.PI) / 180,
    c = Math.cos(angle),
    s = Math.sin(angle);
  [dx, dy] = [dx * c + dy * s, -dx * s + dy * c];
  const o = { ...original };
  if (handle.includes("e"))
    o.width = Math.max(16, Math.min(100000, original.width + dx));
  if (handle.includes("s"))
    o.height = Math.max(16, Math.min(100000, original.height + dy));
  if (handle.includes("w")) {
    o.width = Math.max(16, Math.min(100000, original.width - dx));
    o.x = original.x + original.width - o.width;
  }
  if (handle.includes("n")) {
    o.height = Math.max(16, Math.min(100000, original.height - dy));
    o.y = original.y + original.height - o.height;
  }
  const cx = original.x + original.width / 2,
    cy = original.y + original.height / 2;
  const sx = o.x + o.width / 2 - cx,
    sy = o.y + o.height / 2 - cy;
  o.x = cx + sx * c - sy * s - o.width / 2;
  o.y = cy + sx * s + sy * c - o.height / 2;
  return o;
}
function nodeAt(p) {
  return [...(page().shapes || [])]
    .filter((o) => !o.isNote && !["Line", "Arrow"].includes(o.kind))
    .reverse()
    .find(
      (o) =>
        p.x >= o.x &&
        p.x <= o.x + o.width &&
        p.y >= o.y &&
        p.y <= o.y + o.height,
    );
}
function groupSelection(group) {
  if (!current() || (group && selectedObjects().length < 2)) return;
  const groupId = group ? crypto.randomUUID() : "";
  mutateSelection((o) => ({ ...o, groupId }));
  selectionIds.clear();
  render();
}
function remapObjects(records) {
  const ids = new Map(records.map(({ obj }) => [obj.id, crypto.randomUUID()])),
    groups = new Map();
  for (const { obj } of records) {
    obj.id = ids.get(obj.id);
    if (obj.groupId) {
      if (!groups.has(obj.groupId))
        groups.set(obj.groupId, crypto.randomUUID());
      obj.groupId = groups.get(obj.groupId);
    }
    if (obj.startId) obj.startId = ids.get(obj.startId) || null;
    if (obj.endId) obj.endId = ids.get(obj.endId) || null;
  }
  return records;
}
async function insertObjects(records, fitPlacement = false) {
  if (!data || busy) return;
  try {
    flush();
    busy = true;
    updateStatus();
    if (!Array.isArray(records) || records.length > 12100)
      throw new EditorError("invalid");
    const copied = remapObjects(structuredClone(records));
    const points = copied.flatMap(
      ({ obj }) =>
        obj.points || [
          obj,
          {
            x: obj.x + (fitPlacement ? obj.width : 0),
            y: obj.y + (fitPlacement ? obj.height : 0),
          },
        ],
    );
    let left = Infinity,
      top = Infinity,
      right = -Infinity,
      bottom = -Infinity;
    for (const p of points) {
      left = Math.min(left, p.x);
      top = Math.min(top, p.y);
      right = Math.max(right, p.x);
      bottom = Math.max(bottom, p.y);
    }
    if (!points.length) {
      left = top = right = bottom = 0;
    }
    const scale = fitPlacement
      ? Math.min(
          1,
          (page().width * 0.8) / Math.max(1, right - left),
          (page().height * 0.8) / Math.max(1, bottom - top),
        )
      : 1;
    const origin = fitPlacement
      ? pasteOrigin()
      : {
          x:
            left +
            Math.max(-left, Math.min(14 / zoom, page().width - 1 - right)),
          y:
            top +
            Math.max(-top, Math.min(14 / zoom, page().height - 1 - bottom)),
        };
    const transform = (p) => ({
      ...p,
      x: origin.x + (p.x - left) * scale,
      y: origin.y + (p.y - top) * scale,
    });
    for (const record of copied) {
      const o = record.obj;
      if (o.points) {
        o.points = o.points.map(transform);
        o.width *= scale;
      } else {
        Object.assign(o, transform(o));
        o.width *= scale;
        o.height *= scale;
        if (o.fontSize) o.fontSize *= scale;
        if (o.strokeWidth) o.strokeWidth *= scale;
      }
    }
    const next = structuredClone(page());
    for (const r of copied) {
      if (!collections.includes(r.group)) throw new EditorError("invalid");
      next[r.group].push(r.obj);
    }
    await validateDocument({ ...data, pages: [next] });
    mutatePage((p) => {
      for (const group of collections) p[group] = next[group];
    });
    selectionIds = new Set(copied.map((r) => r.obj.id));
    selected = copied[0]?.obj.id;
    tool = "select";
    render();
  } catch (error) {
    errorMessage(error, "clipboard");
  } finally {
    busy = false;
    render();
  }
}
async function copyObjects() {
  if (!current()) return;
  flush();
  localClipboard = selectedRecords();
  try {
    await navigator.clipboard.writeText(
      JSON.stringify({ format: "inklane.selection", objects: localClipboard }),
    );
  } catch {
    /* Session clipboard remains available. */
  }
  message("copiedObjects");
}
async function pasteObjects() {
  try {
    const text = await navigator.clipboard.readText();
    if (text.length <= JSON_LIMIT) {
      const parsed = JSON.parse(text);
      if (parsed.format === "inklane.selection")
        localClipboard = parsed.objects;
    }
  } catch {
    /* Fall back to the current local selection. */
  }
  if (localClipboard) await insertObjects(localClipboard);
  else message("clipboard", true);
}
function readElements() {
  try {
    const entries = JSON.parse(
      localStorage.getItem("inklane.elements") || "[]",
    );
    return Array.isArray(entries) ? entries : [];
  } catch {
    return [];
  }
}
function saveElement() {
  if (!current()) return;
  flush();
  const name = prompt(t("elementName"));
  if (!name?.trim()) return;
  const category = prompt(t("elementCategory"), "") ?? "";
  const entries = readElements(),
    entry = {
      id: crypto.randomUUID(),
      name: name.trim().slice(0, 100),
      category: category.trim().slice(0, 100),
      objects: selectedRecords(),
    };
  try {
    localStorage.setItem(
      "inklane.elements",
      JSON.stringify([...entries, entry]),
    );
    message("elementSaved");
  } catch {
    message("elementStorage", true);
  }
}
function showElements() {
  let dialog = $("#element-library");
  if (!dialog) {
    dialog = document.createElement("dialog");
    dialog.id = "element-library";
    document.body.append(dialog);
  }
  dialog.replaceChildren();
  const heading = document.createElement("h2");
  heading.textContent = t("elements");
  dialog.append(heading);
  const entries = readElements().sort((a, b) =>
    (a.category + a.name).localeCompare(b.category + b.name),
  );
  if (!entries.length) {
    const p = document.createElement("p");
    p.textContent = t("noElements");
    dialog.append(p);
  }
  for (const entry of entries) {
    const row = document.createElement("div");
    row.className = "element-row";
    const insert = document.createElement("button");
    insert.textContent = [entry.category, entry.name]
      .filter(Boolean)
      .join(" / ");
    insert.onclick = () => {
      dialog.close();
      insertObjects(entry.objects, true);
    };
    const rename = document.createElement("button");
    rename.textContent = t("renameElement");
    rename.onclick = () => {
      const name = prompt(t("elementName"), entry.name);
      if (!name?.trim()) return;
      entry.name = name.trim().slice(0, 100);
      try {
        localStorage.setItem("inklane.elements", JSON.stringify(entries));
        showElements();
      } catch {
        message("elementStorage", true);
      }
    };
    const remove = document.createElement("button");
    remove.textContent = "×";
    remove.setAttribute("aria-label", t("remove"));
    remove.onclick = () => {
      try {
        localStorage.setItem(
          "inklane.elements",
          JSON.stringify(entries.filter((e) => e.id !== entry.id)),
        );
        showElements();
      } catch {
        message("elementStorage", true);
      }
    };
    row.append(insert, rename, remove);
    dialog.append(row);
  }
  const close = document.createElement("button");
  close.textContent = t("done");
  close.onclick = () => dialog.close();
  dialog.append(close);
  if (!dialog.open) dialog.showModal();
}
$("#note-read").onclick = () => {
  flush();
  notePreview = true;
  renderCard();
};
$("#note-edit").onclick = () => {
  notePreview = false;
  renderCard();
  $("#note-body").focus();
};
$("#note-resize").onpointerdown = (e) => {
  if (!openNote || busy) return;
  e.preventDefault();
  flush();
  const item = find(openNote).obj;
  const start = {
    x: e.clientX,
    y: e.clientY,
    width: card.offsetWidth,
    height: card.offsetHeight,
  };
  const button = e.currentTarget;
  button.setPointerCapture(e.pointerId);
  button.onpointermove = (event) => {
    card.style.width =
      Math.max(
        210,
        Math.min(
          viewport.clientWidth - 12,
          start.width + event.clientX - start.x,
        ),
      ) + "px";
    card.style.height =
      Math.max(
        150,
        Math.min(
          viewport.clientHeight - 12,
          start.height + event.clientY - start.y,
        ),
      ) + "px";
    positionCard();
  };
  button.onpointerup = () => {
    button.onpointermove = null;
    applyProperties({ width: card.offsetWidth, height: card.offsetHeight });
  };
  button.onpointercancel = () => {
    button.onpointermove = null;
    renderCard();
  };
};
properties = setupProperties({
  active: () => Boolean(data),
  current,
  tool: () => tool,
  render,
  apply: applyProperties,
  copy: copyObjects,
  paste: pasteObjects,
  group: groupSelection,
  saveElement,
  elements: showElements,
  layer: (front) => {
    if (!current()) return;
    const layers = allObjects().map((o) => o.layer || 0);
    const layer = Math.max(
      -1000000,
      Math.min(
        1000000,
        (front ? Math.max(...layers) : Math.min(...layers)) + (front ? 1 : -1),
      ),
    );
    mutateSelection((o) => ({ ...o, layer }));
    render();
  },
});
