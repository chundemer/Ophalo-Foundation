import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkPendingReviewsCard } from "../ActualWorkPendingReviewsCard";
import type { ActualWorkPendingReviewsState } from "../useActualWorkPendingReviews";
import type { ActualWorkRequestPendingReviewEntry } from "../../../lib/apiClient";

const item: ActualWorkRequestPendingReviewEntry = {
  actualWorkId: "aw-1",
  submittedAtUtc: "2026-08-27T12:00:00Z",
  lineCount: 3,
  recorderDisplayName: "Dana Tech",
  reviewStatus: "ReadyToReview",
};

const baseProps = { onRetry: vi.fn(), onReviewVisit: vi.fn() };

function loaded(...items: ActualWorkRequestPendingReviewEntry[]): ActualWorkPendingReviewsState {
  return { status: "loaded", count: items.length, items };
}

describe("ActualWorkPendingReviewsCard", () => {
  it("renders the header count and one row per pending visit with recorder, line count, and status", () => {
    render(
      <ActualWorkPendingReviewsCard
        {...baseProps}
        state={loaded(item, { ...item, actualWorkId: "aw-2", lineCount: 0, reviewStatus: "NeedsNoChargeDisposition" })}
      />,
    );
    expect(screen.getByText("Pending financial reviews (2)")).toBeInTheDocument();
    expect(screen.getByText(/3 work lines · recorded by Dana Tech/)).toBeInTheDocument();
    expect(screen.getByText("Ready to review")).toBeInTheDocument();
    expect(screen.getByText(/No work lines/)).toBeInTheDocument();
    // BL138 locked zero-line copy: the action verb, not "Needs …".
    expect(screen.getByText("Record no-charge disposition")).toBeInTheDocument();
  });

  it("invokes onReviewVisit with the exact visit id", async () => {
    const onReviewVisit = vi.fn();
    render(<ActualWorkPendingReviewsCard {...baseProps} onReviewVisit={onReviewVisit} state={loaded(item)} />);
    await userEvent.click(screen.getByRole("button", { name: "Review financials" }));
    expect(onReviewVisit).toHaveBeenCalledWith("aw-1");
  });

  it("renders nothing while loading, hidden, or when nothing is pending", () => {
    const { rerender, container } = render(
      <ActualWorkPendingReviewsCard {...baseProps} state={{ status: "loading" }} />,
    );
    expect(container).toBeEmptyDOMElement();
    rerender(<ActualWorkPendingReviewsCard {...baseProps} state={{ status: "hidden" }} />);
    expect(container).toBeEmptyDOMElement();
    rerender(<ActualWorkPendingReviewsCard {...baseProps} state={{ status: "loaded", count: 0, items: [] }} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("offers a retry on error", async () => {
    const onRetry = vi.fn();
    render(<ActualWorkPendingReviewsCard {...baseProps} onRetry={onRetry} state={{ status: "error" }} />);
    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalled();
  });
});
