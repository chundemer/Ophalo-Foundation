import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ActualWorkCard } from "../ActualWorkCard";
import type { ActualWorkCaptureState } from "../useActualWorkCapture";

describe("ActualWorkCard", () => {
  it("labels an empty draft (no open draft) as 'Add actual work', never 'Resume'", () => {
    const state: ActualWorkCaptureState = { status: "no-draft", submittedCount: 0 };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Add actual work/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Resume/ })).not.toBeInTheDocument();
  });

  it("labels an open draft with zero lines as 'Resume draft', never 'Add actual work'", () => {
    const state: ActualWorkCaptureState = {
      status: "draft",
      draft: {
        id: "d1",
        status: "Draft",
        outcome: null,
        completionNote: null,
        submittedAtUtc: null,
        concurrencyVersion: "v1",
        lines: [],
      },
      submittedCount: 0,
    };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Resume draft/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add actual work/ })).not.toBeInTheDocument();
  });

  it("labels an open draft with saved lines as 'Continue draft'", () => {
    const state: ActualWorkCaptureState = {
      status: "draft",
      draft: {
        id: "d1",
        status: "Draft",
        outcome: null,
        completionNote: null,
        submittedAtUtc: null,
        concurrencyVersion: "v1",
        lines: [
          { id: "l1", displayNameSnapshot: "Filter", unitOfMeasureSnapshot: null, actualQuantity: 1, note: null },
        ],
      },
      submittedCount: 0,
    };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Continue draft/ })).toBeInTheDocument();
  });
});
