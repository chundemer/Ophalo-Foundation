import { type KeepRequestDetailResult } from "../../lib/apiClient";
import { type TimelineFilter } from "./TimelineEvent";
import { type RequestDetailLayoutProps, ProminentFeedbackCard, AttentionGuidanceCard } from "./DetailPanels";
import { TodayPromiseBanner, DetailHero } from "./DetailHero";
import { CustomerContactStrip } from "./CustomerContactStrip";
import { OriginalRequestCard, RelatedWorkPanel } from "./DetailPanels";
import { RequestDetailMobileActions, RequestDetailMobileContext } from "./RequestDetailMobileLayout";
import { UnifiedComposer } from "./UnifiedComposer";
import { RequestDetailDesktopLayout } from "./RequestDetailDesktopLayout";
import { RequestDetailActivity } from "./RequestDetailActivity";

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

export function RequestDetailContent(props: RequestDetailContentProps) {
  const { detail, requestId, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onRecordFollowUp, onCreateFollowUp, onReviewSuccess } = props;
  const layoutProps: RequestDetailLayoutProps = { requestId, detail, highlights, showProminentFeedbackCard, onDetailUpdated, onContactLaunched, onEditLocation, onRecordFollowUp, onCreateFollowUp, onReviewSuccess };
  return <div className="flex flex-1 min-h-0 overflow-hidden md:grid md:[grid-template-columns:minmax(0,7fr)_minmax(320px,3fr)]">
    <div className="flex-1 md:flex-none overflow-y-auto px-4 md:px-6 py-5 space-y-4">
      <TodayPromiseBanner detail={detail} onRecordFollowUp={onRecordFollowUp} />
      <DetailHero detail={detail} canRecordShareIntent={props.canRecordShareIntent} needsShare={props.needsShare} onOpenShareDrawer={props.onOpenShareDrawer} />
      <CustomerContactStrip requestId={requestId} phone={detail.customerPhone ?? null} email={detail.customerEmail ?? null} customerName={detail.customerName} pageToken={detail.pageToken ?? null} onContactLaunched={onContactLaunched} />
      <OriginalRequestCard detail={detail} />
      <RelatedWorkPanel requestId={requestId} onNavigate={props.onNavigate} />
      <AttentionGuidanceCard detail={detail} highlights={highlights} />
      <div className="md:hidden space-y-4"><RequestDetailMobileActions {...layoutProps} /></div>
      <div id="focus-panel-update"><UnifiedComposer requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} customerUpdateDraft={props.customerUpdateDraft} onCustomerUpdateDraftChange={props.onCustomerUpdateDraftChange} customerUpdateDraftStatus={props.customerUpdateDraftStatus} onCustomerUpdateDraftStatusChange={props.onCustomerUpdateDraftStatusChange} highlight={highlights.sendUpdate} /></div>
      {showProminentFeedbackCard && <ProminentFeedbackCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />}
      {props.reviewSuccessMsg && <div role="status" aria-live="polite" className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm text-[var(--ophalo-success)] font-medium">{props.reviewSuccessMsg}</div>}
      <RequestDetailActivity timelineFilter={props.timelineFilter} onTimelineFilterChange={props.onTimelineFilterChange} displayedEvents={props.displayedEvents} />
      <div className="md:hidden space-y-4 pb-6"><RequestDetailMobileContext {...layoutProps} /></div>
    </div>
    <RequestDetailDesktopLayout {...layoutProps} />
  </div>;
}
