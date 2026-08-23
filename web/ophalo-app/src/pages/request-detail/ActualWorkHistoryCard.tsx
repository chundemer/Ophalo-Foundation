import { ChevronRight } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";
import { type ActualWorkHistoryState } from "./useActualWorkHistory";
import { formatDate, FOCUS_RING } from "./helpers";

interface ActualWorkHistoryCardProps {
  state: ActualWorkHistoryState;
  onRetry: () => void;
  // bare: no outer card chrome — used when a parent shares one enclosing Work Execution module
  // with ActualWorkCard (locked exception, 2026-08-22).
  bare?: boolean;
}

const OUTCOME_LABELS: Record<string, string> = {
  DiagnosticOnly: "Diagnostic only",
  NoWorkAuthorized: "No work authorized",
  NoAccess: "No access",
};

function outcomeLabel(outcome: string | null): string | null {
  if (!outcome) return null;
  return OUTCOME_LABELS[outcome] ?? outcome;
}

function VisitEntry({ visit }: { visit: ActualWorkSubmittedVisitEntry }) {
  const outcome = outcomeLabel(visit.outcome);
  return (
    <div className="border-t border-[var(--ophalo-border)] pt-3 first:border-t-0 first:pt-0">
      <div className="flex items-baseline justify-between gap-2">
        <p className="text-xs font-medium text-[var(--ophalo-ink)]">
          {visit.submittedAtUtc ? formatDate(visit.submittedAtUtc) : "Submitted"}
        </p>
        {outcome && <p className="text-xs text-[var(--ophalo-muted)]">{outcome}</p>}
      </div>
      {visit.completionNote && (
        <p className="text-xs text-[var(--ophalo-muted)] mt-1">{visit.completionNote}</p>
      )}
      {visit.lines.length > 0 && (
        <ul className="mt-2 space-y-1">
          {visit.lines.map((line) => (
            <li key={line.id} className="text-xs text-[var(--ophalo-ink)]">
              {line.displayNameSnapshot}
              <span className="text-[var(--ophalo-muted)]">
                {" — "}
                {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
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

  const wrapperCls = bare ? "px-5 py-3.5" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-3.5";

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

  const [mostRecent, ...older] = state.submittedVisits;

  return (
    <div className={wrapperCls}>
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-3">Visit history</p>
      <div className="space-y-3">
        <VisitEntry visit={mostRecent} />
        {older.length > 0 && (
          <details className="group">
            <summary
              className={`list-none flex cursor-pointer items-center gap-1 text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING} rounded`}
            >
              <ChevronRight className="h-3.5 w-3.5 shrink-0 transition-transform group-open:rotate-90" />
              {older.length} earlier {older.length === 1 ? "visit" : "visits"}
            </summary>
            <div className="mt-3 space-y-3">
              {older.map((visit) => (
                <VisitEntry key={visit.id} visit={visit} />
              ))}
            </div>
          </details>
        )}
      </div>
    </div>
  );
}
