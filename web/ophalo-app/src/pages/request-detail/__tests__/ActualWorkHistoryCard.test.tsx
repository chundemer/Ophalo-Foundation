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

  it("renders nothing when loaded with no submitted visits (no empty-state filler)", () => {
    const state: ActualWorkHistoryState = { status: "loaded", submittedVisits: [] };
    const { container } = render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
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

  it("shows only the most recent visit open, collapsing older visits behind a disclosure", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v3", status: "SubmittedToOffice", outcome: null, completionNote: "Most recent visit note", submittedAtUtc: "2026-03-01T12:00:00Z", lines: [] },
        { id: "v2", status: "SubmittedToOffice", outcome: null, completionNote: "Middle visit note", submittedAtUtc: "2026-02-01T12:00:00Z", lines: [] },
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: "Oldest visit note", submittedAtUtc: "2026-01-01T12:00:00Z", lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("Most recent visit note")).toBeInTheDocument();
    expect(screen.getByText("2 earlier visits")).toBeInTheDocument();
    // Collapsed <details> content is present in the DOM but not open by default.
    const details = screen.getByText("2 earlier visits").closest("details");
    expect(details).not.toHaveAttribute("open");
    expect(screen.getByText("Middle visit note")).toBeInTheDocument();
    expect(screen.getByText("Oldest visit note")).toBeInTheDocument();
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
