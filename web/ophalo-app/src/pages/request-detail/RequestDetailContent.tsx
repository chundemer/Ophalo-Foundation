import { useRef, useState, useEffect, useCallback } from "react";
import { type KeepRequestDetailResult } from "../../lib/apiClient";
import { type TimelineFilter } from "./TimelineEvent";
import {
  type RequestDetailLayoutProps,
  ProminentFeedbackCard,
  HeroAttentionBanner,
  OriginalRequestCard,
  RelatedWorkPanel,
  CustomerSignalPanel,
  FeedbackSummaryCard,
  SourceMetaPanel,
  WorkControlsGroup,
} from "./DetailPanels";
import { TodayPromiseBanner } from "./DetailHero";
import { RequestDetailAnchor } from "./RequestDetailAnchor";
import { MobileRequestAnchor, MobileActionRail } from "./MobileRequestAnchor";
import { MobileContactLocationCard } from "./MobileContactLocationCard";
import { UnifiedComposer, type UnifiedComposerHandle } from "./UnifiedComposer";
import { KeepButton } from "../../components/keep/KeepButton";
import { RequestDetailActivity } from "./RequestDetailActivity";
import { useActualWorkCapture } from "./useActualWorkCapture";
import { ActualWorkCard } from "./ActualWorkCard";
import { useActualWorkHistory } from "./useActualWorkHistory";
import { ActualWorkHistoryCard } from "./ActualWorkHistoryCard";
import { ActualWorkComposer } from "./ActualWorkComposer";
import { ActualWorkRecoveryDrawer } from "./ActualWorkRecoveryDrawer";
import { TeamSection } from "./TeamSection";
import { FOCUS_RING } from "./helpers";
import { useActualWorkFinancialReview } from "./useActualWorkFinancialReview";
import { ActualWorkReviewCard } from "./ActualWorkReviewCard";

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
  onOpenClearAttention: () => void;
  canReviewActualWork?: boolean;
  focusPanel?: string;
  onActualWorkReviewSuccess?: () => void;
}

