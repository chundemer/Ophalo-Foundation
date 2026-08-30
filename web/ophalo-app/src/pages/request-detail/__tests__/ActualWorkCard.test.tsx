import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ActualWorkCard } from "../ActualWorkCard";
import type { ActualWorkCaptureState } from "../useActualWorkCapture";

describe("ActualWorkCard", () => {
  it("offers the two entry-intent choices (no draft yet), never a Resume label", () => {
    const state: ActualWorkCaptureState = { status: "no-draft", submittedCount: 0 };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Record my work/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Enter a tech's work/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Resume/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Draft — not submitted/)).not.toBeInTheDocument();
  });

  it("passes 'record-mine' from Record my work and 'transcribe' from Enter a tech's work", () => {
    const onStartCapture = vi.fn();
    const state: ActualWorkCaptureState = { status: "no-draft", submittedCount: 0 };
    render(<ActualWorkCard state={state} onStartCapture={onStartCapture} />);

    screen.getByRole("button", { name: /Record my work/ }).click();
    expect(onStartCapture).toHaveBeenLastCalledWith("record-mine");

    screen.getByRole("button", { name: /Enter a tech's work/ }).click();
    expect(onStartCapture).toHaveBeenLastCalledWith("transcribe");
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
        isRecorder: true,
        lines: [],
      },
      submittedCount: 0,
    };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Resume draft/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add actual work/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Draft — not submitted/)).toBeInTheDocument();
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
        isRecorder: true,
        lines: [
          { id: "l1", displayNameSnapshot: "Filter", unitOfMeasureSnapshot: null, actualQuantity: 1, note: null, performedByAccountUserId: "au-1", performerDisplayName: "Sam Field" },
        ],
      },
      submittedCount: 0,
    };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Continue draft/ })).toBeInTheDocument();
    expect(screen.getByText(/Draft — not submitted/)).toBeInTheDocument();
  });

  it("shows a non-actionable notice for held-by-other, with no start or resume button", () => {
    const state: ActualWorkCaptureState = { status: "held-by-other", submittedCount: 2 };
    render(<ActualWorkCard state={state} onStartCapture={vi.fn()} />);
    expect(screen.getByText(/Another team member is recording this visit/)).toBeInTheDocument();
    expect(screen.getByText(/2 prior visits recorded/)).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  const ownerRecoveryState: ActualWorkCaptureState = {
    status: "owner-recovery",
    draft: {
      id: "d1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: false,
      recorderAccountUserId: "au-current",
      recorderDisplayName: "Sam Field",
      lines: [],
    },
    submittedCount: 0,
  };

  it("shows the current recorder and a Reassign recorder action for owner-recovery", () => {
    const onReassign = vi.fn();
    render(<ActualWorkCard state={ownerRecoveryState} onStartCapture={vi.fn()} onReassignRecorder={onReassign} />);
    expect(screen.getByText(/Sam Field is recording this visit/)).toBeInTheDocument();
    screen.getByRole("button", { name: /Reassign recorder/ }).click();
    expect(onReassign).toHaveBeenCalled();
  });

  it("renders a dismissible recovery notice over the resolved state", () => {
    const onDismiss = vi.fn();
    render(
      <ActualWorkCard
        state={{ status: "held-by-other", submittedCount: 0 }}
        onStartCapture={vi.fn()}
        recoveryNotice={{ tone: "success", text: "Recording handed to Jordan Lead." }}
        onDismissRecoveryNotice={onDismiss}
      />,
    );
    expect(screen.getByText(/Recording handed to Jordan Lead/)).toBeInTheDocument();
    screen.getByRole("button", { name: /Dismiss/ }).click();
    expect(onDismiss).toHaveBeenCalled();
  });
});
