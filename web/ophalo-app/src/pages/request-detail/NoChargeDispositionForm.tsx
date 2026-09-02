import { useEffect, useRef, useState } from "react";
import { KeepButton } from "../../components/keep/KeepButton";
import { INPUT_CLS } from "./helpers";
import { type FinancialReviewOutcome } from "./useActualWorkFinancialReview";

interface NoChargeDispositionFormProps {
  busy: boolean;
  onSubmit: (reason: string) => Promise<FinancialReviewOutcome>;
  /** BL138 Slice 2: reports whether the reason field holds unsaved text, so the wide
   *  financial-review workspace can guard a visit switch / back / next navigation. */
  onDirtyChange?: (dirty: boolean) => void;
}

/** Inline no-charge disposition entry for a zero-line submitted visit that has no disposition yet.
 * The card renders this only for an unreviewed, zero-line visit with hasNoChargeDisposition === false. */
export function NoChargeDispositionForm({ busy, onSubmit, onDirtyChange }: NoChargeDispositionFormProps) {
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

    if (reason.trim() === "") {
      setErrored(true);
      reasonRef.current?.focus();
      setNotice("A reason is required to record no charge.");
      return;
    }

    const outcome = await onSubmit(reason.trim());
    if (outcome.kind === "success" || outcome.kind === "hidden") return;
    if (outcome.kind === "reconciled") {
      setNotice("This visit changed and was reloaded. Re-check it before recording no charge.");
      return;
    }
    if (outcome.kind === "validation-failure") {
      setErrored(true);
      reasonRef.current?.focus();
      setNotice("The office system rejected this. Correct the reason and try again.");
      return;
    }
    setNotice("Unable to record no charge. Try again.");
  }

  return (
    <details className="mt-3 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2">
      <summary className="cursor-pointer list-none text-xs font-semibold text-[var(--ophalo-ink)]">
        Record this visit as no charge
      </summary>

      {notice && <p role="alert" className="mt-2 text-xs text-[var(--ophalo-danger)]">{notice}</p>}

      <label className="mt-3 block text-xs font-semibold text-[var(--ophalo-ink)]">
        Reason
        <textarea
          ref={reasonRef}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          disabled={busy}
          rows={2}
          placeholder="Record why nothing is billable for this visit…"
          className={`${INPUT_CLS} mt-1 ${errored ? "border-[var(--ophalo-danger)]" : ""}`}
        />
      </label>

      <div className="mt-3 flex justify-end">
        <KeepButton onClick={() => void submit()} disabled={busy}>
          {busy ? "Saving…" : "Record no charge"}
        </KeepButton>
      </div>
    </details>
  );
}
