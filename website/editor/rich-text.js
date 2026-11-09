import { Marked } from "./rich-lib/marked.js";
import DOMPurify from "./rich-lib/purify.js";
import katex from "./rich-lib/katex.mjs";
import hljs from "./rich-lib/highlight.js";

const escape = (text) =>
  text.replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const math = (source, displayMode) =>
  katex.renderToString(source, {
    displayMode,
    throwOnError: false,
    trust: false,
    strict: "ignore",
    maxExpand: 500,
    maxSize: 20,
  });
const markdown = new Marked({ gfm: true, breaks: true });
markdown.use({
  renderer: {
    html({ text }) {
      return escape(text);
    },
    image({ text }) {
      return escape(text);
    }, // Documents never fetch remote images while rendering notes.
    code({ text, lang }) {
      const language = (lang || "").split(/\s/)[0];
      const code = hljs.getLanguage(language)
        ? hljs.highlight(text, { language, ignoreIllegals: true }).value
        : escape(text);
      return `<pre><code class="hljs">${code}</code></pre>`;
    },
  },
  extensions: [
    {
      name: "mathBlock",
      level: "block",
      start: (source) => source.indexOf("$$"),
      tokenizer(source) {
        const m = /^\$\$\n?([\s\S]+?)\n?\$\$(?:\n|$)/.exec(source);
        if (m) return { type: "mathBlock", raw: m[0], text: m[1] };
      },
      renderer(token) {
        return math(token.text, true);
      },
    },
    {
      name: "mathInline",
      level: "inline",
      start: (source) => source.indexOf("$"),
      tokenizer(source) {
        const m = /^\$([^$\n]+?)\$(?!\d)/.exec(source);
        if (m) return { type: "mathInline", raw: m[0], text: m[1] };
      },
      renderer(token) {
        return math(token.text, false);
      },
    },
    {
      name: "underline",
      level: "inline",
      start: (source) => source.indexOf("++"),
      tokenizer(source) {
        const m = /^\+\+([^\n]+?)\+\+/.exec(source);
        if (m)
          return {
            type: "underline",
            raw: m[0],
            tokens: this.lexer.inlineTokens(m[1]),
          };
      },
      renderer(token) {
        return `<u>${this.parser.parseInline(token.tokens)}</u>`;
      },
    },
  ],
});

export function textStyle(element, item, scale = 1) {
  Object.assign(element.style, {
    fontFamily: item.fontFamily || "Segoe UI, system-ui, sans-serif",
    fontSize: item.fontSize * scale + "px",
    fontWeight: item.bold ? "700" : "400",
    fontStyle: item.italic ? "italic" : "normal",
    textDecoration: item.underline ? "underline" : "none",
    textAlign: (item.alignment || "Left").toLowerCase(),
    color: item.color,
    opacity: item.opacity ?? 1,
  });
}

export function renderText(element, item) {
  element.classList.toggle("rich-content", item.contentFormat === "Markdown");
  if (item.contentFormat !== "Markdown") {
    element.textContent = item.text;
    return;
  }
  // Bound expensive parsing separately from the lossless stored source.
  if (item.text.length > 100000) {
    element.textContent = item.text;
    return;
  }
  const clean = DOMPurify.sanitize(markdown.parse(item.text), {
    USE_PROFILES: { html: true, mathMl: true },
    FORBID_TAGS: ["img", "style", "input", "button", "form", "video", "audio"],
    FORBID_ATTR: ["id", "name"],
  });
  element.innerHTML = clean;
  for (const link of element.querySelectorAll("a")) {
    if (!/^(https?:|mailto:)/i.test(link.getAttribute("href") || ""))
      link.removeAttribute("href");
    link.target = "_blank";
    link.rel = "noopener noreferrer";
  }
}
