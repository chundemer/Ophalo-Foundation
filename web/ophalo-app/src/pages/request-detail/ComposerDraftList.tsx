import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError, type ProposedScopeLineResponse } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import type { PendingUndo } from "./ComposerUndoToast";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-2 py-1.5 ${FOCUS_RING}`;

interface ComposerDraftLineProps {
  line: ProposedScopeLineResponse;
  readOnly: boolean;
  proposedScopeId: string;
  version: string;
  onCommitted: () => void;
  onConflict: (message?: string) => void;
  onRemoved: (removed: PendingUndo) => void;
}

/**
 * Session 5D, build-log/120: touch-safe inline edit (Quantity/Note, plus IsException — only legal
 * on an AssociatedItem line, ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem) and remove.
 * The server remains sole validation authority; this component only gates the IsException control's
 * visibility for line types that can never carry it. PATCH is full-field, so DisplayOrder is always
 * echoed back unchanged — no reorder UI.
 */
function ComposerDraftLine({ line, readOnly, proposedScopeId, version, onCommitted, onConflict, onRemoved }: ComposerDraftLineProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [quantity, setQuantity] = useState(String(line.quantity));
  const [note, setNote] = useState(line.note ?? "");
  const [isException, setIsException] = useState(line.isException);
  const [error, setError] = useState<string | null>(null);
  // Session 5D review fix: `error` also covers non-quantity failures (removed elsewhere, generic
  // mutation failure), so aria-invalid/aria-describedby on the quantity input must only fire when the
  // error is actually about quantity — the same rule ComposerSearchAndAdd already follows.
  const [quantityInvalid, setQuantityInvalid] = useState(false);

  function resetFields() {
    setQuantity(String(line.quantity));
    setNote(line.note ?? "");
    setIsException(line.isException);
    setError(null);
    setQuantityInvalid(false);
  }

  function onMutationError(err: unknown, code: string) {
    if (err instanceof ApiError && err.status === 409) {
      onConflict();
      setIsEditing(false);
      return;
    }
    if (!(err instanceof ApiError)) {
      onConflict();
      setIsEditing(false);
      return;
    }
    if (err.code === "ProposedScope.LineNotFound") {
      onConflict("This item was already updated elsewhere — refreshed with the latest scope.");
      setIsEditing(false);
      return;
    }
    if (code === "update" && err.code === "ProposedScope.LineQuantityMustBePositive") {
      setError("Quantity must be greater than zero.");
      setQuantityInvalid(true);
      return;
    }
    setError("Something went wrong. Try again.");
    setQuantityInvalid(false);
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
      setQuantityInvalid(false);
      setIsEditing(false);
      onCommitted();
    },
    onError: (err) => onMutationError(err, "update"),
  });

  const removeMutation = useMutation({
    mutationFn: () => api.removeProposedScopeLine(proposedScopeId, line.id, version),
    onSuccess: (result) => {
      setError(null);
      onRemoved({ lineId: line.id, version: result.concurrencyVersion, label: line.displayNameSnapshot });
      onCommitted();
    },
    onError: (err) => onMutationError(err, "remove"),
  });

  if (isEditing) {
    return (
      <li className="rounded-lg border border-[var(--ophalo-border)] p-2 space-y-2">
        <p className="text-sm font-medium text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
        <div>
          <label htmlFor={`composer-line-quantity-${line.id}`} className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Quantity{line.unitOfMeasureSnapshot ? ` (${line.unitOfMeasureSnapshot})` : ""}
          </label>
          <input
            id={`composer-line-quantity-${line.id}`}
            type="number"
            min="0"
            step="any"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            aria-invalid={quantityInvalid ? true : undefined}
            aria-describedby={quantityInvalid ? `composer-line-error-${line.id}` : undefined}
            className={`${INPUT_CLS} min-h-[44px]`}
          />
        </div>
        <div>
          <label htmlFor={`composer-line-note-${line.id}`} className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Note
          </label>
          <input
            id={`composer-line-note-${line.id}`}
            type="text"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            className={`${INPUT_CLS} min-h-[44px]`}
          />
        </div>
        {line.lineType === "AssociatedItem" && (
          <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
            <input type="checkbox" checked={isException} onChange={(e) => setIsException(e.target.checked)} className={FOCUS_RING} />
            Exception (differs from the assembly default)
          </label>
        )}
        {error && (
          <p id={`composer-line-error-${line.id}`} role="alert" className="text-sm text-[var(--ophalo-danger)]">
            {error}
          </p>
        )}
        <div className="flex gap-2">
          <KeepButton
            type="button"
            variant="teal"
            disabled={updateMutation.isPending}
            onClick={() => updateMutation.mutate()}
            className="flex-1 min-h-[44px]"
          >
            Save
          </KeepButton>
          <KeepButton
            type="button"
            variant="secondary"
            disabled={updateMutation.isPending}
            onClick={() => {
              setIsEditing(false);
              resetFields();
            }}
            className="flex-1 min-h-[44px]"
          >
            Cancel
          </KeepButton>
        </div>
      </li>
    );
  }

  return (
    <li className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2 text-sm text-[var(--ophalo-ink)]">
      <div className="flex items-center justify-between gap-2">
        <div className="min-w-0">
          <span className="truncate">{line.displayNameSnapshot}</span>
          <span className="text-[var(--ophalo-muted)]">
            {" "}
            × {line.quantity}
            {line.unitOfMeasureSnapshot ? ` ${line.unitOfMeasureSnapshot}` : ""}
          </span>
          {line.isException && <span className="ml-1 text-xs text-[var(--ophalo-muted)]">(exception)</span>}
        </div>
        {!readOnly && (
          <div className="flex gap-3 shrink-0">
            <button
              type="button"
              onClick={() => setIsEditing(true)}
              className={`min-h-[44px] text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
            >
              Edit
            </button>
            <button
              type="button"
              disabled={removeMutation.isPending}
              onClick={() => removeMutation.mutate()}
              className={`min-h-[44px] text-xs font-medium text-[var(--ophalo-danger)] ${FOCUS_RING}`}
            >
              Remove
            </button>
          </div>
        )}
      </div>
      {line.offeringAssemblyNameSnapshot && (
        <p className="text-xs text-[var(--ophalo-muted)] truncate">From {line.offeringAssemblyNameSnapshot}</p>
      )}
      {line.note && <p className="text-xs text-[var(--ophalo-muted)] truncate">{line.note}</p>}
    </li>
  );
}

interface ComposerDraftListProps {
  lines: ProposedScopeLineResponse[];
  readOnly: boolean;
  proposedScopeId: string;
  version: string;
  onCommitted: () => void;
  onConflict: (message?: string) => void;
  onRemoved: (removed: PendingUndo) => void;
}

/**
 * Session 5B/5C/5D, build-log/120: the authoritative Draft. Repeated catalog-item/assembly
 * selections and assembly-expanded default lines are always separate rows, keyed by line id — never
 * merged or aggregated locally (locked shared implementation rule).
 */
export function ComposerDraftList({ lines, readOnly, proposedScopeId, version, onCommitted, onConflict, onRemoved }: ComposerDraftListProps) {
  if (lines.length === 0) {
    return <p className="text-sm text-[var(--ophalo-muted)]">No items added yet.</p>;
  }

  return (
    <ul className="space-y-2">
      {lines.map((line) => (
        <ComposerDraftLine
          key={line.id}
          line={line}
          readOnly={readOnly}
          proposedScopeId={proposedScopeId}
          version={version}
          onCommitted={onCommitted}
          onConflict={onConflict}
          onRemoved={onRemoved}
        />
      ))}
    </ul>
  );
}
