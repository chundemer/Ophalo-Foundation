import { ClipboardList } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ActualWorkCaptureState } from "./useActualWorkCapture";

interface ActualWorkCardProps {
  state: ActualWorkCaptureState;
  onStartCapture: () => void;
  // bare: no outer card chrome — used when a parent shares one enclosing Work Execution module
  // with ActualWorkHistoryCard (locked exception, 2026-08-22).
  bare?: boolean;
}

/**
 * Batch 5b entry point: "Record completed work". `canCaptureActualWork` (Batch 5a) means this
 * only renders for the request's active Responsible recorder — loading/hidden/error states
 * render nothing, same as ProposedScopeCard, so a transient probe failure never blocks the rest
 * of the request-detail cards and a non-Responsible watcher never sees an action that would fail.
 */
export function ActualWorkCard({ state, onStartCapture, bare = false }: ActualWorkCardProps) {
  if (state.status === "loading" || state.status === "hidden" || state.status === "error") {
    return null;
  }

  const wrapperCls = bare ? "px-5 py-3.5" : "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-3.5";

  const isNoDraft = state.status === "no-draft";
  const hasSavedLines = state.status === "draft" && state.draft.lines.length > 0;
  const summary = isNoDraft
    ? state.submittedCount === 0
      ? "Record the work completed on this visit."
      : `${state.submittedCount} prior visit${state.submittedCount === 1 ? "" : "s"} recorded.`
    : state.draft.lines.length === 0
      ? "Draft visit started — no items added yet."
      : `Draft visit in progress — ${state.draft.lines.length} item${state.draft.lines.length === 1 ? "" : "s"} added.`;

  return (
    <div className={wrapperCls}>
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Actual work</p>
          <p className="text-xs text-[var(--ophalo-muted)] truncate">{summary}</p>
        </div>
        {/* secondary, not teal — the Anchor owns the one primary-weight action (locked spec);
            this module's CTA must not visually compete with it or the composer's Send update. */}
        <KeepButton
          variant="secondary"
          onClick={onStartCapture}
          className="shrink-0 inline-flex items-center gap-1.5"
        >
          <ClipboardList className="h-3.5 w-3.5 shrink-0" />
          {hasSavedLines ? "Continue draft" : "Add actual work"}
        </KeepButton>
      </div>
    </div>
  );
}
