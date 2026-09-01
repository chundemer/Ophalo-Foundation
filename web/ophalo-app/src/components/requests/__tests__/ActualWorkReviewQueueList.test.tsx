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
