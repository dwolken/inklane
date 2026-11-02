import { digest, EditorError } from './model.js';

// Page coordinates are the displayed crop box at 96 units/inch, after rotation,
// matching the Windows review surface. Raster resolution never changes an anchor.
const units = 96 / 72;
const library = new URL('./pdf-lib/', import.meta.url);

export async function openPdf(file) {
  if (file.size > 64 * 1024 * 1024) throw new EditorError('size');
  const bytes = new Uint8Array(await file.arrayBuffer());
  const hash = await digest(bytes);
  const pdfjs = await import('./pdf-lib/pdf.min.mjs');
  pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdf.worker.min.mjs', library).href;
  const task = pdfjs.getDocument({
    data: bytes, cMapUrl: new URL('cmaps/', library).href, cMapPacked: true,
    standardFontDataUrl: new URL('standard_fonts/', library).href,
    wasmUrl: new URL('wasm/', library).href, iccUrl: new URL('iccs/', library).href,
    useWorkerFetch: true, useSystemFonts: true, enableXfa: false,
    stopAtErrors: true, canvasMaxAreaInBytes: 32_000_000,
  });
  try {
    const doc = await task.promise;
    if (doc.numPages < 1 || doc.numPages > 1000) throw new EditorError('size');
    const pages = [];
    for (let n = 1; n <= doc.numPages; n++) {
      const page = await doc.getPage(n);
      const { width, height } = page.getViewport({ scale: units });
      if (![width, height].every(v => Number.isFinite(v) && v >= 1 && v <= 100_000)) throw new EditorError('pdfGeometry');
      pages.push({ width, height });
    }
    return {
      name: file.name, mediaType: 'application/pdf', hash, pages,
      async render(index) {
        if (!Number.isInteger(index) || index < 0 || index >= pages.length) throw new EditorError('pdfGeometry');
        const page = await doc.getPage(index + 1);
        const { width, height } = pages[index];
        const scale = Math.min(2, 4096 / Math.max(width, height), Math.sqrt(8_000_000 / (width * height)));
        const viewport = page.getViewport({ scale: units * scale });
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.ceil(viewport.width)); canvas.height = Math.max(1, Math.ceil(viewport.height));
        try {
          await page.render({ canvas, viewport, background: '#ffffff' }).promise;
          return canvas;
        } catch { throw new EditorError('pdfInvalid'); }
        finally { page.cleanup(); }
      },
      dispose() { return task.destroy(); },
    };
  } catch (error) {
    await task.destroy();
    if (error instanceof EditorError) throw error;
    throw new EditorError(error?.name === 'PasswordException' ? 'pdfPassword' : 'pdfInvalid');
  }
}
