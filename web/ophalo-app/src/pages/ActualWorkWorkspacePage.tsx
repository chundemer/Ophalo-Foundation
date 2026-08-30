import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
  type RefObject,
} from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight } from "lucide-react";
import { api, type KeepRequestDetailResult } from "../lib/apiClient";
import { statusLabel, statusBadgeVariant } from "../lib/requestStatus";
import { KeepBadge } from "../components/keep/KeepBadge";
import { KeepButton } from "../components/keep/KeepButton";
import { ActualWorkComposer } from "./request-detail/ActualWorkComposer";
import { ActualWorkReviewCard } from "./request-detail/ActualWorkReviewCard";
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
  // BL136 4f-ii: on a wide viewport the Owner/Admin office financial-review region lives here
  // (moved off Request Detail, which keeps it only below 1001px). Same role check RequestDetail
  // applies. A 403 on the financial-detail read still degrades the region to nothing.
  const canReviewActualWork =
    meQuery.data?.accountRole === "owner" || meQuery.data?.accountRole === "admin";
  const reviewVisitId = visit !== "new" && visit !== "draft" ? visit : undefined;
  const { capture, history, requestQuery, submittedVisit, financialReview } = useActualWorkWorkspace(
    requestId,
    meQuery.data?.accountUserId,
    canReviewActualWork,
    reviewVisitId,
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
  const contextBand = <TicketContextBand request={request} onExit={onExit} headingRef={headingRef} />;

  // Editable Draft path — host the composer inline below the persistent Keep top nav and the
  // ticket-context band, so the operator always sees which request they are recording against.
  if (visit === "draft" || visit === "new") {
    return (
      <div className="flex min-h-0 flex-1 flex-col">
        {contextBand}
        {capture.state.status === "draft" ? (
          <ActualWorkComposer
            presentation="inline"
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
        ) : (
          <div className="min-h-0 flex-1 overflow-y-auto">
            <div className="mx-auto w-full max-w-4xl px-4 py-6 md:px-6">
              {capture.state.status === "loading" || visit === "new" ? (
                <p className="text-sm text-[var(--ophalo-muted)]">Loading…</p>
              ) : (
                <WorkspaceNotice state={capture.state.status} onExit={onExit} />
              )}
            </div>
          </div>
        )}
      </div>
    );
  }

  // Read-only submitted visit.
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {contextBand}
      <div className="min-h-0 flex-1 overflow-y-auto">
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
        {readOnlyVisit && (
          <ReadOnlyVisit
            visit={readOnlyVisit}
            officeRegion={
              canReviewActualWork && !readOnlyVisit.superseded ? (
                <ActualWorkReviewCard
                  state={financialReview.state}
                  onRetry={() => void financialReview.retry()}
                  onReview={financialReview.review}
                  onResolveLine={financialReview.resolveLine}
                  onRecordNoChargeDisposition={financialReview.recordNoChargeDisposition}
                  onReplace={async (v, reason) => {
                    const outcome = await financialReview.replace(v, reason);
                    if (outcome.kind === "replaced") {
                      await history.retry();
                      // This page instance is retained across the `:visitId` → `draft` route
                      // change, so the capture hook still holds its pre-replacement (typically
                      // `no-draft`) state. Re-probe it onto the successor Draft the service just
                      // created *before* switching the route, or `/draft` renders "no open draft".
                      await capture.refetchDraft();
                      onResolvedToDraft();
                    }
                    return outcome;
                  }}
                  isVisitMutating={financialReview.isVisitMutating}
                  onReviewSuccess={() => void history.retry()}
                />
              ) : null
            }
          />
        )}
       </div>
      </div>
    </div>
  );
}

/**
 * BL136 4f-iii: the compact, persistently pinned ticket-context band above the Actual Work
 * capture surface. Field-focused — reference / customer / service location / status plus a
 * visually secondary, collapsible Customer Need. No prices, costs, or office-only controls.
 * Reuses the Request List/Detail visual language (tokens, `KeepBadge`, rounded card language).
 */
