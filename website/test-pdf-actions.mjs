import assert from "node:assert/strict";

export async function verifyPdfActions(browser, base) {
  const context = await browser.newContext({ viewport: { width: 1000, height: 800 } });
  let resume;
  try {
    const held = new Promise(resolve => { resume = resolve; });
    await context.route("**/editor/pdf-export.js", async route => {
      await held;
      await route.continue();
    });
    await context.addInitScript(() => {
      globalThis.pdfSaves = [];
      globalThis.cancelPdfSave = false;
      globalThis.inklaneSaveCopy = async (blob, name) => {
        if (cancelPdfSave) throw new DOMException("Cancelled", "AbortError");
        const pdfjs = await import(new URL("./pdf-lib/pdf.min.mjs", location.href));
        pdfjs.GlobalWorkerOptions.workerSrc = new URL("./pdf-lib/pdf.worker.min.mjs", location.href).href;
        const task = pdfjs.getDocument({ data: new Uint8Array(await blob.arrayBuffer()), isEvalSupported: false });
        const pdf = await task.promise;
        try { pdfSaves.push({ name, type: blob.type, pages: pdf.numPages }); }
        finally { await task.destroy(); }
      };
    });
    const page = await context.newPage();
    await page.goto(base + "editor/?lang=en");
    await page.locator("#new-notebook").click();
    await page.locator("#book-title").fill("Export review");
    await page.locator("#paper-form button.primary").click();
    await page.locator('[data-tool="note"]').click();
    const sheet = await page.locator("#sheet").boundingBox();
    await page.mouse.click(sheet.x + sheet.width * .3, sheet.y + sheet.height * .2);
    await page.locator("#note-title").fill("Keep this editable");
    await page.locator("#note-body").fill("The review copy must include this complete explanation.");
    await page.locator("#close-card").focus();
    await page.keyboard.press("Enter");
    const state = await page.locator("#state").textContent();
    const openExport = async () => {
      await page.locator("#more summary").click();
      await page.locator("#export-pdf").click();
    };

    // Cancel while the actual lazy module is loading; no save may occur later.
    await openExport();
    await page.locator("#pdf-progress").waitFor();
    await page.locator("#cancel-pdf-progress").click();
    resume();
    await page.locator("#pdf-progress").waitFor({ state: "hidden" });
    assert.deepEqual(await page.evaluate(() => pdfSaves), []);
    assert.equal(await page.locator("#state").textContent(), state);
    assert.equal(await page.locator("#more summary").evaluate(el => el === document.activeElement), true);

    // A native save cancellation must preserve the unsaved document and return focus.
    await page.evaluate(() => { cancelPdfSave = true; });
    await openExport();
    await page.waitForFunction(() => !document.querySelector("#export-pdf").disabled);
    assert.deepEqual(await page.evaluate(() => pdfSaves), []);
    assert.equal(await page.locator("#state").textContent(), state);
    assert.equal(await page.locator("#more summary").evaluate(el => el === document.activeElement), true);

    // All/current page selection must change the actual PDF page count, including notes.
    await page.evaluate(() => { cancelPdfSave = false; });
    await page.locator("#add-page").click();
    await page.locator("#prev-page").click();
    const dirty = await page.locator("#state").textContent();
    await openExport();
    await page.locator("#pdf-form button.primary").click();
    await page.waitForFunction(() => pdfSaves.length === 1);
    await page.waitForFunction(() => !document.querySelector("#export-pdf").disabled);
    assert.equal(await page.locator("#state").textContent(), dirty);
    assert.equal(await page.locator("#pdf-progress").isVisible(), false);
    await openExport();
    await page.locator("#pdf-range").selectOption("current");
    await page.locator("#pdf-form button.primary").click();
    await page.waitForFunction(() => pdfSaves.length === 2);
    await page.waitForFunction(() => !document.querySelector("#export-pdf").disabled);
    const saved = await page.evaluate(() => pdfSaves);
    assert.equal(saved[0].name, "Export review-review.pdf");
    assert.equal(saved[0].type, "application/pdf");
    assert.ok(saved[0].pages >= 3, "Two source pages plus the complete note appendix");
    assert.equal(saved[1].pages, saved[0].pages - 1, "Current page keeps its note appendix and omits the other source page");
    assert.equal(await page.locator("#state").textContent(), dirty);
    await page.locator(".marker").click();
    assert.equal(await page.locator("#note-body").inputValue(), "The review copy must include this complete explanation.");
    console.log("Review PDF actions passed preparation/native-save cancellation, focus return, unchanged edits and all/current output.");
  } finally {
    resume?.();
    await context.close();
  }
}
