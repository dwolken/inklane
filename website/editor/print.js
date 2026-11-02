// Print uses CSS physical units, independent of the current viewport or device pixel ratio.
export async function printNotebook(doc, strokeShape, noteLabel) {
  const frame = document.createElement("iframe");
  frame.className = "print-frame";
  frame.setAttribute("aria-hidden", "true");
  document.body.append(frame);
  const target = frame.contentDocument;
  const style = target.createElement("style");
  const sizes = [];
  const mm = (dip) => ((dip * 25.4) / 96).toFixed(5) + "mm";
  const node = (tag, parent) => parent.appendChild(target.createElement(tag));
  const svg = (tag, parent, attributes = {}) => {
    const element = parent.appendChild(target.createElementNS("http://www.w3.org/2000/svg", tag));
    for (const [name, val] of Object.entries(attributes)) element.setAttribute(name, val);
    return element;
  };
  try {
    for (const [index, p] of doc.pages.entries()) {
      const name = "paper" + index;
      sizes.push("@page " + name + " {size:" + mm(p.width) + " " + mm(p.height) + ";margin:0}");
      const page = node("section", target.body);
      Object.assign(page.style, { page: name, width: mm(p.width), height: mm(p.height) });
      const paper = svg("svg", page, { viewBox: `0 0 ${p.width} ${p.height}` });
      const margin = (p.marginMm * 96) / 25.4,
        step = (p.spacingMm * 96) / 25.4;
      if (p.pattern !== "Blank") {
        let lines = "";
        for (let y = margin; y <= p.height - margin + 0.001; y += step)
          lines += `M${margin} ${y}H${p.width - margin}`;
        if (p.pattern === "Squared")
          for (let x = margin; x <= p.width - margin + 0.001; x += step)
            lines += `M${x} ${margin}V${p.height - margin}`;
        svg("path", paper, { d: lines, fill: "none", stroke: "#d7d7dd", "stroke-width": ".65" });
      }
      for (const pic of p.images)
        svg("image", paper, {
          x: pic.x,
          y: pic.y,
          width: pic.width,
          height: pic.height,
          href: "data:image/png;base64," + pic.pngData
        });
      for (const stroke of p.ink)
        paper.append(target.importNode(strokeShape(stroke).firstElementChild, true));
      let number = 0;
      for (const item of p.items) {
        const box = node("div", page);
        Object.assign(box.style, { left: mm(item.x), top: mm(item.y) });
        if (item.isNote) {
          box.className = "pin";
          box.textContent = ++number;
        } else {
          Object.assign(box.style, {
            width: mm(item.width),
            height: mm(item.height),
            fontSize: mm(item.fontSize),
            color: item.color
          });
          box.textContent = item.text;
        }
      }
    }
    const withNotes = doc.pages.flatMap((p, index) =>
      p.items.filter((o) => o.isNote).map((note, i) => ({ note, page: index + 1, number: i + 1 }))
    );
    if (withNotes.length) {
      const aside = node("article", target.body);
      sizes.push(
        "@page notes {size:" + mm(doc.pages[0].width) + " " + mm(doc.pages[0].height) + ";margin:15mm}"
      );
      aside.style.page = "notes";
      node("h1", aside).textContent = noteLabel;
      for (const { note, page, number } of withNotes) {
        node("h2", aside).textContent = `${page} · ${number}` + (note.title ? "   " + note.title : "");
        node("p", aside).textContent = note.text;
      }
    }
    style.textContent = `@page{margin:15mm} ${sizes.join("\n")}
    *{box-sizing:border-box}html,body{margin:0;color:#272231;font:16px/1.2 system-ui;print-color-adjust:exact;-webkit-print-color-adjust:exact}
    section{position:relative;overflow:hidden;break-after:page}section:last-child{break-after:auto}
    svg{position:absolute;inset:0;width:100%;height:100%}section div{position:absolute;white-space:pre-wrap;overflow-wrap:anywhere;overflow:hidden}
    .pin{padding:1mm 2mm;border-radius:1.5mm;background:#ffe1a4;color:#4d3515;font-size:3mm}
    h1{font-size:18pt}h2{font-size:11pt;break-after:avoid}p{white-space:pre-wrap;overflow-wrap:anywhere;font-size:10pt}`;
    target.head.append(style);
    target.title = doc.title;
    await target.fonts.ready;
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    frame.contentWindow.addEventListener("afterprint", () => setTimeout(() => frame.remove(), 300), {
      once: true
    });
    frame.contentWindow.print();
  } catch (error) {
    frame.remove();
    throw error;
  }
}
