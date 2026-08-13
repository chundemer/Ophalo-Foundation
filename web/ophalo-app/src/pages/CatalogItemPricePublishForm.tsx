import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../lib/apiClient";

const INPUT_CLS =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base text-[var(--ophalo-ink)] px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

// User-facing reason choices (build-log/113, owner-UX simplification pass, 2026-08-07). The
// server's audit reason is still free text — Other's typed value is submitted verbatim (trimmed);
// the three fixed choices submit their exact label as the reason.
const REASON_OPTIONS = [
  { value: "supplier-cost-changed", label: "Supplier cost changed" },
  { value: "correcting-a-price", label: "Correcting a price" },
  { value: "promotion-or-seasonal-pricing", label: "Promotion or seasonal pricing" },
  { value: "other", label: "Other" },
] as const;

type ReasonChoice = (typeof REASON_OPTIONS)[number]["value"] | "";

interface PublishPriceFormState {
  cost: string;
  sellPrice: string;
  pricingMode: "StandalonePrice" | "NoStandalonePrice";
  reasonChoice: ReasonChoice;
  otherReason: string;
}

function currencySymbol(currency: string): string {
  try {
    const parts = new Intl.NumberFormat(undefined, { style: "currency", currency }).formatToParts(0);
    return parts.find((p) => p.type === "currency")?.value ?? currency;
  } catch {
    return currency;
  }
}

function resolveReason(reasonChoice: ReasonChoice, otherReason: string): string {
  if (reasonChoice === "other") return otherReason.trim();
  return REASON_OPTIONS.find((o) => o.value === reasonChoice)?.label ?? "";
}

function parseNumberOrNull(text: string): number | null {
  return text.trim() === "" ? null : Number(text);
}

interface CatalogItemPricePublishFormProps {
  catalogItemId: string;
  currency: string;
  currentCost: number | null;
  currentSellPrice: number | null;
  currentPricingMode: string | null;
  onClose: () => void;
}

/**
 * "Update pricing & cost" (Session 2e.6d, build-log/113; owner-UX simplification pass,
 * 2026-08-07; renamed 2026-08-13 — the form edits both customer sell price and internal cost, and
 * the assembly margin repair loop links here for the cost field specifically): the server
 * operation is still a later ADR-470 price publish — a new immutable, audited price-book version —
 * but the UI presents it as a plain update, since that's the mental model a small-business owner
 * has (they're changing what the customer pays and what it costs them, not managing a price book).
 * Deliberately kept separate from CatalogItemDetail's conflictRefreshPending/itemBusy gate — this
 * endpoint takes no version token (ADR-470's lock is account-scoped, not
 * CatalogItem.ConcurrencyVersion), so a manual retry is always safe and does not need to wait on a
 * refetch the way header/alias/lifecycle actions do. On any error, including a lock conflict, the
 * whole draft is retained and never resubmitted automatically — the owner must explicitly re-click
 * Update pricing & cost.
 */
