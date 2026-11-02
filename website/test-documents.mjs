import { checkPdfReview, checkImageReview, checkPrintRange } from "./test-print-review.mjs";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

export async function verifyDocuments(browser, base, output) {
  const context = await browser.newContext({ locale: "en-GB", viewport: { width: 1180, height: 760 } });
  const page = await context.newPage(),
    errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  const visible = (id) => page.locator("#" + id).waitFor();
  const click = (id) => page.locator("#" + id).click();
  const more = async (id) => {
    await page.locator("#more summary").click();
    await click(id);
  };
  const source = fileURLToPath(new URL("./fixtures/review-pages.pdf", import.meta.url));
  async function open(files, discard = false) {
    if (discard) page.once("dialog", (d) => d.accept());
    await page.locator("#files").setInputFiles(files);
    await page.waitForFunction(() => !document.querySelector("#open").disabled);
  }
  async function writeNote(title, body) {
    await page.locator("[data-tool=note]").first().click();
    const r = await page.locator("#viewport").boundingBox(),
      box = await page.locator("#sheet").boundingBox();
    await page.mouse.click(Math.max(r.x, box.x) + 74, Math.max(r.y, box.y) + 75);
    await page.locator("#note-title").fill(title);
    await page.locator("#note-body").fill(body);
    await click("close-card");
  }
  async function download(action, filename) {
    const next = page.waitForEvent("download");
    await more(action);
    const item = await next,
      path = resolve(output, filename);
    await item.saveAs(path);
    return path;
  }
  try {
    await page.goto(base + "editor/");
    await click("new-notebook");
    await page.locator("#book-title").fill("Undo checkpoint");
    await page.locator("#paper-form button.primary").click();
    await click("add-page");
    await page.waitForFunction(async () =>
      (await (await import("./drafts.js")).listDrafts()).some((d) => d.name === "Undo checkpoint")
    );
    await click("undo");
    await more("home");
    await visible("new-notebook");
    await page.reload();
    await visible("new-notebook");
    assert.equal(
      await page.evaluate(async () =>
        (await (await import("./drafts.js")).listDrafts()).some((d) => d.name === "Undo checkpoint")
      ),
      false
    );
    await click("new-notebook");
    await page.locator("#book-title").fill("Field corrections");
    await page.locator("#paper-size").selectOption("A5");
    await page.locator("#pattern").selectOption("Squared");
    await page.locator("#spacing").selectOption("custom");
    await page.locator("#custom-spacing").fill("7.5");
    await page.locator("#paper-form button.primary").click();
    await visible("sheet");
    await writeNote("One", "Use a lighter outline");
    await click("add-page");
    assert.equal(await page.locator("#page-count").textContent(), "2 / 2");
    await writeNote("Two", "Keep the scale here.");
    await more("index");
    await page.locator("#search").fill("lighter");
    await page.locator("#results button").click();
    assert.equal(await page.locator("#page-count").textContent(), "1 / 2");
    assert.equal(await page.locator("#note-title").inputValue(), "One");
    await click("close-card");
    await click("next-page");
    await click("add-page");
    await click("undo");
    assert.equal(await page.locator("#page-count").textContent(), "2 / 2");
    await click("redo");
    assert.equal(await page.locator("#page-count").textContent(), "3 / 3");
    await click("undo");
    const book = await download("portable", "fieldbook.inklane");
    const raw = await readFile(book);
    assert.equal(raw.subarray(0, 8).toString(), "INKLANE1");
    const bookReport = JSON.parse(await readFile(await download("feedback", "notebook-feedback.json")));
    assert.equal(bookReport.source.sha256, createHash("sha256").update(raw).digest("hex"));
    assert.equal(bookReport.source.mediaType, "application/vnd.inklane.workspace");
    const props = JSON.parse(raw.subarray(12, 12 + raw.readUint32BE(8)));
    assert.equal(props.document.pages.length, 2);
    assert(Math.abs((props.document.pages[0].width * 25.4) / 96 - 148) < 1e-6);
    assert.equal(props.document.pages[1].spacingMm, 7.5);
    assert.equal(props.document.pages[0].items[0].text, "Use a lighter outline");
    // Inspect the actual print surface before handing it to the system print dialog.
    await page.evaluate(() => {
      const append = Element.prototype.append;
      Element.prototype.append = function (...nodes) {
        const result = append.apply(this, nodes);
        for (const n of nodes)
          if (n instanceof HTMLIFrameElement && n.className === "print-frame")
            n.contentWindow.print = () => {
              n.contentDocument.body.dataset.printRequested = "true";
            };
        return result;
      };
    });
    await more("print");
    const frame = page.frameLocator(".print-frame");
    await frame.locator("article h1").waitFor({ state: "attached" });
    assert.equal(await frame.locator("section").count(), 2);
    assert.equal(
      await frame
        .locator("section")
        .first()
        .evaluate((el) => el.style.width),
      "148mm"
    );
    assert.equal(
      await frame.locator("article").getByText("Use a lighter outline", { exact: true }).count(),
      1
    );
    assert.match(await frame.locator("svg path").first().getAttribute("d"), /M[0-9.]+ [0-9.]+H/);
    const printHtml = await frame.locator("html").evaluate((el) => el.outerHTML);
    const printedPage = await context.newPage();
    await printedPage.setContent(printHtml);
    const printFile = await printedPage.pdf({
      path: resolve(output, "A5-print.pdf"),
      preferCSSPageSize: true,
      printBackground: true
    });
    await printedPage.close();
    const boxes = [
      ...printFile.toString("latin1").matchAll(/\/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)/g)
    ];
    assert.equal(boxes.length, 3);
    for (const [, width, height] of boxes)
      assert(
        Math.abs((width * 25.4) / 72 - 148) < 0.25 && Math.abs((height * 25.4) / 72 - 210) < 0.25,
        "Printed A5 exceeds browser print rounding"
      );

    await more("home");
    await page.locator("#draft-list strong").filter({ hasText: "Field corrections" }).click();
    await page.waitForFunction(() => document.querySelector("#state").textContent === "Unsaved changes");
    await more("index");
    await page.locator("#search").fill("Keep the scale");
    assert.equal(await page.locator("#results button").count(), 1);
    await page.locator("#browse form button").click();
    await open(book, true);
    assert.equal(await page.locator("#name").textContent(), "Field corrections");
    await page.screenshot({ path: resolve(output, "workspace-notebook.png") });

    await open(source);
    await page.waitForFunction(() => document.querySelector("#name").textContent === "review-pages.pdf");
    assert.equal(await page.locator("#page-count").textContent(), "1 / 2");
    const inked = await page.locator("#source").evaluate((canvas) => {
      const { data } = canvas.getContext("2d").getImageData(0, 0, canvas.width, canvas.height);
      let n = 0;
      for (let i = 0; i < data.length; i += 4) if (data[i + 1] > data[i] * 1.5 && data[i] < 170) n++;
      return n;
    });
    assert(inked > 500, "PDF contents were not painted");
    await click("next-page");
    await writeNote("Scale bar", "Set this label to 1 cm.");
    const side = await download("download", "review-pages.pdf.inklane.json"),
      layer = JSON.parse(await readFile(side));
    assert.equal(layer.kind, "Pdf");
    assert.equal(layer.schemaVersion, 3);
    assert.equal(
      layer.sourcePdfSha256,
      createHash("sha256")
        .update(await readFile(source))
        .digest("hex")
        .toUpperCase()
    );
    assert.equal(layer.sourcePdfName, "review-pages.pdf");
    assert.deepEqual(
      layer.pages.map((p) => [p.width, p.height]),
      [
        [400, 280],
        [400, 300]
      ]
    );
    assert.equal(layer.pages[1].items[0].text, "Set this label to 1 cm.");
    const portable = await download("portable", "complete-review.inklane");
    const combo = await readFile(portable);
    assert((await readFile(source)).equals(combo.subarray(12 + combo.readUInt32BE(8))));
    const report = JSON.parse(await readFile(await download("feedback", "page-feedback.json")));
    assert.equal(report.pages[1].unit, "dip-96");
    assert.equal(report.pages[1].annotations[0].title, "Scale bar");
    const corrupt = Buffer.from(combo);
    corrupt[corrupt.length - 1] ^= 1;
    await open({ name: "changed.inklane", mimeType: "application/octet-stream", buffer: corrupt });
    assert.equal(await page.locator("#page-count").textContent(), "2 / 2");
    await open(portable, true);
    await click("next-page");
    await page.locator(".marker").click();
    assert.equal(await page.locator("#note-title").inputValue(), "Scale bar");
    await open(fileURLToPath(new URL("./fixtures/locked-review.pdf", import.meta.url)));
    await page.getByRole("status").filter({ hasText: "password" }).waitFor();
    assert.equal(await page.locator("#note-title").inputValue(), "Scale bar");
    await open([source, side]);
    await click("next-page");
    await page.locator(".marker").click();
    assert.equal(await page.locator("#note-body").inputValue(), "Set this label to 1 cm.");
    await page.screenshot({ path: resolve(output, "workspace-pdf.png") });
    await checkPdfReview({ page, context, base, output, layer });
    await page.locator("#undo").click();
    await checkImageReview({ page, context, base, output, open, writeNote });
    await checkPrintRange({
      page,
      context,
      base,
      output,
      open,
      file: fileURLToPath(new URL("./fixtures/print-range.pdf", import.meta.url))
    });
    await more("home");
    await visible("start");
    await page.screenshot({ path: resolve(output, "workspace-library.png") });
    assert.equal(await page.locator("#open").isVisible(), false);
    assert.equal(await page.locator("#save").isVisible(), false);
    for (const width of [320, 390]) {
      await page.setViewportSize({ width, height: 740 });
      await page.screenshot({ path: resolve(output, "library-" + width + ".png") });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), width);
    }
    await page.setViewportSize({ width: 1180, height: 760 });
    await open(fileURLToPath(new URL("../tests/fixtures/windows-notebook.inklane", import.meta.url)));
    assert.equal(await page.locator(".placed").textContent(), "Reviewed on Windows");
    await more("rename-book");
    await page.locator("#rename-title").fill("Revised notebook");
    await page.locator("#rename-form button.primary").click();
    assert.equal(await page.locator("#name").textContent(), "Revised notebook");
    await click("page-count");
    await page.locator("[data-page-action=copy]").click();
    assert.equal(await page.locator("#page-count").textContent(), "2 / 3");
    await click("page-count");
    await page.locator("[data-page-action=forward]").click();
    assert.equal(await page.locator("#page-count").textContent(), "3 / 3");
    await click("page-count");
    page.once("dialog", (d) => d.accept());
    await page.locator("[data-page-action=remove]").click();
    assert.equal(await page.locator("#page-count").textContent(), "2 / 2");
    await click("undo");
    assert.equal(await page.locator("#page-count").textContent(), "3 / 3");
    await page.evaluate(() => {
      window.inklaneSaveCopy = async () => {
        throw new DOMException("Canceled", "AbortError");
      };
    });
    await click("save");
    assert.equal(await page.locator("#state").textContent(), "Unsaved changes");
    await page.evaluate(() => {
      window.inklaneSaveCopy = async (blob, name) => {
        if (blob.size < 100 || !name.endsWith(".inklane")) throw Error("Invalid save payload");
      };
    });
    await click("save");
    await page.waitForFunction(() => document.querySelector("#state").textContent === "Annotations saved");
    await page.goto(base + "editor/?sample=review&embed=1&lang=de");
    await page.locator(".marker").first().waitFor();
    assert.equal(await page.locator(".marker").count(), 2);
    assert.equal(await page.locator("#note-title").inputValue(), "Blütenfarbe");
    await page.screenshot({ path: resolve(output, "workspace-sample-de.png") });
    assert.deepEqual(errors, []);
    console.log(
      "Passed notebook create, physical print, editable save/reopen, draft recovery, cross-page search, source-preserving PDF review, rotated page geometry, portable handoff, protected-file refusal and sample loading."
    );
  } finally {
    await context.close();
  }
}
