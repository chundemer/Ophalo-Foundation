import { useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { api, type ProposedScopeDetailResult } from "../../lib/apiClient";
import { ComposerSearchAndAdd } from "./ComposerSearchAndAdd";
import { ComposerQuickActions } from "./ComposerQuickActions";
import { ComposerDraftList } from "./ComposerDraftList";
import { ComposerUndoToast, type PendingUndo } from "./ComposerUndoToast";
import { PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE } from "./useProposedScopeCapture";

const SUBMITTED_OUTCOME_LABEL = "Submitted to office — awaiting review";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

interface ProposedScopeComposerProps {
  scope: ProposedScopeDetailResult;
  conflictNotice: string | null;
  onClose: () => void;
  onCommitted: () => void;
  onConflict: (message?: string) => void;
  onDismissNotice: () => void;
  onRetryReconciliation: () => void;
}

/**
 * Session 5A–5E, build-log/120: the ADR-482/483 replacement surface — shell, Quick actions,
 * unified search+add, live Draft with edit/remove/Undo, and submit. The sole scope-entry surface
 * as of Session 5E; the prior five-rung ladder is removed.
 *
 * Fixed `100dvh` full-screen presentation on phone; a constrained centered dialog from `md:` up,
 * per the locked shared implementation rules — the page behind never becomes the active scroller.
 */
export function ProposedScopeComposer({
  scope,
  conflictNotice,
  onClose,
  onCommitted,
  onConflict,
  onDismissNotice,
  onRetryReconciliation,
}: ProposedScopeComposerProps) {
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [pendingUndo, setPendingUndo] = useState<PendingUndo | null>(null);
  const [submitted, setSubmitted] = useState(false);

  function handleRemoved(removed: PendingUndo) {
    setPendingUndo(removed);
  }

  function handleUndoSettled() {
    setPendingUndo(null);
    onCommitted();
  }

  const submitMutation = useMutation({
    mutationFn: () => api.submitProposedScope(scope.id, scope.concurrencyVersion),
    onSuccess: () => {
      setSubmitted(true);
      onCommitted();
    },
    onError: () => onConflict(),
  });

  const readOnly = submitted || scope.status !== "Draft";

  return (
    <KeepModal
      onClose={onClose}
      labelledBy="proposed-scope-composer-heading"
      initialFocus={searchInputRef}
      overlayClassName="md:flex md:items-center md:justify-center md:px-4"
      backdropClassName="bg-black/40"
      panelClassName={
        "fixed inset-0 h-[100dvh] w-full flex flex-col bg-[var(--ophalo-card)] " +
        "md:relative md:h-auto md:max-h-[85vh] md:w-full md:max-w-lg md:rounded-xl md:shadow-xl"
      }
    >
      <div className="flex items-center justify-between px-4 py-3 border-b border-[var(--ophalo-border)] shrink-0">
        <h2 id="proposed-scope-composer-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
          Proposed scope
        </h2>
        <button
          type="button"
          onClick={onClose}
          className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
        >
          <X className="h-4 w-4" />
          <span className="sr-only">Close</span>
        </button>
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto px-4 py-3 space-y-4">
        {conflictNotice && (
          <div
            role="status"
            aria-live="polite"
            className="flex items-start justify-between gap-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)]"
          >
            <span>{conflictNotice}</span>
            <div className="flex items-center gap-2 shrink-0">
              {conflictNotice === PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE && (
                <button
                  type="button"
                  onClick={onRetryReconciliation}
                  className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
                >
                  Retry
                </button>
              )}
              <button
                type="button"
                onClick={onDismissNotice}
                className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
              >
                Dismiss
              </button>
            </div>
          </div>
        )}

        {!readOnly && (
          <ComposerQuickActions
            proposedScopeId={scope.id}
            version={scope.concurrencyVersion}
            onCommitted={onCommitted}
            onConflict={onConflict}
          />
        )}

        {!readOnly && (
          <ComposerSearchAndAdd
            ref={searchInputRef}
            proposedScopeId={scope.id}
            version={scope.concurrencyVersion}
            onCommitted={onCommitted}
            onConflict={onConflict}
          />
        )}

        {pendingUndo && (
          <ComposerUndoToast
            proposedScopeId={scope.id}
            pendingUndo={pendingUndo}
            onRestored={handleUndoSettled}
            onConflict={(message) => {
              setPendingUndo(null);
              onConflict(message);
            }}
            onExpire={() => setPendingUndo(null)}
          />
        )}

        <div className="border-t border-[var(--ophalo-border)] pt-3">
          <ComposerDraftList
            lines={scope.lines}
            readOnly={readOnly}
            proposedScopeId={scope.id}
            version={scope.concurrencyVersion}
            onCommitted={onCommitted}
            onConflict={onConflict}
            onRemoved={handleRemoved}
          />
        </div>
      </div>

      <div className="px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0">
        {submitted ? (
          <p role="status" aria-live="polite" className="text-center text-sm font-medium text-[var(--ophalo-ink)]">
            {SUBMITTED_OUTCOME_LABEL}
          </p>
        ) : (
          <>
            <button
              type="button"
              disabled={scope.lines.length === 0 || submitMutation.isPending}
              onClick={() => submitMutation.mutate()}
              className={`w-full rounded-lg bg-[var(--keep-accent)] px-4 py-2.5 text-sm font-semibold text-white ${FOCUS_RING} disabled:opacity-50`}
            >
              Submit scope to office
            </button>
            {scope.lines.length === 0 && (
              <p className="mt-1 text-center text-xs text-[var(--ophalo-muted)]">Add at least one item before submitting.</p>
            )}
          </>
        )}
      </div>
    </KeepModal>
  );
}
