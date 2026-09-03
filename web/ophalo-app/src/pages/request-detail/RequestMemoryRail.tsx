import { useMemo, useRef, useState } from "react";
import type { KeepRequestEventItem } from "../../lib/apiClient";
import { FOCUS_RING } from "./helpers";
import { TimelineEvent } from "./TimelineEvent";

type RequestMemoryTab = "history" | "details";

const TAB_STORAGE_KEY = "ophalo.request-memory-tab";
const TABS: Array<{ id: RequestMemoryTab; label: string }> = [
  { id: "history", label: "Request history" },
  { id: "details", label: "Details" },
];

// These records have a dedicated, full-detail home in the Communications workspace.
// A status change that carries a business update remains here because the status transition
// itself is operational history; its message is not rendered in this compact rail.
const COMMUNICATION_EVENT_TYPES = new Set([
  "message_added",
  "internal_note_added",
  "external_contact_logged",
  "share_intent_recorded",
  "feedback_received",
  "notification_confirmed",
]);

function initialTab(): RequestMemoryTab {
  if (typeof window === "undefined") return "history";
  try {
    return window.sessionStorage?.getItem(TAB_STORAGE_KEY) === "details" ? "details" : "history";
  } catch {
    return "history";
  }
}

function sortEvents(events: KeepRequestEventItem[]): KeepRequestEventItem[] {
  return [...events].sort((a, b) => {
    const byDate = new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime();
    return byDate !== 0 ? byDate : b.id.localeCompare(a.id);
  });
}

interface RequestMemoryRailProps {
  events: KeepRequestEventItem[];
  details: React.ReactNode;
}

export function RequestMemoryRail({ events, details }: RequestMemoryRailProps) {
  const [activeTab, setActiveTab] = useState<RequestMemoryTab>(initialTab);
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const historyEvents = useMemo(
    () => sortEvents(events.filter((event) => !COMMUNICATION_EVENT_TYPES.has(event.eventType))),
    [events],
  );

  function selectTab(tab: RequestMemoryTab) {
    setActiveTab(tab);
    try {
      window.sessionStorage?.setItem(TAB_STORAGE_KEY, tab);
    } catch {
      // Session persistence is a convenience; privacy/storage restrictions must not block the rail.
    }
  }

  function moveTab(currentIndex: number, delta: number) {
    const nextIndex = (currentIndex + delta + TABS.length) % TABS.length;
    const next = TABS[nextIndex];
    if (!next) return;
    selectTab(next.id);
    tabRefs.current[nextIndex]?.focus();
  }

  return (
    <aside data-request-memory-rail className="min-w-0">
      <section className="overflow-hidden rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm">
        <div role="tablist" aria-label="Request memory" className="grid grid-cols-2 border-b border-[var(--ophalo-border)]">
          {TABS.map((tab, index) => (
            <button
              key={tab.id}
              ref={(node) => { tabRefs.current[index] = node; }}
              id={`request-memory-tab-${tab.id}`}
              type="button"
              role="tab"
              aria-selected={activeTab === tab.id}
              aria-controls={`request-memory-panel-${tab.id}`}
              tabIndex={activeTab === tab.id ? 0 : -1}
              onClick={() => selectTab(tab.id)}
              onKeyDown={(event) => {
                if (event.key === "ArrowRight") {
                  event.preventDefault();
                  moveTab(index, 1);
                } else if (event.key === "ArrowLeft") {
                  event.preventDefault();
                  moveTab(index, -1);
                } else if (event.key === "Home") {
                  event.preventDefault();
                  selectTab(TABS[0]!.id);
                  tabRefs.current[0]?.focus();
                } else if (event.key === "End") {
                  event.preventDefault();
                  selectTab(TABS[TABS.length - 1]!.id);
                  tabRefs.current[TABS.length - 1]?.focus();
                }
              }}
              className={`min-w-0 border-b-2 px-2 py-3 text-xs font-bold leading-4 transition-colors ${FOCUS_RING} ${
                activeTab === tab.id
                  ? "border-[var(--keep-accent)] text-[var(--keep-accent)]"
                  : "border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div
          id="request-memory-panel-history"
          role="tabpanel"
          aria-labelledby="request-memory-tab-history"
          hidden={activeTab !== "history"}
          className="space-y-4 p-4"
        >
          <div>
            <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Request history</p>
            <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
              Lifecycle and operational changes · {historyEvents.length} {historyEvents.length === 1 ? "event" : "events"}
            </p>
          </div>
          {historyEvents.length === 0 ? (
            <p className="rounded-lg bg-[var(--ophalo-canvas)] px-3 py-4 text-sm text-[var(--ophalo-muted)]">
              No request history yet.
            </p>
          ) : (
            <div className="space-y-3">
              {historyEvents.map((event) => (
                <TimelineEvent key={event.id} event={event} isFirst={false} compact />
              ))}
            </div>
          )}
        </div>

        <div
          id="request-memory-panel-details"
          role="tabpanel"
          aria-labelledby="request-memory-tab-details"
          hidden={activeTab !== "details"}
          className="space-y-4 p-4"
        >
          {details}
        </div>
      </section>
    </aside>
  );
}
