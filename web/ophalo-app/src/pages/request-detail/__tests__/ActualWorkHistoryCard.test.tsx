import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkHistoryCard } from "../ActualWorkHistoryCard";
import type { ActualWorkHistoryState } from "../useActualWorkHistory";

describe("ActualWorkHistoryCard", () => {
  it("renders nothing while loading", () => {
    const state: ActualWorkHistoryState = { status: "loading" };
    const { container } = render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when hidden (403)", () => {
    const state: ActualWorkHistoryState = { status: "hidden" };
    const { container } = render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders the card with an explicit empty-state message when there are no submitted visits", () => {
    const state: ActualWorkHistoryState = { status: "loaded", submittedVisits: [] };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);
    expect(screen.getByText("Visit history")).toBeInTheDocument();
    expect(screen.getByText("No visits submitted yet.")).toBeInTheDocument();
  });

  it("renders a compact retry state on error and calls onRetry", async () => {
    const onRetry = vi.fn();
    const state: ActualWorkHistoryState = { status: "error" };
    render(<ActualWorkHistoryCard state={state} onRetry={onRetry} />);

    expect(screen.getByText("Unable to load visit history.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("renders submitted visits with a human-readable outcome label and formatted date", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        {
          id: "v1",
          status: "SubmittedToOffice",
          outcome: "NoWorkAuthorized",
          completionNote: "Customer declined repair.",
          submittedAtUtc: "2026-01-15T18:30:00Z",
          lines: [
            { id: "l1", displayNameSnapshot: "Filter", unitOfMeasureSnapshot: "each", actualQuantity: 2, note: null },
          ],
        },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("No work authorized")).toBeInTheDocument();
    expect(screen.getByText("Customer declined repair.")).toBeInTheDocument();
    expect(screen.getByText(/Filter/)).toBeInTheDocument();
    expect(screen.queryByText("NoWorkAuthorized")).not.toBeInTheDocument();
  });

  it("null-guards a missing submittedAtUtc", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: null, lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("Submitted")).toBeInTheDocument();
  });
});
