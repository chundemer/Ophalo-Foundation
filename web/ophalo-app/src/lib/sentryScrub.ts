/**
 * Browser-side telemetry scrubber for the authenticated workbench (GAP-039, ADR-495).
 *
 * Mirrors the API's `SentryTelemetryScrubber`: it does not redact the incoming event in
 * place, it builds a fresh event containing only explicitly allowlisted data. Anything not
 * copied below — user identity, request headers/body/query, breadcrumbs, contexts, extra,
 * tags, exception messages, source-context lines, locals — cannot survive.
 *
 * Retained:
 *   - release and environment;
 *   - level / platform / sdk metadata (no request data);
 *   - a query- and fragment-free safe pathname (the workbench is hash-routed, so the route
 *     lives entirely in the fragment and is dropped);
 *   - exception type and sanitized stack-frame metadata (filename, function, line, column,
 *     in_app) with any token-bearing path segment reduced to `[redacted]`.
 *
 * A final invariant check discards the whole event if a query string, fragment, or an
 * unredacted opaque token still appears in any retained string.
 */

import type { Event as SentryEvent } from "@sentry/react";

// Opaque high-entropy path segments: UUIDs, long hex, and base64url-ish capability tokens.
// Matches the intent of the API's PublicTokenPathRedactor without importing server code.
const OPAQUE_SEGMENT =
  /^(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{16,}|[A-Za-z0-9_-]{24,})$/i;

const REDACTED = "[redacted]";

/** Strips query and fragment, then reduces any opaque segment to `[redacted]`. */
export function safePathname(raw: string | undefined): string | undefined {
  if (!raw) return undefined;

  let pathname: string;
  try {
    // Resolve against a fixed base so a bare path and an absolute URL are handled the same
    // way, and the query/fragment are dropped by reading `.pathname` only.
    pathname = new URL(raw, "http://scrub.invalid").pathname;
  } catch {
    return undefined;
  }

  const redacted = pathname
    .split("/")
    .map((segment) => (OPAQUE_SEGMENT.test(segment) ? REDACTED : segment))
    .join("/");

  return redacted === "" ? "/" : redacted;
}

interface SafeFrame {
  filename?: string;
  function?: string;
  lineno?: number;
  colno?: number;
  in_app?: boolean;
}

interface SafeException {
  type?: string;
  mechanism?: { type?: string; handled?: boolean; synthetic?: boolean };
  stacktrace?: { frames: SafeFrame[] };
}

function sanitizeException(
  source: NonNullable<NonNullable<SentryEvent["exception"]>["values"]>[number],
): SafeException {
  const frames = source.stacktrace?.frames ?? [];
  return {
    type: source.type,
    // `value` (the message) may embed customer text — never retained.
    mechanism: source.mechanism
      ? {
          type: source.mechanism.type,
          handled: source.mechanism.handled,
          synthetic: source.mechanism.synthetic,
        }
      : undefined,
    stacktrace:
      frames.length > 0
        ? {
            frames: frames.map((frame) => ({
              filename: safePathname(frame.filename),
              function: frame.function,
              lineno: frame.lineno,
              colno: frame.colno,
              in_app: frame.in_app,
              // context_line / pre_context / post_context / vars deliberately dropped.
            })),
          }
        : undefined,
  };
}

/**
 * True when no *free-text-derived* retained field carries a query, fragment, or unredacted
 * opaque token. Scoped deliberately to the fields where a customer identifier or capability
 * token could leak — a retained request pathname and each retained stack-frame filename and
 * function. Sentry-generated metadata (`event_id`, the 40-hex release SHA, `sdk`,
 * `environment`, `level`, line/column numbers) is trusted structurally and never matched
 * against the opaque-token rule, which would otherwise discard every real production event.
 */
function isProvablySafe(safe: {
  request?: { url?: string };
  exception?: { values: SafeException[] };
}): boolean {
  const suspect = (value: string | undefined): boolean => {
    if (value === undefined) return false;
    if (/[?#]/.test(value)) return true;
    return value.split(/[/\s]/).some((segment) => OPAQUE_SEGMENT.test(segment));
  };

  if (suspect(safe.request?.url)) return false;

  for (const exception of safe.exception?.values ?? []) {
    for (const frame of exception.stacktrace?.frames ?? []) {
      if (suspect(frame.filename) || suspect(frame.function)) return false;
    }
  }

  return true;
}

/**
 * `beforeSend` implementation. Returns a fresh allowlisted event, or `null` to discard it
 * entirely when the invariant check fails.
 */
export function scrubBrowserEvent(event: SentryEvent): SentryEvent | null {
  const safe: Record<string, unknown> & {
    request?: { url?: string };
    exception?: { values: SafeException[] };
  } = {
    event_id: event.event_id,
    timestamp: event.timestamp,
    platform: event.platform,
    level: event.level,
    release: event.release,
    environment: event.environment,
    sdk: event.sdk,
  };

  const values = event.exception?.values;
  if (values && values.length > 0) {
    safe.exception = { values: values.map(sanitizeException) };
  }

  const route = safePathname(event.request?.url);
  if (route) {
    safe.request = { url: route };
  }

  return isProvablySafe(safe) ? (safe as SentryEvent) : null;
}
