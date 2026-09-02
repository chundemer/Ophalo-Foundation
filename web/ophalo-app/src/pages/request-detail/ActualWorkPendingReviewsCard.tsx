import { CircleAlert, Clock } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import type { ActualWorkPendingReviewStatus } from "../../lib/apiClient";
import { formatDate } from "./helpers";
import { type ActualWorkPendingReviewsState } from "./useActualWorkPendingReviews";

// BL138 Slice 1B-client: Owner/Admin request-scoped "Pending financial reviews (N)" task card,
// rendered above the visit-history card. It lists every submitted / unreviewed / non-superseded
// visit on this request with its server-derived readiness, and offers a direct route to that exact
// visit's financial review — the wide viewport navigates to the Actual Work workspace deep link,
// the narrow viewport scrolls to and focuses the matching inline review card (the caller resolves
// which via `onReviewVisit`). It never re-derives membership or status. The whole card self-hides
// when there is nothing pending, and degrades to nothing on a 403.
interface ActualWorkPendingReviewsCardProps {
  state: ActualWorkPendingReviewsState;
  onRetry: () => void;
  onReviewVisit: (actualWorkId: string) => void;
}

const STATUS_LABELS: Record<ActualWorkPendingReviewStatus, string> = {
  ReadyToReview: "Ready to review",
  NeedsCostPriceResolution: "Needs cost / price resolution",
  NeedsNoChargeDisposition: "Record no-charge disposition",
};

function lineCountLabel(count: number): string {
  if (count === 0) return "No work lines";
  return `${count} work ${count === 1 ? "line" : "lines"}`;
}

export function ActualWorkPendingReviewsCard({ state, onRetry, onReviewVisit }: ActualWorkPendingReviewsCardProps) {
  if (state.status === "loading" || state.status === "hidden") return null;
  if (state.status === "loaded" && state.count === 0) return null;

  if (state.status === "error") {
    return (
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4">
        <p className="text-sm text-[var(--ophalo-muted)]">Unable to load pending financial reviews.</p>
        <KeepButton variant="secondary" className="mt-3" onClick={onRetry}>Retry</KeepButton>
      </div>
    );
  }

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]">
      <div className="px-4 py-3">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)]">
          Pending financial reviews ({state.count})
        </p>
        <p className="text-xs text-[var(--ophalo-muted)]">
          Submitted visits on this request awaiting internal financial review. Reviewing does not change the customer request.
        </p>
      </div>
      <ul className="divide-y divide-[var(--ophalo-border)]">
        {state.items.map((item) => {
          const needsWork = item.reviewStatus !== "ReadyToReview";
          return (
            <li key={item.actualWorkId} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <p className="flex items-center gap-1.5 text-sm font-medium text-[var(--ophalo-ink)]">
                  <Clock className="h-3.5 w-3.5 shrink-0 text-[var(--ophalo-muted)]" />
                  Submitted {formatDate(item.submittedAtUtc)}
                </p>
                <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
                  {lineCountLabel(item.lineCount)}
                  {item.recorderDisplayName ? ` · recorded by ${item.recorderDisplayName}` : ""}
                </p>
                <p
                  className={`mt-1 inline-flex items-center gap-1 text-xs font-semibold ${
                    needsWork ? "text-[var(--ophalo-attention)]" : "text-[var(--ophalo-success)]"
                  }`}
                >
                  {needsWork && <CircleAlert className="h-3.5 w-3.5 shrink-0" />}
                  {STATUS_LABELS[item.reviewStatus]}
                </p>
              </div>
              <KeepButton variant="secondary" onClick={() => onReviewVisit(item.actualWorkId)}>
                Review financials
              </KeepButton>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
