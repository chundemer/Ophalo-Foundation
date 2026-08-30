import { forwardRef, useEffect, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Check, ChevronRight, Lock, X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  api,
  ApiError,
  type ActualWorkAddLineBody,
  type ActualWorkHistoryResult,
  type ActualWorkLineHistoryEntry,
  type ActualWorkNudgeSuggestionFieldRowResponse,
  type ActualWorkSubmitBody,
  type ActualWorkSubmittedVisitEntry,
  type ActualWorkUpdateLineBody,
  type FieldScopeSearchResultResponse,
} from "../../lib/apiClient";
import {
  ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE,
  type ActualWorkHandoffOutcome,
} from "./useActualWorkCapture";

/** Mirrors `useActualWorkCapture`'s `setDefaultPerformer` return contract (kept local — the hook
 * declares it inline). `set` unmounts this gate on the parent's refetch; the rest stay in place. */
type SetDefaultPerformerOutcome = "set" | "ineligible" | "stale" | "failed";

/** Mirrors `useActualWorkCapture`'s `setVisitNote` return contract. `set` / `stale` settle through
 * the parent refetch + shared reconcile; `too-long` is surfaced inline under the textarea. */
type SetVisitNoteOutcome = "set" | "too-long" | "stale" | "failed";

/** Mirrors `useActualWorkCapture`'s `setZeroLineDisposition` return contract (BL136 §4e-iii). `set` /
 * `stale` settle through the parent refetch + shared reconcile; `invalid` is surfaced inline (the
 * server rejected the outcome enum value); `failed` keeps the local edit for a retry. */
type SetZeroLineDispositionOutcome = "set" | "invalid" | "stale" | "failed";
import { ConnectionFailureBanner } from "./ConnectionFailureBanner";
import { announcePolite } from "../../lib/liveAnnouncer";

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
  // BL136 4e-iii: this Draft was auto-opened by an Owner/Admin replacement-copy correction in the
  // current session — show contextual guidance that it replaces a superseded visit. UI-only and
  // session-scoped (durable lineage is on the history record); a hard reload clears it.
  replacementCorrection?: boolean;
  conflictNotice: string | null;
  // Slice 4 (2026-08-26): full-bleed workspace below 1001px, right-drawer at/above it — same
  // 1001px threshold RequestDetailContent measures via ResizeObserver. Threaded as a prop rather
  // than a `min-[1001px]:` CSS pair so only one close control (X vs "Back to Request") ever
  // exists in the DOM/accessibility tree at a time, matching Slice 2/3's convention.
  isWide: boolean;
  // BL136 4f-iii: `"modal"` (default) keeps the historical full-bleed / right-drawer `KeepModal`
  // presentation used from Request Detail. `"inline"` renders the same capture surface as a plain
  // in-page region (no overlay, no backdrop, no Escape/focus trap) so the dedicated Actual Work
  // workspace route can show it beneath the persistent Keep top nav and ticket-context band. No
  // capture logic differs between the two.
  presentation?: "modal" | "inline";
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
  submittedVisits?: ActualWorkSubmittedVisitEntry[];
  // 4c-i-c-2 (ADR-494 D2): the caller's own account-user id, used only to render "you" in the
  // performer caption when the Draft's persisted default is the current user and its display name
  // has not yet been resolved by the projection.
  currentAccountUserId?: string;
  // Persists the office-transcription path's selected technician as the Draft's ticket default
  // (recorder-only, Draft-only, existing version protocol). Until it resolves `"set"`, the entire
  // add region — direct add-line, assembly expansion, nudge-accept — stays gated.
  onSetDefaultPerformer: (performerId: string | null) => Promise<SetDefaultPerformerOutcome>;
  // ADR-494 D5 (4c-ii): autosaves the visit-level note on blur (recorder-only, Draft-only, existing
  // version protocol). A `too-long` outcome is surfaced under the textarea; `stale` reconciles
  // through the shared conflict path.
  onSetVisitNote: (visitNote: string | null) => Promise<SetVisitNoteOutcome>;
  // BL136 §4e-iii: autosaves the zero-line disposition (outcome + completion note) on blur once a
  // valid outcome exists (recorder-only, Draft-only, existing version protocol). Durability/reload
  // survival only — the final `Submit` still sends the local fields. `invalid` is surfaced inline;
  // `stale` reconciles through the shared conflict path.
  onSetZeroLineDisposition: (
    outcome: string,
    completionNote: string | null,
  ) => Promise<SetZeroLineDispositionOutcome>;
  // Slice 4d: the current recorder hands their own unsubmitted Draft to a chosen office member
  // (the `transfer-recorder` endpoint with the reason omitted). On `"handed-off"` / `"stale"` the
  // composer is already closing; `"ineligible"` / `"failed"` keep the picker open for a retry.
  onHandOffToOffice: (newRecorderAccountUserId: string) => Promise<ActualWorkHandoffOutcome>;
}

/**
 * Batch 5b, build-log/129: the field capture composer for Direct Actual Work — mirrors
 * ProposedScopeComposer's shell/mount pattern, simplified to this feature's shape (no Undo — a
 * submitted visit is immediately immutable, and the pilot has no cross-user takeover to reconcile
 * against). A catalog-backed or off-catalog line is added directly against the open Draft;
 * quantity/note are the only editable fields; the zero-line submit path requires a truthful
 * outcome and non-blank completion note (ActualWork.Submit, build-log/129) — a submit with at
 * least one line accepts both as optional. Assembly expansion (5d-i-b) and Paired Nudges
 * (5d-ii-d) live inline in `ActualWorkSearchAndAdd` below.
 */
