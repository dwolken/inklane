import { t, translate } from "./language.js";

const palette = [
  "#202020",
  "#FFFFFF",
  "#6447B0",
  "#2563EB",
  "#0891B2",
  "#15803D",
  "#EAB308",
  "#F97316",
  "#DC2626",
  "#DB2777",
];
const stored = (key) => {
  try {
    return JSON.parse(localStorage.getItem(key) || "[]")
      .filter((v) => /^#[0-9a-f]{6}$/i.test(v))
      .slice(0, 16);
  } catch {
    return [];
  }
};
let recent = stored("inklane.recentColors"),
  saved = stored("inklane.savedColors");
let inkColor = "#6447B0",
  inkOpacity = 1,
  fillColor = null,
  shapeKind = "Rectangle";
const persist = (key, value) => {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    /* Color preferences are optional. */
  }
};

export function setupProperties(api) {
  const button = document.createElement("button");
  button.id = "properties";
  button.innerHTML =
    '<svg aria-hidden="true" viewBox="0 0 24 24"><path d="M3 6h18M3 12h18M3 18h18M8 3v6m8 0v6m-7 0v6"/></svg><span data-i="properties"></span>';
  document.querySelector("#tools").append(button);
  const panel = document.createElement("aside");
  panel.id = "inspector";
  panel.hidden = true;
  panel.setAttribute("aria-label", t("properties"));
  panel.innerHTML = `<div class="inspector-head"><strong data-i="properties"></strong><button id="close-properties" class="icon" data-label="done">×</button></div>
    <div class="property-row" id="shape-row"><label data-i="shape"></label><select id="shape-kind"><option value="Rectangle" data-i="rectangle"></option><option value="Ellipse" data-i="ellipse"></option><option value="Diamond" data-i="diamond"></option><option value="Line" data-i="line"></option><option value="Arrow" data-i="arrow"></option></select></div>
    <div class="property-section"><label data-i="color"></label><div class="property-row" id="color-target-row"><select id="color-target" data-label="colorTarget"><option value="stroke" data-i="strokeColor"></option><option value="fill" data-i="fill"></option></select></div><div class="property-row"><input id="object-color" type="color" data-label="color"><input id="color-hex" maxlength="7" pattern="#[0-9a-fA-F]{6}" data-label="hex" spellcheck="false"><button id="save-color" data-label="saveColor">＋</button></div><div id="palette" class="swatches"></div><small data-i="recentColors"></small><div id="recent-colors" class="swatches"></div><small data-i="savedColors"></small><div id="saved-colors" class="swatches"></div></div>
    <div class="property-row"><label for="object-opacity" data-i="opacity"></label><input id="object-opacity" type="range" min="0" max="100" step="1"><output id="opacity-value"></output></div>
    <div id="shape-properties"><div class="property-row"><label for="shape-fill" data-i="fill"></label><input id="shape-fill" type="color"><label class="check"><input id="no-fill" type="checkbox"><span data-i="transparent"></span></label></div><div class="property-row"><label for="shape-stroke" data-i="strokeWidth"></label><input id="shape-stroke" type="number" min="0.1" max="1000" step="0.5" value="2"></div><label class="property-section"><span data-i="shapeLabel"></span><input id="shape-label" maxlength="1000"></label><small data-i="connectorHint"></small></div>
    <div id="text-properties"><div class="property-row"><label for="font-family" data-i="font"></label><select id="font-family"><option>Segoe UI</option><option>Arial</option><option>Georgia</option><option>Courier New</option></select><input id="font-size" type="number" min="1" max="1000" data-label="fontSize"></div><div class="format-buttons"><button id="text-bold" data-label="bold"><b>B</b></button><button id="text-italic" data-label="italic"><i>I</i></button><button id="text-underline" data-label="underline"><u>U</u></button><select id="text-align" data-label="alignment"><option value="Left" data-i="alignLeft"></option><option value="Center" data-i="alignCenter"></option><option value="Right" data-i="alignRight"></option><option value="Justify" data-i="alignJustify"></option></select></div><label class="property-section"><span data-i="contentFormat"></span><select id="content-format"><option value="Plain" data-i="plainText"></option><option value="Markdown">Markdown · LaTeX</option></select></label><p class="property-help" data-i="markdownHelp"></p></div>
    <div id="geometry-properties"><div class="property-row"><label for="object-width" data-i="width"></label><input id="object-width" type="number" min="1" max="100000"><label for="object-height" data-i="height"></label><input id="object-height" type="number" min="1" max="100000"></div><div class="property-row"><label for="object-rotation" data-i="rotation"></label><input id="object-rotation" type="number" min="-360" max="360" step="1"></div></div>
    <div id="selection-actions" class="property-section"><div class="property-row"><button id="bring-front" data-i="bringFront"></button><button id="send-back" data-i="sendBack"></button></div><div class="property-row"><button id="copy-object" data-i="copy"></button><button id="paste-object" data-i="paste"></button></div><div class="property-row"><button id="group-objects" data-i="group"></button><button id="ungroup-objects" data-i="ungroup"></button></div><button id="save-element" data-i="saveElement"></button></div><button id="open-elements" data-i="elements"></button>`;
  document.querySelector("#work").append(panel);
  const $ = (id) => panel.querySelector("#" + id);
  button.onclick = () => {
    panel.hidden = !panel.hidden;
    button.setAttribute("aria-expanded", String(!panel.hidden));
    api.render();
  };
  $("close-properties").onclick = () => {
    panel.hidden = true;
    button.setAttribute("aria-expanded", "false");
  };
  const remember = (color) => {
    recent = [color, ...recent.filter((c) => c !== color)].slice(0, 10);
    persist("inklane.recentColors", recent);
  };
  const setColor = (value) => {
    if (!/^#[0-9a-f]{6}$/i.test(value)) {
      $("color-hex").setCustomValidity(t("validHex"));
      return;
    }
    $("color-hex").setCustomValidity("");
    const chosen = value.toUpperCase();
    remember(chosen);
    if ($("color-target").value === "fill") {
      fillColor = chosen;
      api.apply({ fillColor: chosen });
    } else {
      inkColor = chosen;
      document.querySelector("#ink-color").value = inkColor;
      api.apply({ color: inkColor });
    }
    update();
  };
  const swatches = (target, colors) => {
    target.replaceChildren();
    for (const color of colors) {
      const b = document.createElement("button");
      b.style.background = color;
      b.title = color;
      b.setAttribute("aria-label", color);
      b.onclick = () => setColor(color);
      target.append(b);
    }
  };
  $("object-color").onchange = (e) => setColor(e.target.value);
  $("color-hex").onchange = (e) =>
    setColor(
      e.target.value.startsWith("#") ? e.target.value : "#" + e.target.value,
    );
  document.querySelector("#ink-color").onchange = (e) => {
    inkColor = e.target.value;
    remember(inkColor);
    update();
  };
  $("save-color").onclick = () => {
    const color = $("object-color").value.toUpperCase();
    saved = [color, ...saved.filter((c) => c !== color)].slice(0, 16);
    persist("inklane.savedColors", saved);
    update();
  };
  $("saved-colors").oncontextmenu = (e) => {
    const color = e.target.title;
    if (saved.includes(color)) {
      e.preventDefault();
      saved = saved.filter((c) => c !== color);
      persist("inklane.savedColors", saved);
      update();
    }
  };
  $("object-opacity").oninput = (e) => {
    $("opacity-value").value = e.target.value + "%";
  };
  $("object-opacity").onchange = (e) => {
    inkOpacity = Number(e.target.value) / 100;
    api.apply({ opacity: inkOpacity });
  };
  $("shape-kind").onchange = (e) => {
    shapeKind = e.target.value;
    if (api.current()?.group === "shapes") api.apply({ kind: shapeKind });
  };
  $("shape-fill").onchange = (e) => {
    $("color-target").value = "fill";
    setColor(e.target.value);
  };
  $("color-target").onchange = update;
  $("no-fill").onchange = (e) => {
    fillColor = e.target.checked ? null : $("shape-fill").value;
    api.apply({ fillColor });
  };
  for (const [id, field] of [
    ["font-size", "fontSize"],
    ["object-width", "width"],
    ["object-height", "height"],
    ["object-rotation", "rotation"],
    ["shape-stroke", "strokeWidth"],
  ]) {
    $(id).onchange = (e) => {
      if (e.target.checkValidity())
        api.apply({ [field]: Number(e.target.value) });
      else e.target.reportValidity();
    };
  }
  for (const [id, field] of [
    ["font-family", "fontFamily"],
    ["text-align", "alignment"],
    ["content-format", "contentFormat"],
    ["shape-label", "label"],
  ])
    $(id).onchange = (e) => api.apply({ [field]: e.target.value });
  for (const prop of ["bold", "italic", "underline"])
    $("text-" + prop).onclick = () =>
      api.apply({ [prop]: !api.current()?.obj[prop] });
  $("bring-front").onclick = () => api.layer(true);
  $("send-back").onclick = () => api.layer(false);
  $("copy-object").onclick = () => api.copy();
  $("paste-object").onclick = () => api.paste();
  $("group-objects").onclick = () => api.group(true);
  $("ungroup-objects").onclick = () => api.group(false);
  $("save-element").onclick = () => api.saveElement();
  $("open-elements").onclick = () => api.elements();
  function update() {
    const sel = api.current(),
      o = sel?.obj,
      text = sel?.group === "items",
      shape = sel?.group === "shapes";
    button.hidden = !api.active();
    panel.hidden ||= !api.active();
    $("text-properties").hidden = !text;
    $("geometry-properties").hidden = !o || sel.group === "ink";
    $("shape-properties").hidden = !shape && api.tool() !== "shape";
    $("shape-row").hidden = !shape && api.tool() !== "shape";
    $("selection-actions").hidden = !o;
    $("color-target-row").hidden = !shape && api.tool() !== "shape";
    if ($("color-target-row").hidden) $("color-target").value = "stroke";
    const selectedColor =
      $("color-target").value === "fill"
        ? o?.fillColor || fillColor || "#EDE9F5"
        : o?.color || inkColor;
    $("object-color").value = selectedColor;
    $("color-hex").value = selectedColor;
    $("object-opacity").value = Math.round((o?.opacity ?? inkOpacity) * 100);
    $("opacity-value").value = $("object-opacity").value + "%";
    $("font-size").value = o?.fontSize || 20;
    $("font-family").value = o?.fontFamily || "Segoe UI";
    $("text-align").value = o?.alignment || "Left";
    $("content-format").value = o?.contentFormat || "Plain";
    for (const prop of ["bold", "italic", "underline"])
      $("text-" + prop).setAttribute(
        "aria-pressed",
        String(Boolean(o?.[prop])),
      );
    $("object-width").value = o?.width || 240;
    $("object-height").value = o?.height || 80;
    $("object-rotation").value = o?.rotation || 0;
    $("object-rotation").disabled = Boolean(o?.isNote);
    $("shape-kind").value = shape ? o.kind : shapeKind;
    $("shape-fill").value = o?.fillColor || fillColor || "#EDE9F5";
    $("no-fill").checked = !(shape ? o.fillColor : fillColor);
    $("shape-label").value = o?.label || "";
    $("shape-stroke").value = o?.strokeWidth || 2;
    swatches($("palette"), palette);
    swatches($("recent-colors"), recent);
    swatches($("saved-colors"), saved);
  }
  translate();
  update();
  return {
    update,
    open() {
      panel.hidden = false;
      update();
    },
    defaults: () => ({
      color: inkColor,
      opacity: inkOpacity,
      fillColor,
      kind: shapeKind,
      strokeWidth: Number($("shape-stroke").value) || 2,
    }),
  };
}
