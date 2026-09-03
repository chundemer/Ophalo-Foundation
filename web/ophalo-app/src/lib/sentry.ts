/**
 * Errors-only, no-PII Sentry init for the authenticated workbench (GAP-039, ADR-495).
 *
 * - The DSN (`VITE_SENTRY_DSN`) is public configuration, not a secret. When it is absent
 *   (local dev, preview builds without the console value) `initSentry()` is a complete
 *   no-op and the SDK is never initialized.
 * - `__SENTRY_RELEASE__` and `__DEPLOY_ENV__` are compile-time constants injected by
 *   `vite.config.ts` from the deployment environment (`VERCEL_GIT_COMMIT_SHA` /
 *   `VERCEL_ENV`). The release matches the API's `RAILWAY_GIT_COMMIT_SHA` when both are
 *   built from one commit; the environment is explicit (`production` / `preview` /
 *   `development`) so production events are never grouped with preview or local ones.
 * - No tracing, no session replay, no breadcrumbs. Every outgoing event is rebuilt from an
 *   allowlist by `scrubBrowserEvent` before it leaves the browser.
 */

import * as Sentry from "@sentry/react";
import type { ErrorEvent as SentryErrorEvent } from "@sentry/react";
import { scrubBrowserEvent } from "./sentryScrub";

export function initSentry(): void {
  const dsn = import.meta.env.VITE_SENTRY_DSN;
  if (!dsn) return;

  Sentry.init({
    dsn,
    release: __SENTRY_RELEASE__,
    environment: __DEPLOY_ENV__,
    sendDefaultPii: false,
    maxBreadcrumbs: 0,
    beforeBreadcrumb: () => null,
    // Errors only — no performance or replay sampling.
    tracesSampleRate: 0,
    beforeSend: (event) => scrubBrowserEvent(event) as SentryErrorEvent | null,
  });
}

/**
 * Report a render error caught by `ErrorBoundary`. React swallows errors caught by an
 * error boundary, so the SDK's global handlers never see them — the boundary forwards
 * them explicitly. A no-op until `initSentry()` has run with a DSN.
 */
export function captureHandledError(error: unknown): void {
  Sentry.captureException(error);
}
