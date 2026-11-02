import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdir, readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { extname, join, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const dist = resolve(process.argv[2] ?? fileURLToPath(new URL("../.scratch/website/inklane", import.meta.url)));
const types = {
  ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript", ".mjs": "text/javascript",
  ".png": "image/png", ".ico": "image/x-icon", ".svg": "image/svg+xml", ".xml": "application/xml",
  ".webmanifest": "application/manifest+json", ".json": "application/json", ".wasm": "application/wasm", ".mp4": "video/mp4"
};
const base = "/inklane/", canonical = "https://dwolken.github.io" + base;
const server = createServer(async (request, response) => {
  let name = new URL(request.url, "http://localhost").pathname;
  if (!name.startsWith(base)) return response.writeHead(404).end();
  if (name.endsWith("/")) name += "index.html";
  const path = resolve(dist, name.slice(base.length));
  if (!path.startsWith(dist + sep)) return response.writeHead(403).end();
  try {
    const bytes = await readFile(path);
    response.writeHead(200, { "Content-Type": types[extname(path)] ?? "application/octet-stream" }).end(bytes);
  } catch { response.writeHead(404).end(); }
});
await new Promise(done => server.listen(0, "127.0.0.1", done));
const local = "http://127.0.0.1:" + server.address().port + base;
const browserOptions = process.env.BROWSER_PATH ? { executablePath: process.env.BROWSER_PATH } :
  { channel: process.platform === "win32" ? "msedge" : "chrome" };

async function capture(page, name, fullPage = false) {
  if (!process.env.SITE_CAPTURE_DIR) return;
  await mkdir(process.env.SITE_CAPTURE_DIR, { recursive: true });
  await page.screenshot({ path: join(process.env.SITE_CAPTURE_DIR, name + ".png"), fullPage });
}
async function scan(page) {
  await page.addScriptTag({ path: require.resolve("axe-core/axe.min.js") });
  const violations = await page.evaluate(async () => (await axe.run(document, {
    runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa", "best-practice"] }
  })).violations.map(v => [v.id, v.nodes.map(n => n.target)]));
  assert.deepEqual(violations, []);
}

let browser;
try {
  browser = await chromium.launch(browserOptions);
  for (const lang of ["en", "de"]) {
    const suffix = lang === "en" ? "" : "de/";
    const context = await browser.newContext({ locale: lang === "en" ? "en-GB" : "de-AT", colorScheme: "light" });
    const page = await context.newPage();
    const errors = [], movieRequests = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("response", r => { if (r.status() >= 400) errors.push(r.status() + " " + r.url()); });
    page.on("request", r => { if (r.url().endsWith(".mp4")) movieRequests.push(r.url()); });
    await page.setViewportSize({ width: 1440, height: 940 });
    await page.goto(local + suffix);
    assert.equal(await page.locator("html").getAttribute("lang"), lang);
    assert.equal(await page.locator("link[rel=canonical]").getAttribute("href"), canonical + suffix);
    assert.equal(await page.locator("link[hreflang='de']").getAttribute("href"), canonical + "de/");
    assert.equal(await page.locator("link[hreflang='en']").getAttribute("href"), canonical);
    const app = JSON.parse(await page.locator('script[type="application/ld+json"]').textContent());
    assert.equal(app["@type"], "SoftwareApplication");
    assert.equal(await page.locator(".download").getAttribute("href"), app.downloadUrl);
    assert.ok(app.downloadUrl.endsWith("/Inklane-Setup-" + app.softwareVersion + "-x64.exe"));
    assert.equal(await page.getByRole("heading", { level: 1 }).count(), 1);
    await page.keyboard.press("Tab");
    assert.equal(await page.locator(":focus").getAttribute("href"), "#main");
    await page.keyboard.press("Enter");
    assert.equal(new URL(page.url()).hash, "#main");
    const frame = page.frameLocator("#review");
    await frame.locator(".marker").first().waitFor();
    assert.equal(await frame.locator(".marker").count(), 2);
    for (const width of [1440, 820, 768, 390, 320]) {
      await page.setViewportSize({ width, height: width > 800 ? 940 : 844 });
      if (width === 390) {
        await page.reload();
        await frame.locator(".marker").first().waitFor();
        assert.equal(await frame.locator("#card").isVisible(), false);
      }
      await page.evaluate(() => scrollTo(0, 0));
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, lang + " " + width);
      if ([1440, 390].includes(width)) {
        await capture(page, lang + "-" + width);
        await capture(page, lang + "-" + width + "-full", true);
      }
      if (width === 320 || width === 1440) await scan(page);
    }
    await page.setViewportSize({ width: 1200, height: 850 });
    await page.locator("header a[href='#workspace']").click();
    if (await frame.locator("#card").isVisible()) await frame.locator("#close-card").click();
    await frame.locator(".marker").first().click();
    await frame.locator("#note-title").fill("Revised plate");
    await frame.locator("#note-body").fill("Keep the pale petal tips.");
    await frame.locator("#close-card").click();
    await page.locator("#expand").click();
    await page.waitForFunction(() => document.querySelector("#expand").getAttribute("aria-pressed") === "true");
    await frame.locator(".marker").first().click();
    assert.equal(await frame.locator("#note-title").inputValue(), "Revised plate");
    assert.equal(await frame.locator("#note-body").inputValue(), "Keep the pale petal tips.");
    await frame.locator("#close-card").click();
    const sheet = await frame.locator("#sheet").boundingBox();
    const spot = (x, y) => ({ x: sheet.x + sheet.width * x, y: sheet.y + sheet.height * y });
    async function place(tool, x, y) {
      await frame.locator("button[data-tool=" + tool + "]").click();
      const p = spot(x, y);
      await page.mouse.click(p.x, p.y);
    }
    await place("text", .17, .73);
    await frame.locator(".placed-editor").fill("Flowering in July");
    await page.keyboard.press("Escape");
    assert.equal(await frame.locator(".placed").textContent(), "Flowering in July");
    await frame.locator("button[data-tool=pen]").click();
    const from = spot(.2,.69), to = spot(.28,.69);
    await page.mouse.move(from.x,from.y);
    await page.mouse.down();
    await page.mouse.move(to.x,to.y,{steps:6});
    await page.mouse.up();
    assert.ok(await frame.locator("#marks [data-id]").count() > 0);
    await place("note", .7, .81);
    await frame.locator("#note-title").fill("Check label");
    await frame.locator("#close-card").click();
    assert.equal(await frame.locator(".marker").count(), 3);
    await frame.locator("button[data-tool=select]").click();
    const pin = frame.getByRole("button", {name:"Check label",exact:true});
    const before = await pin.boundingBox();
    await page.mouse.move(before.x+7,before.y+7);
    await page.mouse.down();
    await page.mouse.move(before.x+48,before.y-22,{steps:8});
    await page.mouse.up();
    assert.ok((await pin.boundingBox()).x > before.x+25, "Moving the remark should move its anchor.");
    await page.locator("#expand").click();
    await page.waitForFunction(() => document.querySelector("#expand").getAttribute("aria-pressed") === "false");
    assert.equal(await frame.locator(".marker").first().getAttribute("aria-label"), "Revised plate");
    assert.equal(await frame.locator("button[data-tool=pen]").isVisible(), true);
    assert.equal(await frame.locator("button[data-tool=text]").isVisible(), true);
    await page.locator("[data-view=image]").click();
    await page.locator("#desktop-shot").evaluate(img => img.decode());
    assert.deepEqual(await page.locator("#desktop-shot").evaluate(img => [img.naturalWidth,img.naturalHeight]), [1484,952]);
    assert.ok((await page.locator(".screenshot").getAttribute("href")).endsWith("assets/image.png"));
    await page.locator("[data-view=notebook]").click();
    assert.ok((await page.locator(".screenshot").getAttribute("href")).endsWith("assets/notebook.png"));
    assert.equal(movieRequests.length, 0, "The optional film loaded before it was opened.");
    await page.locator("#motion-link").click();
    assert.equal(await page.locator("#motion").isVisible(), true);
    assert.equal(await page.locator("#motion-close").evaluate(b => b === document.activeElement), true);
    assert.ok((await page.locator("video").getAttribute("src")).endsWith("/assets/inklane-sting.mp4"));
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => !document.querySelector("video").hasAttribute("src"));
    assert.equal(await page.locator("#motion").isVisible(), false);
    assert.equal(await page.locator("#motion-link").evaluate(b => b === document.activeElement), true);
    assert.equal(await page.locator("video").getAttribute("src"), null);
    await page.emulateMedia({ colorScheme: "dark" });
    await page.evaluate(() => scrollTo(0,0));
    await capture(page, lang + "-dark");
    await scan(page);
    // The product CTA opens an independent, empty workspace. The example never owns that session.
    page.once("dialog", dialog => dialog.accept());
    await page.locator(".intro .solid").click();
    assert.equal(new URL(page.url()).pathname, base + "editor/");
    assert.equal(new URL(page.url()).searchParams.get("lang"), lang);
    assert.equal(new URL(page.url()).searchParams.has("sample"), false);
    assert.deepEqual(errors, []);
    await context.close();
  }
  for (const storageBlocked of [false, true]) {
    const context = await browser.newContext({ locale: "de-AT" });
    if (storageBlocked) await context.addInitScript(() => {
      Object.defineProperty(window, "localStorage", {
        get() { throw new DOMException("Storage disabled", "SecurityError"); }
      });
    });
    const page = await context.newPage();
    await page.goto(local + "#workspace");
    await page.waitForURL(local + "de/#workspace");
    await page.locator("[data-language-link]").click();
    await page.waitForURL(url => url.pathname === base && url.hash === "#workspace");
    await page.reload();
    assert.equal(await page.locator("html").getAttribute("lang"), "en");
    if (!storageBlocked) {
      await page.goto(local + "#download");
      assert.equal(await page.locator("html").getAttribute("lang"), "en");
      await page.locator("[data-language-link]").click();
      await page.waitForURL(local + "de/#download");
      await page.goto(local);
      await page.waitForURL(local + "de/");
    }
    await context.close();
  }
  const unsupported = await browser.newContext({ locale: "fr-CA" });
  const french = await unsupported.newPage();
  await french.goto(local);
  assert.equal(await french.locator("html").getAttribute("lang"), "en");
  await french.goto(local + "de/#workspace");
  assert.equal(await french.locator("html").getAttribute("lang"), "de");
  await unsupported.close();
  const fallback = await browser.newContext({ locale: "fr-CA", javaScriptEnabled: false });
  const page = await fallback.newPage();
  await page.goto(local);
  assert.ok(await page.locator(".download").isVisible());
  assert.ok(await page.locator(".fallback img").isVisible());
  assert.ok(await page.locator("a[data-view=image]").isVisible());
  await page.locator("[data-language-link]").click();
  assert.equal(await page.locator("html").getAttribute("lang"), "de");
  for (const path of ["robots.txt", "sitemap.xml"]) assert.equal((await page.request.get(local + path)).status(), 200);
  await fallback.close();
  console.log("English and German passed at five widths. Checked the real editable sample, fullscreen state, dark and light appearance, desktop screenshot views, opt-in film, download, language preference, no-script fallback, keyboard and accessibility.");
} finally {
  await browser?.close();
  server.close();
}
