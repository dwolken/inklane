export const JSON_LIMIT = 32 * 1024 * 1024;
const SOURCE_LIMIT = 64 * 1024 * 1024;
const PIXEL_LIMIT = 32_000_000;
export class EditorError extends Error {}

function check(ok, reason = "invalid") {
  if (!ok) throw new EditorError(reason);
}
function range(v, min, max) {
  check(Number.isFinite(v) && v >= min && v <= max);
}
function title(v) {
  check(typeof v === "string" && v.length <= 200 && !/[\u0000-\u001f\u007f-\u009f]/.test(v));
}
function color(v) {
  check(typeof v === "string" && /^#[0-9a-f]{6}$/i.test(v));
}
function defaults(obj, values) {
  check(obj && typeof obj === "object" && !Array.isArray(obj));
  for (const [key, value] of Object.entries(values)) if (!Object.hasOwn(obj, key)) obj[key] = value;
}
export async function digest(bytes) {
  check(globalThis.crypto?.subtle, "secure");
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)), (n) =>
    n.toString(16).padStart(2, "0")
  )
    .join("")
    .toUpperCase();
}

function pngSize(bytes) {
  const sig = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82];
  if (bytes.byteLength < 33 || !sig.every((v, i) => bytes[i] === v)) return null;
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  return [view.getUint32(16), view.getUint32(20)];
}
function jpegSize(bytes) {
  if (bytes[0] !== 255 || bytes[1] !== 216) return null;
  for (let i = 2; i + 8 < bytes.length; ) {
    if (bytes[i++] !== 255) return null;
    while (bytes[i] === 255) i++;
    const marker = bytes[i++];
    if (marker === 217 || marker === 218) return null;
    const len = (bytes[i] << 8) + bytes[i + 1];
    if (len < 2 || i + len > bytes.length) return null;
    if (marker >= 192 && marker <= 207 && ![196, 200, 204].includes(marker))
      return [(bytes[i + 5] << 8) + bytes[i + 6], (bytes[i + 3] << 8) + bytes[i + 4]];
    i += len;
  }
  return null;
}
export async function openImage(file) {
  check(file.size <= SOURCE_LIMIT, "size");
  const bytes = await file.arrayBuffer();
  const view = new Uint8Array(bytes);
  const png = pngSize(view);
  const dimensions = png || jpegSize(view);
  check(dimensions && dimensions.every((v) => v > 0), "decode");
  check(dimensions.every((v) => v <= 32768) && dimensions[0] * dimensions[1] <= PIXEL_LIMIT, "size");
  const hash = await digest(bytes);
  let bitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: "from-image" });
  } catch {
    throw new EditorError("decode");
  }
  return {
    name: file.name,
    hash,
    bitmap,
    width: bitmap.width,
    height: bitmap.height,
    mediaType: png ? "image/png" : "image/jpeg"
  };
}
export function newDocument(image) {
  return {
    schemaVersion: 1,
    kind: "Image",
    title: "",
    sourceImagePath: image.name,
    sourceImageSha256: image.hash,
    pages: [
      {
        width: image.width,
        height: image.height,
        pattern: "Blank",
        spacingMm: 5,
        marginMm: 15,
        ink: [],
        items: [],
        images: []
      }
    ]
  };
}

// Preserve optional and unrecognized fields on known versions. Always bind the editable
// layer to the original file bytes and oriented dimensions before replacing a workspace.
export async function readEnvelope(file) {
  check(file.size <= JSON_LIMIT, "size");
  const bytes = await file.arrayBuffer();
  let doc;
  try {
    doc = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  } catch {
    throw new EditorError("invalid");
  }
  check(doc?.format !== "inklane.feedback", "feedbackOnly");
  const pdf = doc?.kind === "Pdf";
  check(
    pdf ? doc.schemaVersion === 3 : [1, 2].includes(doc?.schemaVersion) && doc.kind === "Image",
    "version"
  );
  const identity = pdf ? doc.sourcePdfSha256 : doc.sourceImageSha256;
  check(typeof identity === "string" && /^[0-9a-f]{64}$/i.test(identity));
  return { doc, fingerprint: await digest(bytes) };
}

