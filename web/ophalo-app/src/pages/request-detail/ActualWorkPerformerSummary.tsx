// Maintainability review item 7, slice 2: split out of ActualWorkComposer.tsx (see build-log for
// the composer-family split). No behavior change. ActualWorkPerformerCaption travels with
// ActualWorkPerformerSummary (combined per an explicit scope decision) -- both are tiny and
// rendered adjacently in the same "confirmed performer" block, with no import boilerplate worth
// paying twice. FOCUS_RING is redeclared locally rather than imported, matching the established
// convention in this directory.

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

/** One-line attribution above the add region once a ticket default exists: the resolved performer
 * name from the projection, or "you" when the default is the current user and the name has not been
 * resolved yet (the optimistic "Record my work" create carries no display name). */
export function ActualWorkPerformerCaption({ name, isSelf }: { name: string | null; isSelf: boolean }) {
  const label = name ?? (isSelf ? "you" : null);
  if (!label) return null;
  return (
    <p className="text-xs text-[var(--ophalo-muted)]">
      Recording work for <span className="font-medium text-[var(--ophalo-ink)]">{label}</span>
    </p>
  );
}

/** BL136 large-ticket density: the compact confirmed-performer state for the inline workspace —
 * replaces the large gate once a ticket default exists. "Change" re-opens the explicit gate via
 * the parent (no auto-save, no line entry until re-confirmed). */
export function ActualWorkPerformerSummary({
  name,
  isSelf,
  onChange,
}: {
  name: string | null;
  isSelf: boolean;
  onChange: () => void;
}) {
  const label = name ?? (isSelf ? "you" : "an unnamed technician");
  return (
    <p className="flex flex-wrap items-center gap-x-1.5 text-xs text-[var(--ophalo-muted)]">
      <span>
        Performed by <span className="font-medium text-[var(--ophalo-ink)]">{label}</span>
      </span>
      <span aria-hidden="true">·</span>
      <button
        type="button"
        onClick={onChange}
        className={`font-medium text-[var(--keep-accent)] hover:underline rounded ${FOCUS_RING}`}
      >
        Change
      </button>
    </p>
  );
}

