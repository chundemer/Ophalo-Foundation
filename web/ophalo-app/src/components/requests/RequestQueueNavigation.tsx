import { useEffect, useId, useRef, useState } from "react";
import { ChevronLeft, ChevronDown } from "lucide-react";
import type { KeepRequestViewCounts } from "../../lib/apiClient";
import {
  countForTab,
  HISTORY_SCOPES,
  HISTORY_DATE_SCOPES,
  type TabDef,
  type HistoryScope,
  type HistoryDateScope,
} from "../../pages/requestsWorkspace";

// UI-004 amendment (2026-08-21): the aggregate/loading/error contract for the Office Review
// strip. "error" is distinct from "loading" — a failed count query must never sit behind a
// perpetual placeholder, and must never fall back to a guessed zero.
export type OfficeReviewState =
  | { status: "loading" }
  | { status: "error"; retry: () => void }
  | {
      status: "ready";
      aggregate: number;
      members: { readyToClose: number; feedbackReview: number; actualWorkReview: number };
    };

type DisclosureId = "office_review" | "views";

interface RequestQueueNavigationProps {
  tabs: TabDef[];
  activeTab: TabDef;
  viewCounts: KeepRequestViewCounts | null;
  secondaryViews: TabDef[];
  officeReviewMembers: TabDef[];
  officeReview: OfficeReviewState;
  onSelectTab: (tab: TabDef) => void;
  historyMode: boolean;
  historyScope: HistoryScope;
  historyDateScope: HistoryDateScope;
  isOwnerOrAdmin: boolean;
  onEnterHistory: () => void;
  onExitHistory: () => void;
  onUpdateHistoryScope: (scope: HistoryScope) => void;
  onUpdateHistoryDateScope: (scope: HistoryDateScope) => void;
}

// UI-004 amendment: a plain disclosure/group — not an ARIA menu, since it doesn't implement
// full menu keyboard traversal (arrow keys, typeahead). Escape and an outside pointerdown both
// dismiss and return focus to the trigger; selecting an item does the same after navigating.
// Only one of Office Review / Views may be open at a time — callers pass shared open/onOpen
// state so opening one closes the other.
function DisclosureButton({
  id,
  isOpen,
  onOpenChange,
  label,
  ariaLabel,
  children,
  align = "left",
}: {
  id: DisclosureId;
  isOpen: boolean;
  onOpenChange: (id: DisclosureId | null) => void;
  label: React.ReactNode;
  ariaLabel: string;
  // Render prop: `dismiss` closes this disclosure AND returns focus to its own trigger — use
  // it for Escape, outside-pointerdown, and item selection. A close caused by the *other*
  // disclosure opening (mutual exclusion) does NOT call dismiss and must NOT steal focus back
  // from whatever the user just clicked.
  children: (dismiss: () => void) => React.ReactNode;
  align?: "left" | "right";
}) {
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const popoverId = useId();

  function dismiss() {
    onOpenChange(null);
    triggerRef.current?.focus();
  }

  useEffect(() => {
    if (!isOpen) return;
    function handlePointerDown(e: PointerEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        dismiss();
      }
    }
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        dismiss();
      }
    }
    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  return (
    <div ref={containerRef} className="relative">
      <button
        ref={triggerRef}
        type="button"
        aria-expanded={isOpen}
        aria-controls={isOpen ? popoverId : undefined}
        onClick={() => onOpenChange(isOpen ? null : id)}
        className="flex cursor-pointer items-center gap-1 text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded px-1 py-1"
      >
        {label}
        <ChevronDown className="h-3 w-3" />
      </button>
      {isOpen && (
        <div
          id={popoverId}
          role="group"
          aria-label={ariaLabel}
          className={`absolute ${align === "right" ? "right-0" : "left-0"} z-20 mt-1 min-w-[12rem] rounded-md border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] py-1 shadow-lg`}
        >
          {children(dismiss)}
        </div>
      )}
    </div>
  );
}

function ViewsControl({
  secondaryViews,
  activeTab,
  viewCounts,
  onSelectTab,
  isOpen,
  onOpenChange,
}: {
  secondaryViews: TabDef[];
  activeTab: TabDef;
  viewCounts: KeepRequestViewCounts | null;
  onSelectTab: (tab: TabDef) => void;
  isOpen: boolean;
  onOpenChange: (id: DisclosureId | null) => void;
}) {
  if (secondaryViews.length === 0) return null;
  const activeSecondaryTab = secondaryViews.find((t) => t.id === activeTab.id) ?? null;

  return (
    <DisclosureButton
      id="views"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      ariaLabel="Views"
      label={activeSecondaryTab ? `Views: ${activeSecondaryTab.label}` : "Views"}
    >
      {(dismiss) =>
        secondaryViews.map((tab) => {
          const count = countForTab(tab, viewCounts);
          const isActive = tab.view === activeTab.view;
          return (
            <button
              key={tab.id}
              type="button"
              aria-current={isActive}
              onClick={() => { onSelectTab(tab); dismiss(); }}
              className={`flex w-full items-center justify-between gap-2 px-3 py-1.5 text-sm hover:bg-[var(--ophalo-canvas)] ${
                isActive ? "font-semibold text-[var(--ophalo-navy)]" : "text-[var(--ophalo-ink)]"
              }`}
            >
              <span>{tab.label}</span>
              {count != null && count > 0 && (
                <span className="rounded-full bg-[var(--keep-accent-bg)] px-1.5 py-0.5 text-xs font-semibold text-[var(--keep-accent)]">
                  {count}
                </span>
              )}
            </button>
          );
        })
      }
    </DisclosureButton>
  );
}

