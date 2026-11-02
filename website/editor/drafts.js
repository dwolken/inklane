// IndexedDB stores immutable source bytes separately from the editable layer.
// Each open tab gets its own record, so concurrent sessions never overwrite each other.
let connection;
function database() {
  return (connection ??= new Promise((resolve, reject) => {
    const request = indexedDB.open("inklane-local-work", 1);
    request.onupgradeneeded = () => {
      for (const name of ["layers", "sources", "entries"]) request.result.createObjectStore(name);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => {
      connection = null;
      reject(request.error);
    };
  }));
}
function done(tx) {
  return new Promise((resolve, reject) => {
    tx.oncomplete = resolve;
    tx.onabort = tx.onerror = () => reject(tx.error);
  });
}
function value(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}
export async function listDrafts() {
  const db = await database();
  return (await value(db.transaction("entries").objectStore("entries").getAll())).sort(
    (a, b) => b.time - a.time
  );
}
export async function readDraft(id) {
  const tx = (await database()).transaction(["layers", "sources"]);
  const [doc, file] = await Promise.all([
    value(tx.objectStore("layers").get(id)),
    value(tx.objectStore("sources").get(id))
  ]);
  if (!doc) throw Error("Missing draft");
  return { doc, file };
}
export async function writeDraft(id, doc, source, includeSource, index) {
  const tx = (await database()).transaction(["layers", "sources", "entries"], "readwrite");
  const result = done(tx);
  if (includeSource && source.file) tx.objectStore("sources").put(source.file, id);
  tx.objectStore("layers").put(doc, id);
  tx.objectStore("entries").put({ id, name: source.name, kind: doc.kind, index, time: Date.now() }, id);
  await result;
}
export async function removeDraft(id) {
  const tx = (await database()).transaction(["layers", "sources", "entries"], "readwrite");
  const result = done(tx);
  for (const store of ["layers", "sources", "entries"]) tx.objectStore(store).delete(id);
  await result;
}
