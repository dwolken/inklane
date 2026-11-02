// Render both editable example documents after changing field-guide.svg.
import { mkdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const source = await readFile(new URL("field-guide.svg", import.meta.url), "utf8");
const assets = new URL("../assets/", import.meta.url);
await mkdir(assets, { recursive: true });
const browser = await chromium.launch(process.env.BROWSER_PATH
  ? { executablePath: process.env.BROWSER_PATH }
  : { channel: process.platform === "win32" ? "msedge" : "chrome" });
try {
  const page = await browser.newPage({ viewport: { width: 1160, height: 680 }, deviceScaleFactor: 1 });
  for (const language of ["en", "de"]) {
    await page.setContent('<!doctype html><meta charset="utf-8"><style>*{margin:0;display:block}head{display:none}</style>' + source);
    if (language === "de") await page.locator("[data-de]").evaluateAll(labels => {
      for (const label of labels) label.textContent = label.dataset.de;
    });
    await page.locator("svg").screenshot({ path: fileURLToPath(new URL("field-guide-" + language + ".png", assets)) });
  }
} finally {
  await browser.close();
}
