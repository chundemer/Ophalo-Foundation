import { useEffect, useState } from "react";
import { Check, CircleAlert } from "lucide-react";
import {
  type ActualWorkFinancialDetailResult,
  type ActualWorkFinancialResolutionBody,
} from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { formatDate } from "./helpers";
import {
  type ActualWorkFinancialReviewState,
  type FinancialReviewOutcome,
} from "./useActualWorkFinancialReview";
import { FinancialResolutionForm } from "./FinancialResolutionForm";
import { NoChargeDispositionForm } from "./NoChargeDispositionForm";

interface ActualWorkReviewCardProps {
  state: ActualWorkFinancialReviewState;
  onRetry: () => void;
  onReview: (visit: ActualWorkFinancialDetailResult, note: string | null) => Promise<FinancialReviewOutcome>;
  onResolveLine: (
    visit: ActualWorkFinancialDetailResult,
    lineId: string,
    body: ActualWorkFinancialResolutionBody,
  ) => Promise<FinancialReviewOutcome>;
  onRecordNoChargeDisposition: (
    visit: ActualWorkFinancialDetailResult,
    reason: string,
  ) => Promise<FinancialReviewOutcome>;
  isVisitMutating: (visitId: string) => boolean;
  onReviewSuccess: () => void;
  focusOnMount?: boolean;
}

const OUTCOME_LABELS: Record<string, string> = {
  DiagnosticOnly: "Diagnostic only — no work performed",
  NoWorkAuthorized: "No work authorized",
  NoAccess: "No access to the site",
};

