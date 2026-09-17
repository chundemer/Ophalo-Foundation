import { describe, it, expect, vi, afterEach } from "vitest";
import { formatDate, formatEventTime } from "../helpers";

// GAP-092 2a: formatDate/formatEventTime distinguish `undefined` (not yet migrated — Actual
// Work/history, slice 3) from `null` (migrated, zone genuinely unresolved). This is the
// compatibility seam itself, not just its business-zone-aware successor path.
describe("formatDate / formatEventTime — GAP-092 timeZone compatibility branch", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("formatDate: omitted timeZone keeps the original device-local behavior unchanged", () => {
    // Self-consistent against the same device-local API the legacy path uses — must hold
    // regardless of which timezone this suite happens to run in.
    const iso = "2026-09-03T11:30:00Z";
    const expected = new Date(iso).toLocaleDateString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
    });

    expect(formatDate(iso)).toBe(expected);
  });

  it("formatDate: explicit null renders the UTC-labeled fallback", () => {
    expect(formatDate("2026-09-03T11:30:00Z", null)).toBe("Sep 3, 2026, 11:30 AM UTC");
  });

  it("formatDate: a resolved zone renders in that zone, no UTC label", () => {
    expect(formatDate("2026-09-03T11:30:00Z", "America/Los_Angeles")).toBe("Sep 3, 2026, 4:30 AM");
  });

  it("formatEventTime: omitted timeZone keeps the original device-local relative behavior", () => {
    // The relative-minutes branch is timezone-invariant regardless, so this holds on any machine.
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-03T12:00:00Z"));

    expect(formatEventTime("2026-09-03T11:30:00Z")).toBe("30m ago");
  });

  it("formatEventTime: explicit null falls back to the UTC-labeled absolute date past 24h", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-05T12:00:00Z"));

    expect(formatEventTime("2026-09-03T11:30:00Z", null)).toBe("Sep 3 UTC");
  });
});
