import { useRef, useState } from "react";
import { Plus } from "lucide-react";

// Maintainability review item 7, slice 2: split out of ActualWorkComposer.tsx (see build-log for
// the composer-family split). No behavior change. FOCUS_RING/INPUT_CLS and the SetVisitNoteOutcome
// type are redeclared locally rather than imported, matching the established convention in this
// directory.

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

/** Mirrors `useActualWorkCapture`'s `setVisitNote` return contract. `set` / `stale` settle through
 * the parent refetch + shared reconcile; `too-long` is surfaced inline under the textarea. */
type SetVisitNoteOutcome = "set" | "too-long" | "stale" | "failed";

/** ADR-494 D5 (4c-ii): the visit-level note. Autosaves on blur through the composer's established
 * automatic-save + conflict-reconciliation path (no explicit Save control) — the parent remounts
 * this via `key={draft.visitNote}` after each successful write, so the field always reflects the
 * server's trim and survives a reload. A `too-long` outcome is the only inline error; `stale` /
 * `failed` are handled by the shared reconcile path in the hook. */
export function ActualWorkVisitNoteField({
  initialValue,
  collapsible = false,
  onSetVisitNote,
}: {
  initialValue: string;
  collapsible?: boolean;
  onSetVisitNote: (visitNote: string | null) => Promise<SetVisitNoteOutcome>;
}) {
  const [value, setValue] = useState(initialValue);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  // Inline workspace: an empty note starts as a compact affordance and expands to the textarea
  // on request. A note that already has content is always shown.
  const [expanded, setExpanded] = useState(!collapsible || initialValue.trim().length > 0);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={() => {
          setExpanded(true);
          requestAnimationFrame(() => textareaRef.current?.focus());
        }}
        className={`inline-flex items-center gap-1 rounded-lg border border-dashed border-[var(--ophalo-border)] px-2.5 py-1 text-xs font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] ${FOCUS_RING}`}
      >
        <Plus className="h-3.5 w-3.5" /> Add visit note
      </button>
    );
  }

  async function onBlur() {
    const next = value.trim();
    if (next === initialValue.trim()) return;
    setSaving(true);
    setError(null);
    const outcome = await onSetVisitNote(next.length > 0 ? next : null);
    setSaving(false);
    if (outcome === "too-long") {
      setError("The visit note must be 2,000 characters or fewer.");
    }
  }

  return (
    <div className="pt-1 space-y-1">
      <div className="flex items-baseline gap-1.5">
        <label htmlFor="actual-work-visit-note" className="text-xs font-semibold text-[var(--ophalo-ink)]">
          Visit note
        </label>
        <span className="text-[10px] font-medium uppercase tracking-wide text-[var(--ophalo-muted)]">Optional</span>
      </div>
      <textarea
        ref={textareaRef}
        id="actual-work-visit-note"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onBlur={() => void onBlur()}
        rows={3}
        placeholder="Notes about this visit"
        className={`${INPUT_CLS} resize-y`}
      />
      {saving && <p className="text-xs text-[var(--ophalo-muted)]">Saving…</p>}
      {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
    </div>
  );
}