// UI-004 amendment: navy/neutral, never amber — Needs Attention keeps sole ownership of the
// customer-promise-risk color. Collapsed default reads "Office Review · N pending"; once a
// member is the active tab it names that destination instead, the same convention Views uses
// for Watching. Opened, actionable (non-zero) members lead; zero-count members collapse into
// one quiet line rather than standing as equal-weight zero-badge rows.
function OfficeReviewControl({
  officeReviewMembers,
  activeTab,
  officeReview,
  onSelectTab,
  isOpen,
  onOpenChange,
}: {
  officeReviewMembers: TabDef[];
  activeTab: TabDef;
  officeReview: OfficeReviewState;
  onSelectTab: (tab: TabDef) => void;
  isOpen: boolean;
  onOpenChange: (id: DisclosureId | null) => void;
}) {
  if (officeReviewMembers.length === 0) return null;

  const memberCount = (tab: TabDef): number => {
    if (officeReview.status !== "ready") return 0;
    switch (tab.id) {
      case "ready_to_close": return officeReview.members.readyToClose;
      case "feedback_review": return officeReview.members.feedbackReview;
      case "actual_work_review": return officeReview.members.actualWorkReview;
      default: return 0;
    }
  };

  if (officeReview.status === "loading") {
    return (
      <div
        aria-hidden="true"
        className="inline-flex h-7 w-44 animate-pulse motion-reduce:animate-none items-center rounded-full bg-[var(--ophalo-canvas)]"
      />
    );
  }

  if (officeReview.status === "error") {
    return (
      <button
        type="button"
        onClick={officeReview.retry}
        className="inline-flex items-center gap-1.5 rounded-full border border-[var(--ophalo-border)] px-3 py-1 text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
      >
        Office Review — couldn’t load counts · Retry
      </button>
    );
  }

  if (officeReview.aggregate <= 0) return null;

  const activeMember = officeReviewMembers.find((t) => t.id === activeTab.id) ?? null;
  const [actionable, empty] = officeReviewMembers.reduce<[TabDef[], TabDef[]]>(
    (acc, tab) => {
      acc[memberCount(tab) > 0 ? 0 : 1].push(tab);
      return acc;
    },
    [[], []],
  );

  return (
    <DisclosureButton
      id="office_review"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      ariaLabel="Office Review"
      label={
        <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--ophalo-navy)]/10 px-2.5 py-1 text-[var(--ophalo-navy)]">
          {activeMember ? `Office Review: ${activeMember.label}` : `Office Review · ${officeReview.aggregate} pending`}
        </span>
      }
    >
      {(dismiss) => (
        <>
          {actionable.map((tab) => {
            const count = memberCount(tab);
            const isActive = tab.view === activeTab.view;
            return (
              <button
                key={tab.id}
                type="button"
                aria-current={isActive}
                onClick={() => { onSelectTab(tab); dismiss(); }}
                className={`flex w-full items-center justify-between gap-2 px-3 py-1.5 text-sm hover:bg-[var(--ophalo-canvas)] ${
                  isActive ? "font-semibold text-[var(--ophalo-navy)]" : "text-[var(--ophalo-ink)]"
                }`}
              >
                <span>{tab.label}</span>
                <span className="rounded-full bg-[var(--keep-accent-bg)] px-1.5 py-0.5 text-xs font-semibold text-[var(--keep-accent)]">
                  {count}
                </span>
              </button>
            );
          })}
          {empty.length > 0 && (
            <div className="px-3 py-1.5 text-xs text-[var(--ophalo-muted)]">
              No {empty.map((t) => t.label).join(", ")}
            </div>
          )}
        </>
      )}
    </DisclosureButton>
  );
}

