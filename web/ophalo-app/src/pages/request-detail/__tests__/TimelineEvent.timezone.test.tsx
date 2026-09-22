import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { TimelineEvent } from "../TimelineEvent";
import type { KeepRequestEventItem } from "../../../lib/apiClient";

function event(occurredAtUtc: string): KeepRequestEventItem {
  return {
    id: "evt-1",
    eventType: "status_changed",
    content: null,
    visibility: "all",
    occurredAtUtc,
    actorType: "account_user",
    actorAccountUserId: null,
    actorDisplayName: "Christian Hundemer",
    statusAfter: "in_progress",
    messageIntent: null,
    communicationChannel: null,
    externalContactDirection: null,
    externalContactChannel: null,
    externalContactOutcome: null,
    externalContactRequiresFollowUp: null,
    externalContactSetFirstResponse: null,
    externalContactClearedAttention: null,
    participationAction: null,
    participationTargetAccountUserId: null,
    participationTargetDisplayName: null,
    participationPreviousResponsibleAccountUserId: null,
    participationInternalNote: null,
    plannedForDate: null,
    followUpOnDate: null,
    followUpOnReason: null,
    feedbackWasResolved: null,
    relatedEventId: null,
  };
}

// GAP-092 2c: the timeline event's timestamp previously rendered in the viewer's device-local
// zone. Pinned clock so formatEventTime's business-zone-aware absolute fallback (>24h old) is
// deterministic regardless of when the suite runs.
describe("TimelineEvent — GAP-092 business-timezone-aware timestamp", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders an explicit UTC-labeled fallback while the business timezone is unresolved", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-06T00:00:00Z"));

    // 2026-09-03T02:00:00Z is 2026-09-02 19:00 in America/Los_Angeles (PDT, UTC-7).
    render(<TimelineEvent event={event("2026-09-03T02:00:00Z")} isFirst />);

    expect(screen.getByText("Sep 3 UTC")).toBeInTheDocument();
  });

  it("renders the fallback date in the resolved business zone, a real conversion not a coincidence", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-06T00:00:00Z"));

    render(<TimelineEvent event={event("2026-09-03T02:00:00Z")} isFirst timeZone="America/Los_Angeles" />);

    expect(screen.getByText("Sep 2")).toBeInTheDocument();
  });

  it("keeps the relative label timezone-invariant under 24h old", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-03T12:00:00Z"));

    render(<TimelineEvent event={event("2026-09-03T11:00:00Z")} isFirst timeZone="America/Los_Angeles" />);

    expect(screen.getByText("1h ago")).toBeInTheDocument();
  });

  it("renders compact audit timestamps in the resolved business zone", () => {
    render(<TimelineEvent event={event("2026-09-03T02:00:00Z")} isFirst compact timeZone="America/Los_Angeles" />);

    expect(screen.getByText(/Christian Hundemer · Sep 2, 2026, 7:00 PM/)).toBeInTheDocument();
  });
});
