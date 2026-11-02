const labels = {
  feedbackOnly: [
    "This is an exported feedback report. To edit, open the image with its .inklane.json annotation file.",
    "Dies ist ein exportierter Feedbackbericht. Zum Bearbeiten öffnen Sie das Bild mit seiner .inklane.json-Anmerkungsdatei."
  ],
  resize: ["Resize picture", "Bildgröße ändern"],
  sizeAction: ["Size…", "Größe …"],
  pictureWidth: ["Width in image pixels", "Breite in Bildpixeln"],
  feedback: ["Export feedback…", "Feedback exportieren …"],
  feedbackDownload: ["Feedback JSON download requested.", "Feedback-JSON zum Download angefordert."],
  saveShort: ["Save", "Speichern"],
  index: ["Browse annotations", "Anmerkungen durchsuchen"],
  search: ["Find an annotation", "Anmerkung suchen"],
  empty: ["No matching annotations", "Keine passenden Anmerkungen"],
  relink: ["Choose the original image", "Originalbild auswählen"],
  relinkHint: [
    "This annotation file contains the marks. Open the original PNG or JPEG to place them on the image.",
    "Diese Datei enthält die Markierungen. Öffnen Sie das ursprüngliche PNG- oder JPEG-Bild, um sie anzuzeigen."
  ],
  cancel: ["Cancel", "Abbrechen"],
  inkColor: ["Ink color", "Stiftfarbe"],
  inkSize: ["Stroke width", "Strichbreite"],
  workspace: ["Image workspace", "Bildarbeitsplatz"],
  browser: ["Browser edition", "Browser-Version"],
  open: ["Open", "Öffnen"],
  save: ["Save annotations", "Anmerkungen speichern"],
  more: ["More actions", "Weitere Aktionen"],
  download: ["Download annotation copy", "Anmerkungen herunterladen"],
  guide: ["How to use the workspace", "Hilfe zum Arbeitsbereich"],
  welcome: ["Mark up an image", "Ein Bild kommentieren"],
  intro: [
    "Draw, place text or pin a longer note to a detail.",
    "Zeichnen Sie, fügen Sie Text ein oder heften Sie eine Notiz an ein Detail."
  ],
  choose: ["Choose an image", "Bild auswählen"],
  reopen: [
    "To reopen saved work, choose the image and its .inklane.json file together.",
    "Zum Weiterarbeiten wählen Sie das Bild zusammen mit seiner .inklane.json-Datei aus."
  ],
  local: ["Files stay on this device.", "Ihre Dateien bleiben auf diesem Gerät."],
  tools: ["Editing tools", "Werkzeuge"],
  surface: ["Image and annotations", "Bild mit Anmerkungen"],
  select: ["Select", "Auswahl"],
  pen: ["Pen", "Stift"],
  text: ["Text", "Text"],
  note: ["Note", "Notiz"],
  pan: ["Pan", "Hand"],
  edit: ["Edit", "Bearbeiten"],
  duplicate: ["Duplicate", "Duplizieren"],
  remove: ["Delete", "Löschen"],
  undo: ["Undo", "Rückgängig"],
  redo: ["Redo", "Wiederholen"],
  fit: ["Fit image", "Bild einpassen"],
  out: ["Zoom out", "Verkleinern"],
  in: ["Zoom in", "Vergrößern"],
  title: ["Note title", "Notiztitel"],
  noteBody: ["Note contents", "Notizinhalt"],
  collapse: ["Collapse note", "Notiz einklappen"],
  bodyPlaceholder: ["Write your note…", "Notiz schreiben …"],
  done: ["Done", "Fertig"],
  ready: ["Local image", "Lokales Bild"],
  changed: ["Unsaved changes", "Ungespeicherte Änderungen"],
  saved: ["Annotations saved", "Anmerkungen gespeichert"],
  loaded: ["Annotations opened", "Anmerkungen geöffnet"],
  loading: ["Opening files…", "Dateien werden geöffnet …"],
  saving: ["Saving…", "Wird gespeichert …"],
  downloaded: [
    "Download requested. Keep the JSON file with the original image.",
    "Download angefordert. Bewahren Sie die JSON-Datei beim Originalbild auf."
  ],
  helpOpen: [
    "Open a PNG or JPEG. To continue an earlier session, select the original image and its annotation file together. With the image open, you can also open the JSON file separately.",
    "Öffnen Sie eine PNG- oder JPEG-Datei. Zum Weiterarbeiten wählen Sie das Originalbild zusammen mit der zugehörigen Anmerkungsdatei aus. Bei geöffnetem Bild können Sie die JSON-Datei auch einzeln öffnen."
  ],
  helpEdit: [
    "Select an object to move it, use the arrow keys to nudge it, or press F2 to edit text. Click a note number to read or collapse the card. Escape finishes editing. Ctrl/Cmd+Z undoes an edit.",
    "Verschieben Sie ein ausgewähltes Objekt mit der Maus oder den Pfeiltasten. F2 bearbeitet Text. Klicken Sie auf eine Notiznummer, um die Karte zu öffnen oder einzuklappen. Esc beendet die Texteingabe. Strg/Cmd+Z macht Änderungen rückgängig."
  ],
  helpSave: [
    "Save annotations writes a separate .inklane.json file. If your browser only supports downloads, check that the download finished before closing the tab. Keep the image and JSON together to reopen in Inklane.",
    "Speichern legt eine separate .inklane.json-Datei an. Unterstützt Ihr Browser nur Downloads, prüfen Sie vor dem Schließen, ob die Datei heruntergeladen wurde. Zum erneuten Öffnen in Inklane benötigen Sie das Bild und die JSON-Datei."
  ],
  selectHint: [
    "Select a mark to move or edit it.",
    "Zum Verschieben oder Bearbeiten eine Markierung auswählen."
  ],
  penHint: ["Draw directly on the image.", "Direkt auf das Bild zeichnen."],
  textHint: ["Click the image to place text.", "Zum Platzieren von Text ins Bild klicken."],
  noteHint: ["Click a detail to anchor a note.", "Für eine verankerte Notiz auf ein Detail klicken."],
  panHint: ["Drag to move around the image.", "Ziehen, um die Ansicht zu verschieben."],
  discard: [
    "Discard unsaved changes and open the selected file?",
    "Ungespeicherte Änderungen verwerfen und die ausgewählte Datei öffnen?"
  ],
  imageNeeded: [
    "Choose the original image together with its annotation file.",
    "Bitte das Originalbild zusammen mit der Anmerkungsdatei auswählen."
  ],
  files: [
    "Choose one PNG or JPEG, optionally with its annotation JSON.",
    "Wählen Sie eine PNG- oder JPEG-Datei, gegebenenfalls mit ihrer Anmerkungsdatei."
  ],
  invalid: [
    "This annotation file contains invalid or unsupported data.",
    "Die Anmerkungsdatei enthält ungültige oder nicht unterstützte Daten."
  ],
  version: [
    "This editor supports Inklane image annotations in versions 1 and 2.",
    "Dieser Editor unterstützt Inklane-Bildanmerkungen der Versionen 1 und 2."
  ],
  identity: [
    "These annotations belong to a different image. Choose the unchanged original.",
    "Diese Anmerkungen gehören zu einem anderen Bild. Bitte das unveränderte Original auswählen."
  ],
  size: [
    "This file exceeds the browser editor’s size limits.",
    "Diese Datei überschreitet die Größenbeschränkung des Browser-Editors."
  ],
  decode: [
    "The image could not be read as PNG or JPEG.",
    "Das Bild konnte nicht als PNG oder JPEG gelesen werden."
  ],
  fileError: ["Could not read the selected file.", "Die ausgewählte Datei konnte nicht gelesen werden."],
  saveError: [
    "Could not save the annotations. Your edits are still open.",
    "Die Anmerkungen konnten nicht gespeichert werden. Ihre Änderungen sind weiterhin geöffnet."
  ],
  conflict: [
    "Choose a new .inklane.json filename. The chosen file is different from the copy you opened.",
    "Bitte einen neuen .inklane.json-Dateinamen wählen. Die gewählte Datei weicht von der geöffneten Kopie ab."
  ],
  extension: [
    "Choose a filename ending in .inklane.json.",
    "Bitte einen Dateinamen mit der Endung .inklane.json wählen."
  ],
  secure: [
    "Open this workspace over HTTPS or localhost to verify image files.",
    "Öffnen Sie den Arbeitsbereich über HTTPS oder localhost, damit Bilddateien geprüft werden können."
  ],
  clipboard: [
    "Could not use this clipboard content.",
    "Der Inhalt der Zwischenablage konnte nicht verwendet werden."
  ],
  picture: ["Picture", "Bild"],
  stroke: ["Ink stroke", "Stiftstrich"],
  placed: ["Placed text", "Textfeld"],
  nativeNote: ["Note", "Notiz"],
  selectHintKeyboard: ["Arrow keys move · Delete removes", "Pfeiltasten verschieben · Entf löscht"],
  loadingWait: [
    "Please finish the current file operation first.",
    "Bitte zuerst den laufenden Dateivorgang abschließen."
  ]
};
const query = new URLSearchParams(location.search).get("lang");
let language = query === "de" || query === "en" ? query : navigator.language?.startsWith("de") ? "de" : "en";
export const t = (key) => labels[key]?.[language === "de" ? 1 : 0] ?? key;
export function translate() {
  document.documentElement.lang = language;
  document.title = "Inklane · " + t("workspace");
  document.querySelectorAll("[data-i]").forEach((el) => (el.textContent = t(el.dataset.i)));
  document.querySelectorAll("[data-label]").forEach((el) => {
    el.setAttribute("aria-label", t(el.dataset.label));
    if (el.matches("button,summary")) el.title = t(el.dataset.label);
  });
  document.querySelector("#language").textContent = language === "de" ? "English" : "Deutsch";
  document.querySelector("#note-body").placeholder = t("bodyPlaceholder");
  document.querySelector(".brand").href = language === "de" ? "../de/" : "../";
}
export function switchLanguage() {
  language = language === "de" ? "en" : "de";
  const url = new URL(location.href);
  url.searchParams.set("lang", language);
  history.replaceState(null, "", url);
  translate();
}
