import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ActualWorkHistoryCard } from "../ActualWorkHistoryCard";
import type { ActualWorkHistoryState } from "../useActualWorkHistory";
import type { ActualWorkLineHistoryEntry } from "../../../lib/apiClient";

function line(overrides: Partial<ActualWorkLineHistoryEntry> = {}): ActualWorkLineHistoryEntry {
  return {
    id: "l1",
    displayNameSnapshot: "Filter",
    unitOfMeasureSnapshot: "each",
    actualQuantity: 2,
    note: null,
    performedByAccountUserId: "u-tech",
    performerDisplayName: "Dana Tech",
    ...overrides,
  };
}

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

  it("keeps the locked-record language and icon at card level", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01T12:00:00Z", visitNote: null, lines: [] },
      ],
    };
    const { container } = render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("Visit history")).toBeInTheDocument();
    expect(screen.getByText("1 submitted visit · locked record")).toBeInTheDocument();
    expect(container.querySelector("svg")).toBeInTheDocument();
  });

  it("pluralizes the locked-count summary for multiple submitted visits", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v3", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-03-01T12:00:00Z", visitNote: null, lines: [] },
        { id: "v2", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-02-01T12:00:00Z", visitNote: null, lines: [] },
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01T12:00:00Z", visitNote: null, lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("3 submitted visits · locked record")).toBeInTheDocument();
  });

  it("badges a superseded source and the successor that corrected an earlier visit", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v-new", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-03-02T12:00:00Z", visitNote: null, supersedesActualWorkId: "v-old", lines: [] },
        { id: "v-old", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-03-01T12:00:00Z", visitNote: null, superseded: true, supersededByActualWorkId: "v-new", lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("Superseded · replaced by a correction")).toBeInTheDocument();
    expect(screen.getByText("Correction of an earlier visit")).toBeInTheDocument();
  });

  it("shows no lineage badge on an ordinary standalone visit", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01T12:00:00Z", visitNote: null, lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.queryByText(/replaced by a correction/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Correction of an earlier visit/)).not.toBeInTheDocument();
  });

  it("discloses the visit note and each line's performer for a submitted visit", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        {
          id: "v1",
          status: "SubmittedToOffice",
          outcome: "NoWorkAuthorized",
          completionNote: "Customer declined repair.",
          submittedAtUtc: "2026-01-15T18:30:00Z",
          visitNote: "Gate code is 4321.",
          lines: [
            line({ id: "l1", displayNameSnapshot: "Filter", performerDisplayName: "Dana Tech" }),
            line({ id: "l2", displayNameSnapshot: "Coil clean", performerDisplayName: "Sam Helper" }),
          ],
        },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("Visit note")).toBeInTheDocument();
    expect(screen.getByText("Gate code is 4321.")).toBeInTheDocument();
    expect(screen.getByText("Filter")).toBeInTheDocument();
    expect(screen.getByText("Dana Tech")).toBeInTheDocument();
    expect(screen.getByText("Sam Helper")).toBeInTheDocument();
    expect(screen.getByText("2 lines")).toBeInTheDocument();
  });

  it("shows 'Unknown performer' when a line's performer id no longer resolves", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        {
          id: "v1",
          status: "SubmittedToOffice",
          outcome: null,
          completionNote: null,
          submittedAtUtc: "2026-01-15T18:30:00Z",
          visitNote: null,
          lines: [line({ id: "l1", performerDisplayName: null })],
        },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("Unknown performer")).toBeInTheDocument();
    // No visit-note label when the visit has no note.
    expect(screen.queryByText("Visit note")).not.toBeInTheDocument();
  });

  it("summarizes each visit with its line count", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-15T18:30:00Z", visitNote: null, lines: [line({ id: "l1" })] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("1 line")).toBeInTheDocument();
  });

  it("null-guards a missing submittedAtUtc without affecting the summary count", () => {
    const state: ActualWorkHistoryState = {
      status: "loaded",
      submittedVisits: [
        { id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: null, visitNote: null, lines: [] },
      ],
    };
    render(<ActualWorkHistoryCard state={state} onRetry={vi.fn()} />);

    expect(screen.getByText("1 submitted visit · locked record")).toBeInTheDocument();
    expect(screen.getByText("Submitted")).toBeInTheDocument();
  });
});
