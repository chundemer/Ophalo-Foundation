import type { RefObject } from "react";
import { useEffect, useId, useRef, useState } from "react";
import { Check, ChevronDown, RefreshCw, Search, X } from "lucide-react";
import type { KeepRequestViewCounts } from "../../lib/apiClient";
import { STATUS_OPTIONS, countForTab, type TabDef } from "../../pages/requestsWorkspace";

// UI-004 amendment (2026-08-21): the aggregate/loading/error contract for the Office Review
// destinations inside Views. "error" is distinct from "loading" — a failed count query must
// never sit behind a perpetual placeholder, and must never fall back to a guessed zero.
export type OfficeReviewState =
  | { status: "loading" }
  | { status: "error"; retry: () => void }
  | {
      status: "ready";
      aggregate: number;
      members: { readyToClose: number; feedbackReview: number; actualWorkReview: number };
    };

interface RequestListToolbarProps {
  isAvailableTab: boolean;
  historyMode: boolean;
  presentAsHistory: boolean;
  searchInputRef: RefObject<HTMLInputElement | null>;
  draftQ: string;
  onDraftQChange: (value: string) => void;
  onSubmitSearch: (e: React.FormEvent) => void;
  onClearSearch: () => void;
  statusFilter: string;
  onStatusFilterChange: (value: string) => void;
  showStalenessNotice: boolean;
  onManualRefresh: () => void;
  appliedLineText: string | null;
  // UI-001 post-Step-4 density refinement (build-log 134 §3, locked 2026-08-21): in the Queue
  // pane, Search and Views stay on one row. Undefined/false preserves today's full-page/narrow
  // toolbar layout.
  paneMode?: boolean;
  // Request Queue header consolidation (locked 2026-08-24): Row 2's single custom Views control
  // replaces the native status <select> and absorbs the former standalone Office Review/Views/
  // History controls — saved views, Office Review destinations, status filtering, and History Log
  // entry all live in one popover. See session-log "Request Queue header consolidation".
  activeTab: TabDef;
  viewCounts: KeepRequestViewCounts | null;
  onSelectTab: (tab: TabDef) => void;
  secondaryViews: TabDef[];
  officeReviewMembers: TabDef[];
  officeReview: OfficeReviewState;
  isOwnerOrAdmin: boolean;
  onEnterHistory: () => void;
}

function officeReviewMemberCount(tab: TabDef, officeReview: OfficeReviewState): number {
  if (officeReview.status !== "ready") return 0;
  switch (tab.id) {
    case "ready_to_close": return officeReview.members.readyToClose;
    case "feedback_review": return officeReview.members.feedbackReview;
    case "actual_work_review": return officeReview.members.actualWorkReview;
    default: return 0;
  }
}

