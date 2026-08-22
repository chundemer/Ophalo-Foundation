import { KeepButton } from "../../components/keep/KeepButton";
import { type ActualWorkCaptureState } from "./useActualWorkCapture";

interface ActualWorkCardProps {
  state: ActualWorkCaptureState;
  onStartCapture: () => void;
}

/**
 * Batch 5b entry point: "Record completed work". `canCaptureActualWork` (Batch 5a) means this
 * only renders for the request's active Responsible recorder — loading/hidden/error states
 * render nothing, same as ProposedScopeCard, so a transient probe failure never blocks the rest
 * of the request-detail cards and a non-Responsible watcher never sees an action that would fail.
 */
export function ActualWorkCard({ state, onStartCapture }: ActualWorkCardProps) {
  if (state.status === "loading" || state.status === "hidden" || state.status === "error") {
    return null;
  }

  if (state.status === "no-draft") {
    return (
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Actual work</p>
        <p className="text-xs text-[var(--ophalo-muted)] mb-3">
          {state.submittedCount === 0
            ? "Record the work completed on this visit."
            : `${state.submittedCount} prior visit${state.submittedCount === 1 ? "" : "s"} recorded.`}
        </p>
        {/* secondary, not teal — the Anchor owns the one primary-weight action (locked spec);
            this module's CTA must not visually compete with it or the composer's Send update. */}
        <KeepButton variant="secondary" onClick={onStartCapture} className="w-full">
          Record completed work
        </KeepButton>
      </div>
    );
  }

  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Actual work</p>
      <p className="text-xs text-[var(--ophalo-muted)] mb-3">
        {state.draft.lines.length === 0
          ? "Draft visit started — no items added yet."
          : `Draft visit in progress — ${state.draft.lines.length} item${state.draft.lines.length === 1 ? "" : "s"} added.`}
      </p>
      <KeepButton variant="secondary" onClick={onStartCapture} className="w-full">
        Resume completed work
      </KeepButton>
    </div>
  );
}
