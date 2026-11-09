// Report dependency/evaluation failures instead of leaving an inert Documents page.
import("./editor.js").catch(error => {
  console.error("Workspace initialization failed", error);
  const requested = new URLSearchParams(location.search).get("lang");
  const german = requested === "de" || requested !== "en" && navigator.language?.startsWith("de");
  const panel = document.querySelector("#startup-error");
  document.documentElement.lang = german ? "de" : "en";
  document.title = "Inklane · " + (german ? "Arbeitsbereich" : "Workspace");
  document.body.setAttribute("aria-busy", "false");
  document.querySelector("#name").textContent = german ? "Arbeitsbereich" : "Workspace";
  document.querySelector(".file-actions").hidden = true;
  document.querySelector("#welcome").hidden = true;
  document.querySelector("#state").textContent = german ? "Start fehlgeschlagen" : "Could not start";
  panel.querySelector("p").textContent = german
    ? "Der Arbeitsbereich konnte nicht gestartet werden. Laden Sie ihn erneut."
    : "The workspace could not start. Reload it to try again.";
  const retry = panel.querySelector("button");
  retry.textContent = german ? "Erneut laden" : "Reload";
  retry.onclick = () => location.reload();
  panel.hidden = false;
});
