// Injected only in the local top-level workspace by WebKit.
globalThis.inklaneNativeHost = true;
globalThis.inklaneNativePrinting = false;
window.addEventListener("DOMContentLoaded", () => {
  const brand = document.querySelector("header .brand");
  brand?.addEventListener("click", event => {
    event.preventDefault();
    if (document.querySelector("#welcome")?.hidden) document.querySelector("#home")?.click();
  });
});

globalThis.inklaneSaveCopy = async (blob, filename) => {
  const bridge = window.webkit.messageHandlers.exportFile;
  let token;
  try {
    token = await bridge.postMessage({ op: "begin", filename, size: blob.size });
    for (let start = 0; start < blob.size; start += 196608) {
      const bytes = new Uint8Array(await blob.slice(start, start + 196608).arrayBuffer());
      const parts = [];
      for (let i = 0; i < bytes.length; i += 8192) {
        parts.push(String.fromCharCode(...bytes.subarray(i, i + 8192)));
      }
      await bridge.postMessage({ op: "append", token, bytes: btoa(parts.join("")) });
    }
    const result = await bridge.postMessage({ op: "export", token });
    if (result === "cancelled") throw new DOMException("Cancelled", "AbortError");
  } catch (error) {
    if (token) await bridge.postMessage({ op: "discard", token }).catch(() => {});
    throw error;
  }
};
