import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestDetailActualWorkSection } from "../RequestDetailActualWorkSection";
import type { ActualWorkCaptureState } from "../useActualWorkCapture";
import type { ActualWorkHistoryState } from "../useActualWorkHistory";
import type { ActualWorkFinancialReviewState } from "../useActualWorkFinancialReview";

// GAP-065A: an editable current Draft must not hide earlier submitted/locked visits or their
// wide-viewport "Open in workspace" financial-review route.

vi.mock("../ActualWorkCard", () => ({
  ActualWorkCard: ({ state }: { state: ActualWorkCaptureState }) => (
    <div>capture-card:{state.status}</div>
  ),
}));
vi.mock("../ActualWorkReviewCard", () => ({
  ActualWorkReviewCard: () => <div>inline-review-card</div>,
}));

const draftCapture: ActualWorkCaptureState = {
  status: "draft",
  // Only `status` is read by the section; the drawer/composer own the draft body.
  draft: { id: "d1" } as never,
  submittedCount: 1,
};

const noDraftCapture: ActualWorkCaptureState = { status: "no-draft", submittedCount: 1 };

function historyWithOneVisit(): ActualWorkHistoryState {
  return {
    status: "loaded",
    submittedVisits: [
      {
        id: "visit-777",
        status: "SubmittedToOffice",
        outcome: null,
        completionNote: null,
        submittedAtUtc: "2026-01-02T09:00:00Z",
        visitNote: null,
        lines: [],
      },
    ],
  } as ActualWorkHistoryState;
}

const reviewState: ActualWorkFinancialReviewState = { status: "loaded", visits: [] };

function renderSection(overrides: Partial<React.ComponentProps<typeof RequestDetailActualWorkSection>> = {}) {
  const onOpenVisit = vi.fn();
  render(
    <RequestDetailActualWorkSection
      captureState={draftCapture}
      historyState={historyWithOneVisit()}
      reviewState={reviewState}
      useWorkspaceRoute
      canReviewActualWork={false}
      focusReviewOnMount={false}
      recoveryNotice={null}
      onDismissRecoveryNotice={vi.fn()}
      onStartCapture={vi.fn()}
      onReassignRecorder={vi.fn()}
      onRetryHistory={vi.fn()}
      onOpenVisit={onOpenVisit}
      onRetryReview={vi.fn()}
      onReview={vi.fn()}
      onResolveLine={vi.fn()}
      onRecordNoChargeDisposition={vi.fn()}
      onReplaceVisit={vi.fn()}
      isVisitMutating={vi.fn(() => false) as never}
      onReviewSuccess={vi.fn()}
      replacementRecoverySuccessorId={null}
      onOpenReplacementDraft={vi.fn()}
      {...overrides}
    />,
  );
  return { onOpenVisit };
}

describe("RequestDetailActualWorkSection — Draft plus prior submitted visit (GAP-065A)", () => {
  it("renders both the Draft capture context and the submitted Visit History", () => {
    renderSection();
    expect(screen.getByText("capture-card:draft")).toBeInTheDocument();
    expect(screen.getByText("Visit history")).toBeInTheDocument();
    expect(screen.getByText("1 submitted visit · locked record")).toBeInTheDocument();
  });

  it("wide viewport: the submitted visit still routes via its exact visit ID", async () => {
    const { onOpenVisit } = renderSection();
    await userEvent.click(screen.getByRole("button", { name: "Open in workspace →" }));
    expect(onOpenVisit).toHaveBeenCalledWith("visit-777");
  });

  it("narrow viewport (no workspace route): no per-visit workspace link, inline review stays authoritative", () => {
    const { onOpenVisit } = renderSection({
      useWorkspaceRoute: false,
      onOpenVisit: undefined,
      canReviewActualWork: true,
    });
    expect(screen.queryByRole("button", { name: "Open in workspace →" })).not.toBeInTheDocument();
    expect(screen.getByText("inline-review-card")).toBeInTheDocument();
    expect(onOpenVisit).not.toHaveBeenCalled();
  });

  it("no-Draft behavior is intact: Visit History renders as before", () => {
    renderSection({ captureState: noDraftCapture });
    expect(screen.getByText("capture-card:no-draft")).toBeInTheDocument();
    expect(screen.getByText("Visit history")).toBeInTheDocument();
  });

  it("Draft with empty history shows no Visit History filler", () => {
    renderSection({ historyState: { status: "loaded", submittedVisits: [] } as ActualWorkHistoryState });
    expect(screen.getByText("capture-card:draft")).toBeInTheDocument();
    expect(screen.queryByText("Visit history")).not.toBeInTheDocument();
  });

  it("does not broaden review authorization: no inline review card without canReviewActualWork", () => {
    renderSection({ useWorkspaceRoute: false, onOpenVisit: undefined, canReviewActualWork: false });
    expect(screen.queryByText("inline-review-card")).not.toBeInTheDocument();
  });
});
