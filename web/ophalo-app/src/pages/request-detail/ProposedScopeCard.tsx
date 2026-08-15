import { KeepButton } from "../../components/keep/KeepButton";
import { type ProposedScopeCaptureState } from "./useProposedScopeCapture";

interface ProposedScopeCardProps {
  state: ProposedScopeCaptureState;
  onStartCapture: () => void;
  onStartView: () => void;
}

const SUBMITTED_STATUS_LABEL: Record<string, string> = {
  SubmittedToOffice: "Submitted to office",
  OfficeReviewed: "Reviewed by office",
};

/**
 * Session 3.4f-1 entry point: a 403 on the availability probe hides this card entirely (locked
 * decision, build-log/118) rather than rendering an error — loading/error states also render
 * nothing so a transient probe failure never blocks the rest of the request-detail cards.
 */
export function ProposedScopeCard({ state, onStartCapture, onStartView }: ProposedScopeCardProps) {
  if (state.status === "loading" || state.status === "hidden" || state.status === "error") {
    return null;
  }

  if (state.status === "no-scope") {
    return (
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Proposed scope</p>
        <p className="text-xs text-[var(--ophalo-muted)] mb-3">
          Capture the work items you're proposing for this request.
        </p>
        <KeepButton variant="teal" onClick={onStartCapture} className="w-full">
          Capture proposed scope
        </KeepButton>
      </div>
    );
  }

  if (state.status === "draft") {
    return (
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
        <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Proposed scope</p>
        <p className="text-xs text-[var(--ophalo-muted)] mb-3">
          {state.scope.lines.length === 0
            ? "Draft started — no items added yet."
            : `Draft in progress — ${state.scope.lines.length} item${state.scope.lines.length === 1 ? "" : "s"} added.`}
        </p>
        <KeepButton variant="teal" onClick={onStartCapture} className="w-full">
          Resume proposed scope
        </KeepButton>
      </div>
    );
  }

  const statusLabel = SUBMITTED_STATUS_LABEL[state.scope.status] ?? state.scope.status;
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
      <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Proposed scope</p>
      <p className="text-xs text-[var(--ophalo-muted)] mb-3">
        {statusLabel} — {state.scope.lines.length} item{state.scope.lines.length === 1 ? "" : "s"}.
      </p>
      <div className="flex gap-2">
        <KeepButton variant="secondary" onClick={onStartView} className="flex-1">
          View scope
        </KeepButton>
        <KeepButton variant="secondary" onClick={onStartCapture} className="flex-1">
          Capture new
        </KeepButton>
      </div>
    </div>
  );
}
