import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { ChevronRight, Pencil, Trash2 } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  api,
  ApiError,
  type ActualWorkLineHistoryEntry,
  type ActualWorkUpdateLineBody,
} from "../../lib/apiClient";

// Maintainability review item 7: split out of ActualWorkComposer.tsx (see build-log for the
// composer-family split). No behavior change. FOCUS_RING/INPUT_CLS are redeclared locally rather
// than imported, matching the existing convention in this directory. ActualWorkLinePerformer moves
// with it — a small private helper used only within this component (three call sites, all here).

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

interface ActualWorkDraftLineProps {
  line: ActualWorkLineHistoryEntry;
  readOnly: boolean;
  presentation?: "modal" | "inline";
  actualWorkId: string;
  version: string;
  onCommitted: () => Promise<void>;
  onConflict: (message?: string) => void;
  onConnectionFailure: (message: string, retry: () => void) => void;
  onConnectionRecovered: () => void;
}

/** ADR-494 D2 (4c-iii): read-only per-line attribution. A line's performer is frozen at creation
 * and has no edit route; `null` means the id no longer resolves to a display name. */
function ActualWorkLinePerformer({ name }: { name: string | null }) {
  return (
    <p className="text-xs text-[var(--ophalo-muted)]">
      Performed by <span className="text-[var(--ophalo-ink)]">{name ?? "Unknown performer"}</span>
    </p>
  );
}

export function ActualWorkDraftLine({
  line,
  readOnly,
  presentation = "modal",
  actualWorkId,
  version,
  onCommitted,
  onConflict,
  onConnectionFailure,
  onConnectionRecovered,
}: ActualWorkDraftLineProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [quantity, setQuantity] = useState(String(line.actualQuantity));
  const [note, setNote] = useState(line.note ?? "");
  const [error, setError] = useState<string | null>(null);

  function resetFields() {
    setQuantity(String(line.actualQuantity));
    setNote(line.note ?? "");
    setError(null);
  }

  function onMutationError(err: unknown, retry: () => void, connectionMessage: string) {
    if (!(err instanceof ApiError)) {
      onConnectionFailure(connectionMessage, retry);
      setIsEditing(false);
      return;
    }
    if (err.status === 409) {
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
    mutationFn: (body: ActualWorkUpdateLineBody) => api.updateActualWorkLine(actualWorkId, line.id, body, version),
    onSuccess: async () => {
      setError(null);
      setIsEditing(false);
      onConnectionRecovered();
      await onCommitted();
    },
    onError: (err, body) => onMutationError(err, () => updateMutation.mutate(body), "Couldn't save changes to this item."),
  });

  const removeMutation = useMutation({
    mutationFn: () => api.removeActualWorkLine(actualWorkId, line.id, version),
    onSuccess: async () => {
      onConnectionRecovered();
      await onCommitted();
    },
    onError: (err) => onMutationError(err, () => removeMutation.mutate(), "Couldn't remove this item."),
  });

  if (readOnly) {
    return (
      <div className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2">
        <p className="text-sm text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
        <p className="text-xs text-[var(--ophalo-muted)]">
          {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""}
          {line.note ? ` — ${line.note}` : ""}
        </p>
        <ActualWorkLinePerformer name={line.performerDisplayName} />
      </div>
    );
  }

  const editFields = (
    <>
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
          disabled={updateMutation.isPending}
          onClick={() => updateMutation.mutate({ actualQuantity: Number(quantity), note: note.trim() || null })}
          className="flex-1"
        >
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
    </>
  );

  // The inline workspace keeps a compact list, but each entry still reads as a real record at a
  // glance: quantity, item name, unit, and performer have deliberate visual hierarchy.
  if (presentation === "inline" && !readOnly) {
    const detailId = `aw-line-detail-${line.id}`;
    const open = expanded || isEditing;
    return (
      <div className="overflow-hidden rounded-lg border border-[var(--ophalo-border)] bg-white">
        <div className="flex items-center gap-3 px-3 py-3">
          <button
            type="button"
            aria-expanded={open}
            aria-controls={detailId}
            onClick={() => {
              const next = !open;
              setExpanded(next);
              if (!next) {
                resetFields();
                setIsEditing(false);
              }
            }}
            className={`shrink-0 rounded p-1 text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] hover:text-[var(--ophalo-ink)] ${FOCUS_RING}`}
          >
            <ChevronRight className={`h-3.5 w-3.5 transition-transform ${open ? "rotate-90" : ""}`} />
            <span className="sr-only">
              {open ? "Hide" : "Show"} details for {line.displayNameSnapshot}
            </span>
          </button>
          <span className="shrink-0 rounded border border-slate-200 bg-slate-50 px-2 py-1 text-xs font-semibold tabular-nums text-slate-700">
            {line.actualQuantity}×
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</p>
            <p className="mt-0.5 truncate text-xs text-[var(--ophalo-muted)]">
              Unit of measure: {line.unitOfMeasureSnapshot ?? "—"} · Performed by {line.performerDisplayName ?? "Unknown performer"}
              {line.note ? " · Note added" : ""}
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-1">
            <button
              type="button"
              onClick={() => {
                setExpanded(true);
                setIsEditing(true);
              }}
              className={`rounded p-1 text-[var(--keep-accent)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            >
              <Pencil className="h-3.5 w-3.5" />
              <span className="sr-only">Edit {line.displayNameSnapshot}</span>
            </button>
            <button
              type="button"
              disabled={removeMutation.isPending}
              onClick={() => removeMutation.mutate()}
              className={`rounded p-1 text-[var(--ophalo-muted)] hover:text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-50 ${FOCUS_RING}`}
            >
              <Trash2 className="h-3.5 w-3.5" />
              <span className="sr-only">Remove {line.displayNameSnapshot}</span>
            </button>
          </div>
        </div>
        {open && (
          <div id={detailId} className="space-y-2 border-t border-[var(--ophalo-border)] bg-slate-50/70 px-3 py-3">
            {isEditing ? (
              editFields
            ) : (
              <p className="text-xs text-[var(--ophalo-muted)]">
                {line.note ? line.note : "No note on this line."}
              </p>
            )}
          </div>
        )}
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
          <ActualWorkLinePerformer name={line.performerDisplayName} />
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
      <ActualWorkLinePerformer name={line.performerDisplayName} />
      {editFields}
    </div>
  );
}


