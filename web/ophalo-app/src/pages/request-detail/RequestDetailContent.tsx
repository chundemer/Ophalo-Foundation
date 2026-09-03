import { useRef, useState, useCallback } from "react";
import { type KeepRequestDetailResult } from "../../lib/apiClient";
import { type TimelineFilter } from "./TimelineEvent";
import { type RequestDetailLayoutProps } from "./DetailPanels";
import { RequestDetailAnchor } from "./RequestDetailAnchor";
import { MobileRequestAnchor, MobileActionRail } from "./MobileRequestAnchor";
import { type UnifiedComposerHandle } from "./UnifiedComposer";
import { RequestDetailActivity } from "./RequestDetailActivity";
import { useActualWorkCapture, type ActualWorkEntryIntent } from "./useActualWorkCapture";
import { useActualWorkHistory } from "./useActualWorkHistory";
import { ActualWorkComposer } from "./ActualWorkComposer";
import { ActualWorkRecoveryDrawer } from "./ActualWorkRecoveryDrawer";
import { useActualWorkFinancialReview } from "./useActualWorkFinancialReview";
import { useActualWorkPendingReviews } from "./useActualWorkPendingReviews";
import { useRequestDetailLayout } from "./useRequestDetailLayout";
import { RequestDetailWorkCanvas } from "./RequestDetailWorkCanvas";
import { RequestDetailActualWorkSection } from "./RequestDetailActualWorkSection";
import { RecordDetailsSection } from "./RecordDetailsSection";
import { ActualWorkHistoryCard } from "./ActualWorkHistoryCard";

interface RequestDetailContentProps extends RequestDetailLayoutProps {
  detail: KeepRequestDetailResult;
  canRecordShareIntent: boolean;
  needsShare: boolean;
  onOpenShareDrawer: () => void;
  customerUpdateDraft: string;
  onCustomerUpdateDraftChange: (value: string) => void;
  customerUpdateDraftStatus: string;
  onCustomerUpdateDraftStatusChange: (value: string) => void;
  reviewSuccessMsg: string | null;
  timelineFilter: TimelineFilter;
  onTimelineFilterChange: (filter: TimelineFilter) => void;
  displayedEvents: KeepRequestDetailResult["events"];
  onNavigate?: (id: string) => void;
  // BL136 4f-i: wide-screen capture navigates to the dedicated workspace route instead of opening
  // the in-page modal. Undefined below 1001px — capture stays a full-bleed modal here.
  onNavigateToActualWorkspace?: (requestId: string, visit?: "new" | "draft" | (string & {})) => void;
  onOpenClearAttention: () => void;
  canReviewActualWork?: boolean;
  // 4c-i-c-2: the signed-in member's account-user id, threaded into useActualWorkCapture so
  // "Record my work" can seed the Draft's ticket-default performer with the caller.
  currentAccountUserId?: string;
  focusPanel?: string;
  onActualWorkReviewSuccess?: () => void;
}