function TicketContextBand({
  request,
  onExit,
  headingRef,
}: {
  request: KeepRequestDetailResult | undefined;
  onExit: () => void;
  headingRef?: RefObject<HTMLHeadingElement | null>;
}) {
  const [needExpanded, setNeedExpanded] = useState(false);
  const [needClipped, setNeedClipped] = useState(false);
  const needRef = useRef<HTMLParagraphElement>(null);
  // Only offer the expand/collapse control when the two-line clamp actually hides text. Measured
  // against the live layout and re-measured on resize, so a wide viewport that fits the whole
  // need on two lines shows no control.
  useLayoutEffect(() => {
    const el = needRef.current;
    if (!el || needExpanded) return;
    const measure = () => setNeedClipped(el.scrollHeight - el.clientHeight > 1);
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, [request?.description, needExpanded]);

  const address = request
    ? [
        request.serviceAddressLine1,
        request.serviceAddressLine2,
        request.serviceCity && request.serviceState
          ? `${request.serviceCity}, ${request.serviceState}${request.serviceZip ? ` ${request.serviceZip}` : ""}`
          : null,
      ]
        .filter(Boolean)
        .join(", ")
    : "";

  return (
    <div className="shrink-0 border-b border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 md:px-6">
      <div className="mx-auto w-full max-w-4xl">
        <button
          type="button"
          onClick={onExit}
          className="text-sm font-medium text-[var(--keep-accent)] hover:underline rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
        >
          ← Back to Request
        </button>
        <p className="mt-1 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--ophalo-muted)]">
          Actual Work visit
        </p>
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="max-w-full truncate font-serif text-lg font-semibold text-[var(--ophalo-ink)] focus:outline-none"
          >
            {request?.customerName ?? "Actual Work"}
          </h1>
          {request && (
            <KeepBadge variant={statusBadgeVariant(request.status)}>{statusLabel(request.status)}</KeepBadge>
          )}
        </div>
        {request && (
          <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-[var(--ophalo-muted)]">
            <span className="font-medium text-[var(--ophalo-ink)]">{request.referenceCode}</span>
            {address ? (
              <span className="truncate">· {address}</span>
            ) : (
              <span>· Service location not on file</span>
            )}
          </div>
        )}
        {request?.description && (
          <div className="mt-1.5">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">
              Customer need
            </p>
            <p
              ref={needRef}
              className={`whitespace-pre-wrap text-sm leading-6 text-[var(--ophalo-ink)] ${
                needExpanded ? "" : "line-clamp-2"
              }`}
            >
              {request.description}
            </p>
            {(needClipped || needExpanded) && (
              <button
                type="button"
                onClick={() => setNeedExpanded((v) => !v)}
                aria-expanded={needExpanded}
                className="mt-0.5 inline-flex items-center gap-0.5 rounded text-xs font-semibold text-[var(--keep-accent)] hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2"
              >
                <ChevronRight
                  className={`h-3.5 w-3.5 transition-transform ${needExpanded ? "rotate-90" : ""}`}
                />
                {needExpanded ? "Show less" : "Show full customer need"}
              </button>
            )}
          </div>
        )}
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
  officeRegion,
}: {
  visit: NonNullable<ReturnType<ReturnType<typeof useActualWorkWorkspace>["submittedVisit"]>>;
  /** BL136 4f-ii: the capability-gated office region (reused `ActualWorkReviewCard`), rendered
   *  line-adjacent below the visit note. Null for a non-reviewer or a superseded source. */
  officeRegion?: ReactNode;
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

      {/* BL136 4f-ii: the capability-gated office region — the existing `ActualWorkReviewCard`
          (financial resolution / no-charge disposition / review controls / "Correct this visit" /
          real totals), composed here line-adjacent. Hidden for a non-reviewer (or degraded to
          nothing by a 403) and for a superseded source. */}
      {officeRegion}
    </>
  );
}
