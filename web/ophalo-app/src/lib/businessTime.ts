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
