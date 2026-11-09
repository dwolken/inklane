import { EditorError } from "./model.js";
import { renderText, textStyle } from "./rich-text.js";
import { embeddedStyles, rasterizeElement, reviewPagePng, throwIfAborted } from "./image-export.js";

const mm = 96 / 25.4;
const paper = { width: 210 * mm, height: 297 * mm, margin: 15 * mm };
const maxPages = 500, maxBytes = 64 * 1024 * 1024, maxImages = 40 * 1024 * 1024;
const nextTask = () => new Promise(resolve => setTimeout(resolve, 0));

// Keep the hue of pale annotation colors while making report text readable on white.
function appendixColor(color) {
  if (!/^#[0-9a-f]{6}$/i.test(color || "")) return "#272534";
  const rgb = [1, 3, 5].map(start => parseInt(color.slice(start, start + 2), 16));
  const luminance = factor => rgb.map(value => Math.floor(value * factor) / 255)
    .map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4)
    .reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
  const maximum = 1.05 / 4.5 - .05;
  if (luminance(1) <= maximum) return color;
  let low = 0, high = 1;
  for (let step = 0; step < 16; step++) { const mid = (low + high) / 2; if (luminance(mid) <= maximum) low = mid; else high = mid; }
  return "#" + rgb.map(value => Math.floor(value * low).toString(16).padStart(2, "0")).join("");
}

function hostFor(root) {
  const host = document.createElement("div");
  Object.assign(host.style, { position: "fixed", left: "-100000px", top: "0", pointerEvents: "none" });
  host.append(root); document.body.append(host); return host;
}

// A fixed-height CSS column flow uses the browser's line/table fragmentation.
// Each column becomes a paper page; no tall bitmap is shrunk or cut through lines.
async function notePages(notes, t, signal) {
  throwIfAborted(signal);
  if (notes.reduce((total, note) => total + note.item.text.length + (note.item.title?.length || 0), 0) > 2_000_000)
    throw new EditorError("pdfExportLimit");
  const root = document.createElement("div");
  root.setAttribute("xmlns", "http://www.w3.org/1999/xhtml");
  const width = paper.width - 2 * paper.margin, height = paper.height - 2 * paper.margin, gap = 40;
  Object.assign(root.style, { position: "relative", width: paper.width + "px", height: paper.height + "px", overflow: "hidden", background: "white", color: "#272534", font: "16px/1.45 Segoe UI,system-ui,sans-serif" });
  const style = document.createElement("style");
  style.textContent = (await embeddedStyles()) + `
    *{box-sizing:border-box}.pdf-note-flow{position:absolute;left:0;top:0;width:${width}px;height:${height}px;column-width:${width}px;column-gap:${gap}px;column-fill:auto;overflow:visible;overflow-wrap:anywhere;orphans:2;widows:2}
    .pdf-note-heading{font-size:16px;line-height:1.35;margin:0 0 15px;break-after:avoid;color:#272534}
    .pdf-note-heading:not(:first-child){border-top:1px solid #dcd9df;margin-top:18px;padding-top:12px}.pdf-note-heading span{display:block;font-size:12px;color:#625e6b}.pdf-note-body{white-space:pre-wrap;overflow-wrap:anywhere}
    .pdf-note-body.rich-content{white-space:normal}.pdf-note-body pre{white-space:pre-wrap;overflow-wrap:anywhere;overflow:visible}
    .pdf-note-body table{table-layout:fixed;max-width:100%}.pdf-note-body th,.pdf-note-body td{overflow-wrap:anywhere}
    .pdf-note-body tr,.pdf-note-body .katex-display{break-inside:avoid}.pdf-note-body .katex-display{overflow:visible}
    .pdf-note-body h1,.pdf-note-body h2,.pdf-note-body h3{break-after:avoid}
  `;
  const flow = document.createElement("div"); flow.className = "pdf-note-flow";
  for (const [index, { item, page: sourcePage, number }] of notes.entries()) {
    if (index % 20 === 0) { await nextTask(); throwIfAborted(signal); }
    const heading = document.createElement("h2"); heading.className = "pdf-note-heading";
    const reference = document.createElement("span"); reference.textContent = `${t("page")} ${sourcePage} · ${t("nativeNote")} ${number}`;
    heading.append(reference, document.createTextNode(item.title || t("nativeNote")));
    const body = document.createElement("div"); body.className = "pdf-note-body";
    // The appendix is a readable report; canvas box size does not dictate its type size.
    textStyle(body, { ...item, fontSize: Math.max(12, Math.min(24, item.fontSize || 16)), color: appendixColor(item.color), opacity: 1 });
    renderText(body, item); flow.append(heading, body);
  }
  const viewport = document.createElement("div");
  Object.assign(viewport.style, { position: "absolute", left: paper.margin + "px", top: paper.margin + "px", width: width + "px", height: height + "px", overflow: "hidden" });
  viewport.append(flow); root.append(style, viewport);
  const host = hostFor(root);
  try {
    await document.fonts.ready; throwIfAborted(signal);
    const bounds = flow.getBoundingClientRect();
    for (const element of flow.querySelectorAll("tr,.katex")) {
      const rect = element.getBoundingClientRect();
      if (rect.height > height + 1 || rect.width > width + 1) throw new EditorError("pdfExportLimit");
    }
    const count = Math.max(1, Math.ceil((flow.scrollWidth + gap - 1) / (width + gap)));
    if (!Number.isFinite(count) || count > maxPages) throw new EditorError("pdfExportLimit");
    // Detect unsupported fragmentation rather than silently crop overflowing text.
    const walker = document.createTreeWalker(flow, NodeFilter.SHOW_TEXT);
    let text;
    while ((text = walker.nextNode())) {
      if (!text.textContent.trim()) continue;
      const range = document.createRange(); range.selectNodeContents(text);
      for (const rect of range.getClientRects()) {
        if (!rect.width || !rect.height) continue;
        const column = Math.floor((rect.left - bounds.left + 1) / (width + gap));
        const x = rect.left - bounds.left - column * (width + gap);
        if (column < 0 || column >= count || x < -2 || x + rect.width > width + 2 || rect.top < bounds.top - 2 || rect.bottom > bounds.bottom + 2)
          throw new EditorError("pdfExportLimit");
      }
    }
    return {
      count,
      async render(index) {
        flow.style.transform = `translateX(${-index * (width + gap)}px)`;
        return rasterizeElement(root, paper.width, paper.height, { scale: 2, signal });
      },
      dispose() { host.remove(); },
    };
  } catch (error) { host.remove(); throw error; }
}

