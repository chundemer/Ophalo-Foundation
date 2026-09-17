import { describe, it, expect } from "vitest";
import { businessTodayDateOnly, isDateOnlyToday, isDateOnlyPast } from "../businessTime";

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
