import type { RefObject } from "react";
import { RefreshCw, Search, X } from "lucide-react";
import { STATUS_OPTIONS } from "../../pages/requestsWorkspace";

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
}: RequestListToolbarProps) {
  return (
    <>
      {/* Search + status filter — demoted utility row */}
      {!isAvailableTab && (
        <>
        <div className="flex flex-wrap items-center gap-2 px-4 py-2 sm:px-6 border-t border-[var(--ophalo-border)]">
          <form onSubmit={onSubmitSearch} className="flex items-center gap-2 flex-1 min-w-[180px]">
            <div className="relative flex-1">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-[var(--ophalo-muted)] pointer-events-none" />
              <input
                ref={searchInputRef}
                type="text"
                value={draftQ}
                onChange={(e) => onDraftQChange(e.target.value)}
                placeholder={presentAsHistory ? "Search closed & cancelled history…" : "Search requests…"}
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
          {!historyMode && (
            <select
              value={statusFilter}
              onChange={(e) => onStatusFilterChange(e.target.value)}
              aria-label="Filter by status"
              className="shrink-0 text-sm border border-[var(--ophalo-border)] rounded-lg px-2 py-1.5 bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              {STATUS_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          )}
        </div>
        {/* GAP-046: quiet, informational — reports submitted criteria only, no action button */}
        {appliedLineText && (
          <div className="px-4 pb-2 sm:px-6 text-xs text-[var(--ophalo-muted)]">
            {appliedLineText}
          </div>
        )}
        </>
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