// RD-019A: this component is the page-level Request Detail coordinator. It owns authoritative
// detail state wiring, the Actual Work hooks (capture/history/financial-review), replacement
// recovery, the anchor slot, the mobile action rail, and the in-page modal/drawer surfaces.
// Layout is delegated: `useRequestDetailLayout` centralizes the two width measurements and the
// action-rail focus state; `RequestDetailWorkCanvas` owns canvas structure and region order;
// `RequestDetailActualWorkSection` groups the Actual Work region from already-wired hooks.
export function RequestDetailContent(props: RequestDetailContentProps) {
  const { detail, requestId, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onOpenReassignOwner, onOpenWatchers, onRecordFollowUp, onCreateFollowUp, onReviewSuccess, onOpenClearAttention } = props;
  const layoutProps: RequestDetailLayoutProps = { requestId, detail, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onOpenReassignOwner, onOpenWatchers, onRecordFollowUp, onCreateFollowUp, onReviewSuccess };
  const composerRef = useRef<UnifiedComposerHandle>(null);
  const actualWorkCapture = useActualWorkCapture(requestId, props.currentAccountUserId);
  const actualWorkHistory = useActualWorkHistory(requestId);

  const { rootRef, isViewportWide, isWide, isTextEditing, handleCanvasFocus, handleCanvasBlur } =
    useRequestDetailLayout();

  // On a wide viewport the capture entry point AND the Owner/Admin office financial review live on
  // the dedicated workspace route; below 1001px both stay on this page (the workspace has no narrow
  // form and redirects narrow deep-links back here). The two are mutually exclusive by width.
  const useWorkspaceRoute = isViewportWide && !!props.onNavigateToActualWorkspace;

  const actualWorkFinancialReview = useActualWorkFinancialReview(
    // BL136 4f-ii: on a wide viewport office financial review is rendered only in the workspace, so
    // this hook is fed nothing here (no detail reads fire). BL136 4e-iii: a superseded source is
    // inert for financial review — its detail read returns 409 — so it is excluded even below
    // 1001px, though the history read stays unfiltered for lineage.
    !useWorkspaceRoute && props.canReviewActualWork && actualWorkHistory.state.status === "loaded"
      ? actualWorkHistory.state.submittedVisits.filter((visit) => !visit.superseded)
      : [],
  );
  // BL138 Slice 1B-client: the Owner/Admin request-scoped "Pending financial reviews (N)" card
  // read. Independent of `useActualWorkFinancialReview` (which is fed nothing on the wide route) —
  // this card is the single wide-viewport discovery surface for unreviewed visits on the request.
  // It owns its own `reload()`; there is no shared React Query key to invalidate.
  const pendingReviews = useActualWorkPendingReviews(requestId, props.canReviewActualWork === true);
  // BL138 Slice 1B-client narrow-viewport direct entry: the pending visit id awaiting scroll+focus
  // in the inline review card. Set on click, cleared by the inline card once it has resolved the
  // request — so the action works even if the pending card mounts before the inline card loads.
  const [pendingFocusVisitId, setPendingFocusVisitId] = useState<string | null>(null);

  const [recorderDrawerOpen, setRecorderDrawerOpen] = useState(false);

  // BL136 4e-iii: holds the successor Draft id when a replacement-copy correction succeeded but the
  // Draft could not be auto-opened (e.g. the acting user lacks ActualWorkCapture, or another
  // session opened a different Draft first) — surfaces an explicit "open the replacement draft"
  // recovery affordance instead of a dead-end.
  const [replacementRecoverySuccessorId, setReplacementRecoverySuccessorId] = useState<string | null>(null);

  const handleReplaceVisit = useCallback<typeof actualWorkFinancialReview.replace>(
    async (visit, reason) => {
      const outcome = await actualWorkFinancialReview.replace(visit, reason);
      if (outcome.kind === "replaced") {
        await actualWorkHistory.retry();
        // BL138 Slice 1B-client: the source row leaves the pending card and its successor may
        // appear — refresh the request-scoped projection here, not in the card.
        void pendingReviews.reload();
        if (useWorkspaceRoute) {
          // The workspace route mounts its own capture hook, which re-probes and lands on the
          // successor Draft (already created + source superseded by the service).
          props.onNavigateToActualWorkspace!(requestId, "draft");
        } else {
          const opened = await actualWorkCapture.openReplacementDraft(outcome.successorActualWorkId);
          setReplacementRecoverySuccessorId(opened ? null : outcome.successorActualWorkId);
        }
      }
      return outcome;
    },
    [actualWorkFinancialReview, actualWorkHistory, actualWorkCapture, pendingReviews, useWorkspaceRoute, props, requestId],
  );

  const openReplacementDraft = useCallback(
    (successorId: string) => {
      void actualWorkCapture
        .openReplacementDraft(successorId)
        .then((opened) => setReplacementRecoverySuccessorId(opened ? null : successorId));
    },
    [actualWorkCapture],
  );

  // Route selection and retry policy stay here (RD-019A boundary): the section receives only the
  // resulting callbacks.
  const handleStartCapture = useCallback(
    (intent?: ActualWorkEntryIntent) => {
      if (useWorkspaceRoute) {
        void actualWorkCapture.createDraft(intent).then((r) => {
          if (r === "created" || r === "exists") props.onNavigateToActualWorkspace!(requestId, "draft");
        });
      } else {
        void actualWorkCapture.startCapture(intent);
      }
    },
    [useWorkspaceRoute, actualWorkCapture, props, requestId],
  );
  const handleReviewSuccess = useCallback(() => {
    void actualWorkHistory.retry();
    void props.onActualWorkReviewSuccess?.();
  }, [actualWorkHistory, props]);

  // BL138 Slice 1B-client: single cross-hook refresh point for the pending-review card. Fired at
  // every mutation outcome that can change a row's membership or readiness (resolution / no-charge
  // success, review completion, reconcile / review-blocked branches from the inline card, and the
  // replacement success branch above). `useActualWorkFinancialReview` already self-reloads its own
  // detail state and history refresh stays on `handleReviewSuccess`, so this only reloads here.
  const handleFinancialReviewChanged = useCallback(() => {
    void pendingReviews.reload();
  }, [pendingReviews]);

  // Wide viewport routes to the visit's workspace deep link; narrow viewport hands the visit id to
  // the inline review card, which scrolls to and focuses it once loaded (BL138 locked — no narrow
  // workspace, and the entry point must not depend on the inline card already being mounted).
  const handleReviewPendingVisit = useCallback(
    (actualWorkId: string) => {
      if (useWorkspaceRoute) {
        props.onNavigateToActualWorkspace!(requestId, actualWorkId);
        return;
      }
      setPendingFocusVisitId(actualWorkId);
    },
    [useWorkspaceRoute, props, requestId],
  );
  const handleFocusReviewVisitHandled = useCallback(() => setPendingFocusVisitId(null), []);

  const activityBlock = (
    <RequestDetailActivity timelineFilter={props.timelineFilter} onTimelineFilterChange={props.onTimelineFilterChange} displayedEvents={props.displayedEvents} />
  );

  const recordDetailsBlock = (
    <RecordDetailsSection
      detail={detail}
      requestId={requestId}
      showProminentFeedbackCard={showProminentFeedbackCard}
      onDetailUpdated={onDetailUpdated}
      onNavigate={props.onNavigate}
    />
  );

  const actualWorkSection = (
    <RequestDetailActualWorkSection
      captureState={actualWorkCapture.state}
      historyState={actualWorkHistory.state}
      reviewState={actualWorkFinancialReview.state}
      useWorkspaceRoute={useWorkspaceRoute}
      canReviewActualWork={props.canReviewActualWork}
      focusReviewOnMount={props.focusPanel === "actual-work-review"}
      recoveryNotice={actualWorkCapture.recoveryNotice}
      onDismissRecoveryNotice={actualWorkCapture.clearRecoveryNotice}
      onStartCapture={handleStartCapture}
      onReassignRecorder={() => setRecorderDrawerOpen(true)}
      onRetryHistory={() => void actualWorkHistory.retry()}
      onOpenVisit={
        useWorkspaceRoute
          ? (visitId) => props.onNavigateToActualWorkspace!(requestId, visitId)
          : undefined
      }
      onRetryReview={() => {
        void actualWorkFinancialReview.retry();
        // BL138 Slice 1B-client: a manual retry re-reads the authoritative visit detail, so the
        // request-scoped pending projection must refresh alongside it.
        handleFinancialReviewChanged();
      }}
      onReview={actualWorkFinancialReview.review}
      onResolveLine={actualWorkFinancialReview.resolveLine}
      onRecordNoChargeDisposition={actualWorkFinancialReview.recordNoChargeDisposition}
      onReplaceVisit={handleReplaceVisit}
      isVisitMutating={actualWorkFinancialReview.isVisitMutating}
      onReviewSuccess={handleReviewSuccess}
      replacementRecoverySuccessorId={replacementRecoverySuccessorId}
      onOpenReplacementDraft={openReplacementDraft}
      pendingReviewsState={pendingReviews.state}
      onRetryPendingReviews={() => void pendingReviews.reload()}
      onReviewPendingVisit={handleReviewPendingVisit}
      onFinancialReviewChanged={handleFinancialReviewChanged}
      focusReviewVisitId={pendingFocusVisitId}
      onFocusReviewVisitHandled={handleFocusReviewVisitHandled}
      showHistory={!isWide}
    />
  );

  const visitHistoryBlock = (
    <ActualWorkHistoryCard
      state={actualWorkHistory.state}
      onRetry={() => void actualWorkHistory.retry()}
      onOpenVisit={useWorkspaceRoute ? (visitId) => props.onNavigateToActualWorkspace!(requestId, visitId) : undefined}
      presentation="summary"
    />
  );

  const actualWorkShortcut =
    actualWorkCapture.state.status === "draft"
      ? { label: "Continue Actual Work", onClick: () => handleStartCapture() }
      : actualWorkCapture.state.status === "no-draft"
        ? { label: "Record Actual Work", onClick: () => handleStartCapture() }
        : undefined;
  const financialReviewShortcut = (() => {
    if (pendingReviews.state.status !== "loaded" || pendingReviews.state.count === 0) return undefined;
    const firstPendingVisit = pendingReviews.state.items[0];
    if (!firstPendingVisit) return undefined;
    return {
      label: `Review financials (${pendingReviews.state.count})`,
      onClick: () => handleReviewPendingVisit(firstPendingVisit.actualWorkId),
    };
  })();

  return (
    <div ref={rootRef} onFocus={handleCanvasFocus} onBlur={handleCanvasBlur} className="flex flex-1 min-h-0 min-w-0 flex-col">
      {isWide ? (
        <RequestDetailAnchor
          {...layoutProps}
          canRecordShareIntent={props.canRecordShareIntent}
          needsShare={props.needsShare}
          onOpenShareDrawer={props.onOpenShareDrawer}
          onOpenClearAttention={onOpenClearAttention}
          onActivateCustomerUpdateComposer={() => composerRef.current?.activateCustomerUpdate()}
          actualWorkShortcut={actualWorkShortcut}
          financialReviewShortcut={financialReviewShortcut}
        />
      ) : (
        <MobileRequestAnchor detail={detail} />
      )}
      <RequestDetailWorkCanvas
        isWide={isWide}
        requestId={requestId}
        detail={detail}
        highlights={highlights}
        showProminentFeedbackCard={showProminentFeedbackCard}
        onDetailUpdated={onDetailUpdated}
        onContactLaunched={onContactLaunched}
        onEditLocation={onEditLocation}
        onRecordFollowUp={onRecordFollowUp}
        onCreateFollowUp={onCreateFollowUp}
        onReviewSuccess={onReviewSuccess}
        onOpenClearAttention={onOpenClearAttention}
        onActivateCustomerUpdateComposer={() => composerRef.current?.activateCustomerUpdate()}
        composerRef={composerRef}
        customerUpdateDraft={props.customerUpdateDraft}
        onCustomerUpdateDraftChange={props.onCustomerUpdateDraftChange}
        customerUpdateDraftStatus={props.customerUpdateDraftStatus}
        onCustomerUpdateDraftStatusChange={props.onCustomerUpdateDraftStatusChange}
        reviewSuccessMsg={props.reviewSuccessMsg}
        actualWorkSection={actualWorkSection}
        activityBlock={activityBlock}
        recordDetailsBlock={recordDetailsBlock}
        visitHistoryBlock={visitHistoryBlock}
      />
      {!isWide && (
        <MobileActionRail
          {...layoutProps}
          onOpenClearAttention={onOpenClearAttention}
          onActivateCustomerUpdateComposer={() => composerRef.current?.activateCustomerUpdate()}
          hidden={isTextEditing}
        />
      )}
      {/* BL136 4f-i: the in-page full-bleed modal is the narrow-screen capture surface only. On
          wide screens capture lives on the dedicated workspace route (`useWorkspaceRoute`). */}
      {!useWorkspaceRoute && actualWorkCapture.isModalOpen && actualWorkCapture.state.status === "draft" && (
        <ActualWorkComposer
          isWide={isWide}
          draft={actualWorkCapture.state.draft}
          replacementCorrection={actualWorkCapture.replacementCorrection}
          conflictNotice={actualWorkCapture.conflictNotice}
          onClose={actualWorkCapture.closeModal}
          onCommitted={async () => {
            await actualWorkCapture.refetchDraft();
          }}
          onConflict={(message) => void actualWorkCapture.reconcileAfterConflict(message)}
          onDismissNotice={actualWorkCapture.clearConflictNotice}
          onRetryReconciliation={() => void actualWorkCapture.retryReconciliation()}
          onSubmitted={() => {
            actualWorkCapture.markSubmitted();
            void actualWorkHistory.retry();
          }}
          onDiscarded={actualWorkCapture.onDraftDiscarded}
          submittedVisits={actualWorkHistory.state.status === "loaded" ? actualWorkHistory.state.submittedVisits : []}
          currentAccountUserId={props.currentAccountUserId}
          onSetDefaultPerformer={actualWorkCapture.setDefaultPerformer}
          onSetVisitNote={actualWorkCapture.setVisitNote}
          onSetZeroLineDisposition={actualWorkCapture.setZeroLineDisposition}
          onHandOffToOffice={actualWorkCapture.handOffToOffice}
        />
      )}
      {recorderDrawerOpen && actualWorkCapture.state.status === "owner-recovery" && (
        <ActualWorkRecoveryDrawer
          draft={actualWorkCapture.state.draft}
          onClose={() => setRecorderDrawerOpen(false)}
          onTransfer={actualWorkCapture.transferRecorder}
        />
      )}
    </div>
  );
}
