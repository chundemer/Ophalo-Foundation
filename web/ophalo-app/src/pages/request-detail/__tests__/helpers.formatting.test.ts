import { describe, it, expect, vi, afterEach } from "vitest";
import { formatDate, formatEventTime } from "../helpers";

describe("formatDate / formatEventTime — GAP-092 business-zone-aware formatting", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("formatDate: explicit null renders the UTC-labeled fallback", () => {
    expect(formatDate("2026-09-03T11:30:00Z", null)).toBe("Sep 3, 2026, 11:30 AM UTC");
  });

  it("formatDate: a resolved zone renders in that zone, no UTC label", () => {
    expect(formatDate("2026-09-03T11:30:00Z", "America/Los_Angeles")).toBe("Sep 3, 2026, 4:30 AM");
  });

  it("formatEventTime: explicit null falls back to the UTC-labeled absolute date past 24h", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-05T12:00:00Z"));

    expect(formatEventTime("2026-09-03T11:30:00Z", null)).toBe("Sep 3 UTC");
  });
});
