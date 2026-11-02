import { EditorError } from "./model.js";

const dipPerMm = 96 / 25.4;
const mm = (dip) => (dip / dipPerMm).toFixed(5) + "mm";
const maxPagePixels = 8_000_000;
let disposePrevious;

// PDF print pages hold decoded source rasters until the browser has spooled the job.
// Budget using the same DPI and edge limits as the PDF renderer.
export function canPrintAll(doc) {
  if (doc.kind !== "Pdf") return true;
  if (doc.pages.length > 16) return false;
  let total = 0;
  for (const p of doc.pages) {
    const scale = Math.min(
      2,
      4096 / Math.max(p.width, p.height),
      Math.sqrt(maxPagePixels / (p.width * p.height))
    );
    total += Math.ceil(p.width * scale) * Math.ceil(p.height * scale);
  }
  return total <= 24_000_000;
}

function layout(p, image) {
  if (!image) return { width: p.width, height: p.height, left: 0, top: 0, scale: 1 };
  const landscape = p.width > p.height;
  const width = (landscape ? 297 : 210) * dipPerMm,
    height = (landscape ? 210 : 297) * dipPerMm;
  const inset = 10 * dipPerMm;
  const scale = Math.min((width - 2 * inset) / p.width, (height - 2 * inset) / p.height);
  return { width, height, scale, left: (width - p.width * scale) / 2, top: (height - p.height * scale) / 2 };
}

async function sourceRaster(source, index, image) {
  let canvas = await source.render(index);
  // Image workspaces own their display canvas. Downsample a copy for print when needed.
  const factor = Math.min(
    1,
    4096 / Math.max(canvas.width, canvas.height),
    Math.sqrt(maxPagePixels / (canvas.width * canvas.height))
  );
  let temporary = !image;
  if (image && factor < 1) {
    const copy = document.createElement("canvas");
    copy.width = Math.max(1, Math.round(canvas.width * factor));
    copy.height = Math.max(1, Math.round(canvas.height * factor));
    copy.getContext("2d").drawImage(canvas, 0, 0, copy.width, copy.height);
    canvas = copy;
    temporary = true;
  }
  try {
    const encoded = await new Promise((resolve) => canvas.toBlob(resolve, "image/png"));
    if (!encoded) throw new EditorError("printFailed");
    return encoded;
  } finally {
    if (temporary) canvas.width = canvas.height = 1;
  }
}

