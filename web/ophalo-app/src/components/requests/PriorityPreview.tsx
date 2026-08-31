import { AlertTriangle, Clock, MapPin } from "lucide-react";
import { KeepBadge } from "../keep/KeepBadge";
import { KeepButton } from "../keep/KeepButton";
import { statusLabel, statusBadgeVariant } from "../../lib/requestStatus";
import type { KeepRequestSummary } from "../../lib/apiClient";
import type { AppliedQueueSnapshot } from "../../pages/Requests";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

// Same curated wording as RequestRow's row-badge labels (RequestRow.tsx ATTENTION_LABELS) —
// duplicated rather than imported so this stays a self-contained, minimal Step 3 surface; a
// reason code outside this set still gets an honest generic label, never fabricated text.
const ATTENTION_LABELS: Record<string, string> = {
  complaint: "Complaint",
  cancellation_requested: "Cancel requested",
  schedule_change_request: "Schedule change",
  timing_change_requested: "Timing change",
  call_requested: "Call requested",
  customer_message: "Customer replied",
  update_request: "Update requested",
  change_or_cancel_request: "Change/cancel",
};

function attentionLabel(reason: string | null): string {
  return (reason && ATTENTION_LABELS[reason]) ?? "Needs attention";
}

// Small, self-contained duplicate of RequestRow.tsx's buildCollapsedSummary (ADR-450): same
// whitespace normalization and 240-char word-boundary cap with ellipsis, minus the show-more
// toggle this compact preview doesn't need — the remaining visual clipping is CSS line-clamp-2.
function normalizeSummary(fullText: string): string {
  const normalized = fullText.replace(/\s+/g, " ").trim();
  if (normalized.length <= 240) return normalized;
  const slice = normalized.slice(0, 239);
  const lastSpace = slice.lastIndexOf(" ");
  return (lastSpace > 0 ? slice.slice(0, lastSpace) : slice).trimEnd() + "…";
}

interface PriorityPreviewProps {
  snapshot: AppliedQueueSnapshot | null;
  onOpenRequest: (requestId: string) => void;
  onStartCapture: () => void;
}

// Locked predicate (build-log 132 preflight) — the same `attentionLevel !== "none"` check used
// throughout the codebase (RequestRow.tsx:568, DetailHero.tsx:151, BusinessSection.tsx:32).
function hasAttention(row: KeepRequestSummary): boolean {
  return row.attention.attentionLevel !== "none";
}

// UI-003: read-only, non-mutating, applied-context summary. Never a blank "select an item"
// placeholder — always one of the four locked branches below, or an honest loading/error state
// while the settled snapshot isn't ready yet.
export function PriorityPreview({ snapshot, onOpenRequest, onStartCapture }: PriorityPreviewProps) {
  if (!snapshot || snapshot.isLoading) {
    return (
      <div className="flex h-full items-center justify-center p-6">
        <span className="text-[var(--ophalo-muted)] text-sm">Loading priority preview…</span>
      </div>
    );
  }

  if (snapshot.isForbidden) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center">
        <span className="text-[var(--ophalo-muted)] text-sm">
          You don&apos;t have access to preview this queue.
        </span>
      </div>
    );
  }

  if (snapshot.isError) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center">
        <span className="text-[var(--ophalo-muted)] text-sm">
          Priority preview couldn&apos;t load.
        </span>
        <button
          type="button"
          onClick={snapshot.onRetry}
          className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm hover:border-[var(--ophalo-navy)] ${FOCUS_RING}`}
        >
          Retry
        </button>
      </div>
    );
  }

  const { requests } = snapshot;

  if (requests.length === 0) {
    if (snapshot.isFiltered) {
      return (
        <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center">
          <span className="keep-row-title">No requests match the current filters</span>
          <button
            type="button"
            onClick={snapshot.onResetFilters}
            className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm hover:border-[var(--ophalo-navy)] ${FOCUS_RING}`}
          >
            Reset filters
          </button>
        </div>
      );
    }

    // GAP-057: a deliberately-selected, empty Needs Attention queue is not "no active
    // requests" when All work still has active items — show the truthful state plus an
    // explicit jump to the real workload, never the New Request CTA.
    if (snapshot.attentionEmptyWithActiveWork) {
      const { activeCount, onViewAllWork } = snapshot.attentionEmptyWithActiveWork;
      return (
        <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center">
          <span className="keep-row-title">Nothing needs attention</span>
          <button
            type="button"
            onClick={onViewAllWork}
            className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm hover:border-[var(--ophalo-navy)] ${FOCUS_RING}`}
          >
            View all {activeCount} active requests
          </button>
        </div>
      );
    }

    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center">
        <span className="keep-row-title">No active requests</span>
        <button
          type="button"
          onClick={onStartCapture}
          className={`rounded-lg bg-[var(--ophalo-navy)] px-3 py-1.5 text-sm text-white hover:opacity-90 ${FOCUS_RING}`}
        >
          New Request
        </button>
      </div>
    );
  }

  const topAttention = requests.find(hasAttention);
  const previewRow = topAttention ?? requests[0];
  const originalNeed = previewRow.originalSummary.fullText.trim()
    ? normalizeSummary(previewRow.originalSummary.fullText)
    : null;
  const hasServiceLocation = Boolean(previewRow.serviceCity && previewRow.serviceState);

  return (
    <div className="flex h-full flex-col gap-4 p-6">
      <div className="flex flex-col gap-2">
        {topAttention ? (
          <div className="flex items-center gap-1.5 text-[var(--ophalo-attention)]">
            <AlertTriangle className="h-3.5 w-3.5" />
            <span className="text-sm font-medium">
              {attentionLabel(previewRow.attention.attentionReason)}
            </span>
          </div>
        ) : (
          <div className="flex items-center gap-1.5 text-[var(--ophalo-muted)]">
            <Clock className="h-3.5 w-3.5" />
            <span className="text-sm">No request in this view currently needs attention</span>
          </div>
        )}
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-mono text-[11px] text-[var(--ophalo-muted)] shrink-0">
            {previewRow.referenceCode}
          </span>
          <span className="keep-row-title truncate">{previewRow.customerName}</span>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <KeepBadge variant={statusBadgeVariant(previewRow.status)}>
            {statusLabel(previewRow.status)}
          </KeepBadge>
        </div>
        {originalNeed && (
          <p className="keep-row-meta line-clamp-2">{originalNeed}</p>
        )}
        {hasServiceLocation && (
          <div className="flex items-center gap-1 text-[var(--ophalo-muted)] text-xs">
            <MapPin className="h-3 w-3 shrink-0" />
            <span className="truncate">
              {previewRow.serviceCity}, {previewRow.serviceState}
              {previewRow.serviceZip ? ` ${previewRow.serviceZip}` : ""}
            </span>
          </div>
        )}
      </div>
      <KeepButton variant="teal" onClick={() => onOpenRequest(previewRow.id)} className="self-start">
        Open request
      </KeepButton>
    </div>
  );
}
