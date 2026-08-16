import { type ProposedScopeLineResponse } from "../../lib/apiClient";

/**
 * Session 5B/5C, build-log/120: read-only rendering of the authoritative Draft. Repeated
 * catalog-item/assembly selections and assembly-expanded default lines are always separate rows,
 * keyed by line id — never merged or aggregated locally (locked shared implementation rule).
 * Edit/remove controls remain 5D scope.
 */
export function ComposerDraftList({ lines }: { lines: ProposedScopeLineResponse[] }) {
  if (lines.length === 0) {
    return <p className="text-sm text-[var(--ophalo-muted)]">No items added yet.</p>;
  }

  return (
    <ul className="space-y-2">
      {lines.map((line) => (
        <li key={line.id} className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2 text-sm text-[var(--ophalo-ink)]">
          <div className="flex items-center justify-between gap-2">
            <span className="min-w-0 truncate">{line.displayNameSnapshot}</span>
            <span className="text-[var(--ophalo-muted)] shrink-0">
              {" "}
              × {line.quantity}
              {line.unitOfMeasureSnapshot ? ` ${line.unitOfMeasureSnapshot}` : ""}
            </span>
          </div>
          {line.offeringAssemblyNameSnapshot && (
            <p className="text-xs text-[var(--ophalo-muted)] truncate">From {line.offeringAssemblyNameSnapshot}</p>
          )}
          {line.note && <p className="text-xs text-[var(--ophalo-muted)] truncate">{line.note}</p>}
        </li>
      ))}
    </ul>
  );
}
