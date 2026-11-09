const stage = document.querySelector("#stage");
const template = document.querySelector("#workspace-template");
template.replaceWith(template.content.cloneNode(true));
const review = document.querySelector("#review");
const expand = document.querySelector("#expand");
const surrounding = [...document.querySelectorAll(".skip,.topbar,.intro,.experiments,.continuity,.platforms,.workspace-foot,.desktop,footer")];
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

// Small session-only demonstrations. No file, account or browser storage is used.
const experiments=document.querySelector(".experiments");
const ink=document.querySelector("#demo-ink");
let drawing=null;
const point=event=>{const p=new DOMPoint(event.clientX,event.clientY).matrixTransform(ink.getScreenCTM().inverse());return [Math.max(0,Math.min(600,p.x)),Math.max(0,Math.min(300,p.y))];};
ink.addEventListener("pointerdown",event=>{
  if(event.button!==0)return;event.preventDefault();ink.setPointerCapture(event.pointerId);
  drawing=document.createElementNS("http://www.w3.org/2000/svg","polyline");drawing.setAttribute("stroke",document.querySelector("#demo-color").value);drawing.setAttribute("points",point(event).join(","));ink.append(drawing);
});
ink.addEventListener("pointermove",event=>{if(drawing && ink.hasPointerCapture(event.pointerId))drawing.setAttribute("points",drawing.getAttribute("points")+" "+point(event).join(","));});
const finishDrawing=()=>{drawing=null;};
ink.addEventListener("pointerup",finishDrawing);ink.addEventListener("pointercancel",finishDrawing);
ink.addEventListener("keydown",event=>{if(event.key==="Delete" || (event.ctrlKey && event.key==="z")){ink.lastElementChild?.remove();event.preventDefault();}});
const pin=document.querySelector("#demo-pin"), card=document.querySelector("#demo-card");
function showRemark(open){card.hidden=!open;pin.setAttribute("aria-expanded",String(open));if(open)document.querySelector("#demo-remark").focus();else pin.focus();}
pin.addEventListener("click",()=>showRemark(card.hidden));document.querySelector("#demo-collapse").addEventListener("click",()=>showRemark(false));
card.addEventListener("keydown",event=>{if(event.key==="Escape"){showRemark(false);event.preventDefault();}});
const object=document.querySelector("#demo-object"), content=document.querySelector("#demo-text"), bold=document.querySelector("#demo-bold");
bold.addEventListener("click",()=>{const enabled=bold.getAttribute("aria-pressed")!=="true";bold.setAttribute("aria-pressed",String(enabled));content.style.fontWeight=enabled?"700":"400";});
for(const kind of ["move","resize"]){
 const handle=document.querySelector("#demo-"+kind);let drag=null;
 function adjust(dx,dy){const parent=object.parentElement;const w=parent.clientWidth,h=parent.clientHeight;
  if(kind==="move"){object.style.left=Math.max(0,Math.min(w-object.offsetWidth,drag.x+dx))+"px";object.style.top=Math.max(0,Math.min(h-object.offsetHeight,drag.y+dy))+"px";}
  else{object.style.width=Math.max(120,Math.min(w-object.offsetLeft,drag.w+dx))+"px";object.style.height=Math.max(76,Math.min(h-object.offsetTop,drag.h+dy))+"px";}
 }
 function snapshot(){return{x:object.offsetLeft,y:object.offsetTop,w:object.offsetWidth,h:object.offsetHeight};}
 handle.addEventListener("pointerdown",event=>{if(event.button!==0)return;event.preventDefault();drag={...snapshot(),px:event.clientX,py:event.clientY};handle.setPointerCapture(event.pointerId);});
 handle.addEventListener("pointermove",event=>{if(drag&&handle.hasPointerCapture(event.pointerId))adjust(event.clientX-drag.px,event.clientY-drag.py);});
 handle.addEventListener("pointerup",()=>drag=null);handle.addEventListener("pointercancel",()=>drag=null);
 handle.addEventListener("keydown",event=>{if(!["ArrowLeft","ArrowRight","ArrowUp","ArrowDown"].includes(event.key))return;event.preventDefault();drag=snapshot();const step=event.shiftKey?20:5;adjust(event.key==="ArrowLeft"?-step:event.key==="ArrowRight"?step:0,event.key==="ArrowUp"?-step:event.key==="ArrowDown"?step:0);drag=null;});
}
for(const reset of document.querySelectorAll("[data-reset]"))reset.addEventListener("click",()=>{
 if(reset.dataset.reset==="draw"){ink.replaceChildren();drawing=null;}
 if(reset.dataset.reset==="remark"){document.querySelector("#demo-remark").value=experiments.dataset.demoRemark;showRemark(false);}
 if(reset.dataset.reset==="text"){object.removeAttribute("style");content.value=experiments.dataset.demoLabel;content.removeAttribute("style");bold.setAttribute("aria-pressed","false");}
});
