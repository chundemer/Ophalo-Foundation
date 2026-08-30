import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../lib/apiClient";
import { statusLabel, statusBadgeVariant } from "../lib/requestStatus";
import { KeepBadge } from "../components/keep/KeepBadge";
import { KeepButton } from "../components/keep/KeepButton";
import { ActualWorkComposer } from "./request-detail/ActualWorkComposer";
import { useActualWorkWorkspace } from "./request-detail/useActualWorkWorkspace";

// Same 1001px protected-workspace minimum RequestWorkbenchShell measures (build-log 133 §13).
const WIDE_QUERY = "(min-width: 1001px)";

const OUTCOME_LABEL: Record<string, string> = {
  DiagnosticOnly: "Diagnostic only",
  NoWorkAuthorized: "No work authorized",
  NoAccess: "No access",
};

interface ActualWorkWorkspacePageProps {
  requestId: string;
  /** `"new"` (self-creates a Draft, then swaps to `"draft"`), `"draft"` (the request's one open
   *  Draft — editable), or a submitted visit id (read-only). */
  visit: "new" | "draft" | (string & {});
  /** Back to Request / narrow fallback / composer close + discard + submitted-dismiss. */
  onExit: () => void;
  /** Called once the `"new"` entry has created (or found) the Draft — the caller replaces the URL
   *  segment with `"draft"`. */
  onResolvedToDraft: () => void;
}

/**
 * BL136 4f-i (D7): the dedicated Actual Work Ticket Workspace route. Desktop-first — the page
 * redirects a narrow deep-link back to Request Detail, where capture stays a full-bleed modal
 * (no new mobile workspace). The editable field region is the existing price-blind
 * `ActualWorkComposer`, hosted unmodified. The office region (financial resolution, totals,
 * blockers) is 4f-ii; this slice renders only a placeholder for it in the read-only view.
 */
export function ActualWorkWorkspacePage({
  requestId,
  visit,
  onExit,
  onResolvedToDraft,
}: ActualWorkWorkspacePageProps) {
  const [isWide, setIsWide] = useState(
    () => typeof window?.matchMedia === "function" && window.matchMedia(WIDE_QUERY).matches,
  );
  useEffect(() => {
    if (typeof window?.matchMedia !== "function") return;
    const mq = window.matchMedia(WIDE_QUERY);
    const sync = () => setIsWide(mq.matches);
    mq.addEventListener("change", sync);
    return () => mq.removeEventListener("change", sync);
  }, []);
  // Narrow (including a shrink after mount, or a hand-authored deep link): fall back to the
  // stacked Request Detail cards — there is no narrow workspace.
  useEffect(() => {
    if (!isWide) onExit();
  }, [isWide, onExit]);

  const meQuery = useQuery({ queryKey: ["me"], queryFn: api.getMe });
  const { capture, history, requestQuery, submittedVisit } = useActualWorkWorkspace(
    requestId,
    meQuery.data?.accountUserId,
  );

  // `"new"` compatibility path: create (or confirm) the Draft, then hand back to the caller to
  // swap the URL to `/draft`. Guarded so it fires once.
  const newHandled = useRef(false);
  useEffect(() => {
    if (visit !== "new" || newHandled.current) return;
    if (capture.state.status === "loading") return;
    newHandled.current = true;
    if (capture.state.status === "draft") {
      onResolvedToDraft();
      return;
    }
    void capture.createDraft().then(() => onResolvedToDraft());
  }, [visit, capture, onResolvedToDraft]);

  const headingRef = useRef<HTMLHeadingElement>(null);
  const readOnlyVisit = visit !== "new" && visit !== "draft" ? submittedVisit(visit) : null;
  useEffect(() => {
    if (readOnlyVisit) headingRef.current?.focus();
  }, [readOnlyVisit]);

  if (!isWide) return null;

  const request = requestQuery.data;
  const header = (
    <div className="border-b border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 md:px-6">
      <div className="mx-auto flex w-full max-w-4xl items-start justify-between gap-3">
        <div className="min-w-0">
          <button
            type="button"
            onClick={onExit}
            className="text-sm font-medium text-[var(--keep-accent)] hover:underline rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          >
            ← Back to Request
          </button>
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="mt-1 truncate font-serif text-xl font-semibold text-[var(--ophalo-ink)] focus:outline-none"
          >
            Actual Work{request ? ` — ${request.customerName}` : ""}
          </h1>
          {request && (
            <p className="mt-0.5 flex items-center gap-2 text-xs text-[var(--ophalo-muted)]">
              <span>{request.referenceCode}</span>
              <KeepBadge variant={statusBadgeVariant(request.status)}>{statusLabel(request.status)}</KeepBadge>
            </p>
          )}
        </div>
      </div>
    </div>
  );

  // Editable Draft path — host the composer unmodified. It renders as its own full-bleed surface
  // (with its own "← Back to Request"), so the page header is not rendered alongside it.
  if (visit === "draft" || visit === "new") {
    if (capture.state.status === "draft") {
      return (
        <ActualWorkComposer
          isWide={false}
          draft={capture.state.draft}
          replacementCorrection={capture.replacementCorrection}
          conflictNotice={capture.conflictNotice}
          onClose={onExit}
          onCommitted={async () => {
            await capture.refetchDraft();
          }}
          onConflict={(message) => void capture.reconcileAfterConflict(message)}
          onDismissNotice={capture.clearConflictNotice}
          onRetryReconciliation={() => void capture.retryReconciliation()}
          onSubmitted={() => {
            capture.markSubmitted();
            void history.retry();
          }}
          onDiscarded={onExit}
          submittedVisits={history.state.status === "loaded" ? history.state.submittedVisits : []}
          currentAccountUserId={meQuery.data?.accountUserId}
          onSetDefaultPerformer={capture.setDefaultPerformer}
          onSetVisitNote={capture.setVisitNote}
          onSetZeroLineDisposition={capture.setZeroLineDisposition}
          onHandOffToOffice={capture.handOffToOffice}
        />
      );
    }
    return (
      <div className="flex flex-1 flex-col">
        {header}
        <div className="mx-auto w-full max-w-4xl px-4 py-6 md:px-6">
          {capture.state.status === "loading" || visit === "new" ? (
            <p className="text-sm text-[var(--ophalo-muted)]">Loading…</p>
          ) : (
            <WorkspaceNotice state={capture.state.status} onExit={onExit} />
          )}
        </div>
      </div>
    );
  }

  // Read-only submitted visit.
  return (
    <div className="flex flex-1 flex-col">
      {header}
      <div className="mx-auto w-full max-w-4xl space-y-3 px-4 py-6 md:px-6">
        {history.state.status === "loading" && (
          <p className="text-sm text-[var(--ophalo-muted)]">Loading visit…</p>
        )}
        {history.state.status === "error" && (
          <p className="text-sm text-[var(--ophalo-danger)]">Unable to load this visit.</p>
        )}
        {history.state.status === "loaded" && !readOnlyVisit && (
          <p className="text-sm text-[var(--ophalo-muted)]">This visit is not available.</p>
        )}
        {readOnlyVisit && <ReadOnlyVisit visit={readOnlyVisit} />}
      </div>
    </div>
  );
}