/** Complete presentation PDF. Source bytes and the editable document are never changed. */
export async function reviewPdf(doc, source, strokeShape, t, { selectedPage = null, signal, onProgress } = {}) {
  throwIfAborted(signal);
  const indices = selectedPage === null ? doc.pages.map((_, i) => i) : [selectedPage];
  if (!indices.length || indices.length > maxPages || indices.some(i => !Number.isInteger(i) || i < 0 || i >= doc.pages.length))
    throw new EditorError("pdfExportLimit");
  for (const index of indices) {
    const p = doc.pages[index];
    if (![p.width, p.height].every(v => Number.isFinite(v) && v >= 1 && v <= 8192) || p.width * p.height > 32_000_000)
      throw new EditorError("pdfExportLimit");
  }
  let pdf, count = 0, imageBytes = 0;
  const add = async (blob, width, height, inset = 0, fit = null) => {
    throwIfAborted(signal);
    if (++count > maxPages || (imageBytes += blob.size) > maxImages) throw new EditorError("pdfExportLimit");
    const orientation = width > height ? "landscape" : "portrait";
    if (!pdf) {
      pdf = new globalThis.jspdf.jsPDF({ unit: "pt", format: [width * .75, height * .75], orientation, compress: true });
      const title = String(doc.title || source.name || "Inklane").replace(/[\u0000-\u001f\u007f]/g, " ").slice(0, 200);
      pdf.setProperties({ title, creator: "Inklane" });
    }
    else pdf.addPage([width * .75, height * .75], orientation);
    let x = inset, y = inset, w = width - 2 * inset, h = height - 2 * inset;
    if (fit) { const scale = Math.min(w / fit.width, h / fit.height); w = fit.width * scale; h = fit.height * scale; x = (width - w) / 2; y = (height - h) / 2; }
    pdf.addImage(new Uint8Array(await blob.arrayBuffer()), "PNG", x * .75, y * .75, w * .75, h * .75, `page-${count}`, "FAST");
    await nextTask(); throwIfAborted(signal);
  };
  try {
    await import("./pdf-write/jspdf.umd.min.js"); throwIfAborted(signal);
    const notes = [];
    for (const [current, index] of indices.entries()) {
      onProgress?.({ phase: "pages", current: current + 1, total: indices.length });
      await nextTask(); throwIfAborted(signal);
      const p = doc.pages[index];
      const blob = await reviewPagePng(doc, source, index, strokeShape, t, { includeNotes: false, signal, maxPixels: 8_000_000, maxEdge: 4096 });
      if (doc.kind === "Image") {
        const landscape = p.width > p.height;
        await add(blob, landscape ? paper.height : paper.width, landscape ? paper.width : paper.height, 10 * mm, p);
      } else await add(blob, p.width, p.height);
      let number = 0;
      for (const item of p.items) if (item.isNote) notes.push({ item, page: index + 1, number: ++number });
    }
    if (notes.length) {
      const layout = await notePages(notes, t, signal);
      try {
        if (count + layout.count > maxPages) throw new EditorError("pdfExportLimit");
        for (let index = 0; index < layout.count; index++) {
          onProgress?.({ phase: "notes", current: index + 1, total: layout.count });
          await nextTask(); throwIfAborted(signal);
          await add(await layout.render(index), paper.width, paper.height);
        }
      } finally { layout.dispose(); }
    }
    throwIfAborted(signal);
    const result = pdf.output("blob");
    if (!result.size || result.size > maxBytes) throw new EditorError("pdfExportLimit");
    await nextTask(); throwIfAborted(signal); return result;
  } catch (error) {
    if (error.name === "AbortError" || error.message === "pdfExportLimit") throw error;
    throw new EditorError("pdfExportFailed");
  } finally { pdf = null; }
}
