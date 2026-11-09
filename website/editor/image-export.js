import { EditorError } from "./model.js";
import { renderText, textStyle } from "./rich-text.js";
import { shapeVisual } from "./shapes.js";
import { backdropCanvas } from "./documents.js";

let styles;
const ns = "http://www.w3.org/2000/svg";
async function embeddedStyles() {
  if (!styles)
    styles = (async () => {
      const rich = await (
        await fetch(new URL("rich-text.css", import.meta.url))
      ).text();
      let math = await (
        await fetch(new URL("rich-lib/katex.css", import.meta.url))
      ).text();
      const urls = [
        ...new Set(
          [...math.matchAll(/url\(([^)]+\.woff2)\)/g)].map((m) => m[1]),
        ),
      ];
      for (const url of urls) {
        const bytes = new Uint8Array(
          await (
            await fetch(new URL("rich-lib/" + url, import.meta.url))
          ).arrayBuffer(),
        );
        let binary = "";
        for (let i = 0; i < bytes.length; i += 8192)
          binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
        math = math.replaceAll(
          `url(${url})`,
          `url(data:font/woff2;base64,${btoa(binary)})`,
        );
      }
      return rich.slice(0, rich.indexOf("#inspector")) + math;
    })().catch((error) => {
      styles = null;
      throw error;
    });
  return styles;
}

// A page image includes the complete remarks in a gutter. The editable project
// remains separate, and the source never gets overwritten by this export.
export async function annotatedPng(doc, source, index, strokeShape, t) {
  const page = doc.pages[index],
    notes = page.items.filter((o) => o.isNote),
    gutter = notes.length ? 360 : 0;
  const root = document.createElement("div");
  root.setAttribute("xmlns", "http://www.w3.org/1999/xhtml");
  const width = page.width + (gutter ? gutter + 28 : 0);
  Object.assign(root.style, {
    position: "relative",
    width: width + "px",
    minHeight: page.height + "px",
    background: "white",
    color: "#292535",
    font: "16px/1.45 Segoe UI,system-ui,sans-serif",
    isolation: "isolate",
  });
  const style = document.createElement("style");
  style.textContent =
    (await embeddedStyles()) +
    `
    *{box-sizing:border-box}.export-surface{position:absolute;left:0;top:0;overflow:hidden;background:white}
    .export-vector{position:absolute;inset:0;width:100%;height:100%}.export-text{position:absolute;overflow:hidden;white-space:pre-wrap;overflow-wrap:anywhere;line-height:1.2}
    .export-text.rich-content{white-space:normal;line-height:1.45}.export-pin{position:absolute;background:#ffe1a4;color:#4d3515;padding:3px 8px;border-radius:6px;font-size:14px;font-weight:700}
    .export-notes{position:relative;margin-left:${page.width + 28}px;width:${gutter}px;padding:18px;background:#f7f5fa}
    .export-note{padding:0 0 20px}.export-note-title{font-size:15px;font-weight:700;margin:0 0 8px}.export-note-body{white-space:pre-wrap;overflow-wrap:anywhere}.export-note-body.rich-content{white-space:normal}
  `;
  root.append(style);
  const surface = document.createElement("div");
  surface.className = "export-surface";
  surface.style.width = page.width + "px";
  surface.style.height = page.height + "px";
  root.append(surface);
  const raster = await backdropCanvas(doc, source, index),
    original = document.createElement("img");
  original.src = raster.toDataURL("image/png");
  Object.assign(original.style, {
    position: "absolute",
    inset: "0",
    width: "100%",
    height: "100%",
  });
  surface.append(original);
  if (doc.kind !== "Image") raster.width = raster.height = 1;
  const position = (el, o) =>
    Object.assign(el.style, {
      position: "absolute",
      left: o.x + "px",
      top: o.y + "px",
      width: o.width + "px",
      height: o.height + "px",
      zIndex: 1000001 + (o.layer || 0),
      opacity: o.opacity ?? 1,
      transform: `rotate(${o.rotation || 0}deg)`,
    });
  const vector = (visual, o) => {
    const svg = document.createElementNS(ns, "svg");
    svg.classList.add("export-vector");
    svg.setAttribute("viewBox", `0 0 ${page.width} ${page.height}`);
    svg.style.zIndex = 1000001 + (o.layer || 0);
    svg.append(visual);
    surface.append(svg);
  };
  for (const item of page.images) {
    const img = document.createElement("img");
    img.src = "data:image/png;base64," + item.pngData;
    position(img, item);
    surface.append(img);
  }
  for (const ink of page.ink)
    vector(strokeShape(ink).firstElementChild.cloneNode(true), ink);
  for (const shape of page.shapes || [])
    vector(shapeVisual(shape, page), shape);
  for (const item of page.items) {
    const el = document.createElement("div");
    position(el, item);
    surface.append(el);
    if (item.isNote) {
      el.className = "export-pin";
      el.textContent = notes.indexOf(item) + 1;
      el.style.width = el.style.height = "auto";
      el.style.transform = `translate(${page.width - item.x < 28 ? "-100%" : "0"},${page.height - item.y < 28 ? "-100%" : "0"})`;
    } else {
      el.className = "export-text";
      textStyle(el, item);
      renderText(el, item);
    }
  }
  if (notes.length) {
    const aside = document.createElement("aside");
    aside.className = "export-notes";
    root.append(aside);
    for (const [i, item] of notes.entries()) {
      const article = document.createElement("article");
      article.className = "export-note";
      const title = document.createElement("h2");
      title.className = "export-note-title";
      title.textContent = i + 1 + " · " + (item.title || t("nativeNote"));
      const body = document.createElement("div");
      body.className = "export-note-body";
      textStyle(body, item);
      renderText(body, item);
      article.append(title, body);
      aside.append(article);
    }
  }
  const host = document.createElement("div");
  Object.assign(host.style, {
    position: "fixed",
    left: "-100000px",
    top: "0",
    pointerEvents: "none",
  });
  host.append(root);
  document.body.append(host);
  try {
    await Promise.all(
      [...root.querySelectorAll("img")].map((img) => img.decode()),
    );
    await document.fonts.ready;
    const height = Math.max(page.height, root.scrollHeight),
      scale = Math.min(
        2,
        8192 / Math.max(width, height),
        Math.sqrt(16_000_000 / (width * height)),
      );
    if (scale < 0.5 || !Number.isFinite(scale))
      throw new EditorError("imageTooTall");
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("width", Math.ceil(width * scale));
    svg.setAttribute("height", Math.ceil(height * scale));
    svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
    const content = document.createElementNS(ns, "foreignObject");
    content.setAttribute("width", width);
    content.setAttribute("height", height);
    content.append(root.cloneNode(true));
    svg.append(content);
    const image = new Image();
    image.src =
      "data:image/svg+xml;charset=utf-8," +
      encodeURIComponent(new XMLSerializer().serializeToString(svg));
    await image.decode();
    const canvas = document.createElement("canvas");
    canvas.width = Math.ceil(width * scale);
    canvas.height = Math.ceil(height * scale);
    canvas.getContext("2d").drawImage(image, 0, 0);
    const result = await new Promise((resolve) =>
      canvas.toBlob(resolve, "image/png"),
    );
    canvas.width = canvas.height = 1;
    if (!result) throw new EditorError("imageExportFailed");
    return result;
  } finally {
    host.remove();
  }
}
