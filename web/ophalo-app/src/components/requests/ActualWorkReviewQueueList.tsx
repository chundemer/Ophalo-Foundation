import type { ActualWorkReviewQueueEntry } from "../../lib/apiClient";
import { formatDate } from "../../pages/request-detail/helpers";

interface ActualWorkReviewQueueListProps {
  entries: ActualWorkReviewQueueEntry[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  onSelectRequest: (requestId: string, focus?: string) => void;
}

function formatCurrency(value: number | null): string {
  if (value == null) return "—";
  return value.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

/** Slice 8A, build-log/129: read-only Owner/Admin review queue — GET /keep/pricebook/actual-work
 * /review-queue rows, not KeepRequestSummary rows, so this does not reuse RequestRow/RequestListContent. */
export function ActualWorkReviewQueueList({
  entries,
  isLoading,
  isError,
  onRetry,
  onSelectRequest,
}: ActualWorkReviewQueueListProps) {
  if (isLoading) {
    return (
      <div className="flex-1 min-h-0 overflow-y-auto">
        <p className="px-4 sm:px-6 py-6 text-sm text-[var(--ophalo-muted)]">Loading review queue…</p>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex-1 min-h-0 overflow-y-auto px-4 sm:px-6 py-6">
        <p className="text-sm text-[var(--ophalo-muted)] mb-3">Unable to load the review queue.</p>
        <button
          type="button"
          onClick={onRetry}
          className="text-sm font-medium text-[var(--keep-accent)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
        >
          Retry
        </button>
      </div>
    );
  }

  if (entries.length === 0) {
    return (
      <div className="flex-1 min-h-0 overflow-y-auto px-4 sm:px-6 py-6">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Nothing to review</p>
        <p className="text-sm text-[var(--ophalo-muted)] mt-1">
          Submitted visits awaiting review will appear here.
        </p>
      </div>
    );
  }

  return (
    <div className="flex-1 min-h-0 overflow-y-auto">
      <div className="max-w-6xl mx-auto w-full divide-y divide-[var(--ophalo-border)]">
        {entries.map((entry) => (
          <button
            key={entry.actualWorkId}
            type="button"
            onClick={() => onSelectRequest(entry.requestId, "actual-work-review")}
            className="w-full flex items-center justify-between gap-4 px-4 sm:px-6 py-4 text-left hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-inset"
          >
            <div className="min-w-0">
              <p className="text-sm font-semibold text-[var(--ophalo-ink)] truncate">{entry.customerName}</p>
              <p className="text-xs text-[var(--ophalo-muted)]">
                {entry.referenceCode} · Submitted {formatDate(entry.submittedAtUtc)}
              </p>
            </div>
            <div className="shrink-0 text-right">
              {entry.hasIncompleteFinancialData ? (
                <p className="text-xs font-semibold text-[var(--ophalo-attention)]">
                  {entry.incompleteLineCount} incomplete line{entry.incompleteLineCount === 1 ? "" : "s"}
                </p>
              ) : (
                <p className="text-sm font-semibold text-[var(--ophalo-ink)]">
                  {formatCurrency(entry.totalMargin)} margin
                </p>
              )}
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
