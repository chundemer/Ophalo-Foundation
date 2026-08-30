import { Lock } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import type { ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";
import { type ActualWorkHistoryState } from "./useActualWorkHistory";

interface ActualWorkHistoryCardProps {
  state: ActualWorkHistoryState;
  onRetry: () => void;
  // bare: no outer card chrome — used when a parent shares one enclosing Work Execution module
  // with ActualWorkCard (locked exception, 2026-08-22).
  bare?: boolean;
}

function formatSubmittedAt(iso: string | null): string {
  if (!iso) return "Submitted";
  return new Date(iso).toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
}

/**
 * ADR-494 D5 (4c-iii): one submitted visit, read-only. Summary carries the submitted timestamp and
 * line count; the body discloses the visit note (when present) and each line with its frozen
 * performer name. "Unknown performer" is shown when the performer id no longer resolves to a
 * display name.
 */
/** BL136 4e-iii: replacement-copy lineage. A superseded source and the successor that corrected an
 * earlier visit each carry an explicit badge so the locked record stays legible after a correction. */
function LineageBadge({ visit }: { visit: ActualWorkSubmittedVisitEntry }) {
  if (visit.superseded) {
    return (
      <span className="shrink-0 rounded bg-[var(--ophalo-attention-bg)] px-1.5 py-0.5 text-[10px] font-semibold text-[var(--ophalo-attention)]">
        Superseded · replaced by a correction
      </span>
    );
  }
  if (visit.supersedesActualWorkId) {
    return (
      <span className="shrink-0 rounded bg-slate-200 px-1.5 py-0.5 text-[10px] font-semibold text-[var(--ophalo-muted)]">
        Correction of an earlier visit
      </span>
    );
  }
  return null;
}

function SubmittedVisitDetails({ visit }: { visit: ActualWorkSubmittedVisitEntry }) {
  return (
    <details className="group rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)]">
      <summary className="flex cursor-pointer list-none flex-wrap items-center justify-between gap-2 px-3 py-2 text-xs font-medium text-[var(--ophalo-ink)]">
        <span>{formatSubmittedAt(visit.submittedAtUtc)}</span>
        <span className="flex items-center gap-2">
          <LineageBadge visit={visit} />
          <span className="shrink-0 rounded bg-slate-200 px-1.5 py-0.5 text-[10px] text-[var(--ophalo-muted)]">
            {visit.lines.length} line{visit.lines.length === 1 ? "" : "s"}
          </span>
        </span>
      </summary>
      <div className="space-y-2 border-t border-[var(--ophalo-border)] px-3 py-2">
        {visit.visitNote ? (
          <p className="text-xs text-[var(--ophalo-muted)]">
            <span className="font-semibold text-[var(--ophalo-ink)]">Visit note</span>
            <br />
            {visit.visitNote}
          </p>
        ) : null}
        {visit.lines.length === 0 ? (
          <p className="text-xs text-[var(--ophalo-muted)]">No line items.</p>
        ) : (
          <ul className="space-y-1">
            {visit.lines.map((line) => (
              <li key={line.id} className="text-xs text-[var(--ophalo-muted)]">
                <span className="text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</span> —{" "}
                {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""}
                {line.note ? ` — ${line.note}` : ""}
                <br />
                Performed by{" "}
                <span className="text-[var(--ophalo-ink)]">
                  {line.performerDisplayName ?? "Unknown performer"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </details>
  );
}

/**
 * Batch 5c, build-log/129: standalone, price-blind, read-only history of submitted Actual Work
 * visits — visible to any normally request-visible caller (not gated on capture permission).
 * Mirrors ActualWorkCard's quiet-degradation convention: loading/hidden render nothing so a 403
 * (no visibility) never blocks the rest of request-detail; a transport/other failure renders a
 * compact retry affordance instead of a generic error card.
 */
export function ActualWorkHistoryCard({ state, onRetry, bare = false }: ActualWorkHistoryCardProps) {
  if (state.status === "loading" || state.status === "hidden") {
    return null;
  }

  const wrapperCls = bare ? "px-4 py-3.5" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3.5";

  if (state.status === "error") {
    return (
      <div className={wrapperCls}>
        <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Visit history</p>
        <p className="text-xs text-[var(--ophalo-muted)] mb-3">Unable to load visit history.</p>
        <KeepButton variant="secondary" onClick={onRetry} className="w-full">
          Retry
        </KeepButton>
      </div>
    );
  }

  // No filler for the empty case — an authorized recorder with no submitted visits yet has
  // nothing to disclose here; ActualWorkCard already covers that state.
  if (state.submittedVisits.length === 0) {
    return null;
  }

  return (
    <div className={wrapperCls}>
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Visit history</p>
          <p className="text-xs text-[var(--ophalo-muted)]">{state.submittedVisits.length} submitted visit{state.submittedVisits.length === 1 ? "" : "s"} · locked record</p>
        </div>
        <Lock className="h-4 w-4 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
      </div>
      <div className="mt-3 space-y-2">
        {state.submittedVisits.map((visit) => (
          <SubmittedVisitDetails key={visit.id} visit={visit} />
        ))}
      </div>
    </div>
  );
}