export async function readSidecar(file, image, envelope = null) {
  const { doc, fingerprint } = envelope ?? (await readEnvelope(file));
  const pdf = doc.kind === "Pdf";
  const identity = pdf ? doc.sourcePdfSha256 : doc.sourceImageSha256;
  check(identity.toUpperCase() === image.hash, "identity");
  const dimensions = pdf ? image.pages : [image];
  check(dimensions && doc.pages?.length === dimensions.length, "identity");
  await validateDocument(doc, dimensions);
  if (pdf) doc.sourcePdfName = image.name;
  else doc.sourceImagePath = image.name;
  return { doc, fingerprint };
}

export async function validateDocument(doc, dimensions) {
  check(Array.isArray(doc.pages) && doc.pages.length >= 1 && doc.pages.length <= 1000, "size");
  defaults(doc, { title: "" });
  title(doc.title);
  const totals = { points: 0, pixels: 0, bytes: 0, ids: new Set() };
  for (const [index, page] of doc.pages.entries()) {
    range(page?.width, 1, 100000);
    range(page?.height, 1, 100000);
    if (dimensions) {
      const original = dimensions[index];
      check(
        original &&
          Math.abs(page.width - original.width) < 0.01 &&
          Math.abs(page.height - original.height) < 0.01,
        "identity"
      );
    }
    if (doc.kind === "Pdf") check(page.pattern === "Blank" && page.marginMm === 0);
    if (doc.kind === "Notebook") range(page.marginMm, 0, (Math.min(page.width, page.height) * 25.4) / 192);
    await validatePage(page, totals);
  }
}
async function validatePage(p, totals) {
  defaults(p, { images: [], pattern: "Blank", spacingMm: 5, marginMm: 15 });
  check(["Blank", "Ruled", "Squared"].includes(p.pattern));
  range(p.spacingMm, 1, 100);
  range(p.marginMm, 0, 1000);
  check([p.ink, p.items, p.images].every(Array.isArray));
  check(p.ink.length <= 10000 && p.items.length <= 2000 && p.images.length <= 100, "size");
  const ids = totals.ids;
  function id(obj) {
    check(typeof obj?.id === "string" && obj.id.trim() && obj.id.length <= 128 && !ids.has(obj.id));
    ids.add(obj.id);
  }
  function point(obj) {
    check(obj && typeof obj === "object");
    range(obj.x, -100000, 200000);
    range(obj.y, -100000, 200000);
  }
  function rect(obj) {
    point(obj);
    range(obj.width, 1, 100000);
    range(obj.height, 1, 100000);
  }

  for (const ink of p.ink) {
    id(ink);
    color(ink.color);
    range(ink.width, 0.1, 1000);
    check(Array.isArray(ink.points));
    totals.points += ink.points.length;
    check(totals.points <= 500000, "size");
    for (const sample of ink.points) {
      point(sample);
      range(sample.pressure, 0, 1);
    }
  }
  for (const item of p.items) {
    id(item);
    rect(item);
    defaults(item, { title: "" });
    title(item.title);
    color(item.color);
    range(item.fontSize, 1, 1000);
    check(typeof item.isNote === "boolean" && typeof item.isExpanded === "boolean");
    check(typeof item.text === "string" && item.text.length <= 1000000);
  }

  for (const pic of p.images) {
    id(pic);
    rect(pic);
    check(typeof pic.pngData === "string" && pic.pngData.length <= 22_369_624);
    let png;
    try {
      png = Uint8Array.from(atob(pic.pngData), (ch) => ch.charCodeAt(0));
    } catch {
      throw new EditorError("invalid");
    }
    const tail = [0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130];
    check(tail.every((byte, i) => png[png.length - 12 + i] === byte));
    totals.bytes += png.length;
    check(totals.bytes <= 24 * 1024 * 1024, "size");
    const dim = pngSize(png);
    check(
      dim &&
        dim.every((v) => v > 0 && v <= 32768) &&
        png.length <= 16 * 1024 * 1024 &&
        dim[0] * dim[1] <= 24000000
    );
    totals.pixels += dim[0] * dim[1];
    check(totals.pixels <= 32_000_000, "size");
    try {
      const decoded = await createImageBitmap(new Blob([png], { type: "image/png" }));
      decoded.close();
    } catch {
      throw new EditorError("invalid");
    }
  }
}
export function serialize(document) {
  let points = 0,
    pixels = 0,
    bytes = 0;
  let extended = Boolean(document.title);
  for (const page of document.pages) {
    check(page.ink.length <= 10000 && page.items.length <= 2000 && page.images.length <= 100, "size");
    for (const stroke of page.ink) points += stroke.points.length;
    for (const picture of page.images) {
      const header = Uint8Array.from(atob(picture.pngData.slice(0, 48)), (ch) => ch.charCodeAt(0));
      const dimensions = pngSize(header);
      check(dimensions, "invalid");
      pixels += dimensions[0] * dimensions[1];
      bytes += Math.floor((picture.pngData.length * 3) / 4);
    }
    if (page.images.length || page.items.some((item) => item.title)) extended = true;
  }
  check(points <= 500000 && pixels <= 32_000_000 && bytes <= 24 * 1024 * 1024, "size");
  const schemaVersion = document.kind === "Pdf" ? 3 : extended ? 2 : document.schemaVersion;
  const json = JSON.stringify({ ...document, schemaVersion }, null, 2);
  const blob = new Blob([json], { type: "application/json" });
  check(blob.size <= JSON_LIMIT, "size");
  return blob;
}
export function newItem(isNote, x, y, page) {
  return {
    id: crypto.randomUUID(),
    title: "",
    text: "",
    x,
    y,
    width: Math.min(isNote ? 280 : 240, page.width),
    height: isNote ? 140 : 76,
    fontSize: 20,
    color: "#30293E",
    isNote,
    isExpanded: isNote
  };
}
export function moved(object, dx, dy, page) {
  const coords = object.points ?? [object];
  let left = Infinity,
    right = -Infinity,
    top = Infinity,
    bottom = -Infinity;
  for (const p of coords) {
    left = Math.min(left, p.x);
    right = Math.max(right, p.x);
    top = Math.min(top, p.y);
    bottom = Math.max(bottom, p.y);
  }
  dx = Math.max(-left, Math.min(dx, page.width - 1 - right));
  dy = Math.max(-top, Math.min(dy, page.height - 1 - bottom));
  return object.points
    ? { ...object, points: coords.map((p) => ({ ...p, x: p.x + dx, y: p.y + dy })) }
    : { ...object, x: object.x + dx, y: object.y + dy };
}

