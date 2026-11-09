// Runs inside the actual native WebView; it never replaces the native save adapter.
async (action, payload) => {
  const hash = async bytes => [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))]
    .map(value => value.toString(16).padStart(2, "0")).join("");
  if (action === "fixture") {
    const document = JSON.parse(payload);
    document.title = "Native PDF review";
    const note = document.pages[0].items.find(item => item.isNote);
    note.color = "#138A36";
    note.text = "# Complete native review\n\nLocal math: $x^2$.\n\n" +
      Array.from({ length: 80 }, (_, index) => `Paragraph ${index + 1}. Keep every part of this explanation readable at its original size when the note continues onto another sheet.`).join("\n\n") +
      "\n\nFINAL GREEN PARAGRAPH — complete content through the last page.";
    const metadata = new TextEncoder().encode(JSON.stringify({ format: "inklane.workspace", version: 1, sourceName: document.title, document }));
    const bytes = new Uint8Array(12 + metadata.length);
    bytes.set(new TextEncoder().encode("INKLANE1"));
    new DataView(bytes.buffer).setUint32(8, metadata.length);
    bytes.set(metadata, 12);
    let binary = "";
    for (let start = 0; start < bytes.length; start += 8192) binary += String.fromCharCode(...bytes.subarray(start, start + 8192));
    return btoa(binary);
  }
  if (action === "observe") {
    globalThis.nativePdfOriginal = globalThis.inklaneSaveCopy;
    globalThis.inklaneSaveCopy = async (blob, name) => {
      if (name.endsWith(".pdf")) globalThis.nativePdfExpected = { name, size: blob.size, hash: await hash(await blob.arrayBuffer()) };
      return nativePdfOriginal(blob, name);
    };
    return true;
  }
  if (action === "restore") {
    globalThis.inklaneSaveCopy = globalThis.nativePdfOriginal;
    delete globalThis.nativePdfOriginal;
    return true;
  }
  if (action !== "inspect") throw new Error("Unknown PDF host check");
  const bytes = Uint8Array.from(atob(payload), value => value.charCodeAt(0));
  const { openPdf } = await import("./pdf-source.js");
  const pdf = await openPdf(new File([bytes], "Native.pdf", { type: "application/pdf" }));
  try {
    const first = await pdf.render(0);
    const pixel = [...first.getContext("2d").getImageData(Math.floor(first.width * 90 / 600), Math.floor(first.height * 380 / 800), 1, 1).data];
    const last = await pdf.render(pdf.pages.length - 1), data = last.getContext("2d").getImageData(0, 0, last.width, last.height).data;
    const mx = Math.ceil(last.width * 15 / 210), my = Math.ceil(last.height * 15 / 297);
    let green = 0;
    for (let y = my; y < last.height - my; y++) for (let x = mx; x < last.width - mx; x++) {
      const p = (y * last.width + x) * 4;
      if (data[p + 1] > data[p] + 30 && data[p + 1] > data[p + 2] + 25 && data[p + 3] > 200) green++;
    }
    return { pages: pdf.pages.length, pixel, green, hash: await hash(bytes), size: bytes.length };
  } finally { await pdf.dispose(); }
}
