import assert from "node:assert/strict";
import { resolve } from "node:path";

export async function verifyStartup(browser, base, output) {
  for (const scenario of [
    { name: "desktop-light", width: 1200, scheme: "light", motion: true },
    { name: "desktop-dark", width: 1200, scheme: "dark", motion: true },
    { name: "reduced-motion", width: 1200, scheme: "light", reduced: "reduce", motion: false },
    { name: "native-reduced-motion", width: 1200, scheme: "dark", hostReduced: true, motion: false },
    { name: "native-motion-change", width: 1200, scheme: "light", liveReduced: true, motion: true },
    { name: "mobile-light", width: 320, scheme: "light", nativeMobile: true, motion: false },
    { name: "mobile-dark", width: 390, scheme: "dark", nativeMobile: true, motion: false },
  ]) {
    const context = await browser.newContext({
      viewport: { width: scenario.width, height: 820 }, locale: "en-US",
      colorScheme: scenario.scheme, reducedMotion: scenario.reduced || "no-preference",
    });
    try {
      await context.addInitScript(({ nativeMobile, hostReduced, liveReduced }) => {
        if (nativeMobile) globalThis.inklaneLaunchMotion = false;
        if (hostReduced) globalThis.inklaneReducedMotion = true;
        globalThis.launchObservations = [];
        document.addEventListener("animationstart", event => {
          if (event.animationName !== "brand-arrive") return;
          const start = document.querySelector("#start"), open = document.querySelector("#open");
          const box = start?.getBoundingClientRect();
          const top = box && document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
          launchObservations.push({
            ready: Boolean(start && !start.disabled && open && !open.disabled),
            blocked: Boolean(start?.inert || start?.closest("[inert]")),
            unobstructed: Boolean(top && start.contains(top)),
          });
          if (liveReduced) {
            globalThis.inklaneReducedMotion = true;
            dispatchEvent(new Event("inklane-motion-change"));
            globalThis.cancelledDuringLaunch = !document.querySelector(".brand").classList.contains("launch");
          }
        });
      }, scenario);
      const page = await context.newPage();
      await page.goto(base + "editor/?lang=en");
      await page.locator("#start").waitFor();
      if (scenario.motion) await page.waitForFunction(() => launchObservations.length > 0);
      await page.waitForFunction(() => !document.querySelector(".brand").classList.contains("launch"));
      const observations = await page.evaluate(() => launchObservations);
      assert.deepEqual(observations, scenario.motion ? [{ ready: true, blocked: false, unobstructed: true }] : [], scenario.name);
      if (scenario.liveReduced) assert.equal(await page.evaluate(() => cancelledDuringLaunch), true,
        "A native preference change must cancel the active launch immediately");
      const expected = `inklane-${scenario.width <= 660 ? "symbol" : "lockup"}-${scenario.scheme}.svg`;
      await page.waitForFunction(expected => {
        const img = document.querySelector(".brand img");
        return img.complete && img.naturalWidth > 0 && img.currentSrc.endsWith(expected);
      }, expected);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), scenario.name + " fits the viewport");
      await page.screenshot({ path: resolve(output, `startup-${scenario.name}.png`) });
      // The identity remains static while using the app and returning to Documents.
      await page.locator("#new-notebook").click();
      await page.locator("#paper-form button.primary").click();
      await page.locator("#more summary").click();
      await page.locator("#home").click();
      await page.locator("#start").waitFor();
      assert.deepEqual(await page.evaluate(() => launchObservations), observations, "Home must not replay the launch");
      await page.reload();
      await page.locator("#start").waitFor();
      assert.deepEqual(await page.evaluate(() => launchObservations), [], "Reload must not replay the launch");
    } finally { await context.close(); }
  }
  console.log("Launch identity passed immediate access, once-per-session motion, Home/reload, OS reduced motion, native mobile static launch, responsive light/dark assets.");
}
