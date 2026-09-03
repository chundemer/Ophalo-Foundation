import { useState } from "react";
import { Lock } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import { ResponsiveSheet } from "../../components/keep/ResponsiveSheet";
import type { ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";
import { type ActualWorkHistoryState } from "./useActualWorkHistory";

interface ActualWorkHistoryCardProps {
  state: ActualWorkHistoryState;
  onRetry: () => void;
  // bare: no outer card chrome — used when a parent shares one enclosing Work Execution module
  // with ActualWorkCard (locked exception, 2026-08-22).
  bare?: boolean;
  // BL136 4f-ii: when set (wide viewport only), each submitted visit offers an "Open in workspace"
  // link to the dedicated Actual Work Ticket Workspace route for that visit, where the Owner/Admin
  // office financial-review region lives. Undefined below 1001px — review stays inline on the page.
  onOpenVisit?: (visitId: string) => void;
  // Desktop's supporting-context rail keeps this read-only audit history concise. The full visit
  // records open in a dedicated drawer; the mobile/main-work presentation stays expanded.
  presentation?: "full" | "summary";
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

function SubmittedVisitDetails({
  visit,
  onOpenVisit,
}: {
  visit: ActualWorkSubmittedVisitEntry;
  onOpenVisit?: (visitId: string) => void;
}) {
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
        {onOpenVisit && (
          <button
            type="button"
            onClick={() => onOpenVisit(visit.id)}
            className="text-xs font-medium text-[var(--keep-accent)] hover:underline rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            Open in workspace →
          </button>
        )}
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
export function ActualWorkHistoryCard({ state, onRetry, bare = false, onOpenVisit, presentation = "full" }: ActualWorkHistoryCardProps) {
  const [historyOpen, setHistoryOpen] = useState(false);
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

  if (presentation === "summary") {
    const latestVisit = state.submittedVisits[0];
    return (
      <>
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-4 shadow-sm">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="text-[11px] font-bold uppercase tracking-wide text-[var(--keep-request-eyebrow)]">Visit history</p>
              <p className="mt-1 text-sm font-semibold text-[var(--ophalo-ink)]">
                {state.submittedVisits.length} submitted visit{state.submittedVisits.length === 1 ? "" : "s"}
              </p>
            </div>
            <Lock className="mt-0.5 h-4 w-4 shrink-0 text-[var(--ophalo-muted)]" aria-hidden="true" />
          </div>
          <div className="mt-3 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs text-[var(--ophalo-muted)]">
            <p className="font-medium text-[var(--ophalo-ink)]">Latest · {formatSubmittedAt(latestVisit.submittedAtUtc)}</p>
            <p className="mt-0.5">{latestVisit.lines.length} line{latestVisit.lines.length === 1 ? "" : "s"} · locked record</p>
          </div>
          <button type="button" onClick={() => setHistoryOpen(true)} className="mt-3 w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2">
            View all visit history
          </button>
        </div>

        {historyOpen && (
          <ResponsiveSheet
            label="Visit history"
            onClose={() => setHistoryOpen(false)}
            header={<div className="flex items-center justify-between gap-3"><div><p className="text-base font-semibold text-[var(--ophalo-ink)]">Visit history</p><p className="text-xs text-[var(--ophalo-muted)]">{state.submittedVisits.length} submitted visit{state.submittedVisits.length === 1 ? "" : "s"} · locked records</p></div><button type="button" onClick={() => setHistoryOpen(false)} className="rounded-lg px-2 py-1 text-sm font-medium text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]">Close</button></div>}
          >
            <div className="space-y-3">
              {state.submittedVisits.map((visit) => (
                <SubmittedVisitDetails key={visit.id} visit={visit} onOpenVisit={onOpenVisit} />
              ))}
            </div>
          </ResponsiveSheet>
        )}
      </>
    );
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
          <SubmittedVisitDetails key={visit.id} visit={visit} onOpenVisit={onOpenVisit} />
        ))}
      </div>
    </div>
  );
}
