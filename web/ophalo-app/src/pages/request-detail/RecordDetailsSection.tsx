import { type KeepRequestDetailResult } from "../../lib/apiClient";
import {
  RelatedWorkPanel,
  CustomerSignalPanel,
  FeedbackSummaryCard,
  SourceMetaPanel,
} from "./DetailPanels";
import { TeamSection } from "./TeamSection";
import { FOCUS_RING } from "./helpers";

// RD-019A: the lower-frequency "Record details" collapsible, extracted verbatim from
// `RequestDetailContent`. Layout-only; each panel self-hides (returns null) when it has nothing
// meaningful to show, so `divide-y` never leaves an empty gap.
interface RecordDetailsSectionProps {
  detail: KeepRequestDetailResult;
  requestId: string;
  showProminentFeedbackCard: boolean;
  onDetailUpdated: (detail: KeepRequestDetailResult) => void;
  onNavigate?: (id: string) => void;
}

export function RecordDetailsSection({
  detail,
  requestId,
  showProminentFeedbackCard,
  onDetailUpdated,
  onNavigate,
}: RecordDetailsSectionProps) {
  return (
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
        <RelatedWorkPanel requestId={requestId} onNavigate={onNavigate} bare />
        <TeamSection requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} bare />
        {!showProminentFeedbackCard && <FeedbackSummaryCard detail={detail} bare />}
        <SourceMetaPanel detail={detail} bare />
      </div>
    </details>
  );
}
