import { type KeepRequestDetailResult } from "../../lib/apiClient";
import { type TimelineFilter } from "./TimelineEvent";
import {
  type RequestDetailLayoutProps,
  ProminentFeedbackCard,
  AttentionGuidanceCard,
  MarkHandledCard,
  OriginalRequestCard,
  RelatedWorkPanel,
  TriagePanel,
  FeedbackSummaryCard,
  SourceMetaPanel,
  WorkControlsGroup,
} from "./DetailPanels";
import { TodayPromiseBanner } from "./DetailHero";
import { RequestDetailAnchor } from "./RequestDetailAnchor";
import { UnifiedComposer } from "./UnifiedComposer";
import { TimingPanel } from "./TimingPanel";
import { KeepButton } from "../../components/keep/KeepButton";
import { RequestDetailActivity } from "./RequestDetailActivity";
import { useActualWorkCapture } from "./useActualWorkCapture";
import { ActualWorkCard } from "./ActualWorkCard";
import { useActualWorkHistory } from "./useActualWorkHistory";
import { ActualWorkHistoryCard } from "./ActualWorkHistoryCard";
import { ActualWorkComposer } from "./ActualWorkComposer";
import { TeamSection } from "./TeamSection";
import { FOCUS_RING } from "./helpers";

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
}

// Work Canvas — the Workbench's sole vertical scroll surface (locked spec §1.2, §1.5, §5, §7.1).
// Fixed module order: attention guidance -> original customer need -> Actual Work context ->
// communication/note composition -> activity/history -> lower-frequency record context. Proposed
// Scope is explicitly deferred from this pilot Workbench (locked spec §1.7/§3) and is not wired
// here.
export function RequestDetailContent(props: RequestDetailContentProps) {
  const { detail, requestId, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onRecordFollowUp, onCreateFollowUp, onReviewSuccess } = props;
  const layoutProps: RequestDetailLayoutProps = { requestId, detail, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onRecordFollowUp, onCreateFollowUp, onReviewSuccess };
  const actualWorkCapture = useActualWorkCapture(requestId);
  const actualWorkHistory = useActualWorkHistory(requestId);
  return (
    <div className="flex flex-1 min-h-0 flex-col">
      <RequestDetailAnchor
        {...layoutProps}
        canRecordShareIntent={props.canRecordShareIntent}
        needsShare={props.needsShare}
        onOpenShareDrawer={props.onOpenShareDrawer}
      />
      <div className="flex-1 min-h-0 overflow-y-auto px-4 md:px-6 py-5">
      <div className="max-w-6xl mx-auto w-full space-y-6">
        {/* 1. Active attention guidance */}
        <div id="focus-panel-attention" className="space-y-3">
          <AttentionGuidanceCard detail={detail} highlights={highlights} />
          <MarkHandledCard
            requestId={requestId}
            detail={detail}
            onDetailUpdated={onDetailUpdated}
            highlight={highlights.markHandled}
          />
          <TodayPromiseBanner detail={detail} onRecordFollowUp={onRecordFollowUp} />
        </div>

        {/* 2. Customer need — the original request only */}
        <OriginalRequestCard detail={detail} />

        {/* 3. Work execution — Actual Work, conditional (self-hides when not entitled/present) */}
        <div className="space-y-3">
          <ActualWorkCard
            state={actualWorkCapture.state}
            onStartCapture={() => void actualWorkCapture.startCapture()}
          />
          <ActualWorkHistoryCard state={actualWorkHistory.state} onRetry={() => void actualWorkHistory.retry()} />
        </div>

        {/* 4. Communication & planning — one cohesive surface: composer and Follow-Up/Planned-For
            share one enclosing card (locked correction, 2026-08-22), not two disconnected ones.
            Log Contact's one entry point lives in the Anchor now, not duplicated here. */}
        <div className="space-y-3">
          {showProminentFeedbackCard && <ProminentFeedbackCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />}
          {props.reviewSuccessMsg && <div role="status" aria-live="polite" className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm text-[var(--ophalo-success)] font-medium">{props.reviewSuccessMsg}</div>}
          <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]">
            <div id="focus-panel-update">
              <UnifiedComposer requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} customerUpdateDraft={props.customerUpdateDraft} onCustomerUpdateDraftChange={props.onCustomerUpdateDraftChange} customerUpdateDraftStatus={props.customerUpdateDraftStatus} onCustomerUpdateDraftStatusChange={props.onCustomerUpdateDraftStatusChange} highlight={highlights.sendUpdate} bare />
            </div>
            <TimingPanel
              requestId={requestId}
              detail={detail}
              onDetailUpdated={onDetailUpdated}
              onRecordFollowUp={onRecordFollowUp}
              bare
            />
          </div>
          <TriagePanel detail={detail} onDetailUpdated={onDetailUpdated} />
        </div>

        {/* 5. Activity — one chronological timeline */}
        <RequestDetailActivity timelineFilter={props.timelineFilter} onTimelineFilterChange={props.onTimelineFilterChange} displayedEvents={props.displayedEvents} />

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

        {/* 6. Lower-frequency record context — concise disclosure, not a stack of full-width
            cards (locked correction, 2026-08-22). Owner/contact are already in the Anchor, so
            they are not repeated here; TeamSection's assigned-owner row is omitted in this mode. */}
        <details className="group rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3">
          <summary
            className={`flex cursor-pointer list-none items-center justify-between text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)] ${FOCUS_RING} rounded`}
          >
            Record details
            <span className="text-[var(--ophalo-muted)] transition-transform group-open:rotate-180">⌄</span>
          </summary>
          <div className="mt-3 space-y-3">
            <RelatedWorkPanel requestId={requestId} onNavigate={props.onNavigate} />
            <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
            {!showProminentFeedbackCard && <FeedbackSummaryCard detail={detail} />}
            <SourceMetaPanel detail={detail} />
          </div>
        </details>
      </div>
      </div>
      {actualWorkCapture.isModalOpen && actualWorkCapture.state.status === "draft" && (
        <ActualWorkComposer
          draft={actualWorkCapture.state.draft}
          conflictNotice={actualWorkCapture.conflictNotice}
          onClose={actualWorkCapture.closeModal}
          onCommitted={() => actualWorkCapture.refetchDraft()}
          onConflict={(message) => void actualWorkCapture.reconcileAfterConflict(message)}
          onDismissNotice={actualWorkCapture.clearConflictNotice}
          onRetryReconciliation={() => void actualWorkCapture.retryReconciliation()}
          onSubmitted={() => {
            actualWorkCapture.markSubmitted();
            void actualWorkHistory.retry();
          }}
          onDiscarded={actualWorkCapture.onDraftDiscarded}
        />
      )}
    </div>
  );
}