function currency(value: number | null) {
  return value == null ? "—" : value.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

function marginPercent(sales: number | null, margin: number | null) {
  if (sales == null || margin == null || sales === 0) return "—";
  return `${((margin / sales) * 100).toFixed(1)}%`;
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div><p className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">{label}</p><p className="mt-1 text-sm font-semibold text-[var(--ophalo-ink)]">{value}</p></div>;
}

function Visit({ visit, index, onReview, onResolveLine, onRecordNoChargeDisposition, busy, onReviewSuccess }: {
  visit: ActualWorkFinancialDetailResult;
  index: number;
  onReview: ActualWorkReviewCardProps["onReview"];
  onResolveLine: ActualWorkReviewCardProps["onResolveLine"];
  onRecordNoChargeDisposition: ActualWorkReviewCardProps["onRecordNoChargeDisposition"];
  busy: boolean;
  onReviewSuccess: () => void;
}) {
  const [note, setNote] = useState(visit.reviewNote ?? "");
  const [notice, setNotice] = useState<string | null>(null);
  const reviewed = visit.reviewedAtUtc != null;
  const zeroLine = visit.lines.length === 0;
  const outcomeLabel = visit.outcome ? OUTCOME_LABELS[visit.outcome] ?? visit.outcome : null;
  // The no-charge form renders only for an unreviewed, zero-line visit with no disposition yet;
  // a reviewed visit shows read-only state only.
  const showNoChargeForm = !reviewed && zeroLine && !visit.hasNoChargeDisposition;

  async function markReviewed() {
    if (busy) return;
    setNotice(null);
    const outcome = await onReview(visit, note.trim() || null);
    if (outcome.kind === "success") {
      onReviewSuccess();
      return;
    }
    if (outcome.kind === "hidden") return;
    setNotice(
      outcome.kind === "review-blocked-incomplete"
        ? "Resolve the missing pricing or cost on every line before marking this visit reviewed."
        : outcome.kind === "review-blocked-zero-line"
          ? "Record this visit as no charge before marking it reviewed."
          : outcome.kind === "reconciled"
            ? "This visit was already reviewed or changed. The latest record is shown below."
            : "Unable to mark this visit reviewed. Try again.",
    );
  }

  return (
    <details open={!reviewed} className="group px-4 py-4" aria-label={`Financial review visit ${index + 1}`}>
      <summary className="cursor-pointer list-none">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Financial review · Visit #{index + 1}</p>
          <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Submitted {formatDate(visit.submittedAtUtc)}</p>
        </div>
        {reviewed ? <span className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--ophalo-success)]"><Check className="h-3.5 w-3.5" /> Reviewed</span> : <span className="text-xs font-semibold text-[var(--ophalo-attention)]">Unreviewed</span>}
      </div>
      {reviewed && <p className="mt-1 text-xs text-[var(--ophalo-muted)]">Reviewed {formatDate(visit.reviewedAtUtc!)} by {visit.reviewedByDisplayName ?? "an authorized reviewer"}{visit.reviewNote ? ` · “${visit.reviewNote}”` : ""}</p>}
      </summary>

      {notice && <p role="alert" className="mt-3 text-xs text-[var(--ophalo-danger)]">{notice}</p>}

      {(outcomeLabel || visit.completionNote) && (
        <div className="mt-3 rounded-lg bg-[var(--ophalo-canvas)] px-3 py-2 text-xs">
          {outcomeLabel && <p className="font-semibold text-[var(--ophalo-ink)]">{outcomeLabel}</p>}
          {visit.completionNote && <p className="mt-0.5 text-[var(--ophalo-muted)]">{visit.completionNote}</p>}
        </div>
      )}

      {visit.hasIncompleteFinancialData && <p className="mt-3 flex items-center gap-1.5 rounded-lg bg-[var(--ophalo-attention-bg)] px-3 py-2 text-xs font-medium text-[var(--ophalo-attention)]"><CircleAlert className="h-4 w-4 shrink-0" />Missing cost data — visit totals and margin are unavailable.</p>}

      <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Metric label="Sales price" value={currency(visit.totalSalesPrice)} />
        <Metric label="Std direct cost" value={currency(visit.totalStandardExpectedDirectCost)} />
        <Metric label="Expected margin" value={currency(visit.totalMargin)} />
        <Metric label="Margin %" value={marginPercent(visit.totalSalesPrice, visit.totalMargin)} />
      </div>

      {zeroLine ? (
        <div className="mt-4 border-t border-[var(--ophalo-border)] pt-3">
          <p className="text-xs text-[var(--ophalo-muted)]">No work lines were recorded for this visit.</p>
          {visit.hasNoChargeDisposition && <p className="mt-1 flex items-center gap-1 text-xs font-semibold text-[var(--ophalo-success)]"><Check className="h-3.5 w-3.5" /> Recorded as no charge</p>}
          {showNoChargeForm && <NoChargeDispositionForm busy={busy} onSubmit={(reason) => onRecordNoChargeDisposition(visit, reason)} />}
        </div>
      ) : (
        <div className="mt-4 border-t border-[var(--ophalo-border)] pt-3">
          <p className="text-xs font-semibold text-[var(--ophalo-ink)]">Line item breakdown</p>
          <ul className="mt-2 space-y-2">
            {visit.lines.map((line) => <li key={line.id} className="flex flex-wrap justify-between gap-x-4 gap-y-0.5 text-xs"><span className="text-[var(--ophalo-ink)]">{line.actualQuantity}× {line.displayNameSnapshot}{!line.isFinancialDataComplete && <span className="ml-1 font-medium text-[var(--ophalo-attention)]">(cost missing)</span>}{line.isFinancialDataComplete && (line.sellPriceResolved || line.directCostResolved) && <span className="ml-1 font-medium text-[var(--ophalo-muted)]">(resolved)</span>}</span><span className="text-[var(--ophalo-muted)]">Price {currency(line.lineSalesTotal)} · Cost {currency(line.lineStandardExpectedDirectCostTotal)} · {marginPercent(line.lineSalesTotal, line.lineMargin)} margin</span></li>)}
          </ul>
          {!reviewed && visit.blockers.map((blocker) => (
            <FinancialResolutionForm
              key={blocker.lineId}
              blocker={blocker}
              busy={busy}
              onSubmit={(lineId, body) => onResolveLine(visit, lineId, body)}
            />
          ))}
        </div>
      )}

      {!reviewed && <div className="mt-4"><label className="text-xs font-semibold text-[var(--ophalo-ink)]" htmlFor={`review-note-${visit.id}`}>Reviewer note <span className="font-normal text-[var(--ophalo-muted)]">(optional)</span></label><textarea id={`review-note-${visit.id}`} value={note} onChange={(event) => setNote(event.target.value)} placeholder="Add internal note for billing/payroll…" rows={2} className="mt-1 w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)]" /><div className="mt-3 flex justify-end"><KeepButton onClick={() => void markReviewed()} disabled={busy}>{busy ? "Working…" : "Mark visit reviewed"}</KeepButton></div></div>}
    </details>
  );
}

export function ActualWorkReviewCard({ state, onRetry, onReview, onResolveLine, onRecordNoChargeDisposition, isVisitMutating, onReviewSuccess, focusOnMount = false }: ActualWorkReviewCardProps) {
  useEffect(() => {
    if (focusOnMount && state.status === "loaded" && state.visits.length) document.getElementById("focus-panel-actual-work-review")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [focusOnMount, state]);
  if (state.status === "loading" || state.status === "hidden" || (state.status === "loaded" && !state.visits.length)) return null;
  if (state.status === "error") return <div id="focus-panel-actual-work-review" className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4"><p className="text-sm text-[var(--ophalo-muted)]">Unable to load financial review.</p><KeepButton variant="secondary" className="mt-3" onClick={onRetry}>Retry</KeepButton></div>;
  return <div id="focus-panel-actual-work-review" className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]"><div className="px-4 py-3"><p className="text-sm font-semibold text-[var(--ophalo-ink)]">Actual Work financial review</p><p className="text-xs text-[var(--ophalo-muted)]">Owner/Admin review only</p></div>{state.visits.map((visit, index) => <Visit key={visit.id} visit={visit} index={index} onReview={onReview} onResolveLine={onResolveLine} onRecordNoChargeDisposition={onRecordNoChargeDisposition} busy={isVisitMutating(visit.id)} onReviewSuccess={onReviewSuccess} />)}</div>;
}
