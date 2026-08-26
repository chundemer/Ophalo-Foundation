import type { RefObject } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { KeepRequestSummary, KeepRequestAvailableItem } from "../../lib/apiClient";
import { AvailableRequestRow } from "../RequestRow";
import { RequestRowSkeleton } from "./RequestRowSkeleton";

interface RequestListHeadingState {
  headingText: string;
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  isForbidden: boolean;
  emptyState: { heading: string; detail: string; isFiltered?: boolean };
  onClearFilters: () => void;
}

function RequestListRefetchBar() {
  return (
    <div role="status" aria-label="Refreshing requests" className="h-0.5 w-full overflow-hidden bg-[var(--ophalo-border)]">
      <div className="h-full w-full animate-pulse motion-reduce:animate-none bg-[var(--ophalo-accent)]" />
    </div>
  );
}

interface RequestListRowsState {
  itemCount: number;
  isAvailableTab: boolean;
  availableRequests: KeepRequestAvailableItem[];
  onAvailableSelect: (requestId: string) => void;
  requests: KeepRequestSummary[];
  isDefaultTab: boolean;
  needsAttentionRows: KeepRequestSummary[];
  openWorkRows: KeepRequestSummary[];
  renderRow: (row: KeepRequestSummary) => React.ReactNode;
}

interface RequestListPagerState {
  hasMore: boolean;
  isOnFirstPage: boolean;
  onPrevPage: () => void;
  onNextPage: () => void;
}

interface RequestListContentProps {
  listRegionRef: RefObject<HTMLDivElement | null>;
  pageHeadingRef: RefObject<HTMLHeadingElement | null>;
  contextLabel: string;
  heading: RequestListHeadingState;
  rows: RequestListRowsState;
  pager: RequestListPagerState;
}

export function RequestListContent({
  listRegionRef,
  pageHeadingRef,
  contextLabel,
  heading,
  rows,
  pager,
}: RequestListContentProps) {
  const { headingText, isLoading, isFetching, isError, isForbidden, emptyState, onClearFilters } = heading;

  return (
    <>
      {isFetching && !isLoading && !isError && <RequestListRefetchBar />}
      {/* Content — scrollable, canvas background shows between cards */}
      <div
        ref={listRegionRef}
        className="flex-1 min-h-0 overflow-y-auto"
        role="region"
        aria-label={`${contextLabel} requests`}
        aria-live="polite"
        aria-busy={isLoading}
      >
        <div className="max-w-6xl mx-auto w-full px-4 py-4 sm:px-6 sm:py-5">
        {/* GAP-043: rendered whenever it has a meaningful label (loading/range/empty-state
            heading, never a blank node in the outline) — the same DOM node persists across
            the loading→loaded transition, so a pending post-page-change focus() lands once
            the new page's real content, not the stale prior range, is in place. */}
        {/* Session 3.5 follow-up: when the visible centered empty-state heading below already
            carries this exact text, this node stays mounted only for its aria-live
            announcement and post-page-change focus target — sr-only avoids a duplicate
            visible heading. Loading/range text has no visible counterpart, so it stays shown. */}
        {headingText && (
          <h2
            ref={pageHeadingRef}
            tabIndex={-1}
            className={`mb-2 text-xs font-medium text-[var(--ophalo-muted)] outline-none ${
              !isLoading && !isError && rows.itemCount === 0 ? "sr-only" : ""
            }`}
          >
            {headingText}
          </h2>
        )}

        {isLoading && <RequestRowSkeleton />}

        {isError && !isForbidden && (
          <div className="flex flex-col items-center py-12 text-center gap-2">
            <p className="text-[var(--ophalo-ink)] text-sm font-medium">Something went wrong</p>
            <p className="text-[var(--ophalo-muted)] text-sm">Try refreshing the page.</p>
          </div>
        )}

        {isError && isForbidden && (
          <div className="flex justify-center py-12">
            <p className="text-[var(--ophalo-muted)] text-sm">You don't have access to this view.</p>
          </div>
        )}

        {!isLoading && !isError && rows.itemCount === 0 && (
          <div className="flex flex-col items-center justify-center py-16 text-center max-w-sm mx-auto gap-2">
            <p className="text-[var(--ophalo-ink)] text-sm font-semibold">
              {emptyState.heading}
            </p>
            <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed">
              {emptyState.detail}
            </p>
            {emptyState.isFiltered && (
              <button
                type="button"
                onClick={onClearFilters}
                className="mt-1 text-sm font-semibold text-[var(--keep-accent)] hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
              >
                Clear filters
              </button>
            )}
          </div>
        )}

        {!isLoading && !isError && rows.itemCount > 0 && (
          <div className="space-y-2">
            {rows.isAvailableTab
              ? rows.availableRequests.map((row) => (
                  <AvailableRequestRow key={row.requestId} row={row} onSelect={rows.onAvailableSelect} />
                ))
              : rows.isDefaultTab
                ? (
                  <>
                    {rows.needsAttentionRows.length > 0 && (
                      <div className="space-y-2">
                        <h2 className="px-1 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
                          Needs attention
                        </h2>
                        {rows.needsAttentionRows.map(rows.renderRow)}
                      </div>
                    )}
                    {rows.openWorkRows.length > 0 && (
                      <div className={`space-y-2 ${rows.needsAttentionRows.length > 0 ? "mt-4" : ""}`}>
                        <h2 className="px-1 text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
                          Open work
                        </h2>
                        {rows.openWorkRows.map(rows.renderRow)}
                      </div>
                    )}
                  </>
                )
                : rows.requests.map(rows.renderRow)
            }
          </div>
        )}
        </div>{/* /max-w-6xl */}
      </div>

      {/* Pagination */}
      {!isLoading && !isError && (pager.hasMore || !pager.isOnFirstPage) && (
        <div className="shrink-0 border-t border-[var(--ophalo-border)] bg-[var(--ophalo-card)]">
        <div className="max-w-6xl mx-auto w-full flex items-center justify-between px-4 py-3 sm:px-6">
          <button
            type="button"
            onClick={pager.onPrevPage}
            disabled={pager.isOnFirstPage}
            className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)] disabled:opacity-40 hover:text-[var(--ophalo-ink)] disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
          >
            <ChevronLeft className="h-4 w-4" />
            Previous
          </button>
          {!pager.hasMore && (
            <span className="text-xs text-[var(--ophalo-muted)]">End of results</span>
          )}
          <button
            type="button"
            onClick={pager.onNextPage}
            disabled={!pager.hasMore}
            className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)] disabled:opacity-40 hover:text-[var(--ophalo-ink)] disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1 rounded"
          >
            Next
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>{/* /max-w-6xl */}
        </div>
      )}
    </>
  );
}