export async function printReview(doc, source, strokeShape, t, selectedPage = null) {
  if (selectedPage === null && !canPrintAll(doc)) throw new EditorError("printLimit");
  const indices = selectedPage === null ? doc.pages.map((_, i) => i) : [selectedPage];
  if (indices.some((i) => !Number.isInteger(i) || i < 0 || i >= doc.pages.length))
    throw new EditorError("printFailed");
  disposePrevious?.();
  const frame = document.createElement("iframe");
  frame.className = "print-frame";
  frame.setAttribute("aria-hidden", "true");
  document.body.append(frame);
  const target = frame.contentDocument,
    urls = [];
  const dispose = () => {
    frame.remove();
    for (const url of urls.splice(0)) URL.revokeObjectURL(url);
    if (disposePrevious === dispose) disposePrevious = null;
  };
  disposePrevious = dispose;
  const node = (tag, parent) => parent.appendChild(target.createElement(tag));
  const svg = (tag, parent, attrs = {}) => {
    const element = parent.appendChild(target.createElementNS("http://www.w3.org/2000/svg", tag));
    for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, value);
    return element;
  };
  const sizes = [],
    notes = [],
    images = [];
  try {
    for (const index of indices) {
      const p = doc.pages[index],
        g = layout(p, doc.kind === "Image");
      if (Math.max(g.width, g.height) > 19200) throw new EditorError("printBounds");
      const name = "paper" + index;
      sizes.push("@page " + name + " {size:" + mm(g.width) + " " + mm(g.height) + ";margin:0}");
      const page = node("section", target.body);
      Object.assign(page.style, { page: name, width: mm(g.width), height: mm(g.height) });
      const surface = node("div", page);
      surface.className = "surface";
      Object.assign(surface.style, {
        left: mm(g.left),
        top: mm(g.top),
        width: mm(p.width * g.scale),
        height: mm(p.height * g.scale)
      });
      if (doc.kind !== "Notebook") {
        const url = URL.createObjectURL(await sourceRaster(source, index, doc.kind === "Image"));
        urls.push(url);
        const original = node("img", surface);
        original.className = "source";
        original.alt = "";
        original.src = url;
        await original.decode();
      }
      const paper = svg("svg", surface, { viewBox: `0 0 ${p.width} ${p.height}` });
      if (doc.kind === "Notebook" && p.pattern !== "Blank") {
        const margin = p.marginMm * dipPerMm,
          step = p.spacingMm * dipPerMm;
        let lines = "";
        for (let y = margin; y <= p.height - margin + 0.001; y += step)
          lines += `M${margin} ${y}H${p.width - margin}`;
        if (p.pattern === "Squared")
          for (let x = margin; x <= p.width - margin + 0.001; x += step)
            lines += `M${x} ${margin}V${p.height - margin}`;
        svg("path", paper, { d: lines, fill: "none", stroke: "#d7d7dd", "stroke-width": ".65" });
      }
      for (const pic of p.images) {
        const href = "data:image/png;base64," + pic.pngData;
        // Finish decoding placed pictures before opening the print dialog.
        const ready = new Image();
        ready.src = href;
        images.push(ready.decode());
        svg("image", paper, { x: pic.x, y: pic.y, width: pic.width, height: pic.height, href });
      }
      for (const stroke of p.ink)
        paper.append(target.importNode(strokeShape(stroke).firstElementChild, true));
      let number = 0;
      for (const item of p.items) {
        const box = node("div", surface);
        Object.assign(box.style, { left: mm(item.x * g.scale), top: mm(item.y * g.scale) });
        if (item.isNote) {
          box.className = "pin";
          box.textContent = ++number;
          box.style.transform = `translate(${(p.width - item.x) * g.scale < 8 * dipPerMm ? "-100%" : "0"},${(p.height - item.y) * g.scale < 7 * dipPerMm ? "-100%" : "0"})`;
          notes.push({ item, page: index + 1, number });
        } else {
          Object.assign(box.style, {
            width: mm(item.width * g.scale),
            height: mm(item.height * g.scale),
            fontSize: mm(item.fontSize * g.scale),
            color: item.color
          });
          box.textContent = item.text;
        }
      }
    }
    if (notes.length) {
      const aside = node("article", target.body);
      const appendix =
        doc.kind === "Notebook" ? mm(doc.pages[0].width) + " " + mm(doc.pages[0].height) : "A4 portrait";
      sizes.push("@page notes {size:" + appendix + ";margin:15mm}");
      aside.style.page = "notes";
      node("h1", aside).textContent = t("printNotes");
      const filename = node("p", aside);
      filename.className = "identity";
      filename.textContent = doc.title || source.name;
      for (const { item, page, number } of notes) {
        const label = node("h2", aside);
        const reference = node("span", label);
        reference.textContent = t("page") + " " + page + " · " + t("nativeNote") + " " + number;
        if (item.title) node("strong", label).textContent = item.title;
        node("p", aside).textContent = item.text;
      }
    }
    node("style", target.head).textContent = `@page{margin:15mm} ${sizes.join("\n")}
      *{box-sizing:border-box}html,body{margin:0;color:#272231;font:16px/1.2 system-ui;print-color-adjust:exact;-webkit-print-color-adjust:exact}
      section{position:relative;overflow:hidden;break-after:page}section:last-child{break-after:auto}
      .surface,.surface div{position:absolute;overflow:hidden}.surface div{white-space:pre-wrap;overflow-wrap:anywhere}
      .source,svg{position:absolute;inset:0;width:100%;height:100%;display:block}
      .pin{padding:1mm 2mm;border-radius:1.5mm;background:#ffe1a4;color:#4d3515;font-size:3mm}
      article{max-width:165mm}h1{font-size:20pt;font-weight:650;letter-spacing:-.025em;margin:0}
      .identity{margin:2mm 0 7mm;color:#5c5865}h2{border-top:.2mm solid #dbd7e1;padding-top:4mm;margin:5mm 0 1.5mm;break-after:avoid}
      h2 span{display:block;font-size:9pt;font-weight:550;color:#534b66}h2 strong{display:block;font-size:12pt;margin-top:1mm}
      p{white-space:pre-wrap;overflow-wrap:anywhere;font-size:10.5pt;line-height:1.45;margin:0}`;
    target.documentElement.lang = document.documentElement.lang;
    target.title = doc.title || source.name;
    await Promise.all([target.fonts.ready, ...images]);
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    frame.contentWindow.addEventListener("afterprint", () => setTimeout(dispose, 300), { once: true });
    frame.contentWindow.print();
  } catch (error) {
    dispose();
    throw error;
  }
}
