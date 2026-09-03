/**
 * Single parsed accessor for `VITE_PUBLIC_BASE_URL` — the origin of the separate public
 * customer app that this workbench links to (customer request pages, public intake, the
 * sign-in redirect target).
 *
 * GAP-039 Batch 2a: the environment variable is read and validated exactly once, here.
 * No consumer may call `.replace()` or any other string operation on the raw environment
 * value. `main.tsx` inspects `publicBaseUrlResult` at startup and renders the
 * configuration-error screen instead of mounting `<App>` when it is invalid, so
 * `getPublicBaseUrl()` only ever returns `""` on a path the startup guard already blocked.
 *
 * Parsing is throw-free: a missing or malformed value produces an `{ ok: false }` result,
 * never an exception.
 */

export type PublicBaseUrlResult =
  | { readonly ok: true; readonly value: string }
  | { readonly ok: false; readonly reason: "missing" | "malformed" };

function parse(raw: string | undefined): PublicBaseUrlResult {
  if (raw === undefined || raw.trim() === "") {
    return { ok: false, reason: "missing" };
  }

  let url: URL;
  try {
    url = new URL(raw.trim());
  } catch {
    return { ok: false, reason: "malformed" };
  }

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    return { ok: false, reason: "malformed" };
  }

  // Normalize so consumers can append `/keep/...` without worrying about a trailing slash.
  const normalized = `${url.origin}${url.pathname}`.replace(/\/+$/, "");
  return { ok: true, value: normalized };
}

export const publicBaseUrlResult: PublicBaseUrlResult = parse(
  import.meta.env.VITE_PUBLIC_BASE_URL,
);

/**
 * Normalized public base URL with no trailing slash. Returns `""` only when configuration
 * is invalid — unreachable in the running app because `main.tsx` renders the
 * configuration-error screen rather than mounting `<App>` in that case.
 */
export function getPublicBaseUrl(): string {
  return publicBaseUrlResult.ok ? publicBaseUrlResult.value : "";
}
