import { KeepButton } from "../../components/keep/KeepButton";
import { ActualWorkCard } from "./ActualWorkCard";
import { ActualWorkHistoryCard } from "./ActualWorkHistoryCard";
import { ActualWorkReviewCard } from "./ActualWorkReviewCard";
import { ActualWorkPendingReviewsCard } from "./ActualWorkPendingReviewsCard";
import type { useActualWorkCapture, ActualWorkEntryIntent } from "./useActualWorkCapture";
import type { useActualWorkHistory } from "./useActualWorkHistory";
import type { useActualWorkFinancialReview } from "./useActualWorkFinancialReview";
import type { useActualWorkPendingReviews } from "./useActualWorkPendingReviews";

type ActualWorkCaptureState = ReturnType<typeof useActualWorkCapture>["state"];
type ActualWorkHistoryState = ReturnType<typeof useActualWorkHistory>["state"];
type ActualWorkFinancialReview = ReturnType<typeof useActualWorkFinancialReview>;
type ActualWorkPendingReviewsState = ReturnType<typeof useActualWorkPendingReviews>["state"];

// RD-019A: the coherent "Actual Work" shared region — capture card, submitted-visit history,
// Owner/Admin financial review card, and the replacement-recovery affordance. This section is a
// layout surface: it receives state values plus already-wired callbacks from
// `RequestDetailContent` and never touches the Actual Work hook objects, route selection, or retry
// policy itself. It may only derive whether its own regions have anything to show.
interface RequestDetailActualWorkSectionProps {
  captureState: ActualWorkCaptureState;
  historyState: ActualWorkHistoryState;
  reviewState: ActualWorkFinancialReview["state"];
  useWorkspaceRoute: boolean;
  canReviewActualWork?: boolean;
  focusReviewOnMount: boolean;
  recoveryNotice: ReturnType<typeof useActualWorkCapture>["recoveryNotice"];
  onDismissRecoveryNotice: () => void;
  onStartCapture: (intent?: ActualWorkEntryIntent) => void;
  onReassignRecorder: () => void;
  onRetryHistory: () => void;
  // Defined only on the workspace route; below 1001px the inline review card replaces per-visit links.
  onOpenVisit?: (visitId: string) => void;
  onRetryReview: () => void;
  onReview: ActualWorkFinancialReview["review"];
  onResolveLine: ActualWorkFinancialReview["resolveLine"];
  onRecordNoChargeDisposition: ActualWorkFinancialReview["recordNoChargeDisposition"];
  onReplaceVisit: ActualWorkFinancialReview["replace"];
  isVisitMutating: ActualWorkFinancialReview["isVisitMutating"];
  onReviewSuccess: () => void;
  replacementRecoverySuccessorId: string | null;
  onOpenReplacementDraft: (successorId: string) => void;
  // BL138 Slice 1B-client: the Owner/Admin request-scoped "Pending financial reviews (N)" task
  // card, rendered above the Actual Work module and gated on `canReviewActualWork`. `onReviewVisit`
  // is resolved by `RequestDetailContent` — wide viewport routes to the workspace deep link, narrow
  // viewport scrolls to and focuses that visit's inline review card. `onFinancialReviewChanged`
  // refreshes the pending projection after any readiness-changing mutation outcome.
  pendingReviewsState: ActualWorkPendingReviewsState;
  onRetryPendingReviews: () => void;
  onReviewPendingVisit: (actualWorkId: string) => void;
  onFinancialReviewChanged: () => void;
  // Narrow-viewport direct entry: the visit id whose inline review card should be focused, and the
  // clear callback fired once the inline card has resolved the request.
  focusReviewVisitId: string | null;
  onFocusReviewVisitHandled: () => void;
}

