import { useRef } from "react";
import { X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { type ProposedScopeDetailResult } from "../../lib/apiClient";
import { ComposerSearchAndAdd } from "./ComposerSearchAndAdd";
import { ComposerDraftList } from "./ComposerDraftList";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

interface ProposedScopeComposerProps {
  scope: ProposedScopeDetailResult;
  conflictNotice: string | null;
  onClose: () => void;
  onCommitted: () => void;
  onConflict: () => void;
  onDismissNotice: () => void;
}

/**
 * Session 5B, build-log/120: the ADR-482/483 replacement surface — shell, unified search+add, and
 * live Draft. Built alongside `ProposedScopeCaptureModal`'s five-rung ladder, which stays fully
 * intact and unreachable from here until Session 5E's cutover and rung removal. The sticky footer
 * is a structural boundary only; `Submit scope to office` wiring is Session 5D.
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
}: ProposedScopeComposerProps) {
  const searchInputRef = useRef<HTMLInputElement>(null);

  return (
    <KeepModal
      onClose={onClose}
      labelledBy="proposed-scope-composer-heading"
      initialFocus={searchInputRef}
      overlayClassName="md:flex md:items-center md:justify-center md:px-4"
      backdropClassName="bg-black/40"
      panelClassName={
        "fixed inset-0 h-[100dvh] w-full flex flex-col bg-[var(--ophalo-card)] " +
        "md:static md:h-auto md:max-h-[85vh] md:w-full md:max-w-lg md:rounded-xl md:shadow-xl"
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
            <button
              type="button"
              onClick={onDismissNotice}
              className={`text-xs font-medium text-[var(--keep-accent)] shrink-0 ${FOCUS_RING}`}
            >
              Dismiss
            </button>
          </div>
        )}

        <ComposerSearchAndAdd
          ref={searchInputRef}
          proposedScopeId={scope.id}
          version={scope.concurrencyVersion}
          onCommitted={onCommitted}
          onConflict={onConflict}
        />

        <div className="border-t border-[var(--ophalo-border)] pt-3">
          <ComposerDraftList lines={scope.lines} />
        </div>
      </div>

      <div className="px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0">
        <button
          type="button"
          disabled
          className="w-full rounded-lg bg-[var(--keep-accent)] px-4 py-2.5 text-sm font-semibold text-white opacity-50"
        >
          Submit scope to office
        </button>
      </div>
    </KeepModal>
  );
}
