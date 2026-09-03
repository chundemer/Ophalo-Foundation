import { useState } from "react";
import { type KeepRequestEventItem } from "../../lib/apiClient";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import { FOCUS_RING } from "./helpers";
import { type TimelineFilter, TimelineEvent } from "./TimelineEvent";

interface RequestDetailActivityProps {
  timelineFilter: TimelineFilter;
  onTimelineFilterChange: (filter: TimelineFilter) => void;
  displayedEvents: KeepRequestEventItem[];
}

export function RequestDetailActivity({ timelineFilter, onTimelineFilterChange, displayedEvents }: RequestDetailActivityProps) {
  const [fullHistoryOpen, setFullHistoryOpen] = useState(false);
  const filterBtnCls = (active: boolean) => `flex-1 px-3 py-1.5 text-xs font-semibold transition-colors ${FOCUS_RING} ${active ? "bg-[var(--ophalo-navy)] text-white" : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"}`;
  const entryCount = displayedEvents.length;
  const previewEvents = displayedEvents.slice(0, 3);

  return (
    <>
      <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4 shadow-sm">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-[11px] font-bold uppercase tracking-wide text-[var(--keep-request-eyebrow)]">Activity</p>
            <p className="mt-1 text-sm font-semibold text-[var(--ophalo-ink)]">
              {entryCount} {entryCount === 1 ? "entry" : "entries"}
            </p>
          </div>
          {entryCount > 0 && (
            <button type="button" onClick={() => setFullHistoryOpen(true)} className={`shrink-0 text-xs font-semibold text-[var(--keep-accent)] hover:underline ${FOCUS_RING} rounded`}>
              View all →
            </button>
          )}
        </div>
        {entryCount === 0 ? (
          <p className="mt-3 text-sm text-[var(--ophalo-muted)]">{timelineFilter === "communication" ? "No customer updates or internal notes yet." : "No activity yet."}</p>
        ) : (
          <div className="mt-4 space-y-3">
            {previewEvents.map((event) => <TimelineEvent key={event.id} event={event} isFirst={false} compact />)}
            {entryCount > previewEvents.length && (
              <button type="button" onClick={() => setFullHistoryOpen(true)} className={`w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}>
                View all {entryCount} activity entries
              </button>
            )}
          </div>
        )}
      </section>

      {fullHistoryOpen && (
        <ResponsiveSheet
          label="Activity history"
          onClose={() => setFullHistoryOpen(false)}
          header={<div className="flex items-center justify-between gap-3"><div><p className="text-base font-semibold text-[var(--ophalo-ink)]">Activity history</p><p className="text-xs text-[var(--ophalo-muted)]">{entryCount} {entryCount === 1 ? "entry" : "entries"}</p></div><button type="button" onClick={() => setFullHistoryOpen(false)} className={`rounded-lg px-2 py-1 text-sm font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}>Close</button></div>}
        >
          <div className="flex items-center justify-end mb-4">
            <div className="flex rounded-lg border border-[var(--ophalo-border)] overflow-hidden shrink-0" role="group" aria-label="Activity filter">
              <button type="button" aria-pressed={timelineFilter === "communication"} onClick={() => onTimelineFilterChange("communication")} className={filterBtnCls(timelineFilter === "communication")}>Conversation &amp; notes</button>
              <button type="button" aria-pressed={timelineFilter === "all"} onClick={() => onTimelineFilterChange("all")} className={`border-l border-[var(--ophalo-border)] ${filterBtnCls(timelineFilter === "all")}`}>All activity</button>
            </div>
          </div>
          {displayedEvents.length === 0 ? <p className="text-sm text-[var(--ophalo-muted)]">{timelineFilter === "communication" ? "No customer updates or internal notes yet." : "No activity yet."}</p> : <div className="relative space-y-2 border-l border-[var(--ophalo-border)] pl-3 ml-4">{displayedEvents.map((event, idx) => <TimelineEvent key={event.id} event={event} isFirst={idx === 0} />)}</div>}
        </ResponsiveSheet>
      )}
    </>
  );
}
