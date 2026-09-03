import { useMemo, useRef, useState } from "react";
import { FileText, MessageSquare, PhoneCall } from "lucide-react";
import type { KeepRequestEventItem } from "../../lib/apiClient";
import { FOCUS_RING } from "./helpers";
import { TimelineEvent } from "./TimelineEvent";

type RequestMemoryTab = "communications" | "history" | "details";
type CommunicationFilter = "all" | "customer" | "internal";

const TAB_STORAGE_KEY = "ophalo.request-memory-tab";
const TABS: Array<{ id: RequestMemoryTab; label: string }> = [
  { id: "communications", label: "Communications" },
  { id: "history", label: "Request history" },
  { id: "details", label: "Details" },
];
const REQUEST_MEMORY_COMMUNICATION_TYPES = new Set([
  "message_added",
  "internal_note_added",
  "external_contact_logged",
  "share_intent_recorded",
  "feedback_received",
]);

function initialTab(): RequestMemoryTab {
  if (typeof window === "undefined") return "communications";
  try {
    const stored = window.sessionStorage?.getItem(TAB_STORAGE_KEY);
    return TABS.some((tab) => tab.id === stored) ? (stored as RequestMemoryTab) : "communications";
  } catch {
    return "communications";
  }
}

function sortEvents(events: KeepRequestEventItem[]): KeepRequestEventItem[] {
  return [...events].sort((a, b) => {
    const byDate = new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime();
    return byDate !== 0 ? byDate : b.id.localeCompare(a.id);
  });
}

function isInternalCommunication(event: KeepRequestEventItem): boolean {
  return event.eventType === "internal_note_added" || event.visibility === "internal";
}

interface RequestMemoryRailProps {
  events: KeepRequestEventItem[];
  details: React.ReactNode;
  canLogExternalContact: boolean;
  canAddInternalNote: boolean;
  onContactCustomer: () => void;
  onAddInternalNote: () => void;
}

export function RequestMemoryRail({
  events,
  details,
  canLogExternalContact,
  canAddInternalNote,
  onContactCustomer,
  onAddInternalNote,
}: RequestMemoryRailProps) {
  const [activeTab, setActiveTab] = useState<RequestMemoryTab>(initialTab);
  const [communicationFilter, setCommunicationFilter] = useState<CommunicationFilter>("all");
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);

  const historyEvents = useMemo(() => sortEvents(events), [events]);
  const communicationEvents = useMemo(() => {
    const communication = historyEvents.filter((event) => REQUEST_MEMORY_COMMUNICATION_TYPES.has(event.eventType));
    if (communicationFilter === "internal") return communication.filter(isInternalCommunication);
    if (communicationFilter === "customer") return communication.filter((event) => !isInternalCommunication(event));
    return communication;
  }, [historyEvents, communicationFilter]);

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

  const eventList = (items: KeepRequestEventItem[], empty: string) => (
    items.length === 0 ? (
      <p className="rounded-lg bg-[var(--ophalo-canvas)] px-3 py-4 text-sm text-[var(--ophalo-muted)]">
        {empty}
      </p>
    ) : (
      <div className="space-y-3">
        {items.map((event) => (
          <TimelineEvent key={event.id} event={event} isFirst={false} compact />
        ))}
      </div>
    )
  );

  return (
    <aside data-request-memory-rail className="min-w-0">
      <section className="overflow-hidden rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm">
        <div role="tablist" aria-label="Request memory" className="grid grid-cols-3 border-b border-[var(--ophalo-border)]">
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
              className={`min-w-0 border-b-2 px-2 py-3 text-[11px] font-bold leading-4 transition-colors ${FOCUS_RING} ${
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
          id="request-memory-panel-communications"
          role="tabpanel"
          aria-labelledby="request-memory-tab-communications"
          hidden={activeTab !== "communications"}
          className="space-y-4 p-4"
        >
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
                <MessageSquare className="h-4 w-4 text-[var(--keep-accent)]" aria-hidden="true" />
                Communication history
              </p>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Customer contact and internal notes</p>
            </div>
          </div>

          <div className="flex rounded-lg border border-[var(--ophalo-border)]" role="group" aria-label="Communication filter">
            {(["all", "customer", "internal"] as const).map((filter, index) => (
              <button
                key={filter}
                type="button"
                aria-pressed={communicationFilter === filter}
                onClick={() => setCommunicationFilter(filter)}
                className={`flex-1 px-2 py-1.5 text-xs font-semibold capitalize ${index > 0 ? "border-l border-[var(--ophalo-border)]" : ""} ${FOCUS_RING} ${
                  communicationFilter === filter
                    ? "bg-[var(--ophalo-navy)] text-white"
                    : "text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                }`}
              >
                {filter}
              </button>
            ))}
          </div>

          {eventList(communicationEvents, communicationFilter === "internal" ? "No internal notes yet." : communicationFilter === "customer" ? "No customer communication yet." : "No communication or internal notes yet.")}

          {(canLogExternalContact || canAddInternalNote) && (
            <div className="grid gap-2 border-t border-[var(--ophalo-border)] pt-3">
              {canLogExternalContact && (
                <button type="button" onClick={onContactCustomer} className={`inline-flex items-center justify-center gap-1.5 rounded-lg border border-[var(--ophalo-border)] px-3 py-2 text-sm font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}>
                  <PhoneCall className="h-4 w-4 text-[var(--keep-accent)]" aria-hidden="true" />
                  Contact customer
                </button>
              )}
              {canAddInternalNote && (
                <button type="button" onClick={onAddInternalNote} className={`inline-flex items-center justify-center gap-1.5 rounded-lg px-3 py-2 text-sm font-semibold text-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)] ${FOCUS_RING}`}>
                  <FileText className="h-4 w-4" aria-hidden="true" />
                  Add internal note
                </button>
              )}
            </div>
          )}
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
            <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">{historyEvents.length} {historyEvents.length === 1 ? "event" : "events"}</p>
          </div>
          {eventList(historyEvents, "No request history yet.")}
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
