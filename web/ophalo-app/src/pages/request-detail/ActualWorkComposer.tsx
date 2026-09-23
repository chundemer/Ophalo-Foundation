import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Check, Plus, X } from "lucide-react";
import { KeepModal } from "../../components/keep/KeepModal";
import {
  api,
  type ActualWorkHistoryResult,
  type ActualWorkSubmittedVisitEntry,
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
import { ActualWorkItemPickerDrawer } from "./ActualWorkItemPickerDrawer";
import { announcePolite } from "../../lib/liveAnnouncer";
import { ActualWorkSearchAndAdd } from "./ActualWorkSearchAndAdd";
import { ActualWorkDraftLine } from "./ActualWorkDraftLine";
import { SubmittedVisits } from "./SubmittedVisits";
import { ActualWorkPerformerGate } from "./ActualWorkPerformerGate";
import { ActualWorkVisitNoteField } from "./ActualWorkVisitNoteField";
import { ActualWorkPerformerCaption, ActualWorkPerformerSummary } from "./ActualWorkPerformerSummary";
import { ActualWorkSubmitFooter } from "./ActualWorkSubmitFooter";

type ActualWorkDraft = NonNullable<ActualWorkHistoryResult["openDraft"]>;

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

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
  // Maintainability review item 2.1 Group B: account business timezone for the locked
  // submitted-visits section's timestamps, so they render in the account's zone rather than the
  // viewer's device zone.
  timeZone?: string | null;
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
  onHandOffToOffice?: (newRecorderAccountUserId: string) => Promise<ActualWorkHandoffOutcome>;
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
  timeZone = null,
  currentAccountUserId,
  onSetDefaultPerformer,
  onSetVisitNote,
  onSetZeroLineDisposition,
}: ActualWorkComposerProps) {
  const searchInputRef = useRef<HTMLInputElement>(null);
  const [submitted, setSubmitted] = useState(false);
  const readOnly = submitted || draft.status !== "Draft";
  // BL136 large-ticket density: the workspace-route ("inline") presentation gets a compact
  // desktop treatment (dense line rows, performer summary, collapsed visit-note / empty-draft
  // affordances). The Request Detail modal presentation is unchanged.
  const inline = presentation === "inline";
  // ADR-494 D2: no line-creation route opens until the Draft carries a ticket-default performer.
  // "Record my work" seeds it at create time; "Transcribe work" leaves it null and the gate below
  // collects + persists one first (and stays gated across a reload until the projection confirms).
  const needsPerformer = !readOnly && !draft.defaultPerformedByAccountUserId;
  // ADR-494 D2: changing the ticket-default performer after confirmation re-opens the same
  // explicit gate (no auto-save, no line entry until re-confirmed).
  const [changingPerformer, setChangingPerformer] = useState(false);
  async function handleSetDefaultPerformer(performerId: string | null) {
    const outcome = await onSetDefaultPerformer(performerId);
    if (outcome === "set") setChangingPerformer(false);
    return outcome;
  }

  // BL136 large-ticket density: on the inline workspace an empty draft is an explicit mode switch,
  // not a screen that renders every path at once. "neutral" offers the two choices; "work" gives
  // search/results priority; "zero-line" shows the outcome/completion-note form. A reopened draft
  // that already carries a persisted zero-line outcome starts in "zero-line". Modes only matter
  // while the draft has zero lines — any line present collapses the whole apparatus.
  const [emptyDraftMode, setEmptyDraftMode] = useState<"neutral" | "work" | "zero-line">(
    draft.outcome ? "zero-line" : "neutral",
  );
  // Removing the last recorded line returns the empty-draft UI to the intelligible neutral choice
  // rather than leaving a stale work/zero-line surface with nothing in it.
  const prevLineCountRef = useRef(draft.lines.length);
  useEffect(() => {
    if (prevLineCountRef.current > 0 && draft.lines.length === 0) setEmptyDraftMode("neutral");
    prevLineCountRef.current = draft.lines.length;
  }, [draft.lines.length]);

  const isEmptyDraftInline =
    inline && !readOnly && !needsPerformer && !changingPerformer && draft.lines.length === 0;

  // BL136 4f-v: the inline (workspace-route) presentation moves search + catalog results + the
  // custom-item path into a dedicated right-side drawer that stays open for multi-add, instead of
  // an inline dropdown. Opening it from the neutral empty-draft choice commits to "work" mode so a
  // close with nothing added lands on the zero-line escape hatch, not the two-choice card.
  const [pickerOpen, setPickerOpen] = useState(false);
  function openPicker() {
    if (isEmptyDraftInline) setEmptyDraftMode("work");
    setPickerOpen(true);
  }
  // The drawer must not survive a transition into a state where line entry is disallowed or the
  // recorded-line surface it was opened over no longer exists: the performer gate opening, the
  // draft going read-only/submitted, or the last recorded line being removed all force it closed.
  const pickerAllowed = inline && !readOnly && !needsPerformer && !changingPerformer;
  useEffect(() => {
    if (!pickerAllowed) setPickerOpen(false);
  }, [pickerAllowed]);
  const prevPickerLineCountRef = useRef(draft.lines.length);
  useEffect(() => {
    if (prevPickerLineCountRef.current > 0 && draft.lines.length === 0) setPickerOpen(false);
    prevPickerLineCountRef.current = draft.lines.length;
  }, [draft.lines.length]);

  function focusZeroLineOutcome() {
    // The footer stays mounted across mode toggles; give React a frame to render the fields.
    requestAnimationFrame(() => {
      const el = document.getElementById("actual-work-zeroline-outcome");
      el?.scrollIntoView({ block: "center" });
      (el as HTMLElement | null)?.focus();
    });
  }

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

  // Search + catalog/assembly results + custom-item path. The modal (non-inline) presentation
  // renders this directly in the composer; the inline (workspace-route) presentation hosts the
  // same element inside `ActualWorkItemPickerDrawer` (BL136 4f-v).
  const searchAndAdd = (
    <ActualWorkSearchAndAdd
      ref={searchInputRef}
      actualWorkId={draft.id}
      version={draft.concurrencyVersion}
      defaultPerformerName={draft.defaultPerformerDisplayName ?? null}
      onCommitted={onCommitted}
      onConflict={onConflict}
      onConnectionFailure={reportConnectionFailure}
      onConnectionRecovered={clearConnectionFailure}
      // Only the inline presentation hosts this inside `ActualWorkItemPickerDrawer`, where a first
      // Escape should dismiss the open result list before the drawer closes. The modal composer
      // keeps its existing one-Escape-to-close behavior (BL136 4f-v: modal path unchanged).
      dismissResultsOnEscape={inline}
    />
  );

  const composerBody = (
    <>
      <div
        className={`px-4 py-4 border-b border-[var(--ophalo-border)] shrink-0 ${
          isWide || presentation === "inline" ? "" : "pt-[max(1rem,env(safe-area-inset-top))]"
        } ${inline ? "sr-only" : ""}`}
      >
       <div className="min-[1001px]:mx-auto min-[1001px]:max-w-[1000px]">
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
       <div className={`space-y-4 min-[1001px]:mx-auto ${inline ? "min-[1001px]:max-w-[1440px]" : "min-[1001px]:max-w-[1000px]"}`}>
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

        {connectionFailure && !(inline && pickerOpen) && (
          <ConnectionFailureBanner
            message={connectionFailure.message}
            onRetry={retryConnectionFailure}
            isRetrying={isRetryingConnectionFailure}
          />
        )}

        <section className={`space-y-3 ${inline ? "rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-4 shadow-sm" : "rounded-xl border border-sky-200 bg-sky-50/55 p-3"}`}>
          <div className="flex items-center justify-between gap-2">
            <div><h3 className="text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]">Active visit draft</h3><p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Editable work for this visit</p></div>
            <span className="rounded border border-sky-300 bg-white px-2 py-0.5 text-xs font-semibold text-sky-800">Editable</span>
          </div>
          {needsPerformer && (
            <ActualWorkPerformerGate onSetDefaultPerformer={handleSetDefaultPerformer} />
          )}
          {!readOnly && !needsPerformer && changingPerformer && (
            <ActualWorkPerformerGate
              onSetDefaultPerformer={handleSetDefaultPerformer}
              initialSelectedId={draft.defaultPerformedByAccountUserId ?? ""}
              onCancel={() => setChangingPerformer(false)}
            />
          )}
          {!readOnly && !needsPerformer && !changingPerformer && (
            <>
              {inline ? (
                <ActualWorkPerformerSummary
                  name={draft.defaultPerformerDisplayName ?? null}
                  isSelf={
                    !!currentAccountUserId &&
                    draft.defaultPerformedByAccountUserId === currentAccountUserId
                  }
                  onChange={() => setChangingPerformer(true)}
                />
              ) : (
                <ActualWorkPerformerCaption
                  name={draft.defaultPerformerDisplayName ?? null}
                  isSelf={
                    !!currentAccountUserId &&
                    draft.defaultPerformedByAccountUserId === currentAccountUserId
                  }
                />
              )}
              {!inline ? (
                searchAndAdd
              ) : (
                (!isEmptyDraftInline || emptyDraftMode !== "neutral") && (
                  <button
                    type="button"
                    onClick={openPicker}
                    className={`inline-flex items-center gap-1 self-start rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2.5 py-1.5 text-xs font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                  >
                    <Plus className="h-3.5 w-3.5" /> Add work/material lines
                  </button>
                )
              )}
              {inline && pickerOpen && (
                <ActualWorkItemPickerDrawer
                  onClose={() => setPickerOpen(false)}
                  initialFocus={searchInputRef}
                  connectionFailureBanner={
                    connectionFailure && (
                      <ConnectionFailureBanner
                        message={connectionFailure.message}
                        onRetry={retryConnectionFailure}
                        isRetrying={isRetryingConnectionFailure}
                      />
                    )
                  }
                >
                  {searchAndAdd}
                </ActualWorkItemPickerDrawer>
              )}
            </>
          )}
        <div className="space-y-2">
          {draft.lines.length === 0 &&
            (readOnly || needsPerformer || changingPerformer ? (
              <p className="text-xs text-[var(--ophalo-muted)]">No items added yet.</p>
            ) : inline ? (
              emptyDraftMode === "neutral" ? (
                <div className="rounded-lg border border-dashed border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
                  <p className="text-xs font-semibold text-[var(--ophalo-ink)]">Choose how to record this visit</p>
                  <div className="mt-1.5 flex flex-wrap gap-2">
                    <button
                      type="button"
                      onClick={openPicker}
                      className={`inline-flex items-center gap-1 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2.5 py-1 text-xs font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                    >
                      <Plus className="h-3.5 w-3.5" /> Add work/material lines
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setEmptyDraftMode("zero-line");
                        focusZeroLineOutcome();
                      }}
                      className={`inline-flex items-center gap-1 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2.5 py-1 text-xs font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                    >
                      Record a zero-line outcome
                    </button>
                  </div>
                  <p className="mt-1 text-[11px] text-[var(--ophalo-muted)]">
                    A zero-line outcome is for diagnostic-only, no work authorized, or no access.
                  </p>
                </div>
              ) : emptyDraftMode === "work" ? (
                <p className="text-[11px] text-[var(--ophalo-muted)]">
                  No items added yet. Use “Add work/material lines” above, or{" "}
                  <button
                    type="button"
                    onClick={() => {
                      setEmptyDraftMode("zero-line");
                      focusZeroLineOutcome();
                    }}
                    className={`font-medium text-[var(--keep-accent)] hover:underline rounded ${FOCUS_RING}`}
                  >
                    record a zero-line outcome
                  </button>
                  .
                </p>
              ) : null
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
              presentation={presentation}
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
            collapsible={inline}
            onSetVisitNote={onSetVisitNote}
          />
        )}

        {!readOnly && (
          <div className="mt-3 flex justify-end border-t border-[var(--ophalo-border)] pt-3">
            <button
              ref={discardTriggerRef}
              type="button"
              disabled={discardMutation.isPending}
              onClick={() => setShowDiscardConfirm(true)}
              className={`inline-flex shrink-0 items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-semibold text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger-bg)] disabled:opacity-50 ${FOCUS_RING}`}
            >
              <X className="h-3.5 w-3.5" />
              Discard draft
            </button>
          </div>
        )}</section>
        {/* Prior visits are an audit trail, not part of the visit currently being recorded. Keep
            them available in the Request Detail/modal path, but omit them from the dedicated
            recording workspace so their lines cannot be mistaken for draft duplicates. */}
        {!inline && <SubmittedVisits visits={submittedVisits} timeZone={timeZone} />}
       </div>
      </div>

      <ActualWorkSubmitFooter
        key={`${draft.outcome ?? ""}|${draft.completionNote ?? ""}`}
        draft={draft}
        submitted={submitted}
        isWide={isWide}
        showZeroLineForm={
          draft.lines.length === 0 &&
          (!inline || (isEmptyDraftInline && emptyDraftMode === "zero-line"))
        }
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
        className="relative flex min-h-0 flex-1 flex-col bg-[var(--keep-workspace-canvas)]"
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