export function RequestDetailActualWorkSection({
  captureState,
  historyState,
  reviewState,
  useWorkspaceRoute,
  canReviewActualWork,
  focusReviewOnMount,
  recoveryNotice,
  onDismissRecoveryNotice,
  onStartCapture,
  onReassignRecorder,
  onRetryHistory,
  onOpenVisit,
  onRetryReview,
  onReview,
  onResolveLine,
  onRecordNoChargeDisposition,
  onReplaceVisit,
  isVisitMutating,
  onReviewSuccess,
  replacementRecoverySuccessorId,
  onOpenReplacementDraft,
  pendingReviewsState,
  onRetryPendingReviews,
  onReviewPendingVisit,
  onFinancialReviewChanged,
  focusReviewVisitId,
  onFocusReviewVisitHandled,
}: RequestDetailActualWorkSectionProps) {
  // Editable capture states — the recorder's own resume/start affordance.
  const actualWorkCaptureEditable =
    captureState.status === "no-draft" || captureState.status === "draft";
  // Also render the compact strip for the non-actionable "another team member is recording this
  // visit" state (GAP-055), so a qualified non-recorder still sees why there is no entry point.
  const actualWorkCardVisible =
    actualWorkCaptureEditable ||
    captureState.status === "held-by-other" ||
    captureState.status === "owner-recovery";
  const actualWorkHistoryVisible =
    historyState.status === "error" ||
    (historyState.status === "loaded" && historyState.submittedVisits.length > 0);

  return (
    <>
      {/* BL138 Slice 1B-client: Owner/Admin request-scoped pending-financial-review task card,
          above the Actual Work module. Self-hides when nothing is pending or on a 403; renders even
          while an editable Draft is open (GAP-065A). No authorization broadening — the card and its
          data are gated on `canReviewActualWork` and the same server-side reviewer gate. */}
      {canReviewActualWork && (
        <ActualWorkPendingReviewsCard
          state={pendingReviewsState}
          onRetry={onRetryPendingReviews}
          onReviewVisit={onReviewPendingVisit}
        />
      )}

      {/* 4. Work execution — Actual Work, one compact module (locked exception, 2026-08-22:
          capture and visit history share one enclosing card; visit history renders only when
          visits actually exist, no "no visits submitted" filler). Whole module self-hides when
          neither has content. */}
      {(actualWorkCardVisible || actualWorkHistoryVisible) && (
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]">
          <ActualWorkCard
            state={captureState}
            onStartCapture={onStartCapture}
            onReassignRecorder={onReassignRecorder}
            recoveryNotice={recoveryNotice}
            onDismissRecoveryNotice={onDismissRecoveryNotice}
            bare
          />
          {/* GAP-065A: a submitted-visit history renders whenever it has content, even while the
              current capture state is an editable Draft — an active Draft must not hide earlier
              locked visits or their Owner/Admin financial-review route. Non-editable states keep
              rendering the card in their loading/idle phases as before. */}
          {(!actualWorkCaptureEditable || actualWorkHistoryVisible) && (
            <ActualWorkHistoryCard
              state={historyState}
              onRetry={onRetryHistory}
              // BL136 4f-ii: on a wide viewport each submitted visit opens in the Actual Work
              // workspace (where the Owner/Admin office region now lives); below 1001px the
              // review card renders inline on this page instead, so no per-visit link is offered.
              onOpenVisit={onOpenVisit}
              bare
            />
          )}
        </div>
      )}

      {/* BL136 4f-ii: below 1001px only — on a wide viewport office financial review and the
          "Correct this visit" affordance live exclusively on the workspace route. */}
      {!useWorkspaceRoute && canReviewActualWork && (
        <ActualWorkReviewCard
          state={reviewState}
          onRetry={onRetryReview}
          onReview={onReview}
          onResolveLine={onResolveLine}
          onRecordNoChargeDisposition={onRecordNoChargeDisposition}
          onReplace={onReplaceVisit}
          isVisitMutating={isVisitMutating}
          focusOnMount={focusReviewOnMount}
          onReviewSuccess={onReviewSuccess}
          onFinancialReviewChanged={onFinancialReviewChanged}
          focusVisitId={focusReviewVisitId}
          onFocusVisitHandled={onFocusReviewVisitHandled}
        />
      )}

      {!useWorkspaceRoute && replacementRecoverySuccessorId && (
        <div role="status" className="rounded-xl border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] px-4 py-3 text-sm text-[var(--ophalo-attention)]">
          <p className="font-medium">The correction draft was created.</p>
          <p className="mt-0.5 text-xs">Open it to review and submit the replacement visit.</p>
          <KeepButton
            variant="secondary"
            className="mt-2"
            onClick={() => onOpenReplacementDraft(replacementRecoverySuccessorId)}
          >
            Open replacement draft
          </KeepButton>
        </div>
      )}
    </>
  );
}
