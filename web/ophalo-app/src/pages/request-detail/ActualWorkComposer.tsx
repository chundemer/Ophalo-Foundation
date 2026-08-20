import { forwardRef, useEffect, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  api,
  ApiError,
  type ActualWorkHistoryResult,
  type ActualWorkLineHistoryEntry,
  type FieldScopeSearchResultResponse,
} from "../../lib/apiClient";
import { ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE } from "./useActualWorkCapture";

type ActualWorkDraft = NonNullable<ActualWorkHistoryResult["openDraft"]>;

const OUTCOME_OPTIONS: { value: string; label: string }[] = [
  { value: "DiagnosticOnly", label: "Diagnostic only" },
  { value: "NoWorkAuthorized", label: "No work authorized" },
  { value: "NoAccess", label: "No access" },
];

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

interface ActualWorkComposerProps {
  draft: ActualWorkDraft;
  conflictNotice: string | null;
  onClose: () => void;
  // Returns the in-flight refetch — each mutation's onSuccess awaits it before settling, so
  // TanStack Query keeps the mutation (and its disabled controls) pending until the composer's
  // props actually carry the refreshed concurrencyVersion, not just until the write itself
  // finished. Without this, a second rapid edit can fire against the pre-refresh version and draw
  // an avoidable 409.
  onCommitted: () => Promise<void>;
  onConflict: (message?: string) => void;
  onDismissNotice: () => void;
  onRetryReconciliation: () => void;
  onSubmitted: () => void;
  onDiscarded: () => void;
}

/**
 * Batch 5b, build-log/129: the field capture composer for Direct Actual Work — mirrors
 * ProposedScopeComposer's shell/mount pattern, simplified to this feature's shape (no assemblies,
 * no nudges, no Undo — a submitted visit is immediately immutable, and the pilot has no
 * cross-user takeover to reconcile against). A catalog-backed or off-catalog line is added
 * directly against the open Draft; quantity/note are the only editable fields; the zero-line
 * submit path requires a truthful outcome and non-blank completion note (ActualWork.Submit,
 * build-log/129) — a submit with at least one line accepts both as optional.
 */
export function ActualWorkComposer({
  draft,
  conflictNotice,
  onClose,
  onCommitted,
  onConflict,
  onDismissNotice,
  onRetryReconciliation,
  onSubmitted,
  onDiscarded,
}: ActualWorkComposerProps) {
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [submitted, setSubmitted] = useState(false);
  const readOnly = submitted || draft.status !== "Draft";

  const discardMutation = useMutation({
    mutationFn: () => api.discardActualWork(draft.id, draft.concurrencyVersion),
    onSuccess: onDiscarded,
    onError: () => onConflict(),
  });

  return (
    <KeepModal
      onClose={onClose}
      labelledBy="actual-work-composer-heading"
      initialFocus={searchInputRef}
      overlayClassName="md:flex md:items-center md:justify-center md:px-4"
      backdropClassName="bg-black/40"
      panelClassName={
        "fixed inset-0 h-[100dvh] w-full flex flex-col bg-[var(--ophalo-card)] " +
        "md:relative md:h-auto md:max-h-[85vh] md:w-full md:max-w-lg md:rounded-xl md:shadow-xl"
      }
    >
      <div className="flex items-center justify-between px-4 py-3 border-b border-[var(--ophalo-border)] shrink-0">
        <h2 id="actual-work-composer-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
          Record completed work
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
              {conflictNotice === ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE && (
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
          <ActualWorkSearchAndAdd
            ref={searchInputRef}
            actualWorkId={draft.id}
            version={draft.concurrencyVersion}
            onCommitted={onCommitted}
            onConflict={onConflict}
          />
        )}

        <div className="border-t border-[var(--ophalo-border)] pt-3 space-y-2">
          {draft.lines.length === 0 && (
            <p className="text-xs text-[var(--ophalo-muted)]">No items added yet.</p>
          )}
          {draft.lines.map((line) => (
            <ActualWorkDraftLine
              key={line.id}
              line={line}
              readOnly={readOnly}
              actualWorkId={draft.id}
              version={draft.concurrencyVersion}
              onCommitted={onCommitted}
              onConflict={onConflict}
            />
          ))}
        </div>

        {!readOnly && draft.lines.length > 0 && (
          <button
            type="button"
            disabled={discardMutation.isPending}
            onClick={() => discardMutation.mutate()}
            className={`text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] ${FOCUS_RING} disabled:opacity-50`}
          >
            Discard this visit
          </button>
        )}
        {!readOnly && draft.lines.length === 0 && (
          <button
            type="button"
            disabled={discardMutation.isPending}
            onClick={() => discardMutation.mutate()}
            className={`text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] ${FOCUS_RING} disabled:opacity-50`}
          >
            Discard draft
          </button>
        )}
      </div>

      <ActualWorkSubmitFooter
        draft={draft}
        submitted={submitted}
        onConflict={onConflict}
        onSubmitted={() => {
          setSubmitted(true);
          onSubmitted();
        }}
      />
    </KeepModal>
  );
}

