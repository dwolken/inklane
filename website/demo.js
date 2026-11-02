const stage = document.querySelector("#stage");
const template = document.querySelector("#workspace-template");
template.replaceWith(template.content.cloneNode(true));
const review = document.querySelector("#review");
const expand = document.querySelector("#expand");
const surrounding = [...document.querySelectorAll(".skip,.topbar,.intro,.workspace-foot,.desktop,footer")];
let expandedInPage = false;
expand.hidden = false;

function showFrameState() {
  const expanded = expandedInPage || document.fullscreenElement === stage;
  expand.setAttribute("aria-pressed", String(expanded));
  expand.querySelector("span").textContent = expanded ? expand.dataset.exit : expand.dataset.enter;
}
function usePageExpansion(expanded) {
  expandedInPage = expanded;
  stage.classList.toggle("expanded", expanded);
  document.body.classList.toggle("has-expanded-stage", expanded);
  surrounding.forEach(node => { node.inert = expanded; });
  showFrameState();
  expand.focus();
}
expand.addEventListener("click", async () => {
  if (expandedInPage) return usePageExpansion(false);
  if (document.fullscreenElement === stage) return document.exitFullscreen();
  // Expanding the existing frame preserves marks, selection and undo history.
  try {
    if (document.fullscreenEnabled && stage.requestFullscreen) {
      await stage.requestFullscreen();
      return;
    }
  } catch { /* A browser policy may disable native fullscreen. */ }
  usePageExpansion(true);
});
document.addEventListener("fullscreenchange", showFrameState);
function escapePageExpansion(event) {
  if (event.key !== "Escape" || !expandedInPage || event.defaultPrevented) return;
  if (event.target?.closest?.("input,textarea,[contenteditable=true],dialog[open]")) return;
  usePageExpansion(false);
}
document.addEventListener("keydown", escapePageExpansion);
review.addEventListener("load", () => {
  if (review.contentWindow.location.origin === location.origin) {
    review.contentWindow.addEventListener("keydown", escapePageExpansion);
  }
});

const shot = document.querySelector("#desktop-shot");
for (const link of document.querySelectorAll(".view-choice")) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = link.className;
  button.textContent = link.textContent;
  button.dataset.view = link.dataset.view;
  button.setAttribute("aria-controls", shot.id);
  button.setAttribute("aria-pressed", String(link.dataset.view === "notebook"));
  button.addEventListener("click", () => {
    shot.src = link.href;
    shot.alt = link.dataset.imageAlt;
    shot.parentElement.href = link.href;
    for (const choice of document.querySelectorAll("button.view-choice")) {
      choice.setAttribute("aria-pressed", String(choice === button));
    }
  });
  link.replaceWith(button);
}

const motion = document.querySelector("#motion");
const trigger = document.querySelector("#motion-link");
const movie = motion.querySelector("video");
trigger.addEventListener("click", event => {
  if (!motion.showModal || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  movie.src = trigger.href;
  motion.showModal();
  document.querySelector("#motion-close").focus();
  movie.play().catch(() => { /* Native playback controls remain available. */ });
});
document.querySelector("#motion-close").addEventListener("click", () => motion.close());
motion.addEventListener("click", event => {
  if (event.target === motion) {
    const r = motion.getBoundingClientRect();
    if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) motion.close();
  }
});
motion.addEventListener("close", () => {
  movie.pause();
  movie.removeAttribute("src");
  movie.load();
  trigger.focus();
});
