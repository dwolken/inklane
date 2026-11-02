const language = document.documentElement.lang;
const address = new URL(location.href);
const link = document.querySelector("[data-language-link]");
const storageKey = "inklane.site.language";
const browserLanguage = (navigator.languages?.[0] || navigator.language || "en").split("-")[0];
let preference;
try {
  preference = localStorage.getItem(storageKey);
} catch { /* Language links also work with storage disabled. */ }

if (address.searchParams.get("lang") === language) {
  // Following the language switch is an explicit choice. Preserve the query
  // when storage is blocked so a reload still respects the chosen language.
  try {
    localStorage.setItem(storageKey, language);
    address.searchParams.delete("lang");
    history.replaceState(null, "", address);
  } catch { /* The explicit URL remains usable. */ }
} else if (language === "en" && (preference || browserLanguage) === "de") {
  // Only the neutral entry chooses a language. Direct /de/ links stay German.
  const target = new URL(link.href);
  target.hash = location.hash;
  location.replace(target);
}

const alternate = new URL(link.href);
alternate.searchParams.set("lang", link.hreflang);
function updateLanguageLink() {
  alternate.hash = location.hash;
  link.href = alternate.href;
}
updateLanguageLink();
addEventListener("hashchange", updateLanguageLink);
