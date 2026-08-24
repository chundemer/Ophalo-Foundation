import { Lock } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ActualWorkHistoryState } from "./useActualWorkHistory";

interface ActualWorkHistoryCardProps {
  state: ActualWorkHistoryState;
  onRetry: () => void;
  // bare: no outer card chrome — used when a parent shares one enclosing Work Execution module
  // with ActualWorkCard (locked exception, 2026-08-22).
  bare?: boolean;
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

  return (
    <div className={wrapperCls}>
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Visit history</p>
          <p className="text-xs text-[var(--ophalo-muted)]">{state.submittedVisits.length} submitted visit{state.submittedVisits.length === 1 ? "" : "s"} · locked record</p>
        </div>
        <Lock className="h-4 w-4 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
      </div>
    </div>
  );
}