function WorkspaceNotice({ state, onExit }: { state: string; onExit: () => void }) {
  const copy =
    state === "held-by-other"
      ? "Another team member is recording this visit."
      : state === "owner-recovery"
        ? "An open draft exists for this request. Manage it from Request Detail."
        : state === "hidden"
          ? "You do not have access to record work on this request."
          : state === "no-draft"
            ? "There is no open draft for this request."
            : "Unable to open this workspace.";
  return (
    <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-5 text-sm text-[var(--ophalo-ink)]">
      <p>{copy}</p>
      <KeepButton variant="secondary" className="mt-3" onClick={onExit}>
        Back to Request
      </KeepButton>
    </div>
  );
}

function ReadOnlyVisit({
  visit,
}: {
  visit: NonNullable<ReturnType<ReturnType<typeof useActualWorkWorkspace>["submittedVisit"]>>;
}) {
  const submittedAt = useMemo(
    () => (visit.submittedAtUtc ? new Date(visit.submittedAtUtc).toLocaleString() : null),
    [visit.submittedAtUtc],
  );
  return (
    <>
      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3">
        <p className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">
          Submitted visit
        </p>
        {submittedAt && <p className="mt-1 text-xs text-[var(--ophalo-muted)]">Submitted {submittedAt}</p>}
        {visit.superseded && (
          <p className="mt-1 text-xs text-[var(--ophalo-muted)]">Superseded · replaced by a correction.</p>
        )}
      </div>

      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] divide-y divide-[var(--ophalo-border)]">
        {visit.lines.length === 0 ? (
          <div className="px-4 py-3 text-sm text-[var(--ophalo-muted)]">
            No line items.
            {visit.outcome && (
              <span className="text-[var(--ophalo-ink)]">
                {" "}
                Outcome: {OUTCOME_LABEL[visit.outcome] ?? visit.outcome}.
              </span>
            )}
          </div>
        ) : (
          visit.lines.map((line) => (
            <div key={line.id} className="px-4 py-3 text-sm">
              <div className="flex items-baseline justify-between gap-3">
                <span className="font-medium text-[var(--ophalo-ink)]">{line.displayNameSnapshot}</span>
                <span className="shrink-0 text-[var(--ophalo-muted)]">
                  {line.actualQuantity}
                  {line.unitOfMeasureSnapshot ? ` ${line.unitOfMeasureSnapshot}` : ""}
                </span>
              </div>
              <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
                {line.performerDisplayName ?? "Unknown performer"}
              </p>
              {line.note && <p className="mt-1 text-xs text-[var(--ophalo-ink)]">{line.note}</p>}
            </div>
          ))
        )}
      </div>

      <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3">
        <p className="text-xs font-semibold uppercase tracking-widest text-[var(--ophalo-muted)]">Visit note</p>
        <p className="mt-1 whitespace-pre-wrap text-sm text-[var(--ophalo-ink)]">
          {visit.visitNote?.trim() ? visit.visitNote : <span className="text-[var(--ophalo-muted)]">None</span>}
        </p>
        {visit.completionNote?.trim() && (
          <p className="mt-2 text-sm text-[var(--ophalo-ink)]">
            <span className="text-[var(--ophalo-muted)]">Completion note: </span>
            {visit.completionNote}
          </p>
        )}
      </div>

      {/* BL136 4f-ii: the capability-gated office region (financial resolution / disposition /
          review controls, blocker list, and real totals) composes here, line-adjacent. */}
    </>
  );
}
