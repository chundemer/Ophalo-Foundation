import { useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError, type ActualWorkHistoryResult, type ActualWorkSubmitBody } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";

// Maintainability review item 7, slice 2: split out of ActualWorkComposer.tsx (see build-log for
// the composer-family split). No behavior change. FOCUS_RING/INPUT_CLS/OUTCOME_OPTIONS and the
// ActualWorkDraft/SetZeroLineDispositionOutcome types are redeclared locally rather than imported,
// matching the established convention in this directory.

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const OUTCOME_OPTIONS: { value: string; label: string }[] = [
  { value: "DiagnosticOnly", label: "Diagnostic only" },
  { value: "NoWorkAuthorized", label: "No work authorized" },
  { value: "NoAccess", label: "No access" },
];

type ActualWorkDraft = NonNullable<ActualWorkHistoryResult["openDraft"]>;

/** Mirrors `useActualWorkCapture`'s `setZeroLineDisposition` return contract (BL136 §4e-iii). `set` /
 * `stale` settle through the parent refetch + shared reconcile; `invalid` is surfaced inline (the
 * server rejected the outcome enum value); `failed` keeps the local edit for a retry. */
type SetZeroLineDispositionOutcome = "set" | "invalid" | "stale" | "failed";

interface ActualWorkSubmitFooterProps {
  draft: ActualWorkDraft;
  submitted: boolean;
  isWide: boolean;
  // BL136 large-ticket density: on the inline workspace the zero-line outcome/note form is shown
  // only once the technician explicitly chooses that path. This component stays mounted across the
  // mode toggle, so its local outcome/note state is not lost. Modal presentation always passes
  // true when the draft has zero lines (unchanged behaviour).
  showZeroLineForm: boolean;
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
export function ActualWorkSubmitFooter({
  draft,
  submitted,
  isWide,
  showZeroLineForm,
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

  // A zero-line draft can only be submitted once the outcome/note form is actually shown and both
  // fields are truthful — in "neutral"/"work" mode (form hidden) Submit stays disabled so the
  // technician commits to the zero-line path first.
  const canSubmit = zeroLine
    ? showZeroLineForm && outcome !== "" && completionNote.trim().length > 0
    : true;

  return (
    <div
      className={`px-4 py-3 border-t border-[var(--ophalo-border)] shrink-0 ${
        isWide ? "" : "pb-[max(0.75rem,env(safe-area-inset-bottom))]"
      }`}
    >
     <div className="space-y-2 min-[1001px]:mx-auto min-[1001px]:max-w-[1000px]">
      {showZeroLineForm && (
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
      {showZeroLineForm && !canSubmit && (
        <p className="text-center text-xs text-[var(--ophalo-muted)] min-[1001px]:text-right">
          Select an outcome and add a completion note, or add at least one item, before submitting.
        </p>
      )}
     </div>
    </div>
  );
}