export function CatalogItemPricePublishForm({
  catalogItemId,
  currency,
  currentCost,
  currentSellPrice,
  currentPricingMode,
  onClose,
}: CatalogItemPricePublishFormProps) {
  const queryClient = useQueryClient();
  const symbol = currencySymbol(currency);

  // Captured once, when the form opened — not re-derived from props on a background refetch (e.g.
  // after a conflict) — so the no-op guard below always compares against what the owner actually
  // saw and started editing, per spec.
  const [initial] = useState(() => ({
    cost: currentCost,
    sellPrice: currentSellPrice,
    pricingMode: (currentPricingMode === "NoStandalonePrice" ? "NoStandalonePrice" : "StandalonePrice") as
      | "StandalonePrice"
      | "NoStandalonePrice",
  }));

  const [form, setForm] = useState<PublishPriceFormState>(() => ({
    cost: currentCost != null ? String(currentCost) : "",
    sellPrice: currentSellPrice != null ? String(currentSellPrice) : "",
    pricingMode: initial.pricingMode,
    reasonChoice: "",
    otherReason: "",
  }));
  const [fieldErrors, setFieldErrors] = useState<{ cost?: string; sellPrice?: string; reason?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [belowCostConfirmed, setBelowCostConfirmed] = useState(false);
  const belowCostCheckboxRef = useRef<HTMLInputElement>(null);
  const [advancedOpen, setAdvancedOpen] = useState(initial.pricingMode === "NoStandalonePrice");

  const costValue = form.cost.trim() !== "" ? Number(form.cost) : null;
  const sellPriceValue =
    form.pricingMode === "StandalonePrice" && form.sellPrice.trim() !== "" ? Number(form.sellPrice) : null;
  const showBelowCostWarning =
    form.pricingMode === "StandalonePrice" &&
    costValue !== null &&
    sellPriceValue !== null &&
    !Number.isNaN(costValue) &&
    !Number.isNaN(sellPriceValue) &&
    sellPriceValue < costValue;

  // Prevent accidental no-op updates: a different reason alone must not create a duplicate
  // immutable price version. Only Cost, Sell Price, and pricing mode count as a real change.
  const proposedCost = parseNumberOrNull(form.cost);
  const proposedSellPrice = form.pricingMode === "StandalonePrice" ? parseNumberOrNull(form.sellPrice) : null;
  const noPriceChange =
    form.pricingMode === initial.pricingMode &&
    proposedCost === initial.cost &&
    proposedSellPrice === initial.sellPrice;

  const publishPriceMutation = useMutation({
    mutationFn: (input: PublishPriceFormState) => {
      const cost = input.cost.trim() === "" ? null : Number(input.cost);
      const sellPrice =
        input.pricingMode === "StandalonePrice" && input.sellPrice.trim() !== "" ? Number(input.sellPrice) : null;
      const reason = resolveReason(input.reasonChoice, input.otherReason);
      return api.publishCatalogItemPrice(catalogItemId, { cost, sellPrice, reason });
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] });
      void queryClient.invalidateQueries({ queryKey: ["catalogItems"] });
      onClose();
    },
    onError: (err: unknown) => {
      // Never resubmit automatically, on a conflict or otherwise — always retain the whole draft
      // and require the owner to explicitly re-click Update pricing & cost.
      if (err instanceof ApiError && err.code === "PriceBookVersion.PublishLockConflict") {
        setFormError(
          "Someone else updated pricing a moment ago. We refreshed the latest price—review your changes and try again.",
        );
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] });
        return;
      }
      if (err instanceof ApiError && err.code === "PriceBookVersion.CatalogItemNotActive") {
        setFormError("This item is inactive and can't have its price updated. Reactivate it first.");
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] });
        return;
      }
      if (err instanceof ApiError && err.code === "ManualPriceOverride.ReasonRequired") {
        setFieldErrors((prev) => ({ ...prev, reason: "Select a reason." }));
        return;
      }
      if (err instanceof ApiError && err.code === "ManualPriceOverride.ReasonTooLong") {
        setFieldErrors((prev) => ({ ...prev, reason: "Reason must not exceed 500 characters." }));
        return;
      }
      if (err instanceof ApiError && err.code === "PriceBookVersion.CostSnapshotMustNotBeNegative") {
        setFieldErrors((prev) => ({ ...prev, cost: "Cost can't be negative." }));
        return;
      }
      if (err instanceof ApiError && err.code === "PriceBookVersion.SellPriceSnapshotMustNotBeNegative") {
        setFieldErrors((prev) => ({ ...prev, sellPrice: "Sell price can't be negative." }));
        return;
      }
      setFormError("Could not update this price. Try again.");
    },
  });

  function validateBeforeSubmit(): boolean {
    const errors: { cost?: string; sellPrice?: string; reason?: string } = {};

    if (form.cost.trim() !== "") {
      const value = Number(form.cost);
      if (!Number.isFinite(value) || value < 0) errors.cost = "Enter a valid, non-negative cost.";
    }
    if (form.pricingMode === "StandalonePrice") {
      if (form.sellPrice.trim() === "") {
        errors.sellPrice = "Enter a sell price, or use Advanced options for a package-only item.";
      } else {
        const value = Number(form.sellPrice);
        if (!Number.isFinite(value) || value < 0) errors.sellPrice = "Enter a valid, non-negative sell price.";
      }
    }
    if (form.reasonChoice === "") {
      errors.reason = "Select a reason.";
    } else if (form.reasonChoice === "other") {
      const trimmed = form.otherReason.trim();
      if (trimmed === "") {
        errors.reason = "Enter a reason.";
      } else if (trimmed.length > 500) {
        errors.reason = "Reason must not exceed 500 characters.";
      }
    }

    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      return false;
    }
    if (showBelowCostWarning && !belowCostConfirmed) {
      belowCostCheckboxRef.current?.focus();
      return false;
    }
    return true;
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (publishPriceMutation.isPending || noPriceChange) return;
    setFieldErrors({});
    setFormError(null);
    if (!validateBeforeSubmit()) return;
    publishPriceMutation.mutate(form);
  }

  return (
    <form onSubmit={handleSubmit} className="rounded-xl border border-[var(--ophalo-border)] p-4 space-y-4">
      <h2 className="text-sm font-semibold text-[var(--ophalo-ink)]">Update pricing &amp; cost</h2>

      {form.pricingMode === "StandalonePrice" && (
        <div>
          <label htmlFor="publish-sell-price" className="text-sm font-medium text-[var(--ophalo-ink)]">
            Sell price
          </label>
          <div className="relative mt-1">
            <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-[var(--ophalo-muted)]">
              {symbol}
            </span>
            <input
              id="publish-sell-price"
              type="number"
              inputMode="decimal"
              min={0}
              step="0.01"
              value={form.sellPrice}
              onChange={(e) => {
                setForm({ ...form, sellPrice: e.target.value });
                setBelowCostConfirmed(false);
              }}
              disabled={publishPriceMutation.isPending}
              className={`pl-6 text-lg ${INPUT_CLS} ${fieldErrors.sellPrice ? ERROR_INPUT_CLS : ""}`}
            />
          </div>
          {fieldErrors.sellPrice && (
            <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{fieldErrors.sellPrice}</p>
          )}
        </div>
      )}

      <div>
        <label htmlFor="publish-cost" className="text-xs font-medium text-[var(--ophalo-muted)]">
          Internal cost (optional)
        </label>
        <div className="relative mt-1">
          <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-[var(--ophalo-muted)]">
            {symbol}
          </span>
          <input
            id="publish-cost"
            type="number"
            inputMode="decimal"
            min={0}
            step="0.01"
            value={form.cost}
            onChange={(e) => {
              setForm({ ...form, cost: e.target.value });
              setBelowCostConfirmed(false);
            }}
            disabled={publishPriceMutation.isPending}
            className={`pl-6 ${INPUT_CLS} ${fieldErrors.cost ? ERROR_INPUT_CLS : ""}`}
          />
        </div>
        <p className="mt-1 text-xs text-[var(--ophalo-muted)]">
          Used for margin reporting; customers do not see this.
        </p>
        {fieldErrors.cost && <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{fieldErrors.cost}</p>}
      </div>

      {showBelowCostWarning && (
        <div className="rounded-lg px-3 py-2 bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)] flex flex-col gap-2">
          <p className="text-sm">Sell price is below cost. This is permitted, but confirm it's intentional.</p>
          <label className="flex items-center gap-2 text-sm">
            <input
              ref={belowCostCheckboxRef}
              type="checkbox"
              checked={belowCostConfirmed}
              onChange={(e) => setBelowCostConfirmed(e.target.checked)}
              className="rounded border-[var(--ophalo-border)]"
            />
            I understand this item is priced below cost
          </label>
        </div>
      )}

      <details
        open={advancedOpen}
        onToggle={(e) => setAdvancedOpen((e.target as HTMLDetailsElement).open)}
        className="rounded-lg border border-[var(--ophalo-border)] px-3 py-2"
      >
        <summary className="cursor-pointer text-sm font-medium text-[var(--ophalo-ink)]">
          Advanced options
        </summary>
        <label className="mt-2 flex items-start gap-2 text-sm text-[var(--ophalo-ink)]">
          <input
            type="checkbox"
            checked={form.pricingMode === "NoStandalonePrice"}
            onChange={(e) => {
              if (e.target.checked) {
                setForm({ ...form, pricingMode: "NoStandalonePrice", sellPrice: "" });
                setBelowCostConfirmed(false);
              } else {
                setForm({ ...form, pricingMode: "StandalonePrice" });
              }
            }}
            disabled={publishPriceMutation.isPending}
            className="mt-0.5 rounded border-[var(--ophalo-border)]"
          />
          <span>
            This item doesn&apos;t have its own sell price
            <span className="block text-xs text-[var(--ophalo-muted)]">
              Use this only when the item is priced as part of a package.
            </span>
          </span>
        </label>
      </details>

      <div>
        <label htmlFor="publish-reason" className="text-xs font-medium text-[var(--ophalo-muted)]">
          Why are you updating this?
        </label>
        <select
          id="publish-reason"
          value={form.reasonChoice}
          onChange={(e) => setForm({ ...form, reasonChoice: e.target.value as ReasonChoice })}
          disabled={publishPriceMutation.isPending}
          className={`mt-1 ${INPUT_CLS} ${fieldErrors.reason ? ERROR_INPUT_CLS : ""}`}
        >
          <option value="" disabled>
            Select a reason
          </option>
          {REASON_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        {form.reasonChoice === "other" && (
          <input
            type="text"
            aria-label="Reason"
            maxLength={500}
            placeholder="Describe the reason"
            value={form.otherReason}
            onChange={(e) => setForm({ ...form, otherReason: e.target.value })}
            disabled={publishPriceMutation.isPending}
            className={`mt-2 ${INPUT_CLS} ${fieldErrors.reason ? ERROR_INPUT_CLS : ""}`}
          />
        )}
        {fieldErrors.reason && <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{fieldErrors.reason}</p>}
      </div>

      {formError && (
        <p className="text-sm rounded-lg px-3 py-2 text-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)]">
          {formError}
        </p>
      )}

      {!publishPriceMutation.isPending && noPriceChange && (
        <p className="text-xs text-[var(--ophalo-muted)]">Change a price or pricing option to update this item.</p>
      )}

      {/* Sticky save bar (2026-08-13): the reason selector and save action previously sat below
          the fold, forcing a scroll before an owner arriving from the assembly cost-repair link
          could finish the fix. This is the form's last child and normal document flow is
          untouched above it — sticky's own geometry keeps it from ever covering the reason
          selector, error text, or below-cost confirmation as they scroll past. The negative
          margin/matching padding bleeds it to the card's edges so it reads as a docked bar rather
          than a floating box, while `rounded-b-xl` keeps the card's corner radius intact. */}
      <div className="sticky bottom-0 -mx-4 -mb-4 rounded-b-xl border-t border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-3 flex items-center gap-3">
        <button
          type="submit"
          disabled={publishPriceMutation.isPending || noPriceChange}
          className="rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white disabled:opacity-60"
        >
          {publishPriceMutation.isPending ? "Updating…" : "Update pricing & cost"}
        </button>
        <button
          type="button"
          onClick={onClose}
          disabled={publishPriceMutation.isPending}
          className="text-sm text-[var(--ophalo-muted)] hover:underline disabled:opacity-60"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
