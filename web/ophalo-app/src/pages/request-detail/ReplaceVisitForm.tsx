import { useEffect, useRef, useState } from "react";
import { KeepButton } from "../../components/keep/KeepButton";
import { INPUT_CLS } from "./helpers";
import { type FinancialReviewOutcome } from "./useActualWorkFinancialReview";

const REASON_MAX = 2000;

interface ReplaceVisitFormProps {
  busy: boolean;
  onSubmit: (reason: string) => Promise<FinancialReviewOutcome>;
  /** `"inline"` (default): the compact disclosure used inside the stacked Request Detail review
   *  card. `"button"`: an outlined secondary-action trigger for the wide financial-review
   *  workspace, so it reads as an operable corrective path beside the primary teal action. */
  presentation?: "inline" | "button";
  /** BL138 Slice 2: reports whether the correction-reason field holds unsaved text, so the wide
   *  financial-review workspace can guard a visit switch / back / next navigation. */
  onDirtyChange?: (dirty: boolean) => void;
}

const TRIGGER_INLINE =
  "cursor-pointer list-none text-xs font-semibold text-[var(--ophalo-ink)]";
const TRIGGER_BUTTON =
  "cursor-pointer list-none inline-flex items-center rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-xs font-semibold text-[var(--ophalo-ink)] transition-colors hover:border-[var(--ophalo-danger)] hover:bg-[var(--ophalo-danger-bg)] hover:text-[var(--ophalo-danger)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

/** ADR-494 D6 (BL136 4e-iii): Owner/Admin reason-required "Correct this visit" action for a live,
 * non-superseded, pre-export submitted visit (reviewed or not). On a `replaced` outcome the parent
 * supersedes this card's data and routes to the successor Draft, so this form only has to surface
 * the failure outcomes. The erroneous source and its financial evidence are retained — this starts
 * a linked correction, it does not delete anything. */
export function ReplaceVisitForm({ busy, onSubmit, presentation = "inline", onDirtyChange }: ReplaceVisitFormProps) {
  const [reason, setReason] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [errored, setErrored] = useState(false);
  const reasonRef = useRef<HTMLTextAreaElement>(null);

  const dirty = reason.trim() !== "";
  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange?.(false), [onDirtyChange]);

  async function submit() {
    if (busy) return;
    setNotice(null);
    setErrored(false);

    const trimmed = reason.trim();
    if (trimmed === "") {
      setErrored(true);
      reasonRef.current?.focus();
      setNotice("A correction reason is required.");
      return;
    }
    if (trimmed.length > REASON_MAX) {
      setErrored(true);
      reasonRef.current?.focus();
      setNotice(`Keep the correction reason under ${REASON_MAX} characters.`);
      return;
    }

    const outcome = await onSubmit(trimmed);
    // `replaced` and `hidden` are handled by the parent (route to successor / degrade the surface).
    if (outcome.kind === "replaced" || outcome.kind === "hidden") return;
    if (outcome.kind === "reconciled") {
      setNotice("This visit changed and was reloaded. Re-check it before starting a correction.");
      return;
    }
    if (outcome.kind === "replace-blocked-open-draft") {
      setNotice("Another visit draft is already open for this request. Submit or discard it before correcting this visit.");
      return;
    }
    if (outcome.kind === "validation-failure") {
      setErrored(true);
      reasonRef.current?.focus();
      setNotice("The office system rejected this. Adjust the reason and try again.");
      return;
    }
    setNotice("Unable to start a correction. Try again.");
  }

  const asButton = presentation === "button";
  return (
    <details
      className={
        asButton
          ? ""
          : "mt-3 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2"
      }
    >
      <summary className={asButton ? TRIGGER_BUTTON : TRIGGER_INLINE}>Correct this visit</summary>

      <div
        className={
          asButton
            ? "mt-2 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] px-3 py-2.5"
            : ""
        }
      >
      <p className="mt-2 text-xs text-[var(--ophalo-muted)]">
        Starts a linked replacement draft with this visit&rsquo;s captured work copied in. The
        original submitted visit and its financial record are kept as history.
      </p>

      {notice && <p role="alert" className="mt-2 text-xs text-[var(--ophalo-danger)]">{notice}</p>}

      <label className="mt-3 block text-xs font-semibold text-[var(--ophalo-ink)]">
        Correction reason
        <textarea
          ref={reasonRef}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          disabled={busy}
          rows={2}
          maxLength={REASON_MAX}
          placeholder="Record what was wrong with the submitted visit…"
          className={`${INPUT_CLS} mt-1 ${errored ? "border-[var(--ophalo-danger)]" : ""}`}
        />
      </label>

      <div className="mt-3 flex justify-end">
        <KeepButton onClick={() => void submit()} disabled={busy}>
          {busy ? "Starting…" : "Start correction"}
        </KeepButton>
      </div>
      </div>
    </details>
  );
}
