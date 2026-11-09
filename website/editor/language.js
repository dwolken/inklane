const labels = {
  colorTarget: ["Color target", "Farbbereich"],
  strokeColor: ["Stroke / text", "Linie / Text"],
  linkText: ["Link text", "Linktext"],
  properties: ["Properties", "Eigenschaften"],
  shape: ["Shape", "Form"],
  shapeHint: [
    "Drag to draw a shape. Choose its kind in Properties.",
    "Ziehen Sie eine Form auf. Den Typ wählen Sie in Eigenschaften.",
  ],
  rectangle: ["Rectangle", "Rechteck"],
  ellipse: ["Ellipse", "Ellipse"],
  diamond: ["Diamond", "Raute"],
  line: ["Line", "Linie"],
  arrow: ["Arrow", "Pfeil"],
  color: ["Color", "Farbe"],
  hex: ["HEX color", "HEX-Farbe"],
  saveColor: ["Save color", "Farbe speichern"],
  recentColors: ["Recent colors", "Zuletzt verwendet"],
  savedColors: [
    "Saved colors · right-click to remove",
    "Gespeicherte Farben · Rechtsklick entfernt",
  ],
  opacity: ["Opacity", "Deckkraft"],
  fill: ["Fill", "Füllung"],
  transparent: ["None", "Keine"],
  strokeWidth: ["Stroke", "Linienstärke"],
  shapeLabel: ["Label", "Beschriftung"],
  connectorHint: [
    "Draw a line or arrow from one object to another to attach it.",
    "Ziehen Sie eine Linie oder einen Pfeil zwischen Objekten, um sie zu verbinden.",
  ],
  font: ["Font", "Schrift"],
  fontSize: ["Font size", "Schriftgröße"],
  bold: ["Bold", "Fett"],
  italic: ["Italic", "Kursiv"],
  underline: ["Underline", "Unterstrichen"],
  alignment: ["Alignment", "Ausrichtung"],
  alignLeft: ["Left", "Links"],
  alignCenter: ["Center", "Zentriert"],
  alignRight: ["Right", "Rechts"],
  alignJustify: ["Justify", "Blocksatz"],
  contentFormat: ["Content", "Inhalt"],
  plainText: ["Plain text", "Einfacher Text"],
  markdownHelp: [
    "Markdown: # heading, **bold**, *italic*, ++underline++, lists, links, tables, fenced code and $LaTeX$. Edit the source; Preview shows the result.",
    "Markdown: # Überschrift, **fett**, *kursiv*, ++unterstrichen++, Listen, Links, Tabellen, Codeblöcke und $LaTeX$. Quelltext bearbeiten, Ergebnis in der Vorschau ansehen.",
  ],
  width: ["Width", "Breite"],
  height: ["Height", "Höhe"],
  rotation: ["Rotation", "Drehung"],
  bringFront: ["Bring to front", "Ganz nach vorne"],
  sendBack: ["Send to back", "Ganz nach hinten"],
  copy: ["Copy", "Kopieren"],
  paste: ["Paste", "Einfügen"],
  group: ["Group", "Gruppieren"],
  ungroup: ["Ungroup", "Gruppe aufheben"],
  saveElement: ["Save as element…", "Als Element speichern …"],
  elements: ["My elements", "Meine Elemente"],
  validHex: [
    "Enter a six-digit HEX color, e.g. #F97316.",
    "Geben Sie sechs HEX-Zeichen ein, z. B. #F97316.",
  ],
  preview: ["Preview", "Vorschau"],
  copiedObjects: ["Selection copied", "Auswahl kopiert"],
  elementName: [
    "Name this reusable element",
    "Dieses wiederverwendbare Element benennen",
  ],
  elementCategory: ["Collection (optional)", "Sammlung (optional)"],
  elementSaved: [
    "Element saved on this device",
    "Element auf diesem Gerät gespeichert",
  ],
  elementStorage: [
    "The element could not be saved. Browser storage may be full.",
    "Das Element konnte nicht gespeichert werden. Der Browserspeicher ist möglicherweise voll.",
  ],
  noElements: [
    "Select objects, then choose Save as element to use them in other documents.",
    "Wählen Sie Objekte und dann Als Element speichern, um sie in anderen Dokumenten zu verwenden.",
  ],
  renameElement: ["Rename", "Umbenennen"],

  rename: ["Rename notebook…", "Notizbuch umbenennen …"],
  moveBack: ["Move earlier", "Nach vorne verschieben"],
  moveForward: ["Move later", "Nach hinten verschieben"],
  copyPage: ["Duplicate page", "Seite duplizieren"],
  deletePage: ["Delete page", "Seite löschen"],
  deletePageConfirm: [
    "Delete this page and its annotations? You can undo this change.",
    "Diese Seite mit ihren Anmerkungen löschen? Sie können dies rückgängig machen.",
  ],
  nativeSaved: ["File saved", "Datei gespeichert"],
  keep: ["Keep a copy", "Kopie behalten"],
  fitPage: ["Fit whole page", "Ganze Seite einpassen"],
  recoverHint: ["Reopen", "Weiterarbeiten"],
  printNotes: ["Anchored notes", "Verankerte Notizen"],
  fileTab: ["Document", "Dokument"],
  newHint: ["On this device", "Auf diesem Gerät"],
  draftStatus: ["Local draft", "Lokaler Entwurf"],
  saveNotebook: ["Save notebook", "Notizbuch speichern"],
  identityPdf: [
    "These annotations belong to a different PDF. Choose the unchanged original.",
    "Diese Anmerkungen gehören zu einer anderen PDF. Wählen Sie das unveränderte Original.",
  ],
  pdfGeometry: [
    "This page exceeds the supported dimensions.",
    "Die Seite überschreitet die unterstützte Größe.",
  ],
  pdfInvalid: [
    "This PDF could not be opened.",
    "Diese PDF konnte nicht geöffnet werden.",
  ],
  pdfPassword: [
    "This PDF is password-protected. Open an unlocked copy.",
    "Diese PDF ist passwortgeschützt. Öffnen Sie eine entsperrte Kopie.",
  ],
  page: ["Page", "Seite"],
  addPage: ["Add page", "Seite hinzufügen"],
  goto: ["Go to page", "Zur Seite"],
  next: ["Next page", "Nächste Seite"],
  prev: ["Previous page", "Vorherige Seite"],
  pages: ["Pages", "Seiten"],
  recovered: ["Recovered on this device", "Auf diesem Gerät wiederhergestellt"],
  forgetConfirm: [
    "Remove this draft from this browser? Downloaded files stay on your device.",
    "Diesen Entwurf aus dem Browser entfernen? Heruntergeladene Dateien bleiben auf Ihrem Gerät.",
  ],
  forget: ["Remove local draft", "Lokalen Entwurf entfernen"],
  draftError: [
    "Local recovery is unavailable. Save a file to keep your work.",
    "Lokale Wiederherstellung nicht verfügbar. Speichern Sie Ihre Arbeit als Datei.",
  ],
  portableExtension: [
    "Choose a name ending in .inklane.",
    "Wählen Sie einen Dateinamen mit der Endung .inklane.",
  ],
  print: ["Print review…", "Mit Anmerkungen drucken …"],
  printTitle: ["Print review", "Mit Anmerkungen drucken"],
  allPages: ["All pages", "Alle Seiten"],
  currentPage: ["Current page", "Aktuelle Seite"],
  continuePrint: ["Continue to print", "Weiter zum Drucken"],
  preparingPrint: ["Preparing print…", "Druck wird vorbereitet …"],
  printLimit: [
    "For a document this size, print one page at a time.",
    "Dokumente dieser Größe lassen sich seitenweise drucken.",
  ],
  printBounds: [
    "The selected sheet exceeds the print size limit of 5.08 metres.",
    "Das gewählte Blatt überschreitet die Druckgrößenbeschränkung von 5,08 Metern.",
  ],
  printFailed: [
    "Could not prepare the print copy. Your original and edits are unchanged.",
    "Die Druckfassung konnte nicht erstellt werden. Original und Anmerkungen sind weiterhin verfügbar.",
  ],
  portableDone: [
    "Workspace download requested. Open the .inklane file to continue.",
    "Download angefordert. Öffnen Sie die .inklane-Datei zum Weiterarbeiten.",
  ],
  portable: ["Download portable workspace", "Arbeitsmappe herunterladen"],
  sampleBody2: [
    "Add a scale bar beside the seed head before printing.",
    "Vor dem Druck einen Maßstabsbalken neben dem Fruchtstand ergänzen.",
  ],
  sampleTitle2: ["Scale", "Maßstab"],
  sampleBody: [
    "Keep the pale tips. They distinguish this specimen from the earlier study.",
    "Die hellen Spitzen bitte beibehalten. Sie unterscheiden dieses Exemplar von der früheren Studie.",
  ],
  sampleTitle: ["Petal colour", "Blütenfarbe"],
  sampleName: ["Field guide.png", "Feldführer.png"],
  openSample: ["Open the field guide", "Feldführer öffnen"],
  recent: ["On this device", "Auf diesem Gerät"],
  untitled: ["Untitled notebook", "Unbenanntes Notizbuch"],
  notebookName: ["Notebook name", "Name des Notizbuchs"],
  squared: ["Squared", "Kariert"],
  ruled: ["Ruled", "Liniert"],
  blank: ["Blank", "Blanko"],
  customSpacing: ["Spacing in mm", "Abstand in mm"],
  custom: ["Custom", "Eigener Wert"],
  spacing: ["Spacing", "Abstand"],
  sizeLabel: ["Page size", "Papierformat"],
  landscape: ["Landscape", "Querformat"],
  portrait: ["Portrait", "Hochformat"],
  orientation: ["Orientation", "Ausrichtung"],
  paper: ["Paper", "Papier"],
  createBook: ["Create notebook", "Notizbuch anlegen"],
  create: ["New notebook", "Neues Notizbuch"],
  paperTypes: ["Blank, ruled or squared paper", "Blanko, liniert oder kariert"],
  supported: ["PDF, PNG, JPEG or Inklane", "PDF, PNG, JPEG oder Inklane"],
  home: ["Documents", "Dokumente"],
  feedbackOnly: [
    "This is an exported feedback report. To edit, open the image with its .inklane.json annotation file.",
    "Dies ist ein exportierter Feedbackbericht. Zum Bearbeiten öffnen Sie das Bild mit seiner .inklane.json-Anmerkungsdatei.",
  ],
  resize: ["Resize picture", "Bildgröße ändern"],
  sizeAction: ["Size…", "Größe …"],
  pictureWidth: ["Width in image pixels", "Breite in Bildpixeln"],
  feedback: ["Export feedback…", "Feedback exportieren …"],
  feedbackDownload: [
    "Feedback JSON download requested.",
    "Feedback-JSON zum Download angefordert.",
  ],
  saveShort: ["Save", "Speichern"],
  index: ["Browse annotations", "Anmerkungen durchsuchen"],
  search: ["Find an annotation", "Anmerkung suchen"],
  empty: ["No matching annotations", "Keine passenden Anmerkungen"],
  relink: ["Choose the original file", "Originaldatei auswählen"],
  relinkHint: [
    "Open the unchanged source to place these annotations.",
    "Öffnen Sie das unveränderte Original für diese Anmerkungen.",
  ],
  cancel: ["Cancel", "Abbrechen"],
  inkColor: ["Ink color", "Stiftfarbe"],
  inkSize: ["Stroke width", "Strichbreite"],
  workspace: ["Workspace", "Arbeitsbereich"],
  browser: ["Browser edition", "Browser-Version"],
  open: ["Open", "Öffnen"],
  save: ["Save annotations", "Anmerkungen speichern"],
  more: ["More actions", "Weitere Aktionen"],
  download: ["Download annotation copy", "Anmerkungen herunterladen"],
  guide: ["How to use the workspace", "Hilfe zum Arbeitsbereich"],
  welcome: ["Mark up an image", "Ein Bild kommentieren"],
  intro: [
    "Open your work to review it, or start with a fresh page.",
    "Öffnen Sie ein Dokument zum Kommentieren oder beginnen Sie mit einem leeren Blatt.",
  ],
  choose: ["Open document", "Dokument öffnen"],
  reopen: [
    "To reopen saved work, choose the image and its .inklane.json file together.",
    "Zum Weiterarbeiten wählen Sie das Bild zusammen mit seiner .inklane.json-Datei aus.",
  ],
  local: [
    "Files stay on this device.",
    "Ihre Dateien bleiben auf diesem Gerät.",
  ],
  tools: ["Editing tools", "Werkzeuge"],
  surface: ["Document and annotations", "Dokument mit Anmerkungen"],
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
  fit: ["Fit document", "Dokument einpassen"],
  out: ["Zoom out", "Verkleinern"],
  in: ["Zoom in", "Vergrößern"],
  title: ["Note title", "Notiztitel"],
  noteBody: ["Note contents", "Notizinhalt"],
  collapse: ["Collapse note", "Notiz einklappen"],
  bodyPlaceholder: ["Write your note…", "Notiz schreiben …"],
  done: ["Done", "Fertig"],
  ready: ["Local document", "Lokales Dokument"],
  changed: ["Unsaved changes", "Ungespeicherte Änderungen"],
  saved: ["Annotations saved", "Anmerkungen gespeichert"],
  loaded: ["Annotations opened", "Anmerkungen geöffnet"],
  loading: ["Opening files…", "Dateien werden geöffnet …"],
  saving: ["Saving…", "Wird gespeichert …"],
  downloaded: [
    "Download requested. Keep the JSON file with the original.",
    "Download angefordert. Bewahren Sie die JSON-Datei mit dem Original auf.",
  ],
  helpOpen: [
    "Open a PDF, PNG, JPEG or portable Inklane file. To restore an annotation layer, open its JSON file and choose the unchanged source.",
    "Öffnen Sie eine PDF-, PNG-, JPEG- oder Inklane-Datei. Zum Laden gespeicherter Anmerkungen öffnen Sie die JSON-Datei und wählen die unveränderte Quelldatei.",
  ],
  helpEdit: [
    "Select an object to move it, use the arrow keys to nudge it, or press F2 to edit text. Click a note number to read or collapse the card. Escape finishes editing. Ctrl/Cmd+Z undoes an edit.",
    "Verschieben Sie ein ausgewähltes Objekt mit der Maus oder den Pfeiltasten. F2 bearbeitet Text. Klicken Sie auf eine Notiznummer, um die Karte zu öffnen oder einzuklappen. Esc beendet die Texteingabe. Strg/Cmd+Z macht Änderungen rückgängig.",
  ],
  helpSave: [
    "A portable workspace includes the source and the editable marks. Saving annotations for an image or PDF writes a separate layer. Check your downloads before closing the tab.",
    "Eine Arbeitsmappe enthält das Original und die bearbeitbaren Markierungen. Für Bilder und PDFs können Sie die Anmerkungen getrennt speichern. Prüfen Sie Ihre Downloads vor dem Schließen.",
  ],
  selectHint: [
    "Select a mark to move or edit it.",
    "Zum Verschieben oder Bearbeiten eine Markierung auswählen.",
  ],
  penHint: ["Draw on the page.", "Auf der Seite zeichnen."],
  textHint: ["Click to place text.", "Klicken, um Text einzufügen."],
  noteHint: [
    "Click a detail to anchor a note.",
    "Für eine verankerte Notiz auf ein Detail klicken.",
  ],
  panHint: ["Drag to pan.", "Ziehen, um die Ansicht zu verschieben."],
  discard: [
    "Discard unsaved changes and open the selected file?",
    "Ungespeicherte Änderungen verwerfen und die ausgewählte Datei öffnen?",
  ],
  imageNeeded: [
    "Choose the original image together with its annotation file.",
    "Bitte das Originalbild zusammen mit der Anmerkungsdatei auswählen.",
  ],
  files: [
    "Choose one PDF, PNG, JPEG or Inklane workspace. A PDF or image can be opened with its annotation JSON.",
    "Wählen Sie eine PDF-, PNG-, JPEG- oder Inklane-Datei. Bilder und PDFs lassen sich zusammen mit ihrer Anmerkungsdatei öffnen.",
  ],
  invalid: [
    "This annotation file contains invalid or unsupported data.",
    "Die Anmerkungsdatei enthält ungültige oder nicht unterstützte Daten.",
  ],
  version: [
    "Supported layers use image versions 1 and 2 or PDF version 3.",
    "Unterstützt werden Bildanmerkungen der Versionen 1 und 2 sowie PDF-Anmerkungen der Version 3.",
  ],
  identity: [
    "These annotations belong to a different image. Choose the unchanged original.",
    "Diese Anmerkungen gehören zu einem anderen Bild. Bitte das unveränderte Original auswählen.",
  ],
  size: [
    "This file exceeds the browser editor’s size limits.",
    "Diese Datei überschreitet die Größenbeschränkung des Browser-Editors.",
  ],
  decode: [
    "The image could not be read as PNG or JPEG.",
    "Das Bild konnte nicht als PNG oder JPEG gelesen werden.",
  ],
  fileError: [
    "Could not read the selected file.",
    "Die ausgewählte Datei konnte nicht gelesen werden.",
  ],
  saveError: [
    "Could not save the annotations. Your edits are still open.",
    "Die Anmerkungen konnten nicht gespeichert werden. Ihre Änderungen sind weiterhin geöffnet.",
  ],
  conflict: [
    "Choose a new filename. The file differs from the copy you opened.",
    "Bitte einen neuen Dateinamen wählen. Die gewählte Datei weicht von der geöffneten Kopie ab.",
  ],
  extension: [
    "Choose a filename ending in .inklane.json.",
    "Bitte einen Dateinamen mit der Endung .inklane.json wählen.",
  ],
  secure: [
    "Open this workspace over HTTPS or localhost to verify image files.",
    "Öffnen Sie den Arbeitsbereich über HTTPS oder localhost, damit Bilddateien geprüft werden können.",
  ],
  clipboard: [
    "Could not use this clipboard content.",
    "Der Inhalt der Zwischenablage konnte nicht verwendet werden.",
  ],
  picture: ["Picture", "Bild"],
  stroke: ["Ink stroke", "Stiftstrich"],
  placed: ["Placed text", "Textfeld"],
  nativeNote: ["Note", "Notiz"],
  selectHintKeyboard: [
    "Arrow keys move · Delete removes",
    "Pfeiltasten verschieben · Entf löscht",
  ],
  loadingWait: [
    "Please finish the current file operation first.",
    "Bitte zuerst den laufenden Dateivorgang abschließen.",
  ],
};
const query = new URLSearchParams(location.search).get("lang");
let language =
  query === "de" || query === "en"
    ? query
    : navigator.language?.startsWith("de")
      ? "de"
      : "en";
export const t = (key) => labels[key]?.[language === "de" ? 1 : 0] ?? key;
export function translate() {
  document.documentElement.lang = language;
  document.title = "Inklane · " + t("workspace");
  document
    .querySelectorAll("[data-i]")
    .forEach((el) => (el.textContent = t(el.dataset.i)));
  document.querySelectorAll("[data-label]").forEach((el) => {
    el.setAttribute("aria-label", t(el.dataset.label));
    if (el.matches("button,summary")) el.title = t(el.dataset.label);
  });
  document.querySelector("#language").textContent =
    language === "de" ? "English" : "Deutsch";
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
