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

  it("renders a compact locked-count summary for a single submitted visit", () => {
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

    expect(screen.getByText("Visit history")).toBeInTheDocument();
    expect(screen.getByText("1 submitted visit · locked record")).toBeInTheDocument();
    // Per-visit outcome/note/line detail moved to the Actual Work drawer's SubmittedVisits accordion.
    expect(screen.queryByText("No work authorized")).not.toBeInTheDocument();
    expect(screen.queryByText("Customer declined repair.")).not.toBeInTheDocument();
  });

  it("pluralizes the locked-count summary for multiple submitted visits", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v3", status: "SubmittedToOffice", outcome: null, completionNote: "Most recent visit note", submittedAtUtc: "2026-03-01T12:00:00Z", lines: [] },
        { id: "v2", status: "SubmittedToOffice", outcome: null, completionNote: "Middle visit note", submittedAtUtc: "2026-02-01T12:00:00Z", lines: [] },
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: "Oldest visit note", submittedAtUtc: "2026-01-01T12:00:00Z", lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("3 submitted visits · locked record")).toBeInTheDocument();
  });

  it("shows a lock affordance icon alongside the summary", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01T12:00:00Z", lines: [] },
      ],
    };
    const { container } = render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);
    expect(container.querySelector("svg")).toBeInTheDocument();
  });

  it("null-guards a missing submittedAtUtc without affecting the summary count", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: null, lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("1 submitted visit · locked record")).toBeInTheDocument();
  });
});
