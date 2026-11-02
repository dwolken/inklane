// The disclosure and its editable text work without JavaScript as well.
for (const note of document.querySelectorAll(".sample-detail")) {
  note.addEventListener("keydown", event => {
    if (event.key !== "Escape" || !note.open) return;
    event.preventDefault();
    note.open = false;
    note.querySelector("summary").focus();
  });
}
