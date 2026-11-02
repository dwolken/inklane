import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdir, readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const require = createRequire(import.meta.url);
const dist = resolve(process.argv[2] ?? fileURLToPath(new URL("../.scratch/website/inklane", import.meta.url)));
const types = { ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript",
  ".svg": "image/svg+xml", ".png": "image/png", ".ico": "image/x-icon", ".xml": "application/xml" };
const base = "/inklane/";
const canonical = "https://dwolken.github.io" + base;
const server = createServer(async (request, response) => {
  let path = new URL(request.url, "http://localhost").pathname;
  if (!path.startsWith(base)) return response.writeHead(404).end();
  if (path.endsWith("/")) path += "index.html";
  const relative = path.slice(base.length);
  try {
    const bytes = await readFile(join(dist, relative));
    response.writeHead(200, { "Content-Type": types[extname(relative)] ?? "text/plain" }).end(bytes);
  } catch {
    response.writeHead(404).end();
  }
});
await new Promise(done => server.listen(0, "127.0.0.1", done));
const origin = `http://127.0.0.1:${server.address().port}`;
const local = origin + base;
const browserOptions = process.env.BROWSER_PATH
  ? { executablePath: process.env.BROWSER_PATH }
  : { channel: process.platform === "win32" ? "msedge" : "chrome" };
let browser;
try {
  browser = await chromium.launch(browserOptions);
  for (const lang of ["en", "de"]) {
    const suffix = lang === "en" ? "" : "de/";
    const context = await browser.newContext({ locale: lang === "en" ? "en-GB" : "de-AT" });
    const page = await context.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("response", response => {
      if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`);
    });
    await page.goto(local + suffix);
    assert.equal(await page.locator("html").getAttribute("lang"), lang);
    assert.equal(await page.locator("link[rel=canonical]").getAttribute("href"), canonical + suffix);
    assert.equal(await page.locator("link[hreflang='de']").getAttribute("href"), canonical + "de/");
    assert.equal(await page.locator("link[hreflang='en']").getAttribute("href"), canonical);
    const app = JSON.parse(await page.locator('script[type="application/ld+json"]').textContent());
    assert.equal(app["@type"], "SoftwareApplication");
    const downloads = page.locator("a.button");
    assert.equal(await downloads.count(), 2);
    for (const link of await downloads.all()) assert.equal(await link.getAttribute("href"), app.downloadUrl);
    assert.ok(app.downloadUrl.endsWith(`/Inklane-Setup-${app.softwareVersion}-x64.exe`));
    assert.equal(await page.getByRole("heading", { level: 1 }).count(), 1);
    for (const width of [1440, 820, 768, 390, 320]) {
      await page.setViewportSize({ width, height: width > 800 ? 900 : 844 });
      await page.evaluate(() => scrollTo(0, 0));
      // Decode even off-screen lazy images before inspecting the rendered figures.
      const figures = await page.locator("img.shot").evaluateAll(async images => {
        await Promise.all(images.map(img => { img.loading = "eager"; return img.decode(); }));
        return images.map(img => [img.naturalWidth, img.naturalHeight, img.getAttribute("alt")]);
      });
      assert.deepEqual(figures.map(f => f.slice(0, 2)), [[1484, 952], [1484, 952], [747, 552]]);
      assert.ok(figures.every(f => f[2]?.length > 20));
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth);
      assert.equal(overflow, false, `${lang} horizontal overflow at ${width}px`);
      if (process.env.SITE_CAPTURE_DIR && [1440, 820, 390, 320].includes(width)) {
        await mkdir(process.env.SITE_CAPTURE_DIR, { recursive: true });
        const file = join(process.env.SITE_CAPTURE_DIR, `${lang}-${width}`);
        await page.screenshot({ path: file + ".png" });
        if (width === 1440 || width === 390) await page.screenshot({ path: file + "-full.png", fullPage: true });
      }
      if (width === 320 || width === 1440) {
        await page.addScriptTag({ path: require.resolve("axe-core/axe.min.js") });
        const violations = await page.evaluate(async () => (await axe.run(document, {
          runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa", "best-practice"] }
        })).violations.map(v => [v.id, v.nodes.map(n => n.target)]));
        assert.deepEqual(violations, [], `${lang}, ${width}px: ${JSON.stringify(violations)}`);
      }
    }
    await page.keyboard.press("Tab");
    assert.equal(await page.locator(":focus").getAttribute("href"), "#main");
    await page.keyboard.press("Enter");
    assert.equal(new URL(page.url()).hash, "#main");
    await page.locator("header a[href='#workspace']").click();
    assert.equal(new URL(page.url()).hash, "#workspace");
    await page.locator("a.image-wrap").click();
    assert.equal(new URL(page.url()).pathname, base + "assets/image.png");
    await page.goBack();
    await page.locator("header a[href='#download']").click();
    assert.equal(new URL(page.url()).hash, "#download");
    await page.locator("[data-language-link]").click();
    assert.equal(await page.locator("html").getAttribute("lang"), lang === "en" ? "de" : "en");
    assert.deepEqual(errors, []);
    await context.close();
  }
  for (const storageBlocked of [false, true]) {
    const context = await browser.newContext({ locale: "de-AT" });
    if (storageBlocked) await context.addInitScript(() => {
      Object.defineProperty(window, "localStorage", {
        get() { throw new DOMException("Storage disabled by the user", "SecurityError"); }
      });
    });
    const page = await context.newPage();
    await page.goto(local + "#workspace");
    await page.waitForURL(local + "de/#workspace");
    assert.equal(await page.locator("html").getAttribute("lang"), "de");
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
  assert.equal(await page.locator("html").getAttribute("lang"), "en");
  assert.ok(await page.locator(".button").first().isVisible());
  await page.locator("[data-language-link]").click();
  assert.equal(await page.locator("html").getAttribute("lang"), "de");
  for (const path of ["robots.txt", "sitemap.xml"]) assert.equal((await page.request.get(local + path)).status(), 200);
  await fallback.close();
  console.log("Both languages passed real browser checks at five widths, including keyboard navigation, screenshot links, metadata, download consistency, browser language, explicit preference and blocked-storage behavior, no-JavaScript fallback and accessibility scans.");
} finally {
  await browser?.close();
  server.close();
}
