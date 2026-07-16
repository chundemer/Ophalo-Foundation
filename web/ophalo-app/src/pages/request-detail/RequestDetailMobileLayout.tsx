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

// Primary actions inserted before the composer in the mobile stack.
export function RequestDetailMobileActions({
  requestId,
  detail,
  highlights,
  showProminentFeedbackCard,
  onDetailUpdated,
  onContactLaunched,
  onCreateFollowUp,
  onReviewSuccess,
}: RequestDetailLayoutProps) {
  return (
    <>
      <WorkDoneCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      <CloseRequestCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      <LogContactCard
        detail={detail}
        onContactLaunched={onContactLaunched}
        highlight={highlights.logContact}
      />
      <MarkHandledCard
        requestId={requestId}
        detail={detail}
        onDetailUpdated={onDetailUpdated}
        highlight={highlights.markHandled}
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
      <FeedbackSummaryCard detail={detail} />
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
    </>
  );
}

// Context panels inserted after the timeline in the mobile stack.
export function RequestDetailMobileContext({
  requestId,
  detail,
  onDetailUpdated,
  onContactLaunched,
  onEditLocation,
  onRecordFollowUp,
}: RequestDetailLayoutProps) {
  return (
    <>
      <CustomerPanel detail={detail} onContactLaunched={onContactLaunched} />
      <ServiceLocationPanel detail={detail} onEditLocation={onEditLocation} />
      <TriagePanel detail={detail} onDetailUpdated={onDetailUpdated} />
      <TimingPanel
        requestId={requestId}
        detail={detail}
        onDetailUpdated={onDetailUpdated}
        onRecordFollowUp={onRecordFollowUp}
      />
      <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
      <SourceMetaPanel detail={detail} />
    </>
  );
}
