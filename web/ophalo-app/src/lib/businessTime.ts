// GAP-092 1a — pure, injectable-reference-date helpers for business-timezone-aware calendar-day
// comparisons. No `Date` parsing of a date-only value: the business "today" string is derived via
// Intl.DateTimeFormat and compared as a plain YYYY-MM-DD string, so an offset can never shift the
// calendar day being compared against.

/** `referenceDate` defaults to the real clock but is injectable so tests never depend on it. */
export function businessTodayDateOnly(timeZone: string, referenceDate: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(referenceDate);
  const lookup = Object.fromEntries(parts.map((p) => [p.type, p.value]));
  return `${lookup.year}-${lookup.month}-${lookup.day}`;
}

export function isDateOnlyToday(isoDate: string | null, timeZone: string | null, referenceDate?: Date): boolean {
  if (!isoDate || !timeZone) return false;
  return isoDate === businessTodayDateOnly(timeZone, referenceDate);
}

export function isDateOnlyPast(isoDate: string | null, timeZone: string | null, referenceDate?: Date): boolean {
  if (!isoDate || !timeZone) return false;
  return isoDate < businessTodayDateOnly(timeZone, referenceDate);
}

const INSTANT_FORMAT_OPTIONS: Intl.DateTimeFormatOptions = {
  month: "short",
  day: "numeric",
  year: "numeric",
  hour: "numeric",
  minute: "2-digit",
};

/**
 * GAP-092: absolute-instant formatting, business-zone-aware. `timeZone: null` means "resolved
 * as genuinely unresolved" (not "not yet migrated" — callers use `undefined` for that): render in
 * UTC, explicitly labeled, rather than a silent device-local guess — e.g. "Sep 17, 2026, 3:20 PM
 * UTC". Once a real zone is known, the label drops and the instant renders in that zone.
 */
export function formatInstant(isoUtc: string, timeZone: string | null): string {
  const zone = timeZone ?? "UTC";
  const formatted = new Intl.DateTimeFormat("en-US", { ...INSTANT_FORMAT_OPTIONS, timeZone: zone }).format(new Date(isoUtc));
  return timeZone ? formatted : `${formatted} UTC`;
}

/**
 * GAP-092: "just now" / "Xm ago" / "Xh ago" are elapsed durations — timezone-invariant, computed
 * from UTC milliseconds — but the >24h fallback renders an absolute calendar date, which must be
 * business-zone-aware (including which year counts as "this year"). Same UTC-fallback labeling as
 * `formatInstant` while the zone is unresolved. `referenceDate` is injectable so tests never
 * depend on the real clock.
 */
export function formatRelativeOrInstant(isoUtc: string, timeZone: string | null, referenceDate: Date = new Date()): string {
  const eventDate = new Date(isoUtc);
  const diffMins = Math.floor((referenceDate.getTime() - eventDate.getTime()) / 60_000);
  if (diffMins < 1) return "just now";
  if (diffMins < 60) return `${diffMins}m ago`;
  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours}h ago`;

  const zone = timeZone ?? "UTC";
  const yearOf = (d: Date) => new Intl.DateTimeFormat("en-US", { year: "numeric", timeZone: zone }).format(d);
  const sameYear = yearOf(eventDate) === yearOf(referenceDate);
  const formatted = new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: sameYear ? undefined : "numeric",
    timeZone: zone,
  }).format(eventDate);
  return timeZone ? formatted : `${formatted} UTC`;
}
