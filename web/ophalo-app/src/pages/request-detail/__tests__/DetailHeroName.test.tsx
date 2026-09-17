import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { DetailHeroName } from "../DetailHero";
import { mockRequestDetails } from "../../../mocks/fixtures";

// GAP-092 2b: the customer-page-viewed timestamp previously rendered in the viewer's device-local
// zone. These prove the corrected surface end-to-end through DetailHeroName's real timeZone prop.
// Pinned system clock so the underlying formatEventTime relative-vs-absolute branch (>24h old)
// is deterministic regardless of when the suite runs.
describe("DetailHeroName — customer-page-viewed timestamp", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders an explicit UTC-labeled fallback while the business timezone is unresolved", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-10T00:00:00Z"));

    render(
      <DetailHeroName
        detail={{
          ...mockRequestDetails["mock-req-001"],
          customerPageLastViewedAtUtc: "2026-09-03T11:30:00Z",
          customerPageViewedAfterLatestUpdate: true,
        }}
      />,
    );

    expect(screen.getByText("Viewed Sep 3 UTC")).toBeInTheDocument();
  });

  it("renders the fallback date in the resolved business zone, no UTC label", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-10T00:00:00Z"));

    render(
      <DetailHeroName
        detail={{
          ...mockRequestDetails["mock-req-001"],
          // 2026-09-03T01:30:00Z is 2026-09-02 18:30 in America/Los_Angeles (PDT, UTC-7) — a real
          // conversion proof, not a coincidental match with the UTC calendar day.
          customerPageLastViewedAtUtc: "2026-09-03T01:30:00Z",
          customerPageViewedAfterLatestUpdate: true,
        }}
        timeZone="America/Los_Angeles"
      />,
    );

    expect(screen.getByText("Viewed Sep 2")).toBeInTheDocument();
  });

  it("shows 'Not yet viewed' unaffected by timeZone when the page is shared but has no viewed timestamp", () => {
    render(
      <DetailHeroName
        detail={{
          ...mockRequestDetails["mock-req-001"],
          customerPageLastViewedAtUtc: null,
          needsShare: false,
        }}
      />,
    );

    expect(screen.getByText("Not yet viewed")).toBeInTheDocument();
  });
});