export function ActualWorkComposer({
  draft,
  replacementCorrection = false,
  conflictNotice,
  isWide,
  presentation = "modal",
  onClose,
  onCommitted,
  onConflict,
  onDismissNotice,
  onRetryReconciliation,
  onSubmitted,
  onDiscarded,
  submittedVisits = [],
  currentAccountUserId,
  onSetDefaultPerformer,
  onSetVisitNote,
  onSetZeroLineDisposition,
  onHandOffToOffice,
}: ActualWorkComposerProps) {
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [submitted, setSubmitted] = useState(false);
  const readOnly = submitted || draft.status !== "Draft";
  // ADR-494 D2: no line-creation route opens until the Draft carries a ticket-default performer.
  // "Record my work" seeds it at create time; "Transcribe work" leaves it null and the gate below
  // collects + persists one first (and stays gated across a reload until the projection confirms).
  const needsPerformer = !readOnly && !draft.defaultPerformedByAccountUserId;

  // Slice 5a: one composer-level connection-failure recovery point rather than six inline ones —
  // a later failure replaces the earlier one, since only one operation's recovery is ever pending
  // at a time. `retry` re-invokes the exact failed operation, captured with its original arguments.
  const [connectionFailure, setConnectionFailure] = useState<{ message: string; retry: () => void } | null>(null);
  const [isRetryingConnectionFailure, setIsRetryingConnectionFailure] = useState(false);

  function reportConnectionFailure(message: string, retry: () => void) {
    setIsRetryingConnectionFailure(false);
    setConnectionFailure({ message, retry });
  }

  function clearConnectionFailure() {
    // Only a retry-driven recovery is announced — an unrelated mutation's ordinary first-attempt
    // success also clears a stale banner (Slice 5a's "any other mutation's success" rule) but
    // that isn't the operator's retry succeeding, so it stays silent. Announced via the
    // root-mounted live region (`liveAnnouncer.ts`), not local state: a successful submit retry
    // closes this composer (`onSubmitted`) in the same commit, so a local `role="status"` region
    // would never reach the DOM.
    if (connectionFailure && isRetryingConnectionFailure) announcePolite("Retry succeeded.");
    setConnectionFailure(null);
    setIsRetryingConnectionFailure(false);
  }

  function retryConnectionFailure() {
    if (!connectionFailure) return;
    setIsRetryingConnectionFailure(true);
    connectionFailure.retry();
  }

  // Discard is a deliberately destructive action: the trigger is a visible danger-outline button
  // (see below) and the actual mutation only fires from the nested confirmation alertdialog. The
  // dialog mirrors CatalogItemEditDrawer's inline discard-confirm — a capture-phase key handler
  // owns Escape and Tab-wrapping between its two buttons so KeepModal's own traps don't reach the
  // backgrounded composer while it is up.
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  const discardTriggerRef = useRef<HTMLButtonElement>(null);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardConfirmRef = useRef<HTMLButtonElement>(null);

  const discardMutation = useMutation({
    mutationFn: () => api.discardActualWork(draft.id, draft.concurrencyVersion),
    onSuccess: onDiscarded,
    onError: () => onConflict(),
  });

  useEffect(() => {
    if (!showDiscardConfirm) return;
    keepEditingRef.current?.focus();
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        if (!discardMutation.isPending) setShowDiscardConfirm(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = keepEditingRef.current;
      const last = discardConfirmRef.current;
      if (!first || !last) return;
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      discardTriggerRef.current?.focus();
    };
  }, [showDiscardConfirm, discardMutation.isPending]);

  const composerBody = (
    <>
      <div
        className={`px-4 py-4 border-b border-[var(--ophalo-border)] shrink-0 ${
          isWide || presentation === "inline" ? "" : "pt-[max(1rem,env(safe-area-inset-top))]"
        }`}
      >
       <div className="min-[1001px]:mx-auto min-[1001px]:max-w-3xl">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--ophalo-muted)]">Work execution manager</p>
            <h2 id="actual-work-composer-heading" className="mt-1 font-serif text-xl font-semibold text-[var(--ophalo-ink)]">
          Record completed work
            </h2>
          </div>
          {/* BL136 4f-iii: inline (workspace-route) presentation delegates "Back to Request" to
              the page's ticket-context band, so the composer header carries no close control. */}
          {presentation === "inline" ? null : isWide ? (
            <button
            type="button"
            onClick={onClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
            >
              <X className="h-4 w-4" />
              <span className="sr-only">Close</span>
            </button>
          ) : (
            <button
            type="button"
            onClick={onClose}
            className={`shrink-0 text-sm font-medium text-[var(--keep-accent)] hover:underline rounded ${FOCUS_RING}`}
            >
              ← Back to Request
            </button>
          )}
        </div>
        <div className="mt-2 flex items-center justify-between gap-2">
          <p className="text-xs text-[var(--ophalo-muted)]">Changes are saved automatically.</p>
          <span className="inline-flex items-center gap-1 rounded-full border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-2 py-0.5 text-[11px] font-semibold text-[var(--ophalo-success)]"><Check className="h-3 w-3" /> Auto-saved</span>
        </div>
       </div>
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto px-4 py-3">
       <div className="space-y-4 min-[1001px]:mx-auto min-[1001px]:max-w-3xl">
        {replacementCorrection && (
          <div
            role="status"
            className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)]"
          >
            This draft replaces a superseded visit. Review the copied work and submit when it is correct.
          </div>
        )}

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

        {connectionFailure && (
          <ConnectionFailureBanner
            message={connectionFailure.message}
            onRetry={retryConnectionFailure}
            isRetrying={isRetryingConnectionFailure}
          />
        )}

        <section className="rounded-xl border border-sky-200 bg-sky-50/55 p-3 space-y-3">
          <div className="flex items-center justify-between gap-2">
            <div><h3 className="text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]">Active visit draft</h3><p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Editable work for this visit</p></div>
            <span className="rounded border border-sky-300 bg-white px-2 py-0.5 text-xs font-semibold text-sky-800">Editable</span>
          </div>
          {needsPerformer && (
            <ActualWorkPerformerGate onSetDefaultPerformer={onSetDefaultPerformer} />
          )}
          {!readOnly && !needsPerformer && (
            <>
              <ActualWorkPerformerCaption
                name={draft.defaultPerformerDisplayName ?? null}
                isSelf={
                  !!currentAccountUserId &&
                  draft.defaultPerformedByAccountUserId === currentAccountUserId
                }
              />
              <ActualWorkSearchAndAdd
                ref={searchInputRef}
                actualWorkId={draft.id}
                version={draft.concurrencyVersion}
                defaultPerformerName={draft.defaultPerformerDisplayName ?? null}
                onCommitted={onCommitted}
                onConflict={onConflict}
                onConnectionFailure={reportConnectionFailure}
                onConnectionRecovered={clearConnectionFailure}
              />
            </>
          )}
        <div className="space-y-2">
          {draft.lines.length === 0 &&
            (readOnly || needsPerformer ? (
              <p className="text-xs text-[var(--ophalo-muted)]">No items added yet.</p>
            ) : (
              <div className="rounded-lg border border-dashed border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5 text-xs text-[var(--ophalo-muted)]">
                <p className="font-semibold text-[var(--ophalo-ink)]">No items added yet.</p>
                <p className="mt-0.5">
                  Add line items with the search above, or record a zero-line outcome
                  (diagnostic only, no work authorized, or no access) in the submit area below.
                </p>
              </div>
            ))}
          {draft.lines.map((line) => (
            <ActualWorkDraftLine
              key={line.id}
              line={line}
              readOnly={readOnly}
              actualWorkId={draft.id}
              version={draft.concurrencyVersion}
              onCommitted={onCommitted}
              onConflict={onConflict}
              onConnectionFailure={reportConnectionFailure}
              onConnectionRecovered={clearConnectionFailure}
            />
          ))}
        </div>

        {!readOnly && (
          <ActualWorkVisitNoteField
            key={draft.visitNote ?? ""}
            initialValue={draft.visitNote ?? ""}
            onSetVisitNote={onSetVisitNote}
          />
        )}

        {!readOnly && (
          <ActualWorkHandoffControl
            currentAccountUserId={currentAccountUserId}
            onHandOffToOffice={onHandOffToOffice}
          />
        )}

        {!readOnly && (
          <div className="mt-3 flex items-center justify-between gap-3 border-t border-[var(--ophalo-border)] pt-3">
            <span className="text-[11px] text-[var(--ophalo-muted)]">Removes this unfinished visit and its recorded work.</span>
            <button
              ref={discardTriggerRef}
              type="button"
              disabled={discardMutation.isPending}
              onClick={() => setShowDiscardConfirm(true)}
              className={`inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-[var(--ophalo-danger)] px-3 py-1.5 text-xs font-semibold text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger-bg)] disabled:opacity-50 ${FOCUS_RING}`}
            >
              <X className="h-3.5 w-3.5" />
              Discard this visit
            </button>
          </div>
        )}</section>
        <SubmittedVisits visits={submittedVisits} />
       </div>
      </div>

      <ActualWorkSubmitFooter
        key={`${draft.outcome ?? ""}|${draft.completionNote ?? ""}`}
        draft={draft}
        submitted={submitted}
        isWide={isWide}
        onSaveDraft={onClose}
        onSetZeroLineDisposition={onSetZeroLineDisposition}
        onConflict={onConflict}
        onConnectionFailure={reportConnectionFailure}
        onConnectionRecovered={clearConnectionFailure}
        onSubmitted={() => {
          setSubmitted(true);
          onSubmitted();
        }}
      />

      {showDiscardConfirm && (
        <div
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="actual-work-discard-confirm-heading"
          aria-describedby="actual-work-discard-confirm-body"
          className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
        >
          <div className="max-w-sm w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
            <h3
              id="actual-work-discard-confirm-heading"
              className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]"
            >
              Discard this visit?
            </h3>
            <p id="actual-work-discard-confirm-body" className="text-sm text-[var(--ophalo-muted)]">
              This permanently removes this unfinished visit and its recorded work.
            </p>
            <div className="flex items-center justify-end gap-3">
              <button
                ref={keepEditingRef}
                type="button"
                disabled={discardMutation.isPending}
                onClick={() => setShowDiscardConfirm(false)}
                className={`text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded disabled:opacity-50 ${FOCUS_RING}`}
              >
                Keep editing
              </button>
              <button
                ref={discardConfirmRef}
                type="button"
                disabled={discardMutation.isPending}
                onClick={() => discardMutation.mutate()}
                className={`px-3 py-1.5 rounded-lg text-sm font-semibold bg-[var(--ophalo-danger)] text-white hover:opacity-90 disabled:opacity-50 ${FOCUS_RING}`}
              >
                Discard visit
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );

  if (presentation === "inline") {
    return (
      <section
        aria-labelledby="actual-work-composer-heading"
        className="relative flex min-h-0 flex-1 flex-col bg-[var(--ophalo-card)]"
      >
        {composerBody}
      </section>
    );
  }

  return (
    <KeepModal
      onClose={onClose}
      labelledBy="actual-work-composer-heading"
      initialFocus={searchInputRef}
      overlayClassName="flex justify-end"
      backdropClassName="bg-slate-950/35 backdrop-blur-[1px]"
      panelClassName={
        isWide
          ? "fixed inset-y-0 right-0 h-[100dvh] w-full max-w-[420px] flex flex-col bg-[var(--ophalo-card)] " +
            "border-l border-[var(--ophalo-border)] shadow-2xl"
          : "fixed inset-0 h-[100dvh] w-full flex flex-col bg-[var(--ophalo-card)]"
      }
    >
      {composerBody}
    </KeepModal>
  );
}

