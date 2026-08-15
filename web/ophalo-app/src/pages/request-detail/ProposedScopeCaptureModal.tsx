import { useState, type ComponentType } from "react";
import { X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { type ProposedScopeDetailResult } from "../../lib/apiClient";
import { PrimaryOfferingRung } from "./PrimaryOfferingRung";
import { CommonItemsRung } from "./CommonItemsRung";
import { CategorySearchRung } from "./CategorySearchRung";
import { GlobalSearchRung } from "./GlobalSearchRung";
import { OffCatalogRung } from "./OffCatalogRung";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

interface ProposedScopeCaptureModalProps {
  scope: ProposedScopeDetailResult;
  onClose: () => void;
  onRefetch: () => Promise<void>;
}

/**
 * ADR-461's five-rung escape ladder, fixed order, progressive with an explicit "not here" advance
 * (never free-jump tabs). Each rung commits immediately on pick (field-select/expand-assembly);
 * {@link ScopeCaptureRungProps.onCommitted} re-fetches the authoritative scope rather than
 * optimistically appending (mutation responses carry only {id, status, version}, never lines).
 * {@link ScopeCaptureRungProps.onConflict} is the narrow 409/timeout reconciliation path: re-fetch
 * plus a non-blocking notice, no auto-retry.
 */
export interface ScopeCaptureRungProps {
  proposedScopeId: string;
  version: string;
  onCommitted: () => void;
  onConflict: (message: string) => void;
}

const RUNGS: ReadonlyArray<{ key: string; label: string; Component: ComponentType<ScopeCaptureRungProps> }> = [
  { key: "primary", label: "Primary offering", Component: PrimaryOfferingRung },
  { key: "common", label: "Common items", Component: CommonItemsRung },
  { key: "categories", label: "Categories", Component: CategorySearchRung },
  { key: "search", label: "Search", Component: GlobalSearchRung },
  { key: "off-catalog", label: "Off-catalog", Component: OffCatalogRung },
];

export function ProposedScopeCaptureModal({ scope, onClose, onRefetch }: ProposedScopeCaptureModalProps) {
  const [rungIndex, setRungIndex] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);

  function handleCommitted() {
    setNotice(null);
    void onRefetch();
  }

  function handleConflict(message: string) {
    setNotice(message);
    void onRefetch();
  }

  const rung = RUNGS[rungIndex];
  const RungComponent = rung.Component;

  return (
    <KeepModal
      onClose={onClose}
      labelledBy="proposed-scope-capture-heading"
      overlayClassName="flex items-center justify-center px-4"
      backdropClassName="bg-black/40"
      panelClassName="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-lg max-h-[85vh] flex flex-col p-5"
    >
      <div className="flex items-center justify-between mb-3 shrink-0">
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

      <div className="flex-1 min-h-0 overflow-y-auto space-y-4">
        {notice && (
          <div role="status" aria-live="polite" className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)]">
            {notice}
          </div>
        )}

        <div>
          {scope.lines.length === 0 ? (
            <p className="text-sm text-[var(--ophalo-muted)]">No items added yet.</p>
          ) : (
            <ul className="space-y-1">
              {scope.lines.map((line) => (
                <li key={line.id} className="text-sm text-[var(--ophalo-ink)] flex justify-between">
                  <span>{line.displayNameSnapshot}</span>
                  <span className="text-[var(--ophalo-muted)]">× {line.quantity}</span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="border-t border-[var(--ophalo-border)] pt-3">
          <div className="flex items-center justify-between mb-2">
            <p className="text-xs font-medium text-[var(--ophalo-muted)]">
              Step {rungIndex + 1} of {RUNGS.length}: {rung.label}
            </p>
            <div className="flex gap-3">
              {rungIndex > 0 && (
                <button
                  type="button"
                  onClick={() => setRungIndex((i) => i - 1)}
                  className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
                >
                  Back
                </button>
              )}
              {rungIndex < RUNGS.length - 1 && (
                <button
                  type="button"
                  onClick={() => setRungIndex((i) => i + 1)}
                  className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
                >
                  Not here →
                </button>
              )}
            </div>
          </div>
          <RungComponent
            key={rung.key}
            proposedScopeId={scope.id}
            version={scope.concurrencyVersion}
            onCommitted={handleCommitted}
            onConflict={handleConflict}
          />
        </div>
      </div>
    </KeepModal>
  );
}
