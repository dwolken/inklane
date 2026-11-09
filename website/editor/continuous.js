import { backdropCanvas } from "./documents.js";
import { renderText, textStyle } from "./rich-text.js";
import { shapeVisual } from "./shapes.js";
import { t } from "./language.js";

const ns = "http://www.w3.org/2000/svg";
// Keep the live editing surface on the active page. Nearby pages have bounded,
// disposable previews; all annotations continue to belong to their own page.
export function continuousPages({
  mat,
  sheet,
  viewport,
  activate,
  strokeShape,
  error,
}) {
  let state = null,
    slots = [],
    observer = null,
    generation = 0,
    queue = Promise.resolve(),
    lastIndex = -1;
  const cached = new Map(),
    pending = new Set();
  function forget(index) {
    const preview = cached.get(index);
    if (!preview) return;
    for (const canvas of preview.querySelectorAll("canvas"))
      canvas.width = canvas.height = 1;
    preview.remove();
    cached.delete(index);
  }
  function reset() {
    generation++;
    observer?.disconnect();
    observer = null;
    pending.clear();
    for (const key of cached.keys()) forget(key);
    mat.append(sheet);
    for (const slot of slots) slot.remove();
    slots = [];
    lastIndex = -1;
    state = null;
    mat.classList.remove("page-stack");
  }
  function near(index) {
    const rect = slots[index]?.getBoundingClientRect(),
      view = viewport.getBoundingClientRect();
    return (
      rect && rect.bottom >= view.top - 600 && rect.top <= view.bottom + 600
    );
  }
  function load(index) {
    if (
      !state ||
      index === state.index ||
      pending.has(index) ||
      cached.has(index) ||
      !near(index)
    )
      return;
    pending.add(index);
    const ticket = generation,
      doc = state.doc,
      source = state.source;
    queue = queue
      .catch(() => {})
      .then(async () => {
        if (
          ticket !== generation ||
          !state ||
          index === state.index ||
          !near(index)
        ) {
          pending.delete(index);
          return;
        }
        let canvas;
        try {
          canvas = await backdropCanvas(doc, source, index);
          if (ticket !== generation || !state || index === state.index) {
            canvas.width = canvas.height = 1;
            return;
          }
          const p = doc.pages[index],
            preview = document.createElement("div");
          preview.className = "page-preview";
          preview.inert = true;
          preview.setAttribute("aria-hidden", "true");
          Object.assign(preview.style, {
            width: p.width + "px",
            height: p.height + "px",
            transform: `scale(${state.zoom})`,
          });
          canvas.className = "preview-source";
          preview.append(canvas);
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
            svg.setAttribute("viewBox", `0 0 ${p.width} ${p.height}`);
            svg.style.zIndex = 1000001 + (o.layer || 0);
            svg.append(visual);
            preview.append(svg);
          };
          for (const image of p.images) {
            const img = document.createElement("img");
            img.src = "data:image/png;base64," + image.pngData;
            position(img, image);
            preview.append(img);
          }
          for (const ink of p.ink)
            vector(strokeShape(ink).firstElementChild.cloneNode(true), ink);
          for (const shape of p.shapes || [])
            vector(shapeVisual(shape, p), shape);
          let number = 0;
          for (const item of p.items) {
            const el = document.createElement("div");
            position(el, item);
            preview.append(el);
            if (item.isNote) {
              el.className = "preview-pin";
              el.textContent = ++number;
              el.style.width = el.style.height = "auto";
            } else {
              el.className = "preview-text";
              textStyle(el, item);
              renderText(el, item);
            }
          }
          slots[index].append(preview);
          cached.set(index, preview);
          while (cached.size > 6) forget(cached.keys().next().value);
        } catch (reason) {
          if (ticket === generation) error(reason);
        } finally {
          pending.delete(index);
        }
      });
  }
  function sync(next) {
    if (!next.enabled || next.doc?.kind === "Image" || !next.doc) {
      if (state) reset();
      return;
    }
    const rebuild =
      !state || state.doc !== next.doc || state.pages !== next.doc.pages;
    if (rebuild) {
      reset();
      state = { ...next, pages: next.doc.pages };
      mat.classList.add("page-stack");
      slots = next.doc.pages.map((p, index) => {
        const slot = document.createElement("section");
        slot.className = "page-slot";
        slot.dataset.page = String(index);
        slot.tabIndex = 0;
        slot.setAttribute("role", "button");
        slot.setAttribute(
          "aria-label",
          t("page") + " " + (index + 1) + " · " + t("activatePage"),
        );
        const number = document.createElement("span");
        number.className = "page-label";
        number.textContent = t("page") + " " + (index + 1);
        slot.append(number);
        slot.onclick = (event) => {
          if (!sheet.contains(event.target)) activate(index, false);
        };
        slot.onkeydown = (event) => {
          if (event.target === slot && ["Enter", " "].includes(event.key)) {
            event.preventDefault();
            activate(index, false);
          }
        };
        mat.append(slot);
        return slot;
      });
      observer = new IntersectionObserver(
        (entries) => {
          for (const entry of entries)
            if (entry.isIntersecting) load(Number(entry.target.dataset.page));
        },
        { root: viewport, rootMargin: "600px" },
      );
      for (const slot of slots) observer.observe(slot);
    }
    const old = lastIndex;
    state = { ...next, pages: next.doc.pages };
    lastIndex = next.index;
    for (const [index, slot] of slots.entries()) {
      slot.style.width = next.doc.pages[index].width * next.zoom + "px";
      slot.style.height = next.doc.pages[index].height * next.zoom + "px";
      slot.classList.toggle("active-page", index === next.index);
      slot.tabIndex = index === next.index ? -1 : 0;
      slot.setAttribute("role", index === next.index ? "group" : "button");
    }
    if (old !== next.index) {
      forget(next.index);
      slots[next.index].append(sheet);
      if (old >= 0) {
        forget(old);
        load(old);
      }
    } else if (sheet.parentElement !== slots[next.index])
      slots[next.index].append(sheet);
    for (const preview of cached.values())
      preview.style.transform = `scale(${next.zoom})`;
    for (
      let i = Math.max(0, next.index - 1);
      i <= Math.min(slots.length - 1, next.index + 1);
      i++
    )
      load(i);
  }
  return {
    sync,
    reset,
    scrollTo(index) {
      slots[index]?.scrollIntoView({ block: "start", inline: "nearest" });
    },
    get enabled() {
      return Boolean(state);
    },
  };
}
