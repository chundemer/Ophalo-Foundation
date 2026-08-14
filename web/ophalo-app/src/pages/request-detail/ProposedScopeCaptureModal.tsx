import { X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { type ProposedScopeDetailResult } from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

interface ProposedScopeCaptureModalProps {
  scope: ProposedScopeDetailResult;
  onClose: () => void;
}

/**
 * Session 3.4f-1: entry-point + draft-lifecycle wiring only. The five-rung escape ladder
 * (Primary Offering → Common Items → Categories → Search → Off-Catalog, ADR-461) is 3.4f-2's
 * scope; this stub proves the open/close/resume plumbing against the real draft.
 */
export function ProposedScopeCaptureModal({ scope, onClose }: ProposedScopeCaptureModalProps) {
  return (
    <KeepModal
      onClose={onClose}
      labelledBy="proposed-scope-capture-heading"
      overlayClassName="flex items-center justify-center px-4"
      backdropClassName="bg-black/40"
      panelClassName="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-lg p-5"
    >
      <div className="flex items-center justify-between mb-4">
        <h2 id="proposed-scope-capture-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
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
      {scope.lines.length === 0 ? (
        <p className="text-sm text-[var(--ophalo-muted)]">No items added yet.</p>
      ) : (
        <ul className="space-y-2">
          {scope.lines.map((line) => (
            <li key={line.id} className="text-sm text-[var(--ophalo-ink)]">
              {line.displayNameSnapshot}
            </li>
          ))}
        </ul>
      )}
    </KeepModal>
  );
}