function SubmittedVisits({ visits }: { visits: ActualWorkSubmittedVisitEntry[] }) {
  if (visits.length === 0) return null;
  return <section className="border-t border-[var(--ophalo-border)] pt-4"><div className="mb-2 flex items-center justify-between"><h3 className="text-xs font-bold uppercase tracking-wide text-[var(--ophalo-muted)]">Submitted visits (locked)</h3><span className="text-[11px] text-[var(--ophalo-muted)]">Read-only audit record</span></div><div className="space-y-2">{visits.map((visit, index) => <details key={visit.id} className="group rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)]"><summary className={`flex cursor-pointer list-none items-center justify-between gap-2 px-3 py-2 text-xs font-medium text-[var(--ophalo-ink)] ${FOCUS_RING}`}><span className="flex items-center gap-2"><Lock className="h-3.5 w-3.5 text-[var(--ophalo-muted)]" />Visit #{visits.length - index} · {visit.submittedAtUtc ? new Date(visit.submittedAtUtc).toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" }) : "Submitted"}<span className="rounded bg-slate-200 px-1.5 py-0.5 text-[10px]">{visit.lines.length} item{visit.lines.length === 1 ? "" : "s"}</span></span><ChevronRight className="h-3.5 w-3.5 transition-transform group-open:rotate-90" /></summary><div className="border-t border-[var(--ophalo-border)] px-3 py-2 space-y-1">{visit.visitNote ? <p className="text-xs text-[var(--ophalo-muted)]"><span className="font-semibold text-[var(--ophalo-ink)]">Visit note:</span> {visit.visitNote}</p> : null}{visit.lines.map((line) => <p key={line.id} className="text-xs text-[var(--ophalo-muted)]">{line.displayNameSnapshot} — {line.actualQuantity} {line.unitOfMeasureSnapshot ?? ""} · Performed by {line.performerDisplayName ?? "Unknown performer"}</p>)}</div></details>)}</div></section>;
}

/** Slice 4d: a field recorder hands their own unsubmitted Draft to a chosen office member. The
 * trigger is a subordinate button; the actual transfer only fires from the nested confirmation
 * alertdialog (mirrors the discard-confirm pattern — capture-phase Escape + Tab-wrap between the
 * two buttons, focus returns to the trigger on close). The candidate list is the same
 * `performer-candidates` read the office-transcription gate uses (recorder-callable; identical
 * eligibility predicate to a recorder), minus the caller. `"handed-off"` / `"stale"` close the
 * composer from the parent, so only the recoverable outcomes update local state. */
function ActualWorkHandoffControl({
  currentAccountUserId,
  onHandOffToOffice,
}: {
  currentAccountUserId?: string;
  onHandOffToOffice: (newRecorderAccountUserId: string) => Promise<ActualWorkHandoffOutcome>;
}) {
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);

  const candidatesQuery = useQuery({
    queryKey: ["actualWorkPerformerCandidates"],
    queryFn: () => api.getActualWorkPerformerCandidates(),
    enabled: open,
  });
  const candidates = (candidatesQuery.data?.candidates ?? []).filter(
    (candidate) => candidate.accountUserId !== currentAccountUserId,
  );

  useEffect(() => {
    if (!open) return;
    cancelRef.current?.focus();
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        if (!submitting) setOpen(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = cancelRef.current;
      const last = confirmRef.current;
      if (!first || !last) return;
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      triggerRef.current?.focus();
    };
  }, [open, submitting]);

  async function submit() {
    if (!selected) return;
    setSubmitting(true);
    setError(null);
    const outcome = await onHandOffToOffice(selected);
    setSubmitting(false);
    if (outcome === "handed-off" || outcome === "stale") return; // parent closes the composer
    if (outcome === "ineligible") {
      setError("That team member can't take over this visit. Pick someone else.");
      setSelected("");
      void candidatesQuery.refetch();
      return;
    }
    setError("Couldn't hand off this visit. Check your connection and try again.");
  }

  return (
    <div className="pt-1">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => {
          setOpen(true);
          setError(null);
          setSelected("");
        }}
        className={`inline-flex items-center gap-1.5 rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-xs font-semibold text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
      >
        Hand off to office
      </button>
      {open && (
        <div
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="actual-work-handoff-heading"
          aria-describedby="actual-work-handoff-body"
          className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
        >
          <div className="max-w-sm w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
            <h3
              id="actual-work-handoff-heading"
              className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]"
            >
              Hand off to office
            </h3>
            <p id="actual-work-handoff-body" className="text-sm text-[var(--ophalo-muted)]">
              The office takes over recording this visit. The work you have already recorded stays on it.
            </p>
            {candidatesQuery.isLoading && (
              <p className="text-sm text-[var(--ophalo-muted)]">Loading team members…</p>
            )}
            {candidatesQuery.isError && (
              <p className="text-sm text-[var(--ophalo-danger)]">
                Couldn&apos;t load team members.{" "}
                <button type="button" className="underline" onClick={() => void candidatesQuery.refetch()}>
                  Retry
                </button>
              </p>
            )}
            {candidatesQuery.isSuccess && candidates.length === 0 && (
              <p className="text-sm text-[var(--ophalo-muted)]">
                No one else on the team can take over this visit.
              </p>
            )}
            {candidatesQuery.isSuccess && candidates.length > 0 && (
              <label className="flex flex-col gap-1 text-sm">
                <span className="font-medium text-[var(--ophalo-ink)]">Hand off to</span>
                <select
                  className={INPUT_CLS}
                  value={selected}
                  onChange={(e) => setSelected(e.target.value)}
                >
                  <option value="">Select a team member…</option>
                  {candidates.map((candidate) => (
                    <option key={candidate.accountUserId} value={candidate.accountUserId}>
                      {candidate.displayName}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {error && <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>}
            <div className="flex items-center justify-end gap-3">
              <button
                ref={cancelRef}
                type="button"
                disabled={submitting}
                onClick={() => setOpen(false)}
                className={`text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded disabled:opacity-50 ${FOCUS_RING}`}
              >
                Keep editing
              </button>
              <button
                ref={confirmRef}
                type="button"
                disabled={submitting || !selected}
                onClick={() => void submit()}
                className={`px-3 py-1.5 rounded-lg text-sm font-semibold bg-[var(--keep-accent)] text-white hover:opacity-90 disabled:opacity-50 ${FOCUS_RING}`}
              >
                {submitting ? "Handing off…" : "Hand off"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/** ADR-494 D2: the office-transcription entry point. No add-line / assembly / nudge affordance is
 * mounted while this is showing — the caller must pick the technician the paper ticket belongs to
 * and persist it as the Draft's ticket default first. On a `"set"` outcome the parent refetches and
 * this subtree unmounts; every other outcome keeps the selector in place. */
function ActualWorkPerformerGate({
  onSetDefaultPerformer,
}: {
  onSetDefaultPerformer: (performerId: string | null) => Promise<SetDefaultPerformerOutcome>;
}) {
  const [selected, setSelected] = useState("");
  const [status, setStatus] = useState<"idle" | "saving" | "ineligible" | "stale" | "failed">("idle");

  const { data, isLoading } = useQuery({
    queryKey: ["actualWorkPerformerCandidates"],
    queryFn: () => api.getActualWorkPerformerCandidates(),
  });
  const candidates = data?.candidates ?? [];

  async function confirm() {
    if (!selected || status === "saving") return;
    setStatus("saving");
    const outcome = await onSetDefaultPerformer(selected);
    setStatus(outcome === "set" ? "idle" : outcome);
  }

  const message =
    status === "ineligible"
      ? "That person can't be recorded as the performer."
      : status === "stale"
        ? "This draft changed elsewhere — reopen it to continue."
        : status === "failed"
          ? "Couldn't save. Try again."
          : null;

  return (
    <div className="rounded-lg border border-[var(--ophalo-border)] p-3 space-y-2">
      <div>
        <p className="flex items-center gap-1 text-sm font-medium text-[var(--ophalo-ink)]">
          Whose work is this?
          <span aria-hidden="true" className="text-[var(--ophalo-danger)]">*</span>
        </p>
        <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">
          Required — add items after you pick the technician this ticket belongs to.
        </p>
      </div>
      <select
        value={selected}
        onChange={(e) => setSelected(e.target.value)}
        disabled={isLoading || status === "saving"}
        aria-label="Technician"
        className={INPUT_CLS}
      >
        <option value="">{isLoading ? "Loading…" : "Select a technician"}</option>
        {candidates.map((candidate) => (
          <option key={candidate.accountUserId} value={candidate.accountUserId}>
            {candidate.displayName} — {candidate.role}
          </option>
        ))}
      </select>
      {message && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{message}</p>}
      <div className="min-[1001px]:flex min-[1001px]:justify-end">
        <KeepButton
          variant="teal"
          disabled={!selected || status === "saving"}
          onClick={() => void confirm()}
          className="w-full min-[1001px]:w-auto min-[1001px]:px-6"
        >
          Confirm technician
        </KeepButton>
      </div>
    </div>
  );
}

/** ADR-494 D5 (4c-ii): the visit-level note. Autosaves on blur through the composer's established
 * automatic-save + conflict-reconciliation path (no explicit Save control) — the parent remounts
 * this via `key={draft.visitNote}` after each successful write, so the field always reflects the
 * server's trim and survives a reload. A `too-long` outcome is the only inline error; `stale` /
 * `failed` are handled by the shared reconcile path in the hook. */
function ActualWorkVisitNoteField({
  initialValue,
  onSetVisitNote,
}: {
  initialValue: string;
  onSetVisitNote: (visitNote: string | null) => Promise<SetVisitNoteOutcome>;
}) {
  const [value, setValue] = useState(initialValue);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

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

/** One-line attribution above the add region once a ticket default exists: the resolved performer
 * name from the projection, or "you" when the default is the current user and the name has not been
 * resolved yet (the optimistic "Record my work" create carries no display name). */
function ActualWorkPerformerCaption({ name, isSelf }: { name: string | null; isSelf: boolean }) {
  const label = name ?? (isSelf ? "you" : null);
  if (!label) return null;
  return (
    <p className="text-xs text-[var(--ophalo-muted)]">
      Recording work for <span className="font-medium text-[var(--ophalo-ink)]">{label}</span>
    </p>
  );
}

interface ActualWorkSearchAndAddProps {
  actualWorkId: string;
  version: string;
  // ADR-494 D2 (4c-iii): the resolved name of the Draft's ticket-default performer, shown as the
  // default option in the add panel's performer picker. A new line inherits the ticket default
  // unless the recorder picks a different technician; existing lines cannot be re-attributed (no
  // backend route).
  defaultPerformerName: string | null;
  onCommitted: () => Promise<void>;
  onConflict: (message?: string) => void;
  onConnectionFailure: (message: string, retry: () => void) => void;
  onConnectionRecovered: () => void;
}

type Selection = { kind: "catalog"; item: FieldScopeSearchResultResponse } | { kind: "custom" };

const ActualWorkSearchAndAdd = forwardRef<HTMLInputElement, ActualWorkSearchAndAddProps>(function ActualWorkSearchAndAdd(
  {
    actualWorkId,
    version,
    defaultPerformerName,
    onCommitted,
    onConflict,
    onConnectionFailure,
    onConnectionRecovered,
  },
  ref,
) {
  const [searchText, setSearchText] = useState("");
  const [debouncedText, setDebouncedText] = useState("");
  const [selection, setSelection] = useState<Selection | null>(null);
  const [customDescription, setCustomDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [note, setNote] = useState("");
  // "" => inherit the Draft's ticket default; any other value is an explicit per-line override.
  const [performerId, setPerformerId] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: performerCandidates } = useQuery({
    queryKey: ["actualWorkPerformerCandidates"],
    queryFn: () => api.getActualWorkPerformerCandidates(),
    enabled: selection !== null,
  });

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
  const assemblyResults = (results?.items ?? []).filter((item) => item.kind === "OfferingAssembly");
  const [expansionNotice, setExpansionNotice] = useState<string | null>(null);

  // Build Log 129, 5d-ii-d: session-only Paired Nudges state, mirroring
  // useProposedScopeCapture's nudge shape (build-log/125) but kept inline here since this
  // composer's mutation logic already lives in this component rather than an extracted hook.
  // Retirement and the read generation live in refs so they reflect the latest value inside async
  // continuations without waiting for a re-render.
  const [nudge, setNudge] = useState<{ ruleId: string; suggestions: ActualWorkNudgeSuggestionFieldRowResponse[] } | null>(null);
  const retiredRuleIdsRef = useRef<Set<string>>(new Set());
  const nudgeGenerationRef = useRef(0);

  async function fetchNudge(trigger: { triggerCatalogItemId: string } | { triggerOfferingAssemblyId: string }) {
    const myGeneration = ++nudgeGenerationRef.current;
    try {
      const result = await api.getActualWorkNudgeFieldSuggestions(actualWorkId, trigger);
      if (myGeneration !== nudgeGenerationRef.current) return;
      if (result.ruleId && result.suggestions.length > 0 && !retiredRuleIdsRef.current.has(result.ruleId)) {
        setNudge({ ruleId: result.ruleId, suggestions: result.suggestions });
      }
    } catch {
      // Silent by design (build-log/125 precedent): a nudge-read failure never surfaces to the technician.
    }
  }

  function resetAfterSuccess() {
    setError(null);
    setSelection(null);
    setSearchText("");
    setDebouncedText("");
    setCustomDescription("");
    setQuantity("1");
    setNote("");
    setPerformerId("");
  }

  // The mutation takes an explicit, click-time snapshot of the payload/trigger rather than reading
  // `selection`/`quantity`/`note`/`customDescription` state — a technician can edit those fields
  // after a connection failure before pressing Retry, and the retry closure must replay the exact
  // operation that failed, not whatever the fields currently hold.
  type AddLineVariables = { body: ActualWorkAddLineBody; trigger: { triggerCatalogItemId: string } | null };

  const addMutation = useMutation({
    mutationFn: (variables: AddLineVariables) => api.addActualWorkLine(actualWorkId, variables.body, version),
    onSuccess: async (_data, variables) => {
      resetAfterSuccess();
      onConnectionRecovered();
      await onCommitted();
      if (variables.trigger) void fetchNudge(variables.trigger);
    },
    onError: (err, variables) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't add actual work.", () => addMutation.mutate(variables));
        return;
      }
      if (err.status === 409) {
        onConflict();
        return;
      }
      // 400 (validation) and 422 (`ActualWork.PerformerIneligible` — a stale per-line performer
      // pick) both surface inline so the recorder can correct the field without a reconcile churn.
      if (err.status !== 400 && err.status !== 422) {
        onConflict();
        return;
      }
      setError(err.message);
    },
  });

  const expandAssemblyMutation = useMutation({
    mutationFn: (assembly: FieldScopeSearchResultResponse) =>
      api.expandActualWorkAssembly(actualWorkId, { offeringAssemblyId: assembly.id, includedOptionalItemIds: [] }, version),
    onSuccess: async (result, assembly) => {
      setExpansionNotice(
        result.skippedCatalogItemIds.length === 0
          ? `${result.lineIds.length} assembly item${result.lineIds.length === 1 ? "" : "s"} added.`
          : `${result.lineIds.length} assembly item${result.lineIds.length === 1 ? "" : "s"} added; ${result.skippedCatalogItemIds.length} already on this visit.`,
      );
      setError(null);
      onConnectionRecovered();
      await onCommitted();
      void fetchNudge({ triggerOfferingAssemblyId: assembly.id });
    },
    onError: (err, assembly) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't add assembly items.", () => expandAssemblyMutation.mutate(assembly));
        return;
      }
      if (err.status !== 400) {
        onConflict();
        return;
      }
      setError(err.message);
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
            <div className="rounded-lg border border-[var(--ophalo-border)] p-1">
              {isLoading && <p className="px-3 py-2 text-xs text-[var(--ophalo-muted)]">Searching...</p>}
              {!isLoading && (
                <ul className="max-h-48 overflow-y-auto space-y-1">
                  {assemblyResults.length > 0 && (
                    <li className="rounded-lg border-l-4 border-[var(--keep-accent)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]">
                      Matching assemblies
                    </li>
                  )}
                  {assemblyResults.map((item) => (
                    <li key={item.id}>
                      <button
                        type="button"
                        disabled={expandAssemblyMutation.isPending}
                        onClick={() => expandAssemblyMutation.mutate(item)}
                        className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING} disabled:opacity-50`}
                      >
                        <span>{item.displayName}</span>
                        <span className="ml-2 rounded bg-[var(--ophalo-canvas)] px-1.5 py-0.5 text-xs font-medium text-[var(--ophalo-muted)]">
                          Assembly
                        </span>
                        {item.defaultItemCount !== null && (
                          <span className="ml-2 text-xs text-[var(--ophalo-muted)]">Expands {item.defaultItemCount} items</span>
                        )}
                      </button>
                    </li>
                  ))}
                  {catalogResults.length > 0 && (
                    <li className="mt-3 rounded-lg border-l-4 border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]">
                      Matching catalog items
                    </li>
                  )}
                  {catalogResults.map((item) => (
                    <li key={item.id}>
                      <button
                        type="button"
                        onClick={() => setSelection({ kind: "catalog", item })}
                        className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                      >
                        {item.displayName}
                      </button>
                    </li>
                  ))}
                  <li>
                    <button
                      type="button"
                      onClick={() => setSelection({ kind: "custom" })}
                      className={`w-full text-left rounded-lg px-3 py-2 text-sm font-medium text-[var(--keep-accent)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                    >
                      Add as custom item
                    </button>
                  </li>
                </ul>
              )}
            </div>
          )}
          {expansionNotice && <p role="status" className="px-3 py-2 text-xs text-[var(--ophalo-muted)]">{expansionNotice}</p>}
          {error && <p className="px-3 text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
          {nudge && (
            <ActualWorkNudgeChips
              actualWorkId={actualWorkId}
              version={version}
              nudge={nudge}
              onAccepted={async () => {
                retiredRuleIdsRef.current.add(nudge.ruleId);
                setNudge(null);
                await onCommitted();
              }}
              onConflict={(message) => {
                setNudge(null);
                onConflict(message);
              }}
              onConnectionFailure={onConnectionFailure}
              onConnectionRecovered={onConnectionRecovered}
              onDismiss={() => {
                retiredRuleIdsRef.current.add(nudge.ruleId);
                setNudge(null);
              }}
            />
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
          <div className="space-y-1">
            <label htmlFor="actual-work-line-performer" className="text-xs font-medium text-[var(--ophalo-muted)]">
              Performed by
            </label>
            <select
              id="actual-work-line-performer"
              value={performerId}
              onChange={(e) => setPerformerId(e.target.value)}
              className={INPUT_CLS}
            >
              <option value="">
                Ticket default{defaultPerformerName ? ` (${defaultPerformerName})` : ""}
              </option>
              {(performerCandidates?.candidates ?? []).map((c) => (
                <option key={c.accountUserId} value={c.accountUserId}>
                  {c.displayName}
                </option>
              ))}
            </select>
          </div>
          {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
          <div className="flex gap-2">
            <KeepButton
              variant="teal"
              disabled={!canAdd || addMutation.isPending}
              onClick={() => {
                const performer = performerId ? { performedByAccountUserId: performerId } : {};
                const body: ActualWorkAddLineBody =
                  selection?.kind === "catalog"
                    ? { catalogItemId: selection.item.id, actualQuantity: Number(quantity), note: note.trim() || null, ...performer }
                    : { offCatalogDescription: customDescription, actualQuantity: Number(quantity), note: note.trim() || null, ...performer };
                const trigger = selection?.kind === "catalog" ? { triggerCatalogItemId: selection.item.id } : null;
                addMutation.mutate({ body, trigger });
              }}
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

interface ActualWorkNudgeChipsProps {
  actualWorkId: string;
  version: string;
  nudge: { ruleId: string; suggestions: ActualWorkNudgeSuggestionFieldRowResponse[] };
  onAccepted: () => void;
  onConflict: (message?: string) => void;
  onConnectionFailure: (message: string, retry: () => void) => void;
  onConnectionRecovered: () => void;
  onDismiss: () => void;
}

/**
 * Build Log 129, 5d-ii-d: the price-blind "Often added together" chip panel — mirrors
 * ComposerNudgePanel's (build-log/125) UX exactly (session-only chips, client-side Dismiss,
 * default quantity 1/no note/no optional-item inclusions on accept) but stays inline in this file
 * rather than an extracted panel component, matching this composer's established shape. Accepting
 * a chip dispatches the same add-line/expand-assembly mutations the rest of the composer uses;
 * only success retires the panel. A 409 clears the panel without retiring the rule — the caller's
 * `onConflict` handles reconciliation — and any other failure keeps the panel up so the technician
 * can retry.
 */
function ActualWorkNudgeChips({
  actualWorkId,
  version,
  nudge,
  onAccepted,
  onConflict,
  onConnectionFailure,
  onConnectionRecovered,
  onDismiss,
}: ActualWorkNudgeChipsProps) {
  const [error, setError] = useState<string | null>(null);

  const acceptMutation = useMutation({
    mutationFn: (suggestion: ActualWorkNudgeSuggestionFieldRowResponse): Promise<unknown> => {
      if (suggestion.catalogItemId !== null) {
        return api.addActualWorkLine(
          actualWorkId,
          { catalogItemId: suggestion.catalogItemId, actualQuantity: 1, note: null },
          version,
        );
      }
      return api.expandActualWorkAssembly(
        actualWorkId,
        { offeringAssemblyId: suggestion.offeringAssemblyId!, includedOptionalItemIds: [] },
        version,
      );
    },
    onSuccess: () => {
      setError(null);
      onConnectionRecovered();
      onAccepted();
    },
    onError: (err, suggestion) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't add suggested item.", () => acceptMutation.mutate(suggestion));
        return;
      }
      if (err.status === 409) {
        onConflict();
        return;
      }
      setError("Something went wrong. Try again.");
    },
  });

  return (
    <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3 space-y-2">
      <p className="text-xs font-medium text-[var(--ophalo-muted)]">Often added together</p>
      <div className="flex flex-wrap gap-2">
        {nudge.suggestions.map((suggestion) => (
          <button
            key={suggestion.id}
            type="button"
            disabled={acceptMutation.isPending}
            onClick={() => acceptMutation.mutate(suggestion)}
            className={`min-h-[44px] rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-50 ${FOCUS_RING}`}
          >
            {suggestion.displayName}
          </button>
        ))}
        <button
          type="button"
          disabled={acceptMutation.isPending}
          onClick={onDismiss}
          className={`min-h-[44px] rounded-lg px-3 py-2 text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-50 ${FOCUS_RING}`}
        >
          Dismiss
        </button>
      </div>
      {error && (
        <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
          {error}
        </p>
      )}
    </div>
  );
}

interface ActualWorkDraftLineProps {
  line: ActualWorkLineHistoryEntry;
  readOnly: boolean;
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

function ActualWorkDraftLine({
  line,
  readOnly,
  actualWorkId,
  version,
  onCommitted,
  onConflict,
  onConnectionFailure,
  onConnectionRecovered,
}: ActualWorkDraftLineProps) {
  const [isEditing, setIsEditing] = useState(false);
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
    </div>
  );
}

interface ActualWorkSubmitFooterProps {
  draft: ActualWorkDraft;
  submitted: boolean;
  isWide: boolean;
  onSaveDraft: () => void;
  onConflict: (message?: string) => void;
  onConnectionFailure: (message: string, retry: () => void) => void;
  onConnectionRecovered: () => void;
  onSubmitted: () => void;
  onSetZeroLineDisposition: (
    outcome: string,
    completionNote: string | null,
  ) => Promise<SetZeroLineDispositionOutcome>;
}

/** Zero-line submit requires a truthful outcome + non-whitespace completion note
 * (ActualWork.Submit, build-log/129); a submit with at least one line accepts both as optional.
 *
 * BL136 §4e-iii: the zero-line outcome / completion note are prefilled from the Draft
 * (`draft.outcome` / `draft.completionNote` — populated when a replacement copy carried them over)
 * and autosaved on blur through `onSetZeroLineDisposition` once a valid outcome exists, so an edit
 * survives a reload. The parent remounts this via `key={outcome|completionNote}` after each
 * persisted write, so the fields reflect the server's trim. Blur persistence is durability only —
 * the final `Submit` remains the single authoritative write for its own interaction: a blur into
 * Submit skips the disposition write (`submitIntentRef`), and Submit is disabled while an ordinary
 * blur write is still in flight so the two can never issue against the same pre-write version. */
function ActualWorkSubmitFooter({
  draft,
  submitted,
  isWide,
  onSaveDraft,
  onConflict,
  onConnectionFailure,
  onConnectionRecovered,
  onSubmitted,
  onSetZeroLineDisposition,
}: ActualWorkSubmitFooterProps) {
  const [outcome, setOutcome] = useState(draft.outcome ?? "");
  const [completionNote, setCompletionNote] = useState(draft.completionNote ?? "");
  const [error, setError] = useState<string | null>(null);
  const [persisting, setPersisting] = useState(false);
  // Set on the Submit button's pointer-down, which fires before the focused field's blur, so the
  // blur handler can tell "leaving the field for Submit" from ordinary navigation. Cleared whenever
  // a field regains focus. (`relatedTarget` on blur is unreliable under jsdom, so a ref is used.)
  const submitIntentRef = useRef(false);

  const zeroLine = draft.lines.length === 0;

  // Persist only once a valid outcome exists (the route rejects a blank outcome), sending outcome +
  // note together so the server stays authoritative. Both fields are disabled while `persisting`,
  // so the two field writes serialize and a rapid outcome/note edit can't race the version.
  //
  // A blur caused by pressing Submit (`submitIntentRef`, set on the button's pointer-down) is
  // skipped: final Submit is the single authoritative write for that interaction, so starting a
  // disposition write against the same pre-submit version would guarantee a 409 on one of the two.
  // Ordinary field-to-field navigation, Save draft/exit, and leaving the composer still persist.
  function persistOnBlur(nextOutcome: string, nextNote: string) {
    if (submitIntentRef.current) return;
    void persistDisposition(nextOutcome, nextNote);
  }

  async function persistDisposition(nextOutcome: string, nextNote: string) {
    if (nextOutcome === "") return;
    const noteArg = nextNote.trim().length > 0 ? nextNote.trim() : null;
    if (nextOutcome === (draft.outcome ?? "") && noteArg === (draft.completionNote ?? null)) return;
    setPersisting(true);
    setError(null);
    const result = await onSetZeroLineDisposition(nextOutcome, noteArg);
    setPersisting(false);
    if (result === "invalid") setError("The visit outcome is not a valid value.");
  }

  const submitMutation = useMutation({
    mutationFn: (body: ActualWorkSubmitBody) => api.submitActualWork(draft.id, body, draft.concurrencyVersion),
    onSuccess: () => {
      onConnectionRecovered();
      onSubmitted();
    },
    onError: (err, body) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't submit this visit.", () => submitMutation.mutate(body));
        return;
      }
      if (err.status === 400) {
        setError(err.message);
        return;
      }
      onConflict();
    },
  });

  if (submitted) {
    return (
      <div
        className={`px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0 ${
          isWide ? "" : "pb-[max(0.75rem,env(safe-area-inset-bottom))]"
        }`}
      >
        <p role="status" aria-live="polite" className="text-center text-sm font-medium text-[var(--ophalo-ink)]">
          Submitted to office — awaiting review
        </p>
      </div>
    );
  }

  const canSubmit = zeroLine ? outcome !== "" && completionNote.trim().length > 0 : true;

  return (
    <div
      className={`px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0 ${
        isWide ? "" : "pb-[max(0.75rem,env(safe-area-inset-bottom))]"
      }`}
    >
     <div className="space-y-2 min-[1001px]:mx-auto min-[1001px]:max-w-3xl">
      {zeroLine && (
        <div className="space-y-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3">
          <p className="text-xs text-[var(--ophalo-muted)]">
            No line items added — submit a zero-line outcome instead.
          </p>
          <div className="space-y-1">
            <div className="flex items-center gap-1">
              <label
                htmlFor="actual-work-zeroline-outcome"
                className="text-xs font-semibold text-[var(--ophalo-ink)]"
              >
                Visit outcome
              </label>
              <span aria-hidden="true" className="text-[var(--ophalo-danger)]">*</span>
            </div>
            <select
              id="actual-work-zeroline-outcome"
              value={outcome}
              aria-required="true"
              aria-label="Visit outcome"
              onChange={(e) => setOutcome(e.target.value)}
              onFocus={() => { submitIntentRef.current = false; }}
              onBlur={() => persistOnBlur(outcome, completionNote)}
              disabled={persisting || submitMutation.isPending}
              className={INPUT_CLS}
            >
              <option value="">Select outcome...</option>
              {OUTCOME_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <div className="space-y-1">
            <div className="flex items-center gap-1">
              <label
                htmlFor="actual-work-zeroline-note"
                className="text-xs font-semibold text-[var(--ophalo-ink)]"
              >
                Completion note
              </label>
              <span aria-hidden="true" className="text-[var(--ophalo-danger)]">*</span>
            </div>
            <textarea
              id="actual-work-zeroline-note"
              value={completionNote}
              aria-required="true"
              onChange={(e) => setCompletionNote(e.target.value)}
              onFocus={() => { submitIntentRef.current = false; }}
              onBlur={() => persistOnBlur(outcome, completionNote)}
              disabled={persisting || submitMutation.isPending}
              placeholder="Completion note — what happened on this visit"
              className={INPUT_CLS}
              rows={2}
            />
          </div>
          {persisting && <p className="text-xs text-[var(--ophalo-muted)]">Saving…</p>}
        </div>
      )}
      {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
      <div className="grid grid-cols-2 gap-3 min-[1001px]:flex min-[1001px]:justify-end">
        <KeepButton
          variant="secondary"
          onClick={onSaveDraft}
          disabled={submitMutation.isPending}
          className="min-[1001px]:px-6"
        >
          Save draft &amp; exit
        </KeepButton>
        <button
          type="button"
          onPointerDown={() => { submitIntentRef.current = true; }}
          disabled={!canSubmit || submitMutation.isPending || persisting}
          onClick={() => submitMutation.mutate({ outcome: outcome || null, completionNote: completionNote.trim() || null })}
          className={`rounded-lg bg-[var(--keep-accent)] px-3 py-2.5 text-sm font-semibold text-white ${FOCUS_RING} disabled:opacity-50 min-[1001px]:px-6`}
        >
          Submit visit to office
        </button>
      </div>
      {zeroLine && !canSubmit && (
        <p className="text-center text-xs text-[var(--ophalo-muted)] min-[1001px]:text-right">
          Select an outcome and add a completion note, or add at least one item, before submitting.
        </p>
      )}
     </div>
    </div>
  );
}