// Work Canvas — the Workbench's sole vertical scroll surface (locked spec §1.2, §1.5, §5, §7.1).
// Desktop module order: attention guidance -> Customer Need -> Actual Work context ->
// communication -> record details -> activity. Mobile (Slice 3, 2026-08-26) inserts a
// contact/service-location card after attention and swaps the last two: attention -> contact/
// location -> Customer Need -> Actual Work -> communication -> activity -> record details.
// Proposed Scope is explicitly deferred from this pilot Workbench (locked spec §1.7/§3) and is
// not wired here.
export function RequestDetailContent(props: RequestDetailContentProps) {
  const { detail, requestId, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onOpenReassignOwner, onOpenWatchers, onRecordFollowUp, onCreateFollowUp, onReviewSuccess, onOpenClearAttention } = props;
  const layoutProps: RequestDetailLayoutProps = { requestId, detail, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onOpenReassignOwner, onOpenWatchers, onRecordFollowUp, onCreateFollowUp, onReviewSuccess };
  const composerRef = useRef<UnifiedComposerHandle>(null);
  const actualWorkCapture = useActualWorkCapture(requestId);
  const actualWorkHistory = useActualWorkHistory(requestId);
  const actualWorkFinancialReview = useActualWorkFinancialReview(
    props.canReviewActualWork && actualWorkHistory.state.status === "loaded" ? actualWorkHistory.state.submittedVisits : [],
  );
  const [recorderDrawerOpen, setRecorderDrawerOpen] = useState(false);
  // Editable capture states — the recorder's own resume/start affordance.
  const actualWorkCaptureEditable = actualWorkCapture.state.status === "no-draft" || actualWorkCapture.state.status === "draft";
  // Also render the compact strip for the non-actionable "another team member is recording this
  // visit" state (GAP-055), so a qualified non-recorder still sees why there is no entry point.
  const actualWorkCardVisible =
    actualWorkCaptureEditable ||
    actualWorkCapture.state.status === "held-by-other" ||
    actualWorkCapture.state.status === "owner-recovery";
  const actualWorkHistoryVisible =
    actualWorkHistory.state.status === "error" ||
    (actualWorkHistory.state.status === "loaded" && actualWorkHistory.state.submittedVisits.length > 0);

  // Locked in keep-ui-design-model-v2.md §13 (build-log 133); duplicated rather than imported —
  // same rule `RequestWorkbenchShell.tsx`'s `PROTECTED_WORKSPACE_MIN_PX` measures.
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [isWide, setIsWide] = useState(false);
  useEffect(() => {
    const el = rootRef.current;
    if (!el) return;
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0;
      setIsWide(width >= 1001);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  // Mobile action-rail hide/unpin while text is being entered (Slice 2, locked spec §4.2).
  // Scoped `focus`/`blur` on the canvas root rather than document, and rather than threading a
  // prop through every sheet/composer — React's `onFocus`/`onBlur` bubble via `focusin`/
  // `focusout` under the hood, so one pair of handlers on the outer wrapper covers every
  // descendant field with no cleanup/effect needed. `relatedTarget` guards the field-to-field
  // flicker case (e.g. tabbing straight from one text field into another).
  const [isTextEditing, setIsTextEditing] = useState(false);
  const isTextEntryElement = useCallback((el: EventTarget | null): boolean => {
    if (!(el instanceof HTMLElement)) return false;
    const tag = el.tagName;
    return tag === "INPUT" || tag === "TEXTAREA" || el.isContentEditable;
  }, []);
  const handleCanvasFocus = useCallback(
    (e: React.FocusEvent<HTMLDivElement>) => {
      if (isTextEntryElement(e.target)) setIsTextEditing(true);
    },
    [isTextEntryElement],
  );
  const handleCanvasBlur = useCallback(
    (e: React.FocusEvent<HTMLDivElement>) => {
      if (!isTextEntryElement(e.target)) return;
      if (isTextEntryElement(e.relatedTarget)) return;
      setIsTextEditing(false);
    },
    [isTextEntryElement],
  );

  const activityBlock = (
    <RequestDetailActivity timelineFilter={props.timelineFilter} onTimelineFilterChange={props.onTimelineFilterChange} displayedEvents={props.displayedEvents} />
  );

  const recordDetailsBlock = (
    <details className="group rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3">
      <summary
        className={`flex cursor-pointer list-none items-center justify-between text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] ${FOCUS_RING} rounded`}
      >
        Record details
        <span className="text-[var(--ophalo-muted)] transition-transform group-open:rotate-180">⌄</span>
      </summary>
      {/* Each panel self-hides (returns null) when it has nothing meaningful to show;
          divide-y only borders elements with an actual preceding DOM sibling, so a hidden
          panel never leaves a divider/empty gap. */}
      <div className="mt-3 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]">
        <CustomerSignalPanel detail={detail} bare />
        <RelatedWorkPanel requestId={requestId} onNavigate={props.onNavigate} bare />
        <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} bare />
        {!showProminentFeedbackCard && <FeedbackSummaryCard detail={detail} bare />}
        <SourceMetaPanel detail={detail} bare />
      </div>
    </details>
  );

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
        />
      ) : (
        <MobileRequestAnchor detail={detail} />
      )}
      <div data-request-detail-work-canvas className="flex-1 min-h-0 min-w-0 overflow-y-auto px-4 md:px-6 py-5">
      <div className="max-w-4xl mx-auto w-full space-y-3">
        {/* 1. Active attention guidance */}
        <div id="focus-panel-attention" className="space-y-3">
          <HeroAttentionBanner
            requestId={requestId}
            detail={detail}
            onDetailUpdated={onDetailUpdated}
            onOpenClearAttention={onOpenClearAttention}
            onRecordFollowUp={onRecordFollowUp}
            onContactLaunched={onContactLaunched}
            onActivateCustomerUpdateComposer={() => composerRef.current?.activateCustomerUpdate()}
          />
          <TodayPromiseBanner detail={detail} onRecordFollowUp={onRecordFollowUp} />
        </div>

        {/* 2. Contact/service location — mobile canvas only (Slice 3, 2026-08-26); desktop
            keeps this content solely in RequestDetailAnchor/CustomerContactStrip. */}
        {!isWide && (
          <MobileContactLocationCard detail={detail} onContactLaunched={onContactLaunched} onEditLocation={onEditLocation} />
        )}

        {/* 3. Customer need — permanent, always mounted regardless of attention state
            (locked spec, 2026-08-24: decoupled from the conditional attention rail). */}
        <OriginalRequestCard detail={detail} />

        {/* 4. Work execution — Actual Work, one compact module (locked exception, 2026-08-22:
            capture and visit history share one enclosing card; visit history renders only when
            visits actually exist, no "no visits submitted" filler). Whole module self-hides when
            neither has content. */}
        {(actualWorkCardVisible || actualWorkHistoryVisible) && (
          <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]">
            <ActualWorkCard
              state={actualWorkCapture.state}
              onStartCapture={() => void actualWorkCapture.startCapture()}
              onReassignRecorder={() => setRecorderDrawerOpen(true)}
              recoveryNotice={actualWorkCapture.recoveryNotice}
              onDismissRecoveryNotice={actualWorkCapture.clearRecoveryNotice}
              bare
            />
            {!actualWorkCaptureEditable && <ActualWorkHistoryCard state={actualWorkHistory.state} onRetry={() => void actualWorkHistory.retry()} bare />}
          </div>
        )}

        {props.canReviewActualWork && (
          <ActualWorkReviewCard
            state={actualWorkFinancialReview.state}
            onRetry={() => void actualWorkFinancialReview.retry()}
            onReview={actualWorkFinancialReview.review}
            focusOnMount={props.focusPanel === "actual-work-review"}
            onReviewSuccess={() => {
              void actualWorkHistory.retry();
              void props.onActualWorkReviewSuccess?.();
            }}
          />
        )}

        {/* 5. Communication — composer only; Follow-Up/Planned-For and priority moved to the
            Anchor's compact Internal Planning strip (locked 2026-08-24). Desktop's one Log Contact
            entry point lives in the Anchor; mobile's lives in the Contact/Location card above
            (Slice 3), not duplicated here. */}
        <div className="space-y-3">
          {showProminentFeedbackCard && <ProminentFeedbackCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />}
          {props.reviewSuccessMsg && <div role="status" aria-live="polite" className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm text-[var(--ophalo-success)] font-medium">{props.reviewSuccessMsg}</div>}
          <div
            id="focus-panel-update"
            tabIndex={-1}
            className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)]"
          >
            <UnifiedComposer ref={composerRef} requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} customerUpdateDraft={props.customerUpdateDraft} onCustomerUpdateDraftChange={props.onCustomerUpdateDraftChange} customerUpdateDraftStatus={props.customerUpdateDraftStatus} onCustomerUpdateDraftStatusChange={props.onCustomerUpdateDraftStatusChange} highlight={highlights.sendUpdate} bare />
          </div>
        </div>

        {/* Actionable, stays visible (not lower-frequency record context) */}
        {!showProminentFeedbackCard && (
          <WorkControlsGroup
            requestId={requestId}
            detail={detail}
            onDetailUpdated={onDetailUpdated}
            highlights={{ feedbackReview: "secondary" }}
            onReviewSuccess={onReviewSuccess}
          />
        )}
        {detail.availableActions.canCreateFollowUpRequest && (
          <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
            <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Follow-up work</p>
            <p className="text-xs text-[var(--ophalo-muted)] mb-3">
              This request is closed. Start a new request for any additional work needed.
            </p>
            <KeepButton variant="secondary" onClick={onCreateFollowUp} className="w-full">
              Create follow-up request
            </KeepButton>
          </div>
        )}

        {/* 6/7. Activity and lower-frequency record context. Desktop keeps its locked order
            (Record details above Activity, slice 5, 2026-08-23) — unchanged. Mobile's locked
            canvas order (Slice 3, 2026-08-26) puts Activity above Record details, so the two
            blocks below swap only when !isWide. Each still self-hides/collapses exactly as
            before; only their relative order changes. */}
        {isWide ? (
          <>
            {recordDetailsBlock}
            {activityBlock}
          </>
        ) : (
          <>
            {activityBlock}
            {recordDetailsBlock}
          </>
        )}
      </div>
      </div>
      {!isWide && (
        <MobileActionRail
          {...layoutProps}
          onOpenClearAttention={onOpenClearAttention}
          onActivateCustomerUpdateComposer={() => composerRef.current?.activateCustomerUpdate()}
          hidden={isTextEditing}
        />
      )}
      {actualWorkCapture.isModalOpen && actualWorkCapture.state.status === "draft" && (
        <ActualWorkComposer
          isWide={isWide}
          draft={actualWorkCapture.state.draft}
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