interface ActualWorkSearchAndAddProps {
  actualWorkId: string;
  version: string;
  onCommitted: () => Promise<void>;
  onConflict: (message?: string) => void;
}

type Selection = { kind: "catalog"; item: FieldScopeSearchResultResponse } | { kind: "custom" };

const ActualWorkSearchAndAdd = forwardRef<HTMLInputElement, ActualWorkSearchAndAddProps>(function ActualWorkSearchAndAdd(
  { actualWorkId, version, onCommitted, onConflict },
  ref,
) {
  const [searchText, setSearchText] = useState("");
  const [debouncedText, setDebouncedText] = useState("");
  const [selection, setSelection] = useState<Selection | null>(null);
  const [customDescription, setCustomDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedText(searchText.trim()), 250);
    return () => clearTimeout(handle);
  }, [searchText]);

  const { data: results, isLoading } = useQuery({
    queryKey: ["fieldScopeSearch", "actualWork", debouncedText],
    queryFn: () => api.getFieldScopeSearch({ search: debouncedText, limit: 20 }),
    enabled: selection === null && debouncedText.length > 0,
  });

  const catalogResults = (results?.items ?? []).filter((item) => item.kind === "CatalogItem");

  function resetAfterSuccess() {
    setError(null);
    setSelection(null);
    setSearchText("");
    setDebouncedText("");
    setCustomDescription("");
    setQuantity("1");
    setNote("");
  }

  const addMutation = useMutation({
    mutationFn: () => {
      if (selection?.kind === "catalog") {
        return api.addActualWorkLine(
          actualWorkId,
          { catalogItemId: selection.item.id, actualQuantity: Number(quantity), note: note.trim() || null },
          version,
        );
      }
      return api.addActualWorkLine(
        actualWorkId,
        { offCatalogDescription: customDescription, actualQuantity: Number(quantity), note: note.trim() || null },
        version,
      );
    },
    onSuccess: async () => {
      resetAfterSuccess();
      await onCommitted();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        onConflict();
        return;
      }
      if (err instanceof ApiError && err.status !== 400) {
        onConflict();
        return;
      }
      setError(err instanceof ApiError ? err.message : "Something went wrong. Try again.");
    },
  });

  const canAdd =
    selection !== null &&
    Number(quantity) > 0 &&
    (selection.kind === "catalog" || customDescription.trim().length > 0);

  return (
    <div className="space-y-2">
      {selection === null ? (
        <>
          <input
            ref={ref}
            type="text"
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            placeholder="Search by name or SKU..."
            className={INPUT_CLS}
          />
          {debouncedText.length > 0 && (
            <div className="rounded-lg border border-[var(--ophalo-border)] divide-y divide-[var(--ophalo-border)] max-h-48 overflow-y-auto">
              {isLoading && <p className="px-3 py-2 text-xs text-[var(--ophalo-muted)]">Searching...</p>}
              {!isLoading &&
                catalogResults.map((item) => (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => setSelection({ kind: "catalog", item })}
                    className={`w-full text-left px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                  >
                    {item.displayName}
                  </button>
                ))}
              <button
                type="button"
                onClick={() => setSelection({ kind: "custom" })}
                className={`w-full text-left px-3 py-2 text-sm font-medium text-[var(--keep-accent)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
              >
                Add as custom item
              </button>
            </div>
          )}
        </>
      ) : (
        <div className="rounded-lg border border-[var(--ophalo-border)] p-3 space-y-2">
          {selection.kind === "catalog" ? (
            <p className="text-sm font-medium text-[var(--ophalo-ink)]">{selection.item.displayName}</p>
          ) : (
            <input
              type="text"
              value={customDescription}
              onChange={(e) => setCustomDescription(e.target.value)}
              placeholder="Describe the item"
              className={INPUT_CLS}
            />
          )}
          <div className="flex gap-2">
            <input
              type="number"
              min="0"
              step="any"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
              className={`${INPUT_CLS} w-24`}
              aria-label="Quantity"
            />
            <input
              type="text"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Note (optional)"
              className={INPUT_CLS}
            />
          </div>
          {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
          <div className="flex gap-2">
            <KeepButton
              variant="teal"
              disabled={!canAdd || addMutation.isPending}
              onClick={() => addMutation.mutate()}
              className="flex-1"
            >
              Add item
            </KeepButton>
            <KeepButton
              variant="secondary"
              onClick={() => {
                setSelection(null);
                setError(null);
              }}
              className="flex-1"
            >
              Cancel
            </KeepButton>
          </div>
        </div>
      )}
    </div>
  );
});

interface ActualWorkDraftLineProps {
  line: ActualWorkLineHistoryEntry;
  readOnly: boolean;
  actualWorkId: string;
  version: string;
  onCommitted: () => Promise<void>;
  onConflict: (message?: string) => void;
}

function ActualWorkDraftLine({ line, readOnly, actualWorkId, version, onCommitted, onConflict }: ActualWorkDraftLineProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [quantity, setQuantity] = useState(String(line.actualQuantity));
  const [note, setNote] = useState(line.note ?? "");
  const [error, setError] = useState<string | null>(null);

  function resetFields() {
    setQuantity(String(line.actualQuantity));
    setNote(line.note ?? "");
    setError(null);
  }

  function onMutationError(err: unknown) {
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
    if (err.status === 400) {
      setError(err.message);
      return;
    }
    onConflict();
    setIsEditing(false);
  }

  const updateMutation = useMutation({
    mutationFn: () => api.updateActualWorkLine(actualWorkId, line.id, { actualQuantity: Number(quantity), note: note.trim() || null }, version),
    onSuccess: async () => {
      setError(null);
      setIsEditing(false);
      await onCommitted();
    },
    onError: onMutationError,
  });

  const removeMutation = useMutation({
    mutationFn: () => api.removeActualWorkLine(actualWorkId, line.id, version),
    onSuccess: onCommitted,
    onError: onMutationError,
  });

  if (readOnly) {
    return (
      <div className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2">
        <p className="text-sm text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
        <p className="text-xs text-[var(--ophalo-muted)]">
          {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""}
          {line.note ? ` — ${line.note}` : ""}
        </p>
      </div>
    );
  }

  if (!isEditing) {
    return (
      <div className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2 flex items-center justify-between gap-2">
        <div>
          <p className="text-sm text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
          <p className="text-xs text-[var(--ophalo-muted)]">
            {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""}
            {line.note ? ` — ${line.note}` : ""}
          </p>
        </div>
        <div className="flex gap-2 shrink-0">
          <button
            type="button"
            onClick={() => setIsEditing(true)}
            className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
          >
            Edit
          </button>
          <button
            type="button"
            disabled={removeMutation.isPending}
            onClick={() => removeMutation.mutate()}
            className={`text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] ${FOCUS_RING} disabled:opacity-50`}
          >
            Remove
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2 space-y-2">
      <p className="text-sm text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
      <div className="flex gap-2">
        <input
          type="number"
          min="0"
          step="any"
          value={quantity}
          onChange={(e) => setQuantity(e.target.value)}
          className={`${INPUT_CLS} w-24`}
          aria-label="Quantity"
        />
        <input
          type="text"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Note (optional)"
          className={INPUT_CLS}
        />
      </div>
      {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
      <div className="flex gap-2">
        <KeepButton variant="teal" disabled={updateMutation.isPending} onClick={() => updateMutation.mutate()} className="flex-1">
          Save
        </KeepButton>
        <KeepButton
          variant="secondary"
          onClick={() => {
            resetFields();
            setIsEditing(false);
          }}
          className="flex-1"
        >
          Cancel
        </KeepButton>
      </div>
    </div>
  );
}

interface ActualWorkSubmitFooterProps {
  draft: ActualWorkDraft;
  submitted: boolean;
  onConflict: (message?: string) => void;
  onSubmitted: () => void;
}

/** Zero-line submit requires a truthful outcome + non-whitespace completion note
 * (ActualWork.Submit, build-log/129); a submit with at least one line accepts both as optional. */
function ActualWorkSubmitFooter({ draft, submitted, onConflict, onSubmitted }: ActualWorkSubmitFooterProps) {
  const [outcome, setOutcome] = useState("");
  const [completionNote, setCompletionNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const zeroLine = draft.lines.length === 0;

  const submitMutation = useMutation({
    mutationFn: () =>
      api.submitActualWork(
        draft.id,
        { outcome: outcome || null, completionNote: completionNote.trim() || null },
        draft.concurrencyVersion,
      ),
    onSuccess: onSubmitted,
    onError: (err) => {
      if (err instanceof ApiError && err.status === 400) {
        setError(err.message);
        return;
      }
      onConflict();
    },
  });

  if (submitted) {
    return (
      <div className="px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0">
        <p role="status" aria-live="polite" className="text-center text-sm font-medium text-[var(--ophalo-ink)]">
          Submitted to office — awaiting review
        </p>
      </div>
    );
  }

  const canSubmit = zeroLine ? outcome !== "" && completionNote.trim().length > 0 : true;

  return (
    <div className="px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0 space-y-2">
      {zeroLine && (
        <>
          <select
            value={outcome}
            onChange={(e) => setOutcome(e.target.value)}
            className={INPUT_CLS}
            aria-label="Visit outcome"
          >
            <option value="">Select outcome...</option>
            {OUTCOME_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
          <textarea
            value={completionNote}
            onChange={(e) => setCompletionNote(e.target.value)}
            placeholder="Completion note (required — no items added)"
            className={INPUT_CLS}
            rows={2}
          />
        </>
      )}
      {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
      <button
        type="button"
        disabled={!canSubmit || submitMutation.isPending}
        onClick={() => submitMutation.mutate()}
        className={`w-full rounded-lg bg-[var(--keep-accent)] px-4 py-2.5 text-sm font-semibold text-white ${FOCUS_RING} disabled:opacity-50`}
      >
        Submit visit to office
      </button>
      {zeroLine && !canSubmit && (
        <p className="text-center text-xs text-[var(--ophalo-muted)]">
          Select an outcome and add a completion note, or add at least one item, before submitting.
        </p>
      )}
    </div>
  );
}
