import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";

// Call with a page displaying the built editor. Uses the real local PDF writer,
// rich renderer and PDF.js reader, independently of the native save bridge.
export async function checkReviewPdf(page, output) {
  const result = await page.evaluate(async () => {
    const { reviewPdf } = await import("./pdf-export.js");
    const { annotatedPng } = await import("./image-export.js");
    const { openPdf } = await import("./pdf-source.js");
    const pdfjs = await import("./pdf-lib/pdf.min.mjs");
    pdfjs.GlobalWorkerOptions.workerSrc = new URL("./pdf-lib/pdf.worker.min.mjs", location.href).href;
    const t = key => ({ page: "Page", nativeNote: "Remark" })[key] || key;
    const stroke = item => {
      const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      const path = document.createElementNS(svg.namespaceURI, "path");
      path.setAttribute("d", `M${item.points[0].x},${item.points[0].y}L${item.points[1].x},${item.points[1].y}`);
      path.setAttribute("stroke", item.color); path.setAttribute("stroke-width", item.width); svg.append(path); return svg;
    };
    const encode = bytes => { let text = ""; for (let i = 0; i < bytes.length; i += 8192) text += String.fromCharCode(...bytes.subarray(i, i + 8192)); return btoa(text); };
    const tasks = [];
    const read = async blob => { const task = pdfjs.getDocument({ data: new Uint8Array(await blob.arrayBuffer()) }); tasks.push(task); return task.promise; };
    const pixels = async (pdf, index) => {
      const sheet = await pdf.getPage(index), viewport = sheet.getViewport({ scale: 96 / 72 });
      const canvas = document.createElement("canvas"); canvas.width = Math.ceil(viewport.width); canvas.height = Math.ceil(viewport.height);
      await sheet.render({ canvas, viewport }).promise; return canvas;
    };
    const marginPixels = canvas => {
      const data = canvas.getContext("2d").getImageData(canvas.width - 45, 50, 40, canvas.height - 100).data;
      let count = 0; for (let i = 0; i < data.length; i += 4) if (Math.min(data[i], data[i + 1], data[i + 2]) < 245) count++; return count;
    };
    const color = (canvas, x, y) => Array.from(canvas.getContext("2d").getImageData(x, y, 1, 1).data).slice(0, 3);
    const greenPixels = canvas => {
      const data = canvas.getContext("2d").getImageData(55, 55, canvas.width - 110, canvas.height - 110).data;
      let count = 0; for (let i = 0; i < data.length; i += 4) if (data[i + 1] > data[i] * 1.5 && data[i + 1] > data[i + 2] * 1.3 && data[i + 1] < 220) count++;
      return count;
    };
    const tile = document.createElement("canvas"); tile.width = tile.height = 1; tile.getContext("2d").fillStyle = "#1460E6"; tile.getContext("2d").fillRect(0, 0, 1, 1);
    const note = { id: "note", isNote: true, isExpanded: false, title: "Long structured explanation", x: 90, y: 200, width: 300, height: 240, fontSize: 16, fontFamily: "Segoe UI", color: "#138A36", contentFormat: "Markdown", opacity: 1,
      text: "# Review\n\n**Bold**, *italic*, ++underline++ and $x^2+1$.\n\n| Item | Finding |\n|---|---|\n| Meadow | Check sample |\n\n```js\nconst count = 42;\n```\n\n" + "A readable explanation with enough words to fill several report pages.\n\n".repeat(120) + "FINAL SENTENCE — ÄÖÜ 🪴" };
    const first = { width: 794, height: 1123, pattern: "Blank", spacingMm: 5, marginMm: 15, items: [note],
      images: [{ id: "picture", x: 40, y: 40, width: 100, height: 100, pngData: tile.toDataURL().split(",")[1], layer: 3, opacity: 1 }],
      ink: [{ id: "ink", color: "#13A536", width: 30, layer: 2, points: [{ x: 50, y: 70 }, { x: 130, y: 70 }] }],
      shapes: [{ id: "shape", kind: "Rectangle", x: 40, y: 40, width: 100, height: 100, color: "#FFD929", fillColor: "#FFD929", strokeWidth: 2, opacity: 1, layer: 1 }] };
    const second = { width: 560, height: 794, pattern: "Ruled", spacingMm: 5, marginMm: 15, items: [], images: [], ink: [], shapes: [] };
    const doc = { schemaVersion: 4, kind: "Notebook", title: "Review PDF (ÄÖÜ) \\ safe", pages: [first, second] }, before = JSON.stringify(doc), progress = [];
    const blob = await reviewPdf(doc, { name: "Review" }, stroke, t, { onProgress: event => progress.push(event) });
    const pdf = await read(blob), sourcePage = await pixels(pdf, 1), firstNote = await pixels(pdf, 3), last = await pixels(pdf, pdf.numPages);
    const views = []; for (let i = 1; i <= 2; i++) { const p = await pdf.getPage(i); views.push(p.view); }
    const selected = await read(await reviewPdf(doc, { name: "Review" }, stroke, t, { selectedPage: 1 }));
    const short = structuredClone(doc); short.pages = [short.pages[0]];
    short.pages[0].items = [1, 2, 3].map(n => ({ ...note, id: `short-${n}`, title: `Short remark ${n}`, text: `Complete short explanation ${n}.`, x: n * 60 }));
    const shortPdf = await read(await reviewPdf(short, { name: "Short notes" }, stroke, t));
    const pale = structuredClone(short); pale.pages[0].items = ["#FFFFFF", "#FFFF00"].map((color, i) => ({ ...note, id: `pale-${i}`, title: `Readable pale note ${i}`, color, text: "Every explanation remains readable on white paper. ".repeat(12) }));
    const paleBefore = JSON.stringify(pale), palePdf = await read(await reviewPdf(pale, { name: "Pale" }, stroke, t));
    const paleCanvas = await pixels(palePdf, 2), palePixels = paleCanvas.getContext("2d").getImageData(55, 110, 680, 850).data;
    let grey = 0, olive = 0; for (let i = 0; i < palePixels.length; i += 4) {
      const [r, g, b] = palePixels.slice(i, i + 3);
      if (r > 60 && r < 170 && Math.abs(r - g) < 2 && Math.abs(g - b) < 2) grey++;
      if (r > 60 && r < 170 && Math.abs(r - g) < 2 && b < r * .4) olive++;
    }
    const metadata = (await pdf.getMetadata()).info;
    const failed = [];
    for (const phase of ["pages", "notes"]) {
      const cancel = new AbortController();
      try { await reviewPdf(doc, { name: "Review" }, stroke, t, { signal: cancel.signal, onProgress: value => { if (value.phase === phase) cancel.abort(); } }); }
      catch (error) { failed.push(error.name); }
    }
    const refused = [];
    for (const invalid of [{ ...doc, pages: Array(501).fill(second) }, { ...doc, pages: [{ ...second, width: 9000 }] }]) {
      try { await reviewPdf(invalid, { name: "Review" }, stroke, t); } catch (error) { refused.push(error.message); }
    }
    // A generated PDF is reopened through the real PDF renderer, then exported
    // sequentially. Original bytes remain intact and no annotation data mutates.
    const sourceFile = new File([blob], "source.pdf", { type: "application/pdf" });
    const source = await openPdf(sourceFile), hashBefore = await sourceFile.arrayBuffer();
    let active = 0, peak = 0, calls = 0;
    const wrapped = { ...source, async render(index) { calls++; peak = Math.max(peak, ++active); try { return await source.render(index); } finally { active--; } } };
    const pdfDoc = { ...doc, kind: "Pdf", pages: [first, second].map(p => ({ ...p, items: [], images: [], ink: [], shapes: [] })) };
    const again = await read(await reviewPdf(pdfDoc, wrapped, stroke, t));
    const sourceUnchanged = encode(new Uint8Array(hashBefore)) === encode(new Uint8Array(await sourceFile.arrayBuffer()));
    source.dispose();
    const owned = document.createElement("canvas"); owned.width = 1200; owned.height = 600; owned.getContext("2d").fillStyle = "#1260E6"; owned.getContext("2d").fillRect(0, 0, 1200, 600);
    const imageDoc = { ...doc, kind: "Image", pages: [{ ...second, width: 1200, height: 600, items: [], images: [], ink: [], shapes: [] }] };
    const imagePdf = await read(await reviewPdf(imageDoc, { name: "Image", render: () => owned }, stroke, t));
    const imageView = (await imagePdf.getPage(1)).view;
    // The existing PNG path must still include the complete note gutter.
    const pngDoc = structuredClone(doc); pngDoc.pages[0].items[0].text = "Short unchanged PNG note";
    const png = await annotatedPng(pngDoc, { name: "Review" }, 0, stroke, t), bitmap = await createImageBitmap(png);
    const result = { bytes: blob.size, pages: pdf.numPages, views, topPixel: color(sourcePage, 70, 70), lastGreen: greenPixels(last), marginPixels: marginPixels(firstNote) + marginPixels(last), selectedPages: selected.numPages, shortPages: shortPdf.numPages,
      cancelled: failed, refused, unchanged: JSON.stringify(doc) === before, sourceUnchanged, sequential: { peak, calls, pages: again.numPages }, imageView, imageSourceSize: [owned.width, owned.height], pngWidth: bitmap.width, pale: { grey, olive, unchanged: JSON.stringify(pale) === paleBefore }, metadata: { title: metadata.Title, creator: metadata.Creator },
      transientHosts: document.querySelectorAll(".pdf-note-flow").length, progress, pdf: encode(new Uint8Array(await blob.arrayBuffer())), firstNote: firstNote.toDataURL(), lastNote: last.toDataURL() };
    bitmap.close(); for (const task of tasks) await task.destroy(); return result;
  });
  assert(result.pages >= 6, "Long remarks did not produce a complete paginated appendix.");
  assert.equal(result.marginPixels, 0, "Adjacent appendix columns leaked into the paper margin.");
  assert(result.lastGreen > 200, "The last appendix page lost the tail of the explanation.");
  assert.deepEqual(result.topPixel, [20, 96, 230], "Mixed object layers changed in PDF output.");
  assert(Math.abs(result.views[0][2] - 595.5) < .1 && Math.abs(result.views[1][2] - 420) < .1, "Source page geometry changed.");
  assert.equal(result.selectedPages, 1);
  assert.equal(result.shortPages, 2, "Short remarks should share a single appendix page.");
  assert.deepEqual(result.cancelled, ["AbortError", "AbortError"]);
  assert.deepEqual(result.refused, ["pdfExportLimit", "pdfExportLimit"]);
  assert(result.unchanged && result.sourceUnchanged);
  assert.deepEqual(result.sequential, { peak: 1, calls: 2, pages: 2 });
  assert(Math.abs(result.imageView[2] - 841.89) < .1 && Math.abs(result.imageView[3] - 595.28) < .1);
  assert.deepEqual(result.imageSourceSize, [1200, 600]);
  assert(result.pngWidth > 2 * 794, "PNG export lost its note gutter.");
  assert.equal(result.transientHosts, 0);
  assert(result.pale.grey > 500 && result.pale.olive > 500 && result.pale.unchanged, "Pale note bodies became invisible or their source color changed.");
  assert.deepEqual(result.metadata, { title: "Review PDF (ÄÖÜ) \\ safe", creator: "Inklane" });
  assert(result.progress.every(p => p.current >= 1 && p.current <= p.total));
  if (output) {
    await mkdir(output, { recursive: true });
    await writeFile(join(output, "complete-review.pdf"), Buffer.from(result.pdf, "base64"));
    await writeFile(join(output, "first-note.png"), Buffer.from(result.firstNote.split(",")[1], "base64"));
    await writeFile(join(output, "last-note.png"), Buffer.from(result.lastNote.split(",")[1], "base64"));
  }
  delete result.pdf; delete result.firstNote; delete result.lastNote;
  return result;
}
