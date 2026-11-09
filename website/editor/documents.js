import {
  EditorError,
  JSON_LIMIT,
  digest,
  newDocument,
  openImage,
  serialize,
  validateDocument
} from "./model.js";

const mm = 96 / 25.4;
const magic = "INKLANE1";
const bundleLimit = 100 * 1024 * 1024;
export function paperPage(size = "A4", orientation = "portrait", pattern = "Blank", spacing = 5) {
  const sides = size === "A5" ? [148, 210] : [210, 297];
  if (orientation === "landscape") sides.reverse();
  return {
    width: sides[0] * mm,
    height: sides[1] * mm,
    pattern,
    spacingMm: spacing,
    marginMm: 15,
    items: [],
    ink: [],
    images: []
  };
}
export function notebookSource(name) {
  return { name, hash: null, mediaType: "application/vnd.inklane.notebook+json" };
}
export function notebookDocument(title, page) {
  return { schemaVersion: 2, kind: "Notebook", title, pages: [page] };
}
export function pdfDocument(source) {
  return {
    schemaVersion: 3,
    kind: "Pdf",
    title: "",
    sourcePdfName: source.name,
    sourcePdfSha256: source.hash,
    pages: source.pages.map(({ width, height }) => ({
      width,
      height,
      pattern: "Blank",
      spacingMm: 5,
      marginMm: 0,
      items: [],
      ink: [],
      images: []
    }))
  };
}
export async function openBackdrop(file) {
  if (/\.pdf$/i.test(file.name)) {
    const { openPdf } = await import("./pdf-source.js");
    return { ...(await openPdf(file)), file };
  }
  const source = await openImage(file);
  const canvas = document.createElement("canvas");
  canvas.width = source.width;
  canvas.height = source.height;
  try {
    const paint = canvas.getContext("2d");
    if (!paint) throw new EditorError("decode");
    paint.drawImage(source.bitmap, 0, 0);
  } finally {
    source.bitmap.close();
  }
  return {
    name: source.name,
    hash: source.hash,
    width: source.width,
    height: source.height,
    mediaType: source.mediaType,
    file,
    render: () => canvas
  };
}
export function createLayer(source) {
  return source.mediaType === "application/pdf" ? pdfDocument(source) : newDocument(source);
}
export async function backdropCanvas(doc, source, index) {
  const p = doc.pages[index];
  if (doc.kind !== "Notebook") return source.render(index);
  // Coordinates stay at 96 units per inch. Doubling the raster makes fine paper lines legible on Retina displays.
  const scale = Math.min(2, 2400 / Math.max(p.width, p.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.max(1, Math.round(scale * p.width));
  canvas.height = Math.max(1, Math.round(scale * p.height));
  const ctx = canvas.getContext("2d");
  ctx.scale(canvas.width / p.width, canvas.height / p.height);
  ctx.fillStyle = "#fffefb";
  ctx.fillRect(0, 0, p.width, p.height);
  if (p.pattern !== "Blank") {
    const margin = p.marginMm * mm,
      step = p.spacingMm * mm;
    ctx.strokeStyle = "#d7d7dd";
    ctx.lineWidth = 0.65;
    ctx.beginPath();
    for (let y = margin; y <= p.height - margin + 0.001; y += step) {
      ctx.moveTo(margin, y);
      ctx.lineTo(p.width - margin, y);
    }
    if (p.pattern === "Squared")
      for (let x = margin; x <= p.width - margin + 0.001; x += step) {
        ctx.moveTo(x, margin);
        ctx.lineTo(x, p.height - margin);
      }
    ctx.stroke();
  }
  return canvas;
}
export function attachCanvas(canvas) {
  canvas.id = "source";
  canvas.setAttribute("aria-hidden", "true");
  document.querySelector("#source").replaceWith(canvas);
}
export async function portableFile(doc, source) {
  const model = serialize(doc);
  const meta = new Blob([
    '{"format":"inklane.workspace","version":1,"sourceName":',
    JSON.stringify(doc.kind === "Notebook" ? source.name.replace(/[\\/\u0000-\u001f]/g, "-") : source.name),
    ',"document":',
    model,
    "}"
  ]);
  if (meta.size > JSON_LIMIT) throw new EditorError("size");
  const length = new ArrayBuffer(4);
  new DataView(length).setUint32(0, meta.size);
  return new Blob([magic, length, meta, ...(source.file ? [source.file] : [])], {
    type: "application/octet-stream"
  });
}
export async function readPortable(file) {
  if (file.size > bundleLimit) throw new EditorError("size");
  if (file.size < 13 || (await file.slice(0, 8).text()) !== magic) throw new EditorError("invalid");
  const length = new DataView(await file.slice(8, 12).arrayBuffer()).getUint32(0);
  if (length < 2 || length > JSON_LIMIT || 12 + length > file.size) throw new EditorError("invalid");
  let meta;
  try {
    meta = JSON.parse(await file.slice(12, 12 + length).text());
  } catch {
    throw new EditorError("invalid");
  }
  const doc = meta?.document;
  if (
    meta?.format !== "inklane.workspace" ||
    meta.version !== 1 ||
    typeof meta.sourceName !== "string" ||
    /[\\/\u0000-\u001f]/.test(meta.sourceName) ||
    meta.sourceName.length > 255 ||
    !["Notebook", "Image", "Pdf"].includes(doc?.kind) ||
    !(doc.kind === "Pdf" ? [3, 4].includes(doc.schemaVersion) : [1, 2, 4].includes(doc.schemaVersion))
  )
    throw new EditorError("invalid");
  const bytes = file.slice(12 + length);
  let source;
  try {
    if (doc.kind === "Notebook") {
      if (
        bytes.size ||
        typeof doc.title !== "string" ||
        doc.sourceImagePath != null ||
        doc.sourceImageSha256 != null ||
        doc.sourcePdfName != null ||
        doc.sourcePdfSha256 != null
      )
        throw new EditorError("invalid");
      source = notebookSource(doc.title || meta.sourceName || "Inklane");
      await validateDocument(doc);
    } else {
      source = await openBackdrop(new File([bytes], meta.sourceName));
      const pdf = doc.kind === "Pdf";
      const expected = pdf ? doc.sourcePdfSha256 : doc.sourceImageSha256;
      if (
        typeof expected !== "string" ||
        source.hash !== expected.toUpperCase() ||
        pdf !== (source.mediaType === "application/pdf")
      )
        throw new EditorError("identity");
      if (doc.pages?.length !== (pdf ? source.pages.length : 1)) throw new EditorError("identity");
      await validateDocument(doc, pdf ? source.pages : [source]);
      if (pdf) doc.sourcePdfName = source.name;
      else doc.sourceImagePath = source.name;
    }
    return { doc, source, fingerprint: await digest(await file.arrayBuffer()) };
  } catch (error) {
    source?.dispose?.();
    throw error;
  }
}
