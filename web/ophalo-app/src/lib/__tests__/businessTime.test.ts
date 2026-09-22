import { describe, it, expect } from "vitest";
import { businessTodayDateOnly, isDateOnlyToday, isDateOnlyPast, formatInstant, formatRelativeOrInstant, accountLocalDate } from "../businessTime";

// GAP-092 1a: every assertion uses an injected reference date — never the real clock — so these
// stay deterministic regardless of when or where the suite runs.

describe("businessTodayDateOnly", () => {
  it("derives the calendar day in the given IANA zone, not the machine's local zone", () => {
    // 2026-07-15T02:30:00Z is still 2026-07-14 in America/Los_Angeles (UTC-7 in July).
    const reference = new Date("2026-07-15T02:30:00Z");
    expect(businessTodayDateOnly("America/Los_Angeles", reference)).toBe("2026-07-14");
    expect(businessTodayDateOnly("UTC", reference)).toBe("2026-07-15");
  });
});

describe("isDateOnlyToday", () => {
  const reference = new Date("2026-07-15T12:00:00Z");

  it("matches the business-zone calendar day as a plain string comparison", () => {
    expect(isDateOnlyToday("2026-07-15", "UTC", reference)).toBe(true);
    expect(isDateOnlyToday("2026-07-16", "UTC", reference)).toBe(false);
  });

  it("is neutral (false) when the timezone is unresolved, never a device-local guess", () => {
    expect(isDateOnlyToday("2026-07-15", null, reference)).toBe(false);
  });

  it("is false for a null date", () => {
    expect(isDateOnlyToday(null, "UTC", reference)).toBe(false);
  });
});

describe("isDateOnlyPast", () => {
  const reference = new Date("2026-07-15T12:00:00Z");

  it("compares business-zone calendar-day strings directly", () => {
    expect(isDateOnlyPast("2026-07-14", "UTC", reference)).toBe(true);
    expect(isDateOnlyPast("2026-07-15", "UTC", reference)).toBe(false);
    expect(isDateOnlyPast("2026-07-16", "UTC", reference)).toBe(false);
  });

  it("is neutral (false) when the timezone is unresolved", () => {
    expect(isDateOnlyPast("2026-07-01", null, reference)).toBe(false);
  });
});

describe("formatInstant", () => {
  it("renders the instant in the resolved business zone, no UTC label", () => {
    // 2026-09-03T11:30:00Z is 4:30 AM in America/Los_Angeles (PDT, UTC-7).
    expect(formatInstant("2026-09-03T11:30:00Z", "America/Los_Angeles")).toBe("Sep 3, 2026, 4:30 AM");
  });

  it("renders an explicit UTC-labeled fallback when the zone is unresolved", () => {
    expect(formatInstant("2026-09-03T11:30:00Z", null)).toBe("Sep 3, 2026, 11:30 AM UTC");
  });
});

describe("accountLocalDate", () => {
  it("renders the calendar date in the resolved business zone, date only", () => {
    // 2026-09-03T11:30:00Z is Sep 3 in America/Los_Angeles (PDT, UTC-7).
    expect(accountLocalDate("2026-09-03T11:30:00Z", "America/Los_Angeles")).toBe("Sep 3");
  });

  it("crosses the UTC calendar-day boundary in a zone ahead of UTC", () => {
    // 2026-09-03T23:30:00Z is Sep 4 in Australia/Sydney (AEST, UTC+10) — proves it doesn't
    // just slice the UTC date string.
    expect(accountLocalDate("2026-09-03T23:30:00Z", "Australia/Sydney")).toBe("Sep 4");
  });

  it("falls back to UTC, unlabeled, when the zone is unresolved", () => {
    expect(accountLocalDate("2026-09-03T11:30:00Z", null)).toBe("Sep 3");
  });

  it("returns null for a null instant", () => {
    expect(accountLocalDate(null, "America/Los_Angeles")).toBeNull();
  });
});

describe("formatRelativeOrInstant", () => {
  const reference = new Date("2026-09-03T12:00:00Z");

  it("stays a timezone-invariant relative label under an hour old", () => {
    const thirtyMinAgo = new Date("2026-09-03T11:30:00Z").toISOString();
    expect(formatRelativeOrInstant(thirtyMinAgo, "America/Los_Angeles", reference)).toBe("30m ago");
    expect(formatRelativeOrInstant(thirtyMinAgo, null, reference)).toBe("30m ago");
  });

  it("falls back to an absolute, business-zone-aware date past 24h, UTC-labeled while unresolved", () => {
    const twoDaysAgo = new Date("2026-09-01T11:30:00Z").toISOString();
    expect(formatRelativeOrInstant(twoDaysAgo, "America/Los_Angeles", reference)).toBe("Sep 1");
    expect(formatRelativeOrInstant(twoDaysAgo, null, reference)).toBe("Sep 1 UTC");
  });

  it("includes the year across a business-zone year boundary, not just a UTC one", () => {
    // 2027-01-01T03:00:00Z is 2026-12-31 19:00 in America/Los_Angeles (PST, UTC-8) — still last
    // year in the business zone even though UTC has already rolled to the new year.
    const reference2 = new Date("2027-01-01T03:00:00Z");
    const twoDaysBefore = new Date("2026-12-30T12:00:00Z").toISOString();
    expect(formatRelativeOrInstant(twoDaysBefore, "America/Los_Angeles", reference2)).toBe("Dec 30");
  });
});
