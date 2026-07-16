import { KeepButton } from "../../components/keep/KeepButton";
import { WorkDoneCard, CloseRequestCard } from "./BusinessSection";
import { TimingPanel } from "./TimingPanel";
import { TeamSection } from "./TeamSection";
import {
  type RequestDetailLayoutProps,
  LogContactCard,
  MarkHandledCard,
  WorkControlsGroup,
  FeedbackSummaryCard,
  CustomerPanel,
  ServiceLocationPanel,
  TriagePanel,
  SourceMetaPanel,
} from "./DetailPanels";

export function RequestDetailDesktopLayout({
  requestId,
  detail,
  highlights,
  showProminentFeedbackCard,
  onDetailUpdated,
  onContactLaunched,
  onEditLocation,
  onRecordFollowUp,
  onCreateFollowUp,
  onReviewSuccess,
}: RequestDetailLayoutProps) {
  return (
    <aside className="hidden md:flex md:flex-col border-l border-[var(--ophalo-border)] bg-[var(--ophalo-card)] overflow-y-auto px-4 py-5 gap-4">
      {/* Actions group */}
      <WorkDoneCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      <div id="focus-panel-closeout">
        <CloseRequestCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      </div>
      <div id="focus-panel-contact">
        <LogContactCard
          detail={detail}
          onContactLaunched={onContactLaunched}
          highlight={highlights.logContact}
        />
      </div>
      <div id="focus-panel-attention">
        <MarkHandledCard
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          highlight={highlights.markHandled}
        />
      </div>
      <TimingPanel
        requestId={requestId}
        detail={detail}
        onDetailUpdated={onDetailUpdated}
        onRecordFollowUp={onRecordFollowUp}
      />
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

      {/* Divider */}
      <hr className="border-[var(--ophalo-border)]" />

      {/* Context group */}
      <CustomerPanel detail={detail} onContactLaunched={onContactLaunched} />
      <ServiceLocationPanel detail={detail} onEditLocation={onEditLocation} />
      <TriagePanel detail={detail} onDetailUpdated={onDetailUpdated} />
      {!showProminentFeedbackCard && <FeedbackSummaryCard detail={detail} />}
      <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      <SourceMetaPanel detail={detail} />
    </aside>
  );
}
