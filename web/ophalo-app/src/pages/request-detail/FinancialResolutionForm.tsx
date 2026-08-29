import { useRef, useState } from "react";
import {
  type ActualWorkFinancialBlockerEntry,
  type ActualWorkFinancialResolutionBody,
} from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { INPUT_CLS } from "./helpers";
import { type FinancialReviewOutcome } from "./useActualWorkFinancialReview";

const BASIS_OPTIONS: { value: string; label: string }[] = [
  { value: "SupplierReceipt", label: "Supplier receipt" },
  { value: "OwnerSetPrice", label: "Owner-set price" },
  { value: "FixedAgreement", label: "Fixed agreement" },
  { value: "Other", label: "Other" },
];

type FieldKey = "sellPrice" | "directCost" | "basis" | "reason";

// Stable error codes → the field the office should correct first (BL135 §4 Batch 3a-ii mapper).
const CODE_FIELD: Record<string, FieldKey> = {
  "ActualWork.FinancialResolutionValueRequired": "sellPrice",
  "ActualWork.FinancialResolutionValueNegative": "sellPrice",
  "ActualWork.FinancialResolutionInvalidBasis": "basis",
  "ActualWork.FinancialResolutionReasonRequired": "reason",
  "ActualWork.FinancialResolutionReasonTooLong": "reason",
};

interface FinancialResolutionFormProps {
  blocker: ActualWorkFinancialBlockerEntry;
  busy: boolean;
  onSubmit: (lineId: string, body: ActualWorkFinancialResolutionBody) => Promise<FinancialReviewOutcome>;
}

/** Inline, missing-component-only resolution entry for one still-incomplete line. Preserves the
 * draft on a validation failure and focuses the first errored field; never a drawer or modal. */
export function FinancialResolutionForm({ blocker, busy, onSubmit }: FinancialResolutionFormProps) {
  const [sellPrice, setSellPrice] = useState("");
  const [directCost, setDirectCost] = useState("");
  const [basis, setBasis] = useState("");
  const [reason, setReason] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [erroredField, setErroredField] = useState<FieldKey | null>(null);

  const sellPriceRef = useRef<HTMLInputElement>(null);
  const directCostRef = useRef<HTMLInputElement>(null);
  const basisRef = useRef<HTMLSelectElement>(null);
  const reasonRef = useRef<HTMLTextAreaElement>(null);

  const needSell = blocker.sellPriceMissing;
  const needCost = blocker.standardExpectedDirectCostMissing;

  function focusField(field: FieldKey) {
    setErroredField(field);
    const map: Record<FieldKey, HTMLElement | null> = {
      sellPrice: needSell ? sellPriceRef.current : directCostRef.current,
      directCost: directCostRef.current,
      basis: basisRef.current,
      reason: reasonRef.current,
    };
    map[field]?.focus();
  }

  async function submit() {
    if (busy) return;
    setNotice(null);
    setErroredField(null);

    const parsedSell = needSell && sellPrice.trim() !== "" ? Number(sellPrice) : null;
    const parsedCost = needCost && directCost.trim() !== "" ? Number(directCost) : null;

    // A resolution may supply one or both missing components (locked contract / backend API); the
    // untouched component is sent as null. Require only that at least one has a value.
    if (parsedSell === null && parsedCost === null) {
      focusField(needSell ? "sellPrice" : "directCost");
      setNotice("Enter at least one of the missing values.");
      return;
    }
    if ((parsedSell !== null && Number.isNaN(parsedSell)) || (parsedCost !== null && Number.isNaN(parsedCost))) {
      focusField(parsedSell !== null && Number.isNaN(parsedSell) ? "sellPrice" : "directCost");
      setNotice("Enter a valid number.");
      return;
    }
    if ((parsedSell !== null && parsedSell < 0) || (parsedCost !== null && parsedCost < 0)) {
      focusField(parsedSell !== null && parsedSell < 0 ? "sellPrice" : "directCost");
      setNotice("Enter a value of zero or more.");
      return;
    }
    if (basis === "") {
      focusField("basis");
      setNotice("Select how this value was determined.");
      return;
    }
    if (reason.trim() === "") {
      focusField("reason");
      setNotice("A reason is required.");
      return;
    }

    const outcome = await onSubmit(blocker.lineId, {
      resolvedUnitSellPrice: parsedSell,
      resolvedUnitStandardExpectedDirectCost: parsedCost,
      basis,
      reason: reason.trim(),
    });

    if (outcome.kind === "success" || outcome.kind === "hidden") return;
    if (outcome.kind === "reconciled") {
      setNotice("This visit changed and was reloaded. Re-check the line before resolving.");
      return;
    }
    if (outcome.kind === "validation-failure") {
      const field = outcome.code ? CODE_FIELD[outcome.code] : undefined;
      if (field) focusField(field);
      setNotice("The office system rejected this value. Correct the highlighted field and try again.");
      return;
    }
    setNotice("Unable to record this resolution. Try again.");
  }

  const invalid = (field: FieldKey) =>
    erroredField === field ? "border-[var(--ophalo-danger)]" : "";

  return (
    <details className="mt-3 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2">
      <summary className="cursor-pointer list-none text-xs font-semibold text-[var(--ophalo-ink)]">
        Resolve missing {needSell && needCost ? "price and cost" : needSell ? "price" : "cost"} · {blocker.displayNameSnapshot}
      </summary>

      {needSell && needCost && <p className="mt-2 text-xs text-[var(--ophalo-muted)]">Supply either component or both.</p>}
      {notice && <p role="alert" className="mt-2 text-xs text-[var(--ophalo-danger)]">{notice}</p>}

      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        {needSell && (
          <label className="text-xs font-semibold text-[var(--ophalo-ink)]">
            Unit sell price
            <input
              ref={sellPriceRef}
              type="number"
              min="0"
              step="0.01"
              value={sellPrice}
              onChange={(event) => setSellPrice(event.target.value)}
              disabled={busy}
              className={`${INPUT_CLS} mt-1 ${invalid("sellPrice")}`}
            />
          </label>
        )}
        {needCost && (
          <label className="text-xs font-semibold text-[var(--ophalo-ink)]">
            Unit standard direct cost
            <input
              ref={directCostRef}
              type="number"
              min="0"
              step="0.01"
              value={directCost}
              onChange={(event) => setDirectCost(event.target.value)}
              disabled={busy}
              className={`${INPUT_CLS} mt-1 ${invalid("directCost")}`}
            />
          </label>
        )}
      </div>

      <label className="mt-3 block text-xs font-semibold text-[var(--ophalo-ink)]">
        How was this determined?
        <select
          ref={basisRef}
          value={basis}
          onChange={(event) => setBasis(event.target.value)}
          disabled={busy}
          className={`${INPUT_CLS} mt-1 ${invalid("basis")}`}
        >
          <option value="">Select…</option>
          {BASIS_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>{option.label}</option>
          ))}
        </select>
      </label>

      <label className="mt-3 block text-xs font-semibold text-[var(--ophalo-ink)]">
        Reason
        <textarea
          ref={reasonRef}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          disabled={busy}
          rows={2}
          placeholder="Record why this value applies…"
          className={`${INPUT_CLS} mt-1 ${invalid("reason")}`}
        />
      </label>

      <div className="mt-3 flex justify-end">
        <KeepButton onClick={() => void submit()} disabled={busy}>
          {busy ? "Saving…" : "Save resolution"}
        </KeepButton>
      </div>
    </details>
  );
}
