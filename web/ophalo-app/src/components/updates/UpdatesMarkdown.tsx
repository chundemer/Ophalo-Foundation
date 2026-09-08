import { useEffect, useMemo, useRef } from "react";
import snarkdown from "snarkdown";
import DOMPurify from "dompurify";

// GAP-038 / BL149 (038-1b-i) — renders a feed entry / guide markdown body.
//
// Trust model: the feed is founder-authored and schema-validated server-side, but it is still
// rendered as HTML, so it goes through a hard DOMPurify allowlist regardless. Content authors may
// only use: p strong em ul ol li a img br  /  href src alt. Everything else is stripped.
//
// Images are special: a guide body references `guides/img/<name>` and nothing else. The renderer
// rewrites that single form to the backend proxy path `/updates/guides/img/<name>`, requires a
// non-empty alt, adds `loading="lazy"`, and drops any other image outright. A real `error` event
// handler (capture phase — image errors do not bubble) then hides an `<img>` that fails to load so
// a broken proxy response never leaves a torn icon in the middle of guide text.

const ALLOWED_TAGS = ["p", "strong", "em", "ul", "ol", "li", "a", "img", "br"];
const ALLOWED_ATTR = ["href", "src", "alt"];

// Only `https://`, `mailto:`, root-relative, and the bare guide-image form survive; `javascript:`
// and `data:` URIs are dropped (attribute removed, surrounding text kept).
const ALLOWED_URI_REGEXP = /^(?:https:\/\/|mailto:|\/|guides\/img\/)/i;

// Mirrors the backend guide-image name rule (`UpdatesEndpoints.GuideImageName`).
const GUIDE_IMG_SRC = /^guides\/img\/([A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(?:png|jpe?g|webp))$/;

const STYLE = `
.updates-md > :first-child { margin-top: 0; }
.updates-md > :last-child { margin-bottom: 0; }
.updates-md p { margin: 0.5rem 0; line-height: 1.55; }
.updates-md ul, .updates-md ol { margin: 0.5rem 0; padding-left: 1.25rem; }
.updates-md li { margin: 0.2rem 0; }
.updates-md a { color: var(--ophalo-accent); text-decoration: underline; }
.updates-md img {
  display: block;
  width: 100%;
  max-width: 480px;
  height: auto;
  margin: 0.6rem 0;
  padding: 8px;
  border: 1px solid var(--ophalo-border);
  border-radius: 4px;
  box-sizing: border-box;
}
.updates-md img[hidden] { display: none; }
`;

export function renderUpdatesHtml(markdown: string): string {
  const clean = DOMPurify.sanitize(snarkdown(markdown), {
    ALLOWED_TAGS: [...ALLOWED_TAGS],
    ALLOWED_ATTR: [...ALLOWED_ATTR],
    ALLOWED_URI_REGEXP,
  });

  // Post-sanitize image pass: the allowlist above is deliberately just `href src alt`, so the
  // renderer-owned rewrite + `loading` attribute are applied here on already-safe nodes.
  const template = document.createElement("template");
  template.innerHTML = clean;
  for (const img of Array.from(template.content.querySelectorAll("img"))) {
    const src = img.getAttribute("src") ?? "";
    const alt = (img.getAttribute("alt") ?? "").trim();
    const match = src.match(GUIDE_IMG_SRC);
    if (!match || alt.length === 0) {
      img.remove();
      continue;
    }
    img.setAttribute("src", `/updates/guides/img/${match[1]}`);
    img.setAttribute("alt", alt);
    img.setAttribute("loading", "lazy");
  }
  return template.innerHTML;
}

export function UpdatesMarkdown({ markdown }: { markdown: string }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const html = useMemo(() => renderUpdatesHtml(markdown), [markdown]);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    // Capture phase: `error` from a failed `<img>` does not bubble, so a listener on the
    // container only sees it during capture.
    const onError = (event: Event) => {
      const target = event.target as HTMLElement | null;
      if (target?.tagName === "IMG") target.setAttribute("hidden", "");
    };
    el.addEventListener("error", onError, true);
    return () => el.removeEventListener("error", onError, true);
  }, [html]);

  return (
    <>
      <style>{STYLE}</style>
      <div
        ref={containerRef}
        className="updates-md text-[0.9375rem] text-[var(--ophalo-ink)]"
        dangerouslySetInnerHTML={{ __html: html }}
      />
    </>
  );
}
