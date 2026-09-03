/**
 * Single, loop-safe path back to the public sign-in page when the SPA learns its
 * session is gone. Used by both `AuthGuard` (initial `/auth/me` failure) and the
 * `apiClient` wrappers (any later protected-call 401), so an expired session that
 * surfaces through both routes still triggers exactly one browser navigation.
 *
 * The redirect target is the separate public app (`VITE_PUBLIC_BASE_URL`), so this
 * cannot loop back into the SPA; the `redirecting` guard only exists to collapse
 * concurrent callers into one `window.location` assignment.
 *
 * Only 401 (authentication) reaches here. 403 and every other failure keep their
 * existing local treatment.
 */
import { getPublicBaseUrl } from "./publicBaseUrl";

let redirecting = false;

export function redirectToSignInOnce(): void {
  if (redirecting) return;
  if (typeof window === "undefined") return;
  redirecting = true;
  window.location.href = `${getPublicBaseUrl()}/signin`;
}

/** Test-only: reset the module guard between cases. */
export function __resetRedirectGuardForTests(): void {
  redirecting = false;
}
