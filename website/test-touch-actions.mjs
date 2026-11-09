import assert from "node:assert/strict";

export async function verifyTouchActions(browser, base) {
  for (const input of ["mouse", "touch"]) {
    const context = await browser.newContext({ viewport: { width: 390, height: 820 }, isMobile: true, hasTouch: true });
    try {
      const page = await context.newPage();
      await page.goto(base + "editor/?lang=en");
      await page.locator("#new-notebook").click();
      await page.locator("#paper-form button.primary").click();
      await page.locator("[data-tool=note]").click();
      const sheet = await page.locator("#sheet").boundingBox();
      await page.touchscreen.tap(sheet.x + sheet.width * .48, sheet.y + sheet.height * .19);
      await page.locator("#note-body").fill("Keep this text when the keyboard closes.");
      await page.evaluate(() => {
        document.querySelector("#note-body").focus();
        // Model the layout change caused by a native keyboard dismissing on
        // focus transfer. The original click was lost as the card moved away.
        document.querySelector("#close-card").addEventListener("focus", () => {
          document.querySelector("#card").style.transform = "translateY(100px)";
        }, { once: true });
      });
      const close = await page.locator("#close-card").boundingBox();
      const x = close.x + close.width / 2, y = close.y + close.height / 2;
      if (input === "touch") await page.touchscreen.tap(x, y);
      else await page.mouse.click(x, y);
      await page.waitForFunction(() => document.querySelector("#card").hidden);
      assert.equal(await page.locator(".marker").getAttribute("aria-expanded"), "false");
      assert.equal(await page.evaluate(() => document.activeElement.id), "viewport", "Collapsing returns focus to the document");
      await page.locator(".marker").tap();
      await page.locator("#note-body").waitFor();
      assert.equal(await page.locator("#note-body").inputValue(), "Keep this text when the keyboard closes.");
      // Keyboard activation keeps normal button semantics as well.
      await page.locator("#close-card").focus();
      await page.keyboard.press("Enter");
      await page.waitForFunction(() => document.querySelector("#card").hidden);
    } finally { await context.close(); }
  }
  console.log("Mouse, touch and keyboard collapse preserve remark text across focus-driven layout changes.");
}
