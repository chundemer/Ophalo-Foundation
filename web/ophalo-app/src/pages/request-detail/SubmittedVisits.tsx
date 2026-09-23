import { ChevronRight, Lock } from "lucide-react";
import type { ActualWorkSubmittedVisitEntry } from "../../lib/apiClient";
import { formatInstantShort } from "../../lib/businessTime";

// Maintainability review item 7, slice 2: split out of ActualWorkComposer.tsx (see build-log for
// the composer-family split). No behavior change. FOCUS_RING is redeclared locally rather than
// imported, matching the established convention in this directory.

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

export function SubmittedVisits({ visits, timeZone }: { visits: ActualWorkSubmittedVisitEntry[]; timeZone: string | null }) {
  if (visits.length === 0) return null;
  return <section className="border-t border-[var(--ophalo-border)] pt-4"><div className="mb-2 flex items-center justify-between"><h3 className="text-xs font-bold uppercase tracking-wide text-[var(--ophalo-muted)]">Submitted visits (locked)</h3><span className="text-[11px] text-[var(--ophalo-muted)]">Read-only audit record</span></div><div className="space-y-2">{visits.map((visit, index) => <details key={visit.id} className="group rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)]"><summary className={`flex cursor-pointer list-none items-center justify-between gap-2 px-3 py-2 text-xs font-medium text-[var(--ophalo-ink)] ${FOCUS_RING}`}><span className="flex items-center gap-2"><Lock className="h-3.5 w-3.5 text-[var(--ophalo-muted)]" />Visit #{visits.length - index} · {visit.submittedAtUtc ? formatInstantShort(visit.submittedAtUtc, timeZone) : "Submitted"}<span className="rounded bg-slate-200 px-1.5 py-0.5 text-[10px]">{visit.lines.length} item{visit.lines.length === 1 ? "" : "s"}</span></span><ChevronRight className="h-3.5 w-3.5 transition-transform group-open:rotate-90" /></summary><div className="border-t border-[var(--ophalo-border)] px-3 py-2 space-y-1">{visit.visitNote ? <p className="text-xs text-[var(--ophalo-muted)]"><span className="font-semibold text-[var(--ophalo-ink)]">Visit note:</span> {visit.visitNote}</p> : null}{visit.lines.map((line) => <p key={line.id} className="text-xs text-[var(--ophalo-muted)]">{line.displayNameSnapshot} — {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""} · Performed by {line.performerDisplayName ?? "Unknown performer"}</p>)}</div></details>)}</div></section>;
}
