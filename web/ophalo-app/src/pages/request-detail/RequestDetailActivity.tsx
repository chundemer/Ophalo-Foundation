import { type KeepRequestEventItem } from "../../lib/apiClient";
import { FOCUS_RING } from "./helpers";
import { type TimelineFilter, TimelineEvent } from "./TimelineEvent";

interface RequestDetailActivityProps {
  timelineFilter: TimelineFilter;
  onTimelineFilterChange: (filter: TimelineFilter) => void;
  displayedEvents: KeepRequestEventItem[];
}

export function RequestDetailActivity({ timelineFilter, onTimelineFilterChange, displayedEvents }: RequestDetailActivityProps) {
  const filterBtnCls = (active: boolean) => `flex-1 px-3 py-1.5 text-xs font-semibold transition-colors ${FOCUS_RING} ${active ? "bg-[var(--ophalo-navy)] text-white" : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"}`;
  return <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5">
    <div className="flex items-center justify-between mb-4 gap-3">
      <p className="text-base font-semibold text-[var(--ophalo-ink)] shrink-0">Activity</p>
      <div className="flex rounded-lg border border-[var(--ophalo-border)] overflow-hidden shrink-0" role="group" aria-label="Activity filter">
        <button type="button" aria-pressed={timelineFilter === "communication"} onClick={() => onTimelineFilterChange("communication")} className={filterBtnCls(timelineFilter === "communication")}>Conversation &amp; notes</button>
        <button type="button" aria-pressed={timelineFilter === "all"} onClick={() => onTimelineFilterChange("all")} className={`border-l border-[var(--ophalo-border)] ${filterBtnCls(timelineFilter === "all")}`}>All activity</button>
      </div>
    </div>
    {displayedEvents.length === 0 ? <p className="text-sm text-[var(--ophalo-muted)]">{timelineFilter === "communication" ? "No customer updates or internal notes yet." : "No activity yet."}</p> : <div className="relative space-y-2 border-l border-[var(--ophalo-border)] pl-3 ml-4">{displayedEvents.map((event, idx) => <TimelineEvent key={event.id} event={event} isFirst={idx === 0} />)}</div>}
  </div>;
}
