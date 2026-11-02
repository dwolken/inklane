import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { createServer } from "node:http";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { basename, extname, resolve, sep } from "node:path";
import { chromium } from "playwright-core";

const dist = resolve(process.argv[2] ?? ".scratch/website/inklane"),
  output = resolve(process.argv[3] ?? ".scratch/browser-editor-qa/out");
const require = createRequire(import.meta.url);
const base = "/inklane/";
const types = { ".mjs": "application/javascript", ".wasm": "application/wasm",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript",
  ".css": "text/css",
  ".svg": "image/svg+xml"
};
const server = createServer(async (req, res) => {
  const path = new URL(req.url, "http://localhost").pathname;
  if (path === "/test-axe.js")
    return res
      .writeHead(200, { "Content-Type": "text/javascript" })
      .end(await readFile(require.resolve("axe-core/axe.min.js")));
  if (!path.startsWith(base)) return res.writeHead(404).end();
  const file = resolve(dist, path.slice(base.length) + (path.endsWith("/") ? "index.html" : ""));
  if (!file.startsWith(dist + sep)) return res.writeHead(403).end();
  try {
    const bytes = await readFile(file);
    res.writeHead(200, { "Content-Type": types[extname(file)] ?? "application/octet-stream" }).end(bytes);
  } catch {
    res.writeHead(404).end();
  }
});
await mkdir(output, { recursive: true });
await new Promise((done) => server.listen(0, "127.0.0.1", done));
const origin = "http://127.0.0.1:" + server.address().port;
const browser = await chromium.launch(
  process.env.BROWSER_PATH
    ? { executablePath: process.env.BROWSER_PATH }
    : { channel: process.platform === "win32" ? "msedge" : "chrome" }
);
const errors = [];
const context = await browser.newContext({ locale: "en-US", viewport: { width: 1200, height: 780 } });
const page = await context.newPage();
page.on("pageerror", (e) => errors.push(String(e)));
page.on("request", (req) => {
  if (!req.url().startsWith(origin + "/") && !req.url().startsWith("data:"))
    errors.push("External request: " + req.url());
});
const msg = (text) => page.getByRole("status").filter({ hasText: text }).waitFor();
async function open(files) {
  await page.locator("#files").setInputFiles(files);
  await page.waitForFunction(() => !document.querySelector("#open").disabled);
}
async function add(tool, x, y) {
  await page.locator('button[data-tool="' + tool + '"]').click();
  const box = await page.locator("#sheet").boundingBox();
  await page.mouse.click(box.x + box.width * x, box.y + box.height * y);
}
async function download(filename) {
  await page.locator("#more summary").click();
  const next = page.waitForEvent("download");
  await page.locator("#download").click();
  const file = await next;
  const path = resolve(output, filename ?? file.suggestedFilename());
  await file.saveAs(path);
  return { path, doc: JSON.parse(await readFile(path, "utf8")) };
}
try {
  await page.goto(origin + base + "editor/");
  await page.locator("#start").waitFor();
  const imageBytes = Buffer.from(
    await page.evaluate(() => {
      const c = document.createElement("canvas");
      c.width = 960;
      c.height = 640;
      const ctx = c.getContext("2d");
      ctx.fillStyle = "#f7f5ed";
      ctx.fillRect(0, 0, c.width, c.height);
      ctx.strokeStyle = "#79867b";
      ctx.lineWidth = 3;
      ctx.strokeRect(85, 140, 790, 380);
      ctx.strokeRect(115, 170, 130, 315);
      ctx.strokeRect(680, 170, 165, 315);
      ctx.fillStyle = "#c9d6bc";
      for (const [x, y, r] of [
        [365, 320, 80],
        [505, 305, 62],
        [500, 420, 49]
      ]) {
        ctx.beginPath();
        ctx.arc(x, y, r, 0, 7);
        ctx.fill();
        ctx.stroke();
      }
      ctx.fillStyle = "#37453f";
      ctx.font = "bold 32px sans-serif";
      ctx.fillText("Courtyard planting", 85, 88);
      ctx.font = "16px sans-serif";
      ctx.fillText("EAST BED     /     PLANTING STUDY", 85, 578);
      return c.toDataURL().split(",")[1];
    }),
    "base64"
  );
  const imagePath = resolve(output, "Planting plan.png");
  await writeFile(imagePath, imageBytes);
  await open(imagePath);
  await page.locator("#sheet").waitFor();
  assert.equal(await page.locator("#name").textContent(), basename(imagePath));
  await add("note", 0.5, 0.35);
  await page.locator("#note-title").fill("Canopy");
  await page.locator("#note-body").fill("Keep the shaded bench clear.\nMeasure the canopy in June.");
  await page.locator("#close-card").click();
  assert.equal(await page.locator("#card").isVisible(), false);
  await page.locator(".marker").click();
  assert.equal(await page.locator("#note-title").inputValue(), "Canopy");
  await page.screenshot({ path: resolve(output, "editor-desktop.png") });
  await page.locator("#close-card").click();
  await add("text", 0.28, 0.75);
  await page.locator(".placed-editor").fill("Proposed bench");
  await page.keyboard.press("Escape");
  const textInset = await page.locator(".placed").evaluate((element) => {
    const range = document.createRange();
    range.selectNodeContents(element);
    return range.getBoundingClientRect().top - element.getBoundingClientRect().top;
  });
  assert(
    Math.abs(textInset) < 6,
    "Placed text must start at the saved anchor rather than halfway down its editing box."
  );
  await page.locator("button[data-tool=pen]").click();
  const box = await page.locator("#sheet").boundingBox();
  await page.mouse.move(box.x + box.width * 0.35, box.y + box.height * 0.67);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.46, box.y + box.height * 0.65, { steps: 6 });
  await page.mouse.up();
  await page.locator("button[data-tool=select]").click();
  const anchor = await page.locator(".marker").boundingBox();
  await page.mouse.move(anchor.x + 6, anchor.y + 6);
  await page.mouse.down();
  await page.mouse.move(anchor.x + 44, anchor.y + 28, { steps: 5 });
  await page.mouse.up();
  await page.keyboard.press("Control+d");
  assert.equal(await page.locator(".marker").count(), 2);
  await page.keyboard.press("Delete");
  assert.equal(await page.locator(".marker").count(), 1);
  await page.keyboard.press("Control+z");
  assert.equal(await page.locator(".marker").count(), 2);
  await page.keyboard.press("Control+y");
  assert.equal(await page.locator(".marker").count(), 1);
  await page.locator("#plus").click();
  await page.locator("#fit").click();
  const saved = await download();
  const native = saved.doc.pages[0];
  assert.equal(saved.doc.kind, "Image");
  assert.equal(saved.doc.schemaVersion, 2);
  assert.equal(saved.doc.sourceImagePath, "Planting plan.png");
  assert.equal(
    saved.doc.sourceImageSha256,
    createHash("sha256").update(imageBytes).digest("hex").toUpperCase()
  );
  assert.equal(native.width, 960);
  assert.equal(native.height, 640);
  assert.equal(native.items.length, 2);
  assert.equal(native.ink.length, 1);
  assert(native.ink[0].points.length > 2);
  assert(native.items[0].x > 480);
  assert(native.items[0].y > 224);
  assert.equal(native.items[0].title, "Canopy");
  assert.match(native.items[0].text, /June/);
  assert.equal(native.items[1].text, "Proposed bench");
  // Exercise the actual FileSystemFileHandle and verified close, while substituting only the operating-system picker.
  await page.evaluate(() =>
    Object.defineProperty(window, "showSaveFilePicker", {
      configurable: true,
      value: async () =>
        (await navigator.storage.getDirectory()).getFileHandle("Plan.inklane.json", { create: true })
    })
  );
  await page.locator("#save").click();
  await msg("Annotations saved");
  assert.equal(await page.locator("#state").textContent(), "Annotations saved");
  await page.evaluate(async () => {
    const file = await (await navigator.storage.getDirectory()).getFileHandle("Plan.inklane.json");
    const stream = await file.createWritable();
    await stream.write("External edit");
    await stream.close();
  });
  await page.locator("#save").click();
  await msg("Choose a new");
  assert.equal(
    await page.evaluate(async () => {
      const file = await (await navigator.storage.getDirectory()).getFileHandle("Plan.inklane.json");
      return (await file.getFile()).text();
    }),
    "External edit"
  );
  await page.locator("#more summary").click();
  const reportDownload = page.waitForEvent("download");
  await page.locator("#feedback").click();
  const report = await reportDownload;
  const reportPath = resolve(output, report.suggestedFilename());
  await report.saveAs(reportPath);
  assert.equal(report.suggestedFilename(), "Planting plan.png.feedback.json");
  const feedback = JSON.parse(await readFile(reportPath, "utf8"));
  assert.equal(feedback.version, 1);
  assert.equal(feedback.format, "inklane.feedback");
  assert.equal(feedback.coordinateSystem, "top-left");
  assert.deepEqual(feedback.source, {
    name: "Planting plan.png",
    mediaType: "image/png",
    sha256: saved.doc.sourceImageSha256.toLowerCase()
  });
  assert.equal(feedback.pages[0].unit, "image-pixel");
  assert.equal(feedback.pages[0].number, 1);
  assert.equal(feedback.pages[0].annotations.length, 3);
  assert(!JSON.stringify(feedback).includes("sourceImagePath"));
  const exportedNote = feedback.pages[0].annotations.find((a) => a.type === "note");
  assert.equal(exportedNote.title, "Canopy");
  assert.equal(exportedNote.text, native.items[0].text);
  assert.equal(exportedNote.x, native.items[0].x);
  assert.deepEqual(feedback.pages[0].annotations.find((a) => a.type === "ink").points, native.ink[0].points);
  assert.equal(await page.locator("#state").textContent(), "Annotations saved");
  const wrong = structuredClone(saved.doc);
  wrong.sourceImageSha256 = "0".repeat(64);
  await open([
    { name: "Planting plan.png", mimeType: "image/png", buffer: imageBytes },
    { name: "other.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify(wrong)) }
  ]);
  await msg("different image");
  const future = structuredClone(saved.doc);
  future.schemaVersion = 900;
  await open({ name: "new.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify(future)) });
  await msg("versions 1 and 2");
  assert.equal(await page.locator(".marker").count(), 1);
  await page.reload();
  await open(reportPath);
  await msg("exported feedback report");
  assert.equal(await page.locator("#relink").isVisible(), false);
  await open(saved.path);
  await page.locator("#relink").waitFor();
  assert.equal(await page.locator("#source-name").textContent(), "Planting plan.png");
  await open(imagePath);
  await page.locator(".marker").click();
  assert.equal(await page.locator("#note-title").inputValue(), "Canopy");
  assert.match(await page.locator("#note-body").inputValue(), /June/);
  await page.locator("#note-body").fill("Unsaved text");
  page.once("dialog", (d) => d.dismiss());
  await open(imagePath);
  assert.equal(await page.locator("#note-body").inputValue(), "Unsaved text");
  assert.equal(await page.locator(".marker").count(), 1);
  for (const width of [390, 320]) {
    await page.setViewportSize({ width, height: 750 });
    await page.locator("#more summary").click();
    await page.locator("#language").click();
    const geometry = await page.evaluate(() => ({
      width: document.documentElement.clientWidth,
      scroll: document.documentElement.scrollWidth,
      card: document.querySelector("#card").getBoundingClientRect().toJSON()
    }));
    assert(geometry.scroll <= width, JSON.stringify(geometry));
    assert(geometry.card.x >= 0 && geometry.card.right <= width + 1, JSON.stringify(geometry));
    await page.screenshot({ path: resolve(output, "editor-mobile-" + width + ".png") });
  }
  // Index works with the keyboard and matches body text as well as titles.
  await page.locator("#more summary").click();
  await page.locator("#index").click();
  await page.locator("#search").fill("Unsaved");
  assert.equal(await page.locator("#results button").count(), 1);
  await page.locator("#results button").press("Enter");
  assert(await page.locator("#card").isVisible());
  await page.locator("#close-card").click();
  // Exercise the document's browser clipboard event handlers without reading or
  // replacing the user's operating-system clipboard.
  const count = await page.locator(".marker").count();
  await page.evaluate(() => {
    const transfer = new DataTransfer();
    const surface = document.querySelector("#viewport");
    surface.focus();
    surface.dispatchEvent(
      new ClipboardEvent("copy", { bubbles: true, cancelable: true, clipboardData: transfer })
    );
    if (!transfer.getData("application/x-inklane-selection+json"))
      throw Error("Missing editable clipboard object");
    surface.dispatchEvent(
      new ClipboardEvent("paste", { bubbles: true, cancelable: true, clipboardData: transfer })
    );
  });
  await page.waitForFunction((n) => document.querySelectorAll(".marker").length === n + 1, count);
  await page.keyboard.press("Delete");
  await page.addScriptTag({ url: origin + "/test-axe.js" });
  const violations = await page.evaluate(async () =>
    (
      await axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } })
    ).violations.map((v) => [v.id, v.nodes.map((n) => n.target)])
  );
  assert.deepEqual(violations, []);
  assert.deepEqual(errors, []);
  await page.emulateMedia({ colorScheme: "dark" });
  await page.setViewportSize({ width: 960, height: 610 });
  await page.screenshot({ path: resolve(output, "editor-dark.png") });
  const darkViolations = await page.evaluate(async () =>
    (
      await axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] } })
    ).violations.map((v) => v.id)
  );
  assert.deepEqual(darkViolations, []);
  await page.emulateMedia({ colorScheme: "light" });
  // JPEG orientation is part of the anchor coordinate system.
  const jpeg = Buffer.from(
    await page.evaluate(() => document.querySelector("#source").toDataURL("image/jpeg").split(",")[1]),
    "base64"
  );
  const orientation6 = Buffer.from(
    "ffe100224578696600004d4d002a00000008000101120003000000010006000000000000",
    "hex"
  );
  page.once("dialog", (d) => d.accept());
  await open({
    name: "Portrait.jpg",
    mimeType: "image/jpeg",
    buffer: Buffer.concat([jpeg.subarray(0, 2), orientation6, jpeg.subarray(2)])
  });
  assert.deepEqual(await page.locator("#source").evaluate((c) => [c.width, c.height]), [640, 960]);
  // A failed native write, a canceled picker and the download-only fallback each leave editable work intact.
  await add("note", 0.3, 0.3);
  await page.locator("#note-body").fill("Keep this change");
  await page.locator("#close-card").click();
  await page.evaluate(() =>
    Object.defineProperty(window, "showSaveFilePicker", {
      configurable: true,
      value: async () => {
        throw new DOMException("Canceled", "AbortError");
      }
    })
  );
  await page.locator("#save").click();
  assert.equal(await page.locator("#state").textContent(), "Unsaved changes");
  await page.evaluate(() =>
    Object.defineProperty(window, "showSaveFilePicker", {
      configurable: true,
      value: async () => ({
        name: "unwritable.inklane.json",
        getFile: async () => new File([], "unwritable.inklane.json"),
        createWritable: async () => ({
          write: async () => {
            throw Error("write failure");
          },
          abort: async () => {}
        })
      })
    })
  );
  await page.locator("#save").click();
  await msg("Could not save");
  assert.equal(await page.locator("#state").textContent(), "Unsaved changes");
  await page.evaluate(() =>
    Object.defineProperty(window, "showSaveFilePicker", { configurable: true, value: undefined })
  );
  const fallbackDownload = page.waitForEvent("download");
  await page.locator("#save").click();
  await fallbackDownload;
  assert.equal(await page.locator("#state").textContent(), "Unsaved changes");
  const overlayDoc = structuredClone(saved.doc);
  overlayDoc.sourceImagePath = "Z:\\sample\\Planting plan.png";
  overlayDoc.pages[0].images = [
    { id: "placed-planting", x: 60, y: 80, width: 90, height: 60, pngData: imageBytes.toString("base64") }
  ];
  page.once("dialog", (d) => d.accept());
  await open([
    { name: "Planting plan.png", mimeType: "image/png", buffer: imageBytes },
    {
      name: "with-picture.json",
      mimeType: "application/json",
      buffer: Buffer.from(JSON.stringify(overlayDoc))
    }
  ]);
  assert.equal(await page.locator("#pictures img").count(), 1);
  await page.locator("#more summary").click();
  const graphicsDownload = page.waitForEvent("download");
  await page.locator("#feedback").click();
  const picturesPath = resolve(output, "placed-picture.feedback.json");
  await (await graphicsDownload).saveAs(picturesPath);
  const placedReport = JSON.parse(await readFile(picturesPath, "utf8"));
  const first = placedReport.pages[0].annotations[0];
  assert.deepEqual(first, {
    id: "placed-planting",
    type: "picture",
    x: 60,
    y: 80,
    width: 90,
    height: 60,
    sha256: createHash("sha256").update(imageBytes).digest("hex")
  });
  assert(!JSON.stringify(placedReport).includes("pngData") && !JSON.stringify(placedReport).includes("Z:"));
  await page.locator("button[data-tool=select]").click();
  await page.locator("#pictures img").click();
  await page.locator("#edit").click();
  await page.locator("#picture-width").fill("135");
  await page.locator("#size-form button.primary").click();
  const resized = await download("resized.inklane.json");
  assert.equal(resized.doc.pages[0].images[0].width, 135);
  assert.equal(resized.doc.pages[0].images[0].height, 90);
  await page.locator("#undo").click();
  const restored = await download("restored.inklane.json");
  assert.equal(restored.doc.pages[0].images[0].width, 90);
  for (const source of process.argv.slice(4)) {
    // Optional historical test pairs are local, read-only fixtures.
    const sidecar = source + ".inklane.json";
    const before = JSON.parse(await readFile(sidecar, "utf8"));
    await page.setViewportSize({ width: 1200, height: 780 });
    await open([source, sidecar]);
    assert.equal(await page.locator("#name").textContent(), basename(source));
    const copy = await download();
    const after = copy.doc.pages[0];
    assert.deepEqual(after.ink, before.pages[0].ink);
    assert.deepEqual(
      after.items,
      before.pages[0].items.map((o) => ({ title: "", ...o }))
    );
    assert.deepEqual(after.images, before.pages[0].images ?? []);
    assert.equal(copy.doc.sourceImagePath, basename(source));
    await page.screenshot({ path: resolve(output, "native-" + basename(source) + ".png") });
  }
  await (await import("./test-documents.mjs")).verifyDocuments(browser,origin + base,output);
  for (const [language,width,height] of [["en",390,844],["de",390,844],["en",320,740],["de",320,740]]) {
    const phone = await browser.newContext({ viewport: { width, height }, isMobile:true, hasTouch:true, locale:language });
    try {
      const screen = await phone.newPage();
      await screen.goto(origin + base + "editor/?sample=review&lang=" + language);
      const marker = screen.locator(".marker").first();
      await marker.waitFor();
      const before = await marker.boundingBox();
      await marker.click();
      const card = screen.locator("#card");
      await card.waitFor();
      const geometry = () => screen.evaluate(() => {
        const box = id => document.querySelector(id).getBoundingClientRect().toJSON();
        return {sheet:box("#sheet"),card:box("#card"),desk:box("#viewport")};
      });
      const offPage = await geometry();
      const gap = Math.max(offPage.sheet.top - offPage.card.bottom, offPage.card.top - offPage.sheet.bottom);
      assert.ok(gap >= 3 && gap <= 10, "The remark should sit just outside the landscape document.");
      assert.ok(offPage.card.top >= offPage.desk.top && offPage.card.bottom <= offPage.desk.bottom);
      assert.deepEqual(await marker.boundingBox(), before, "Opening the remark must leave the source in place.");
      const footerFits = () => screen.locator("footer").evaluate(el => el.scrollWidth <= el.clientWidth);
      assert.ok(await footerFits(), "Object actions and zoom must fit the phone footer.");
      assert.equal(await screen.getByRole("button",{name:language === "de" ? "Duplizieren" : "Duplicate",exact:true}).count(),1);
      await screen.screenshot({path:resolve(output,"phone-remark-" + language + "-" + width + ".png")});
      await screen.locator("#close-card").click();
      await screen.locator("#more summary").click();
      await screen.locator("#new-sheet").click();
      await screen.locator("#paper-form button.primary").click();
      await screen.locator("button[data-tool=note]").click();
      const sheet = await screen.locator("#sheet").boundingBox();
      await screen.mouse.click(sheet.x+sheet.width*.45,sheet.y+sheet.height*.4);
      await card.waitFor();
      const onPage = await geometry();
      assert.deepEqual(await screen.locator("#sheet").boundingBox(),sheet,"Choosing an object on printable paper must leave the page in place.");
      assert.ok(await footerFits(), "Page navigation, object actions and zoom must fit the phone footer.");
      assert.ok(Math.min(onPage.sheet.bottom,onPage.card.bottom) > Math.max(onPage.sheet.top,onPage.card.top));
      assert.equal(await card.getAttribute("data-desk"), null, "A tall sheet should keep the remark at its anchor.");
    } finally {
      await phone.close();
    }
  }
  console.log(
    "Browser editor passed image/sidecar round trip, real file write and conflict protection, editing history, wrong-file refusal, mobile layout and accessibility checks."
  );
} finally {
  await context.close();
  await browser.close();
  server.close();
}
