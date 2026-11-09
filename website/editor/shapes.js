const ns = "http://www.w3.org/2000/svg";
export function shapeEndpoints(shape, page) {
  let a = {
    x: shape.x + (shape.flipX ? shape.width : 0),
    y: shape.y + (shape.flipY ? shape.height : 0),
  };
  let b = {
    x: shape.x + (shape.flipX ? 0 : shape.width),
    y: shape.y + (shape.flipY ? 0 : shape.height),
  };
  const nodes = [...(page.shapes || []), ...page.items, ...page.images];
  const start = nodes.find((o) => o.id === shape.startId),
    end = nodes.find((o) => o.id === shape.endId);
  if (start)
    a = { x: start.x + start.width / 2, y: start.y + start.height / 2 };
  if (end) b = { x: end.x + end.width / 2, y: end.y + end.height / 2 };
  const edge = (node, origin, other) => {
    const dx = other.x - origin.x,
      dy = other.y - origin.y;
    const factor = Math.min(
      node.width / 2 / Math.max(0.001, Math.abs(dx)),
      node.height / 2 / Math.max(0.001, Math.abs(dy)),
    );
    return { x: origin.x + dx * factor, y: origin.y + dy * factor };
  };
  const centreA = a,
    centreB = b;
  if (start) a = edge(start, centreA, centreB);
  if (end) b = edge(end, centreB, centreA);
  return [a, b];
}
export function shapeVisual(shape, page) {
  const group = document.createElementNS(ns, "g");
  group.dataset.id = shape.id;
  const add = (tag, attrs) => {
    const n = document.createElementNS(ns, tag);
    for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
    group.append(n);
    return n;
  };
  const { x, y, width: w, height: h } = shape;
  const style = {
    fill: shape.fillColor || "none",
    stroke: shape.color,
    "stroke-width": shape.strokeWidth,
    "stroke-linejoin": "round",
    "stroke-linecap": "round",
  };
  if (shape.kind === "Ellipse")
    add("ellipse", {
      cx: x + w / 2,
      cy: y + h / 2,
      rx: w / 2,
      ry: h / 2,
      ...style,
    });
  else if (shape.kind === "Diamond")
    add("polygon", {
      points: `${x + w / 2},${y} ${x + w},${y + h / 2} ${x + w / 2},${y + h} ${x},${y + h / 2}`,
      ...style,
    });
  else if (["Line", "Arrow"].includes(shape.kind)) {
    const [a, b] = shapeEndpoints(shape, page);
    add("path", { d: `M${a.x} ${a.y}L${b.x} ${b.y}`, ...style, fill: "none" });
    add("path", {
      d: `M${a.x} ${a.y}L${b.x} ${b.y}`,
      stroke: "transparent",
      "stroke-width": Math.max(14, shape.strokeWidth),
      "pointer-events": "stroke",
      fill: "none",
    });
    if (shape.kind === "Arrow") {
      const angle = Math.atan2(b.y - a.y, b.x - a.x),
        size = Math.max(10, shape.strokeWidth * 4);
      add("path", {
        d: `M${b.x - size * Math.cos(angle - 0.45)} ${b.y - size * Math.sin(angle - 0.45)}L${b.x} ${b.y}L${b.x - size * Math.cos(angle + 0.45)} ${b.y - size * Math.sin(angle + 0.45)}`,
        ...style,
        fill: "none",
      });
    }
  } else add("rect", { x, y, width: w, height: h, ...style });
  if (shape.label) {
    const label = add("text", {
      x: x + w / 2,
      y: y + h / 2,
      "text-anchor": "middle",
      "dominant-baseline": "middle",
      fill: shape.color,
      stroke: "none",
      "font-size": 18,
    });
    label.textContent = shape.label;
  }
  group.setAttribute("opacity", shape.opacity ?? 1);
  if (shape.rotation)
    group.setAttribute(
      "transform",
      `rotate(${shape.rotation},${x + w / 2},${y + h / 2})`,
    );
  return group;
}
