import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ActualWorkReviewQueueList } from "../ActualWorkReviewQueueList";
import type { ActualWorkReviewQueueEntry } from "../../../lib/apiClient";

function entry(overrides: Partial<ActualWorkReviewQueueEntry> = {}): ActualWorkReviewQueueEntry {
  return {
    actualWorkId: "aw-1",
    requestId: "req-1",
    referenceCode: "R-1001",
    customerName: "Jane Customer",
    requestStatus: "received",
    submittedAtUtc: "2026-08-20T12:00:00Z",
    hasIncompleteFinancialData: false,
    incompleteLineCount: 0,
    totalSalesPrice: 100,
    totalStandardExpectedDirectCost: 40,
    totalMargin: 60,
    ...overrides,
  };
}

const noop = vi.fn();

describe("ActualWorkReviewQueueList — RD-058A lifecycle facts", () => {
  it("renders the linked request lifecycle status and the submitted-visit review state as distinct facts", () => {
    render(
      <ActualWorkReviewQueueList
        entries={[entry({ requestStatus: "received" })]}
        isLoading={false}
        isError={false}
        onRetry={noop}
        onSelectRequest={noop}
      />,
    );

    expect(screen.getByText("Request: Received")).toBeInTheDocument();
    expect(
      screen.getByText("Submitted visit awaiting internal financial review"),
    ).toBeInTheDocument();
  });

  it("uses the shared status label map for terminal/lifecycle statuses", () => {
    render(
      <ActualWorkReviewQueueList
        entries={[
          entry({ actualWorkId: "aw-r", requestStatus: "resolved" }),
          entry({ actualWorkId: "aw-p", requestStatus: "pending_customer" }),
        ]}
        isLoading={false}
        isError={false}
        onRetry={noop}
        onSelectRequest={noop}
      />,
    );

    // resolved -> "Work completed" (ADR-434); pending_customer stays "Pending Customer",
    // never the "Waiting on Customer" view name.
    expect(screen.getByText("Request: Work completed")).toBeInTheDocument();
    expect(screen.getByText("Request: Pending Customer")).toBeInTheDocument();
  });
});

// GAP-092 3: the "Submitted" date previously rendered in the viewer's device-local zone. This is
// the Requests.tsx page-entry path — proves it receives and renders a resolved business zone.
describe("ActualWorkReviewQueueList — GAP-092 business-timezone-aware submitted date", () => {
  it("renders an explicit UTC-labeled date while the business timezone is unresolved", () => {
    // 2026-08-20T02:00:00Z is 2026-08-19 19:00 in America/Los_Angeles (PDT, UTC-7).
    render(
      <ActualWorkReviewQueueList
        entries={[entry({ submittedAtUtc: "2026-08-20T02:00:00Z" })]}
        isLoading={false}
        isError={false}
        onRetry={noop}
        onSelectRequest={noop}
      />,
    );

    expect(screen.getByText(/Submitted Aug 20, 2026, 2:00 AM UTC/)).toBeInTheDocument();
  });

  it("renders the date in the resolved business zone (as passed down from Requests.tsx), no UTC label", () => {
    render(
      <ActualWorkReviewQueueList
        entries={[entry({ submittedAtUtc: "2026-08-20T02:00:00Z" })]}
        isLoading={false}
        isError={false}
        onRetry={noop}
        onSelectRequest={noop}
        timeZone="America/Los_Angeles"
      />,
    );

    expect(screen.getByText(/Submitted Aug 19, 2026, 7:00 PM/)).toBeInTheDocument();
  });
});
