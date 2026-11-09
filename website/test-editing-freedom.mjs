import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

export async function verifyEditingFreedom(browser, base, output) {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    locale: "en-US",
  });
  const page = await context.newPage(),
    errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("request", (req) => {
    if (
      !req.url().startsWith(base) &&
      !req.url().startsWith("data:") &&
      !req.url().startsWith("blob:")
    )
      errors.push("Unexpected request " + req.url());
  });
  const click = (id) => page.locator("#" + id).click();
  const input = async (id, value) => {
    await page.locator("#" + id).fill(String(value));
    await page.locator("#" + id).press("Tab");
  };
  const tool = (kind) => page.locator(`button[data-tool=${kind}]`).click();
  const point = async (x, y) => {
    const box = await page.locator("#sheet").boundingBox();
    return { x: box.x + x * box.width, y: box.y + y * box.width };
  };
  const tap = async (x, y) => {
    const p = await point(x, y);
    await page.mouse.click(p.x, p.y);
  };
  const drag = async (x, y, xx, yy) => {
    const a = await point(x, y),
      b = await point(xx, yy);
    await page.mouse.move(a.x, a.y);
    await page.mouse.down();
    await page.mouse.move(b.x, b.y, { steps: 8 });
    await page.mouse.up();
  };
  async function download(name) {
    await page.locator("#more summary").click();
    const wait = page.waitForEvent("download");
    await click("portable");
    const file = await wait,
      path = resolve(output, name);
    await file.saveAs(path);
    const bytes = await readFile(path),
      length = bytes.readUInt32BE(8);
    return {
      path,
      document: JSON.parse(bytes.subarray(12, 12 + length)).document,
    };
  }
  try {
    await page.goto(base + "editor/");
    await click("new-notebook");
    await page.locator("#paper-form button.primary").click();
    await page.locator("#more summary").click();
    await click("whole-page");
    await tool("text");
    await tap(0.12, 0.12);
    await page
      .locator(".placed-editor")
      .fill("A box that wraps without changing its font size.");
    await page.keyboard.press("Escape");
    const before = await page.locator(".placed").evaluate((el) => ({
      width: el.clientWidth,
      font: getComputedStyle(el).fontSize,
    }));
    const handle = await page.locator("[data-resize=e]").boundingBox();
    await page.mouse.move(
      handle.x + handle.width / 2,
      handle.y + handle.height / 2,
    );
    await page.mouse.down();
    await page.mouse.move(
      handle.x + handle.width / 2 + 90,
      handle.y + handle.height / 2,
      { steps: 6 },
    );
    await page.mouse.up();
    const after = await page.locator(".placed").evaluate((el) => ({
      width: el.clientWidth,
      font: getComputedStyle(el).fontSize,
    }));
    assert.ok(after.width > before.width + 70);
    assert.equal(
      after.font,
      before.font,
      "Resizing must reflow text without scaling the font",
    );
    await click("properties");
    await input("font-size", 24);
    await click("text-bold");
    await input("color-hex", "#F97316");
    await page.locator("#text-align").selectOption("Center");
    await page.locator("#content-format").selectOption("Markdown");
    await click("close-properties");
    await page.keyboard.press("F2");
    const markdown =
      '# Review\n\n**Bold**, *italic*, ++underlined++ and $x^2 + y^2$.\n\n| Item | Status |\n| --- | --- |\n| Tree | Keep |\n\n```js\nconst height = 42;\n```\n\n[Unsafe](javascript:alert(1)) <img src="https://example.com/private" onerror=alert(1)>';
    await page.locator(".placed-editor").fill(markdown);
    await page.keyboard.press("Escape");
    assert.equal(await page.locator(".placed h1").textContent(), "Review");
    assert.equal(await page.locator(".placed .katex").count(), 1);
    assert.equal(await page.locator(".placed .hljs-keyword").count(), 1);
    assert.equal(await page.locator(".placed table").count(), 1);
    assert.equal(await page.locator(".placed img").count(), 0);
    assert.equal(
      await page.locator('.placed a[href^="javascript:"]').count(),
      0,
    );
    await click("properties");
    await input("object-height", 350);
    await input("object-width", 420);
    await click("close-properties");
    await tool("shape");
    await drag(0.15, 0.65, 0.35, 0.78);
    await click("properties");
    await input("shape-label", "Source");
    await page.locator("#shape-fill").fill("#eab308");
    await page.locator("#shape-fill").dispatchEvent("change");
    await click("close-properties");
    await tool("shape");
    await drag(0.53, 0.65, 0.73, 0.78);
    await click("properties");
    await input("shape-label", "Result");
    await page.locator("#shape-kind").selectOption("Ellipse");
    await click("close-properties");
    await tool("shape");
    await click("properties");
    await page.locator("#shape-kind").selectOption("Arrow");
    await click("close-properties");
    await drag(0.25, 0.71, 0.63, 0.71);
    let saved = await download("editing-before.inklane");
    let shapes = saved.document.pages[0].shapes;
    assert.equal(saved.document.schemaVersion, 4);
    assert.equal(shapes.length, 3);
    assert.equal(shapes[2].startId, shapes[0].id);
    assert.equal(shapes[2].endId, shapes[1].id);
    await tool("select");
    await tap(0.2, 0.68);
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("Control+a");
    await page.keyboard.press("Control+g");
    await page.keyboard.press("Control+d");
    saved = await download("editing-group.inklane");
    assert.equal(saved.document.pages[0].shapes.length, 6);
    assert.equal(saved.document.pages[0].items.length, 2);
    const clone = saved.document.pages[0].shapes.slice(3);
    assert.equal(clone[2].startId, clone[0].id);
    assert.equal(clone[2].endId, clone[1].id);
    await page.keyboard.press("Control+z");
    saved = await download("editing-rich.inklane");
    assert.equal(saved.document.pages[0].shapes.length, 3);
    const text = saved.document.pages[0].items[0];
    assert.equal(text.color, "#F97316");
    assert.equal(text.bold, true);
    assert.equal(text.fontSize, 24);
    assert.equal(text.alignment, "Center");
    assert.equal(text.contentFormat, "Markdown");
    assert.equal(text.text, markdown);
    await page.keyboard.press("Control+a");
    await click("properties");
    const answers = ["Review component", "Examples"];
    const answer = (d) => d.accept(answers.shift());
    page.on("dialog", answer);
    await click("save-element");
    page.off("dialog", answer);
    await click("close-properties");
    assert.equal(
      await page.evaluate(
        () => JSON.parse(localStorage.getItem("inklane.elements")).length,
      ),
      1,
    );
    page.once("dialog", (d) => d.accept());
    await page.locator("#files").setInputFiles(saved.path);
    await page.waitForFunction(() => !document.querySelector("#open").disabled);
    assert.equal(await page.locator(".placed .katex").count(), 1);
    assert.equal(await page.locator("[data-shape]").count(), 3);
    await click("properties");
    await click("open-elements");
    await page.locator(".element-row button").first().click();
    await page.waitForFunction(
      () => document.querySelectorAll("[data-shape]").length === 6,
    );
    await page.keyboard.press("Control+z");
    assert.equal(await page.locator("[data-shape]").count(), 3);
    await click("close-properties");
    await tool("note");
    await tap(0.8, 0.4);
    await page
      .locator("#note-body")
      .fill(
        "## Explanation\n\n- First point\n- Second point\n\n$$\\frac{a}{b}$$",
      );
    await click("properties");
    await page.locator("#content-format").selectOption("Markdown");
    await click("close-properties");
    await click("note-read");
    assert.equal(
      await page.locator("#note-preview h2").textContent(),
      "Explanation",
    );
    assert.equal(await page.locator("#note-preview .katex").count(), 1);
    const resize = await page.locator("#note-resize").boundingBox();
    await page.mouse.move(resize.x + 8, resize.y + 8);
    await page.mouse.down();
    await page.mouse.move(resize.x + 78, resize.y + 80, { steps: 5 });
    await page.mouse.up();
    await page.locator("#more summary").click();
    await click("whole-page");
    await page.screenshot({
      path: resolve(output, "editing-rich-desktop.png"),
    });
    const result = await download("editing-final.inklane");
    assert.ok(result.document.pages[0].items.at(-1).width > 300);
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
    await page.locator("#more summary").click();
    await click("print");
    await page.waitForFunction(
      () =>
        document.querySelector(".print-frame")?.contentDocument.body.dataset
          .printRequested === "true",
    );
    const frame = page.frameLocator(".print-frame");
    assert.equal(await frame.locator("section .katex").count(), 1);
    assert.equal(await frame.locator("article .katex").count(), 1);
    assert.equal(await frame.locator("section [data-id]").count(), 3);
    const markup = await frame.locator("html").evaluate((el) => el.outerHTML);
    const printed = await context.newPage();
    await printed.goto(base + "editor/");
    await printed.setContent(markup);
    await printed.evaluate(() => document.fonts.ready);
    if(browser.browserType().name()==="chromium")await printed.pdf({
      path: resolve(output, "editing-rich.pdf"),
      preferCSSPageSize: true,
      printBackground: true,
    });
    else await printed.screenshot({path:resolve(output,"editing-rich-print-"+browser.browserType().name()+".png"),fullPage:true});
    await printed.close();
    await page.locator("#more summary").click();
    const pngDownload = page.waitForEvent("download");
    await click("export-image");
    const png = await pngDownload;
    await png.saveAs(resolve(output, "editing-rich.png"));
    const pngBytes = await readFile(resolve(output, "editing-rich.png"));
    assert.equal(pngBytes.subarray(1, 4).toString(), "PNG");
    assert.ok(
      pngBytes.readUInt32BE(16) > 1500,
      "Image export includes the page and full remark gutter",
    );
    if (process.env.CROSS_NATIVE_WORKSPACE) {
      await page
        .locator("#files")
        .setInputFiles(process.env.CROSS_NATIVE_WORKSPACE);
      await page.waitForFunction(
        () => !document.querySelector("#open").disabled,
      );
      assert.equal(await page.locator(".placed .katex").count(), 1);
      assert.equal(await page.locator("[data-shape]").count(), 3);
      const roundtrip = await download("native-browser-verified.inklane");
      assert.equal(roundtrip.document.pages[0].items[0].text, markdown);
      assert.equal(
        roundtrip.document.pages[0].items[0].contentFormat,
        "Markdown",
      );
    }
    await click("add-page");
    await page.locator("#more summary").click();
    await click("continuous-view");
    assert.equal(await page.locator(".page-slot").count(), 2);
    await page
      .locator('.page-slot[data-page="0"]')
      .click({ position: { x: 30, y: 30 } });
    await page.waitForFunction(
      () => document.querySelector("#sheet").parentElement.dataset.page === "0",
    );
    assert.equal(await page.locator(".placed .katex").count(), 1);
    await page
      .locator('.page-slot[data-page="1"]')
      .click({ position: { x: 30, y: 30 } });
    await page.waitForFunction(
      () => document.querySelector("#sheet").parentElement.dataset.page === "1",
    );
    await tool("text");
    await tap(0.15, 0.15);
    await page.locator(".placed-editor").fill("Second page remains editable");
    await page.keyboard.press("Escape");
    const continuous = await download("continuous-rich.inklane");
    assert.equal(continuous.document.pages.length, 2);
    assert.equal(
      continuous.document.pages[1].items[0].text,
      "Second page remains editable",
    );
    await page.screenshot({ path: resolve(output, "editing-continuous.png") });
    await page.locator("#more summary").click();
    await click("continuous-view");
    assert.equal(await page.locator(".page-slot").count(), 0);
    page.once("dialog", (d) => d.accept());
    await page
      .locator("#files")
      .setInputFiles(
        fileURLToPath(new URL("./fixtures/review-pages.pdf", import.meta.url)),
      );
    await page.waitForFunction(() => !document.querySelector("#open").disabled);
    await page.locator("#more summary").click();
    await click("continuous-view");
    await page
      .locator('.page-slot[data-page="1"]')
      .click({ position: { x: 30, y: 30 } });
    await page.waitForFunction(
      () => document.querySelector("#sheet").parentElement.dataset.page === "1",
    );
    await tool("note");
    await tap(0.3, 0.3);
    await page.locator("#note-body").fill("Review of the second source page");
    await click("close-card");
    const review = await download("continuous-review.inklane");
    assert.equal(review.document.kind, "Pdf");
    assert.equal(
      review.document.pages[1].items[0].text,
      "Review of the second source page",
    );
    assert.deepEqual(errors, []);
    console.log(
      "Passed text reflow, post-creation formatting, full HEX colors, safe Markdown/code/LaTeX, connected diagrams, grouping/duplication/undo, remark resize, and schema4 portable save/reopen.",
    );
  } finally {
    await context.close();
  }
}
