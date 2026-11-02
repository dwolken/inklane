import assert from "node:assert/strict";
import { resolve } from "node:path";

async function printBytes(page, context, base, output, filename, range) {
  await page.locator("#more summary").click();
  await page.locator("#print").click();
  if (range) {
    await page.locator("#print-range").selectOption(range);
    await page.locator("#print-form button.primary").click();
  }
  await page.waitForFunction(
    () =>
      document.querySelector(".print-frame")?.contentDocument?.body.dataset.printRequested === "true" &&
      !document.querySelector("#open").disabled
  );
  const markup = await page
    .frameLocator(".print-frame")
    .locator("html")
    .evaluate((el) => el.outerHTML);
  const sheet = await context.newPage();
  try {
    // Keep the same trusted origin so the browser can decode the print frame's source blobs.
    await sheet.goto(base + "editor/");
    await sheet.setContent(markup);
    await sheet.evaluate(async () => {
      await Promise.all(Array.from(document.images, (img) => img.decode()));
      await document.fonts.ready;
    });
    return await sheet.pdf({
      path: resolve(output, filename),
      preferCSSPageSize: true,
      printBackground: true
    });
  } finally {
    await sheet.close();
  }
}

// Reopen Chromium's finished PDF, extract the actual text and sample the rendered pages.
async function inspect(page, bytes, probes) {
  return page.evaluate(
    async ({ base64, probes }) => {
      const lib = await import("./pdf-lib/pdf.min.mjs");
      lib.GlobalWorkerOptions.workerSrc = new URL("./pdf-lib/pdf.worker.min.mjs", location.href).href;
      const task = lib.getDocument({
        data: Uint8Array.from(atob(base64), (ch) => ch.charCodeAt(0)),
        useSystemFonts: true
      });
      const doc = await task.promise;
      try {
        const result = [];
        for (let number = 1; number <= doc.numPages; number++) {
          const p = await doc.getPage(number);
          const v = p.getViewport({ scale: 96 / 72 });
          const text = (await p.getTextContent()).items.map((item) => item.str).join(" ");
          const canvas = document.createElement("canvas");
          canvas.width = Math.ceil(v.width);
          canvas.height = Math.ceil(v.height);
          await p.render({ canvas, viewport: v, background: "#fff" }).promise;
          const bitmap = canvas.getContext("2d").getImageData(0, 0, canvas.width, canvas.height);
          const samples = probes
            .filter((probe) => probe.page === number)
            .map((probe) => {
              let matched = 0;
              for (
                let y = Math.max(0, Math.round(probe.y) - probe.radius);
                y <= Math.min(canvas.height - 1, Math.round(probe.y) + probe.radius);
                y++
              ) {
                for (
                  let x = Math.max(0, Math.round(probe.x) - probe.radius);
                  x <= Math.min(canvas.width - 1, Math.round(probe.x) + probe.radius);
                  x++
                ) {
                  const idx = (y * canvas.width + x) * 4;
                  if (probe.rgb.every((target, c) => Math.abs(bitmap.data[idx + c] - target) < 30)) matched++;
                }
              }
              return { label: probe.label, matched };
            });
          result.push({ width: v.width, height: v.height, text, samples });
          p.cleanup();
          canvas.width = canvas.height = 1;
        }
        return result;
      } finally {
        await task.destroy();
      }
    },
    { base64: bytes.toString("base64"), probes }
  );
}
function colors(pages) {
  for (const p of pages)
    for (const sample of p.samples) assert(sample.matched > 2, "Missing " + sample.label + " in printed PDF");
}
export async function checkPdfReview({ page, context, base, output, layer }) {
  const box = await page.locator("#sheet").boundingBox();
  await page.locator("button[data-tool=pen]").click();
  await page.locator("#ink-color").fill("#126fdd");
  await page.mouse.move(box.x + box.width * 0.5, box.y + box.height * 0.58);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.72, box.y + box.height * 0.58, { steps: 6 });
  await page.mouse.up();
  const note = layer.pages[1].items[0];
  const probes = [
    { page: 1, x: 85, y: 195, radius: 12, rgb: [51, 105, 74], label: "original green PDF artwork" },
    { page: 2, x: 48, y: 45, radius: 9, rgb: [102, 46, 128], label: "rotated PDF artwork" },
    {
      page: 2,
      x: 244,
      y: 174,
      radius: 5,
      rgb: [18, 111, 221],
      label: "blue review stroke in source coordinates"
    },
    {
      page: 2,
      x: note.x + 7,
      y: note.y + 6,
      radius: 3,
      rgb: [255, 225, 164],
      label: "anchor number at the saved coordinate"
    }
  ];
  const all = await inspect(
    page,
    await printBytes(page, context, base, output, "PDF-review-with-remarks.pdf", "all"),
    probes
  );
  assert.equal(all.length, 3);
  colors(all);
  for (const [i, expectedHeight] of [280, 300].entries()) {
    assert(
      Math.abs(all[i].width - 400) < 1.35 && Math.abs(all[i].height - expectedHeight) < 1.35,
      "Printed PDF changed displayed crop or rotation"
    );
  }
  assert.match(all[2].text, /Page 2\s*·\s*Note 1\s*Scale bar/);
  assert.match(all[2].text, /Set this label to 1 cm\./);
  const current = await inspect(
    page,
    await printBytes(page, context, base, output, "PDF-current-page.pdf", "current"),
    probes.filter((p) => p.page === 2).map((p) => ({ ...p, page: 1 }))
  );
  assert.equal(current.length, 2);
  colors(current);
  assert.match(current[1].text, /Page 2\s*·\s*Note 1/);
}
export async function checkImageReview({ page, context, base, output, open, writeNote }) {
  const image = await page.evaluate(() => {
    const c = document.createElement("canvas");
    c.width = 600;
    c.height = 300;
    const ctx = c.getContext("2d");
    ctx.fillStyle = "#45aa58";
    ctx.fillRect(0, 0, 300, 300);
    ctx.fillStyle = "#27496e";
    ctx.fillRect(300, 0, 300, 300);
    return c.toDataURL().split(",")[1];
  });
  await open({ name: "Review.png", mimeType: "image/png", buffer: Buffer.from(image, "base64") });
  await writeNote("Spacing", "Leave a wider gutter here.");
  const copy = await inspect(
    page,
    await printBytes(page, context, base, output, "Image-review-with-remarks.pdf"),
    [
      { page: 1, x: 200, y: 240, radius: 3, rgb: [69, 170, 88], label: "left half of original image" },
      { page: 1, x: 850, y: 240, radius: 3, rgb: [39, 73, 110], label: "right half of original image" },
      {
        page: 1,
        x: 200,
        y: 120,
        radius: 3,
        rgb: [255, 255, 255],
        label: "aspect-ratio-preserving top margin"
      },
      { page: 1, x: 200, y: 142, radius: 3, rgb: [69, 170, 88], label: "top edge of fitted artwork" }
    ]
  );
  assert.equal(copy.length, 2);
  colors(copy);
  assert(
    Math.abs((copy[0].width * 25.4) / 96 - 297) < 0.25 && Math.abs((copy[0].height * 25.4) / 96 - 210) < 0.25
  );
  assert.match(copy[1].text, /Page 1\s*·\s*Note 1\s*Spacing/);
  assert.match(copy[1].text, /Leave a wider gutter here/);
}

export async function checkPrintRange({ page, context, base, output, open, file }) {
  await open(file, true);
  await page.locator("#more summary").click();
  await page.locator("#print").click();
  await page.locator("#print-options").waitFor();
  assert(await page.locator("#print-all").isDisabled());
  assert.equal(await page.locator("#print-range").inputValue(), "current");
  assert(await page.locator("#print-limit").isVisible());
  await page.locator("#cancel-print").click();
  const current = await inspect(
    page,
    await printBytes(page, context, base, output, "Large-current-page.pdf", "current"),
    []
  );
  assert.equal(current.length, 1);
  assert(Math.abs(current[0].height - (200 * 96) / 72) < 1.35);
  await page.evaluate(() => {
    window.inklaneNativePrinting = false;
    document.querySelector("button[data-tool=select]").click();
  });
  assert(await page.locator("#print").isHidden(), "Native shell must hide unsupported printing.");
  await page.evaluate(() => {
    delete window.inklaneNativePrinting;
    document.querySelector("button[data-tool=pen]").click();
  });
}