function ViewsPopover({
  activeTab,
  viewCounts,
  onSelectTab,
  secondaryViews,
  officeReviewMembers,
  officeReview,
  isOwnerOrAdmin,
  onEnterHistory,
  statusFilter,
  onStatusFilterChange,
}: {
  activeTab: TabDef;
  viewCounts: KeepRequestViewCounts | null;
  onSelectTab: (tab: TabDef) => void;
  secondaryViews: TabDef[];
  officeReviewMembers: TabDef[];
  officeReview: OfficeReviewState;
  isOwnerOrAdmin: boolean;
  onEnterHistory: () => void;
  statusFilter: string;
  onStatusFilterChange: (value: string) => void;
}) {
  const [isOpen, setIsOpen] = useState(false);
  const [draftStatus, setDraftStatus] = useState(statusFilter);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const popoverId = useId();
  const hasActiveFilter = statusFilter !== "";
  const showOfficeReview = isOwnerOrAdmin && officeReviewMembers.length > 0;

  function open() {
    setDraftStatus(statusFilter);
    setIsOpen(true);
  }

  function dismiss() {
    setIsOpen(false);
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
      if (e.key === "Escape") dismiss();
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
    <div ref={containerRef} className="relative shrink-0">
      <div className="flex items-center gap-0.5">
        <button
          ref={triggerRef}
          type="button"
          aria-expanded={isOpen}
          aria-controls={isOpen ? popoverId : undefined}
          onClick={() => (isOpen ? dismiss() : open())}
          className={`flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 ${
            hasActiveFilter
              ? "border-[var(--keep-accent)] text-[var(--keep-accent)] font-semibold"
              : "border-[var(--ophalo-border)] text-[var(--ophalo-ink)]"
          }`}
        >
          {hasActiveFilter ? "Views · 1" : "Views"}
          <ChevronDown className="h-3.5 w-3.5" />
        </button>
        {/* One-action reset: clears the applied status filter without opening the popover. */}
        {hasActiveFilter && (
          <button
            type="button"
            onClick={() => onStatusFilterChange("")}
            aria-label="Clear status filter"
            className="p-1.5 rounded text-[var(--keep-accent)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {isOpen && (
        <div
          id={popoverId}
          role="group"
          aria-label="Views"
          className="absolute right-0 z-20 mt-1 w-64 rounded-md border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] py-1 shadow-lg"
        >
          <div className="px-3 py-1 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
            Saved views
          </div>
          {secondaryViews.map((tab) => {
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
          })}

          {showOfficeReview && officeReview.status === "loading" && (
            <div aria-hidden="true" className="mx-3 my-1.5 h-6 animate-pulse motion-reduce:animate-none rounded bg-[var(--ophalo-canvas)]" />
          )}
          {showOfficeReview && officeReview.status === "error" && (
            <button
              type="button"
              onClick={officeReview.retry}
              className="flex w-full items-center px-3 py-1.5 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
            >
              Office Review — couldn’t load counts · Retry
            </button>
          )}
          {showOfficeReview && officeReview.status === "ready" && officeReview.aggregate > 0 && (() => {
            // UI-004 amendment: actionable (non-zero) Office Review members lead as clickable
            // rows; zero-count members collapse into one quiet, non-interactive line rather
            // than standing as equal-weight zero-badge rows.
            const [actionable, empty] = officeReviewMembers.reduce<[TabDef[], TabDef[]]>(
              (acc, tab) => {
                acc[officeReviewMemberCount(tab, officeReview) > 0 ? 0 : 1].push(tab);
                return acc;
              },
              [[], []],
            );
            return (
              <>
                {actionable.map((tab) => {
                  const count = officeReviewMemberCount(tab, officeReview);
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
            );
          })()}

          {isOwnerOrAdmin && (
            <button
              type="button"
              onClick={() => { onEnterHistory(); dismiss(); }}
              className="flex w-full items-center px-3 py-1.5 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)]"
            >
              History Log
            </button>
          )}

          <div className="my-1 border-t border-[var(--ophalo-border)]" />

          <div className="px-3 py-1 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
            Filter by status
          </div>
          <div role="radiogroup" aria-label="Filter by status">
            {STATUS_OPTIONS.map((o) => {
              const isSelected = draftStatus === o.value;
              return (
                <button
                  key={o.value || "all"}
                  type="button"
                  role="radio"
                  aria-checked={isSelected}
                  onClick={() => setDraftStatus(o.value)}
                  className="flex w-full items-center gap-2 px-3 py-1.5 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)]"
                >
                  <Check className={`h-3.5 w-3.5 shrink-0 ${isSelected ? "opacity-100 text-[var(--keep-accent)]" : "opacity-0"}`} />
                  {o.label}
                </button>
              );
            })}
          </div>

          <div className="mt-1 flex items-center justify-between gap-2 border-t border-[var(--ophalo-border)] px-3 pt-2">
            <button
              type="button"
              onClick={() => { setDraftStatus(""); onStatusFilterChange(""); dismiss(); }}
              className="text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
            >
              Reset filters
            </button>
            <button
              type="button"
              onClick={() => { onStatusFilterChange(draftStatus); dismiss(); }}
              className="rounded-md bg-[var(--ophalo-navy)] px-3 py-1 text-xs font-semibold text-white hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              Apply
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

export function RequestListToolbar({
  isAvailableTab,
  historyMode,
  presentAsHistory,
  searchInputRef,
  draftQ,
  onDraftQChange,
  onSubmitSearch,
  onClearSearch,
  statusFilter,
  onStatusFilterChange,
  showStalenessNotice,
  onManualRefresh,
  appliedLineText,
  paneMode = false,
  activeTab,
  viewCounts,
  onSelectTab,
  secondaryViews,
  officeReviewMembers,
  officeReview,
  isOwnerOrAdmin,
  onEnterHistory,
}: RequestListToolbarProps) {
  return (
    <>
      {/* Search + Views — demoted utility row. Search hides on Available Work/Actual Work
          Review (no search over those data sources) and stays out of history mode's own
          search-with-different-placeholder path handled below. Views hides only in history
          mode; it stays mounted across the isAvailableTab toggle so a popover selection
          into/out of those tabs can still return focus to its own trigger. */}
      <div className={`flex items-center gap-2 px-4 py-2 sm:px-6 border-t border-[var(--ophalo-border)] ${paneMode ? "" : "flex-wrap"}`}>
        {!isAvailableTab && (
          <form onSubmit={onSubmitSearch} className={`flex items-center gap-2 flex-1 ${paneMode ? "min-w-0" : "min-w-[180px]"}`}>
            <div className="relative flex-1">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-[var(--ophalo-muted)] pointer-events-none" />
              <input
                id="request-search"
                ref={searchInputRef}
                type="text"
                value={draftQ}
                onChange={(e) => onDraftQChange(e.target.value)}
                placeholder={paneMode ? "Search…" : presentAsHistory ? "Search closed & cancelled history…" : "Search requests…"}
                aria-label="Search requests"
                className={`w-full pl-8 py-1.5 text-sm border border-[var(--ophalo-border)] rounded-lg bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 ${draftQ.length > 0 ? "pr-7" : "pr-3"}`}
              />
              {draftQ.length > 0 && (
                <button
                  type="button"
                  onClick={() => {
                    onClearSearch();
                    searchInputRef.current?.focus();
                  }}
                  aria-label="Clear search"
                  className="absolute right-1.5 top-1/2 -translate-y-1/2 p-1 rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              )}
            </div>
            <button type="submit" className="sr-only">Search</button>
          </form>
        )}
        {!historyMode && (
          <ViewsPopover
            activeTab={activeTab}
            viewCounts={viewCounts}
            onSelectTab={onSelectTab}
            secondaryViews={secondaryViews}
            officeReviewMembers={officeReviewMembers}
            officeReview={officeReview}
            isOwnerOrAdmin={isOwnerOrAdmin}
            onEnterHistory={onEnterHistory}
            statusFilter={statusFilter}
            onStatusFilterChange={onStatusFilterChange}
          />
        )}
      </div>
      {/* GAP-046: quiet, informational — reports submitted criteria only, no action button */}
      {!isAvailableTab && appliedLineText && (
        <div className="px-4 pb-2 sm:px-6 text-xs text-[var(--ophalo-muted)]">
          {appliedLineText}
        </div>
      )}

      {/* Staleness notice */}
      {showStalenessNotice && (
        <div className="flex items-center justify-between px-4 py-2 sm:px-6 bg-[var(--ophalo-attention-bg)] border-t border-[var(--ophalo-border)] text-xs text-[var(--ophalo-attention)]">
          <span>Auto-refresh paused while viewing older results</span>
          <button
            type="button"
            onClick={onManualRefresh}
            className="flex items-center gap-1 font-semibold hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
          >
            <RefreshCw className="h-3 w-3" />
            Refresh
          </button>
        </div>
      )}
    </>
  );
}