export function RequestQueueNavigation({
  tabs,
  activeTab,
  viewCounts,
  secondaryViews,
  officeReviewMembers,
  officeReview,
  onSelectTab,
  historyMode,
  historyScope,
  historyDateScope,
  isOwnerOrAdmin,
  onEnterHistory,
  onExitHistory,
  onUpdateHistoryScope,
  onUpdateHistoryDateScope,
}: RequestQueueNavigationProps) {
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  // UI-004 amendment: only one of Office Review / Views may be open at a time.
  const [openDisclosure, setOpenDisclosure] = useState<DisclosureId | null>(null);

  // GAP-041: roving-tabindex keyboard pattern — Left/Right/Home/End move focus and
  // selection together; Enter/Space activation is native <button> behavior, unchanged.
  function handleTabKeyDown(e: React.KeyboardEvent<HTMLButtonElement>, index: number) {
    let nextIndex: number | null = null;
    switch (e.key) {
      case "ArrowRight":
        nextIndex = (index + 1) % tabs.length;
        break;
      case "ArrowLeft":
        nextIndex = (index - 1 + tabs.length) % tabs.length;
        break;
      case "Home":
        nextIndex = 0;
        break;
      case "End":
        nextIndex = tabs.length - 1;
        break;
      default:
        return;
    }
    e.preventDefault();
    onSelectTab(tabs[nextIndex]);
    tabRefs.current[nextIndex]?.focus();
  }

  return (
    <div className="border-t border-[var(--ophalo-border)]">
      {!historyMode ? (
        <>
          {/* Row 1: primary tabs (own scroll region) + Views/History, sharing the row when
              space permits and wrapping together onto their own line when it doesn't. */}
          <div className="flex items-center justify-between gap-2 flex-wrap px-4 sm:px-6">
            <div role="tablist" aria-label="Request queues" className="flex gap-0 overflow-x-auto shrink min-w-0">
              {tabs.map((tab, i) => {
                const count = countForTab(tab, viewCounts);
                const isActive = tab.view === activeTab.view;
                return (
                  <button
                    key={`${tab.id}-${tab.label}`}
                    ref={(el) => { tabRefs.current[i] = el; }}
                    role="tab"
                    aria-selected={isActive}
                    tabIndex={isActive ? 0 : -1}
                    type="button"
                    onClick={() => onSelectTab(tab)}
                    onKeyDown={(e) => handleTabKeyDown(e, i)}
                    className={`flex items-center gap-1.5 px-3 py-4 text-sm border-b-2 whitespace-nowrap transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-inset ${
                      isActive
                        ? "font-semibold border-[var(--ophalo-navy)] text-[var(--ophalo-navy)]"
                        : "font-medium border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] hover:border-[var(--ophalo-border)]"
                    }`}
                  >
                    {tab.label}
                    {count != null && count > 0 && (
                      <span className={`rounded-full px-1.5 py-0.5 text-xs font-semibold ${
                        isActive
                          ? "bg-[var(--ophalo-navy)] text-white"
                          : "bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                      }`}>
                        {count}
                      </span>
                    )}
                  </button>
                );
              })}
            </div>
            <div className="flex items-center gap-3 shrink-0">
              <ViewsControl
                secondaryViews={secondaryViews}
                activeTab={activeTab}
                viewCounts={viewCounts}
                onSelectTab={onSelectTab}
                isOpen={openDisclosure === "views"}
                onOpenChange={setOpenDisclosure}
              />
              {/* GAP-044: demoted, non-competing entry point — not styled as a peer tab. */}
              {isOwnerOrAdmin && (
                <button
                  type="button"
                  onClick={onEnterHistory}
                  className="shrink-0 text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
                >
                  History
                </button>
              )}
            </div>
          </div>

          {/* Row 2: Office Review — directly below the primary tabs, above search/filter.
              Owner/Admin only; contributes no row when there's nothing to review. */}
          {isOwnerOrAdmin && (
            <div className="px-4 pb-2 sm:px-6">
              <OfficeReviewControl
                officeReviewMembers={officeReviewMembers}
                activeTab={activeTab}
                officeReview={officeReview}
                onSelectTab={onSelectTab}
                isOpen={openDisclosure === "office_review"}
                onOpenChange={setOpenDisclosure}
              />
            </div>
          )}
        </>
      ) : (
        <div className="flex flex-wrap items-center gap-3 px-4 py-3 sm:px-6">
          <button
            type="button"
            onClick={onExitHistory}
            className="flex items-center gap-1 text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
          >
            <ChevronLeft className="h-4 w-4" />
            Back to queues
          </button>
          <div role="group" aria-label="History scope" className="flex items-center gap-1">
            {HISTORY_SCOPES.map((s) => (
              <button
                key={s.id}
                type="button"
                aria-pressed={historyScope === s.id}
                onClick={() => onUpdateHistoryScope(s.id)}
                className={`px-2.5 py-1 text-xs font-semibold rounded-full border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] ${
                  historyScope === s.id
                    ? "border-[var(--ophalo-navy)] bg-[var(--ophalo-navy)] text-white"
                    : "border-[var(--ophalo-border)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                }`}
              >
                {s.label}
              </button>
            ))}
          </div>
          <div role="group" aria-label="Date range" className="flex items-center gap-1">
            {HISTORY_DATE_SCOPES.map((s) => (
              <button
                key={s.id}
                type="button"
                aria-pressed={historyDateScope === s.id}
                onClick={() => onUpdateHistoryDateScope(s.id)}
                className={`px-2.5 py-1 text-xs font-medium rounded-full border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] ${
                  historyDateScope === s.id
                    ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]"
                    : "border-[var(--ophalo-border)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                }`}
              >
                {s.label}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
