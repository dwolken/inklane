/* Android transfers a private MessagePort only to the main workspace origin. */
(() => {
  window.inklaneNativeHost = true;
  window.inklaneLaunchMotion = false; // The OS owns the static launch screen on phones/tablets.
  window.inklaneNativePrinting = false;
  let attach;
  const ready = new Promise(resolve => { attach = resolve; });
  window.addEventListener('message', event => {
    if (event.data === 'inklane:files' && event.ports.length === 1) attach(event.ports[0]);
  });
  let active = false;
  window.inklaneSaveCopy = async (blob, name) => {
    if (active) throw new DOMException('A file operation is active.', 'InvalidStateError');
    active = true;
    try {
      let timer;
      let port;
      try {
        port = await Promise.race([
          ready,
          new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Android file picker unavailable.')), 6000); })
        ]);
      } finally { clearTimeout(timer); }
      const send = message => new Promise((resolve, reject) => {
        port.onmessage = event => {
          const reply = JSON.parse(event.data);
          if (reply.status === 'cancel') reject(new DOMException('Cancelled', 'AbortError'));
          else if (reply.status !== 'ok') reject(new Error(reply.error || 'File write failed.'));
          else resolve();
        };
        port.postMessage(JSON.stringify(message));
      });
      await send({ op: 'begin', name, size: blob.size });
      try {
        for (let offset = 0; offset < blob.size; offset += 49152) {
          const bytes = new Uint8Array(await blob.slice(offset, offset + 49152).arrayBuffer());
          let binary = '';
          for (let i = 0; i < bytes.length; i += 8192) binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
          await send({ op: 'chunk', data: btoa(binary) });
        }
      } catch (failure) {
        await send({ op: 'abort' }).catch(() => {});
        throw failure;
      }
      await send({ op: 'save' });
    } finally {
      active = false;
    }
  };
  document.addEventListener('DOMContentLoaded', () => {
    const brand = document.querySelector('a.brand');
    brand?.addEventListener('click', event => {
      event.preventDefault();
      document.getElementById('home')?.click();
    });
  });
})();
