import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { FeedbackSummaryCard, ProminentFeedbackCard, SourceMetaPanel } from "../DetailPanels";
import { mockRequestDetails } from "../../../mocks/fixtures";

// GAP-092 2d: these timestamps previously rendered in the viewer's device-local zone. Each pair
// proves a real conversion (a UTC instant crossing the calendar-day boundary in
// America/Los_Angeles), not a coincidental same-day match.

describe("FeedbackSummaryCard — GAP-092 business-timezone-aware submitted date", () => {
  const detail = {
    ...mockRequestDetails["mock-req-001"],
    feedbackWasResolved: true,
    // 2026-07-02T02:00:00Z is 2026-07-01 19:00 in America/Los_Angeles (PDT, UTC-7).
    feedbackSubmittedAtUtc: "2026-07-02T02:00:00Z",
  };

  it("renders an explicit UTC-labeled date while the business timezone is unresolved", () => {
    render(<FeedbackSummaryCard detail={detail} />);
    expect(screen.getByText(/on Jul 2, 2026, 2:00 AM UTC/)).toBeInTheDocument();
  });

  it("renders the date in the resolved business zone, no UTC label", () => {
    render(<FeedbackSummaryCard detail={detail} timeZone="America/Los_Angeles" />);
    expect(screen.getByText(/on Jul 1, 2026, 7:00 PM/)).toBeInTheDocument();
  });
});

describe("ProminentFeedbackCard — GAP-092 business-timezone-aware submitted date", () => {
  const detail = {
    ...mockRequestDetails["mock-req-001"],
    availableActions: { ...mockRequestDetails["mock-req-001"].availableActions, canMarkFeedbackReviewed: true },
    feedbackWasResolved: false,
    feedbackReviewedAtUtc: null,
    feedbackSubmittedAtUtc: "2026-07-02T02:00:00Z",
  };

  it("renders an explicit UTC-labeled date while the business timezone is unresolved", () => {
    render(<ProminentFeedbackCard requestId="req-1" detail={detail} onDetailUpdated={() => {}} onReviewSuccess={() => {}} />);
    expect(screen.getByText(/on Jul 2, 2026, 2:00 AM UTC/)).toBeInTheDocument();
  });

  it("renders the date in the resolved business zone, no UTC label", () => {
    render(
      <ProminentFeedbackCard
        requestId="req-1"
        detail={detail}
        onDetailUpdated={() => {}}
        onReviewSuccess={() => {}}
        timeZone="America/Los_Angeles"
      />,
    );
    expect(screen.getByText(/on Jul 1, 2026, 7:00 PM/)).toBeInTheDocument();
  });
});

describe("SourceMetaPanel — GAP-092 business-timezone-aware submitted date", () => {
  const detail = { ...mockRequestDetails["mock-req-001"], createdAtUtc: "2026-07-02T02:00:00Z" };

  it("renders an explicit UTC-labeled date while the business timezone is unresolved", () => {
    render(<SourceMetaPanel detail={detail} />);
    expect(screen.getByText("Submitted Jul 2, 2026, 2:00 AM UTC")).toBeInTheDocument();
  });

  it("renders the date in the resolved business zone, a real conversion not a coincidence", () => {
    render(<SourceMetaPanel detail={detail} timeZone="America/Los_Angeles" />);
    expect(screen.getByText("Submitted Jul 1, 2026, 7:00 PM")).toBeInTheDocument();
  });
});
