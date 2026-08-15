import { useState, type ComponentType } from "react";
import { useMutation } from "@tanstack/react-query";
import { X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { KeepButton } from "../../components/keep/KeepButton";
import { api, ApiError, type ProposedScopeDetailResult, type ProposedScopeLineResponse } from "../../lib/apiClient";
import { PrimaryOfferingRung } from "./PrimaryOfferingRung";
import { CommonItemsRung } from "./CommonItemsRung";
import { CategorySearchRung } from "./CategorySearchRung";
import { GlobalSearchRung } from "./GlobalSearchRung";
import { OffCatalogRung } from "./OffCatalogRung";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-2 py-1.5 ${FOCUS_RING}`;

const CONFLICT_NOTICE = "This proposed scope changed elsewhere — refreshed with the latest scope. Try again.";

const SUBMITTED_STATUS_LABEL: Record<string, string> = {
  SubmittedToOffice: "Submitted to office",
  OfficeReviewed: "Reviewed by office",
};

interface ProposedScopeCaptureModalProps {
  scope: ProposedScopeDetailResult;
  /** Session 3.4g: a submitted/reviewed scope opens the same modal read-only — full line detail,
   * no ladder, no edit/remove/submit controls. Never reopened back to Draft (ADR-464). */
  readOnly: boolean;
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

interface LineRowProps {
  line: ProposedScopeLineResponse;
  readOnly: boolean;
  proposedScopeId: string;
  version: string;
  onCommitted: () => void;
  onConflict: (message: string) => void;
}

/** Session 3.4g: inline edit-in-place (Quantity/Note, plus IsException — only legal on an
 * AssociatedItem line, ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem) and Remove. PATCH
 * is full-field, so DisplayOrder is always echoed back unchanged — no reorder UI. */
function ProposedScopeLineRow({ line, readOnly, proposedScopeId, version, onCommitted, onConflict }: LineRowProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [quantity, setQuantity] = useState(String(line.quantity));
  const [note, setNote] = useState(line.note ?? "");
  const [isException, setIsException] = useState(line.isException);
  const [error, setError] = useState<string | null>(null);

  function onMutationError(err: unknown) {
    if (err instanceof ApiError && err.status === 409) {
      onConflict(CONFLICT_NOTICE);
      setIsEditing(false);
      return;
    }
    if (!(err instanceof ApiError)) {
      onConflict(CONFLICT_NOTICE);
      setIsEditing(false);
      return;
    }
    setError("Something went wrong. Try again.");
  }

  const updateMutation = useMutation({
    mutationFn: () =>
      api.updateProposedScopeLine(
        proposedScopeId,
        line.id,
        { quantity: Number(quantity), isException, note: note.trim() || null, displayOrder: line.displayOrder },
        version,
      ),
    onSuccess: () => {
      setError(null);
      setIsEditing(false);
      onCommitted();
    },
    onError: onMutationError,
  });

  const removeMutation = useMutation({
    mutationFn: () => api.removeProposedScopeLine(proposedScopeId, line.id, version),
    onSuccess: () => {
      setError(null);
      onCommitted();
    },
    onError: onMutationError,
  });

  if (isEditing) {
    return (
      <li className="rounded-lg border border-[var(--ophalo-border)] p-2 space-y-2">
        <p className="text-sm font-medium text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
        <div>
          <label htmlFor={`line-quantity-${line.id}`} className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Quantity{line.unitOfMeasureSnapshot ? ` (${line.unitOfMeasureSnapshot})` : ""}
          </label>
          <input
            id={`line-quantity-${line.id}`}
            type="number"
            min="0"
            step="any"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            className={INPUT_CLS}
          />
        </div>
        <div>
          <label htmlFor={`line-note-${line.id}`} className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Note
          </label>
          <input id={`line-note-${line.id}`} type="text" value={note} onChange={(e) => setNote(e.target.value)} className={INPUT_CLS} />
        </div>
        {line.lineType === "AssociatedItem" && (
          <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
            <input
              type="checkbox"
              checked={isException}
              onChange={(e) => setIsException(e.target.checked)}
              className={FOCUS_RING}
            />
            Exception (differs from the assembly default)
          </label>
        )}
        {error && <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>}
        <div className="flex gap-2">
          <KeepButton
            type="button"
            variant="teal"
            disabled={updateMutation.isPending}
            onClick={() => updateMutation.mutate()}
            className="flex-1"
          >
            Save
          </KeepButton>
          <KeepButton
            type="button"
            variant="secondary"
            disabled={updateMutation.isPending}
            onClick={() => {
              setIsEditing(false);
              setQuantity(String(line.quantity));
              setNote(line.note ?? "");
              setIsException(line.isException);
              setError(null);
            }}
            className="flex-1"
          >
            Cancel
          </KeepButton>
        </div>
      </li>
    );
  }

  return (
    <li className="text-sm text-[var(--ophalo-ink)]">
      <div className="flex items-center justify-between gap-2">
        <div className="min-w-0">
          <span>{line.displayNameSnapshot}</span>
          <span className="text-[var(--ophalo-muted)]"> × {line.quantity}</span>
          {line.isException && <span className="ml-1 text-xs text-[var(--ophalo-muted)]">(exception)</span>}
          {line.note && <p className="text-xs text-[var(--ophalo-muted)] truncate">{line.note}</p>}
        </div>
        {!readOnly && (
          <div className="flex gap-2 shrink-0">
            <button type="button" onClick={() => setIsEditing(true)} className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}>
              Edit
            </button>
            <button
              type="button"
              disabled={removeMutation.isPending}
              onClick={() => removeMutation.mutate()}
              className={`text-xs font-medium text-[var(--ophalo-danger)] ${FOCUS_RING}`}
            >
              Remove
            </button>
          </div>
        )}
      </div>
    </li>
  );
}

export function ProposedScopeCaptureModal({ scope, readOnly, onClose, onRefetch }: ProposedScopeCaptureModalProps) {
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

  const submitMutation = useMutation({
    mutationFn: () => api.submitProposedScope(scope.id, scope.concurrencyVersion),
    onSuccess: () => {
      setNotice("Submitted to office.");
      void onRefetch();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        handleConflict(CONFLICT_NOTICE);
        return;
      }
      if (!(err instanceof ApiError)) {
        handleConflict(CONFLICT_NOTICE);
        return;
      }
      setNotice("Something went wrong submitting. Try again.");
    },
  });

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
          Proposed scope{readOnly && SUBMITTED_STATUS_LABEL[scope.status] ? ` — ${SUBMITTED_STATUS_LABEL[scope.status]}` : ""}
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
            <ul className="space-y-2">
              {scope.lines.map((line) => (
                <ProposedScopeLineRow
                  key={line.id}
                  line={line}
                  readOnly={readOnly}
                  proposedScopeId={scope.id}
                  version={scope.concurrencyVersion}
                  onCommitted={handleCommitted}
                  onConflict={handleConflict}
                />
              ))}
            </ul>
          )}
        </div>

        {!readOnly && (
          <>
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

            <div className="border-t border-[var(--ophalo-border)] pt-3">
              <KeepButton
                type="button"
                variant="primary"
                disabled={scope.lines.length === 0 || submitMutation.isPending}
                onClick={() => submitMutation.mutate()}
                className="w-full"
              >
                Submit to office
              </KeepButton>
              {scope.lines.length === 0 && (
                <p className="text-xs text-[var(--ophalo-muted)] mt-1 text-center">Add at least one line before submitting.</p>
              )}
            </div>
          </>
        )}
      </div>
    </KeepModal>
  );
}
