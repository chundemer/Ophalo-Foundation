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
  const entryCount = displayedEvents.length;
  // Collapsed at rest (slice 5, 2026-08-23): native <details>/<summary> gives keyboard-accessible
  // disclosure semantics for free, matching the Record details pattern below it. The filter
  // controls and timeline only render once expanded; this is the canvas's sole timeline render —
  // no duplicate data, no second scroll owner.
  return (
    <details className="group rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
      <summary className={`flex cursor-pointer list-none items-center justify-between gap-3 ${FOCUS_RING} rounded`}>
        <span className="flex items-baseline gap-2">
          <span className="text-base font-semibold text-[var(--ophalo-ink)]">Activity</span>
          <span className="text-xs text-[var(--ophalo-muted)]">
            {entryCount} {entryCount === 1 ? "entry" : "entries"}
          </span>
        </span>
        <span className="text-[var(--ophalo-muted)] transition-transform group-open:rotate-180">⌄</span>
      </summary>
      <div className="mt-4">
        <div className="flex items-center justify-end mb-4">
          <div className="flex rounded-lg border border-[var(--ophalo-border)] overflow-hidden shrink-0" role="group" aria-label="Activity filter">
            <button type="button" aria-pressed={timelineFilter === "communication"} onClick={() => onTimelineFilterChange("communication")} className={filterBtnCls(timelineFilter === "communication")}>Conversation &amp; notes</button>
            <button type="button" aria-pressed={timelineFilter === "all"} onClick={() => onTimelineFilterChange("all")} className={`border-l border-[var(--ophalo-border)] ${filterBtnCls(timelineFilter === "all")}`}>All activity</button>
          </div>
        </div>
        {displayedEvents.length === 0 ? <p className="text-sm text-[var(--ophalo-muted)]">{timelineFilter === "communication" ? "No customer updates or internal notes yet." : "No activity yet."}</p> : <div className="relative space-y-2 border-l border-[var(--ophalo-border)] pl-3 ml-4">{displayedEvents.map((event, idx) => <TimelineEvent key={event.id} event={event} isFirst={idx === 0} />)}</div>}
      </div>
    </details>
  );
}
