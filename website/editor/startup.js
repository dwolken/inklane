// A small decoration on an already usable header, never a splash or loading gate.
export function launchBrand() {
  try {
    if (sessionStorage.getItem("inklane:launched")) return;
    sessionStorage.setItem("inklane:launched", "1");
  } catch {
    return; // Without session storage, keep the identity static instead of replaying it.
  }
  const preference = matchMedia("(prefers-reduced-motion: reduce)");
  if (globalThis.inklaneLaunchMotion === false || globalThis.inklaneReducedMotion || preference.matches) return;
  const brand = document.querySelector("header .brand");
  if (!brand) return;
  let timer;
  const settle = () => {
    brand.classList.remove("launch");
    clearTimeout(timer);
    preference.removeEventListener("change", preferenceChanged);
    globalThis.removeEventListener("inklane-motion-change", preferenceChanged);
    brand.removeEventListener("animationend", settle);
    brand.removeEventListener("animationcancel", settle);
  };
  const preferenceChanged = () => {
    if (preference.matches || globalThis.inklaneReducedMotion) settle();
  };
  brand.addEventListener("animationend", settle, { once: true });
  brand.addEventListener("animationcancel", settle, { once: true });
  preference.addEventListener("change", preferenceChanged);
  globalThis.addEventListener("inklane-motion-change", preferenceChanged);
  brand.classList.add("launch");
  // Also settle if a window loses visibility or its motion preference changes mid-launch.
  timer = setTimeout(settle, 450);
}