export async function feedbackFile(doc, image) {
  const pages = [];
  for (const [index, p] of doc.pages.entries()) {
    const annotations = [];
    for (const picture of p.images) {
      const { id, x, y, width, height } = picture;
      const bytes = Uint8Array.from(atob(picture.pngData), (ch) => ch.charCodeAt(0));
      annotations.push({
        id,
        type: "picture",
        x,
        y,
        width,
        height,
        sha256: (await digest(bytes)).toLowerCase()
      });
    }
    for (const stroke of p.ink) {
      annotations.push({
        id: stroke.id,
        type: "ink",
        color: stroke.color,
        width: stroke.width,
        points: stroke.points.map(({ x, y, pressure }) => ({ x, y, pressure }))
      });
    }
    for (const item of p.items) {
      const { id, x, y, width, height, text, color, fontSize } = item;
      const record = { id, type: item.isNote ? "note" : "text", x, y, width, height, text, color, fontSize };
      if (item.isNote) record.title = item.title;
      annotations.push(record);
    }
    pages.push({
      number: index + 1,
      width: p.width,
      height: p.height,
      unit: doc.kind === "Image" ? "image-pixel" : "dip-96",
      annotations
    });
  }
  const hash = image.hash;
  return new Blob(
    [
      JSON.stringify(
        {
          format: "inklane.feedback",
          version: 1,
          source: { name: image.name, mediaType: image.mediaType, sha256: hash.toLowerCase() },
          coordinateSystem: "top-left",
          pages
        },
        null,
        2
      )
    ],
    { type: "application/json" }
  );
}
