import { type ProposedScopeLineResponse } from "../../lib/apiClient";

/**
 * Session 5B, build-log/120: read-only rendering of the authoritative Draft. Quick-action/assembly
 * source context, duplicate-row stacking, and edit/remove controls are 5C/5D scope — this only
 * proves the shell can display whatever lines the composer's mutations produce.
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
            <span className="text-[var(--ophalo-muted)] shrink-0"> × {line.quantity}</span>
          </div>
          {line.note && <p className="text-xs text-[var(--ophalo-muted)] truncate">{line.note}</p>}
        </li>
      ))}
    </ul>
  );
}
