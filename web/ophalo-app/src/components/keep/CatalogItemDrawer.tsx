import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { KeepModal } from "./KeepModal";
import { CategoryCombobox } from "./CategoryCombobox";
import {
  api,
  ApiError,
  type CatalogCategoryResponse,
  type CreateAndActivateCatalogItemResult,
} from "../../lib/apiClient";

// Session 2e.5, build-log/112/113: catalog item creation drawer. Single creation outcome —
// atomic Save & activate. Per Christian's 2026-08-05 decision (ADR-468 amendment), this pilot is
// explicitly USD-only until a server-owned account-currency setting exists — a deliberate approved
// posture, not an unresolved gap. De-emphasized in the UI as a "Prices in USD" note plus $ prefixes
// on Cost/Sell Price, rather than a dedicated read-only Currency field.
const ACCOUNT_CURRENCY = "USD";

const TYPE_OPTIONS = [
  { value: "Material", label: "Material" },
  { value: "Equipment", label: "Equipment" },
  { value: "Service", label: "Service" },
  { value: "Fee", label: "Fee" },
];

const UOM_SUGGESTIONS = ["each", "hour", "ft", "sq ft", "gal", "lb", "box", "lot"];

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

interface CatalogItemDrawerProps {
  categories: CatalogCategoryResponse[];
  onCategoriesChanged: () => void;
  onClose: () => void;
  onCreated: (result: CreateAndActivateCatalogItemResult) => void;
}

interface FormState {
  type: string;
  displayName: string;
  externalKey: string;
  unitOfMeasure: string;
  categoryId: string | null;
  isCommonItem: boolean;
  aliasText: string;
  pricingMode: "StandalonePrice" | "NoStandalonePrice";
  cost: string;
  sellPrice: string;
}

const EMPTY_FORM: FormState = {
  type: "Material",
  displayName: "",
  externalKey: "",
  unitOfMeasure: "each",
  categoryId: null,
  isCommonItem: false,
  aliasText: "",
  pricingMode: "StandalonePrice",
  cost: "",
  sellPrice: "",
};

// build-log/112: Save & add another retains category, type, UOM, currency, and price mode for
// rapid multi-item entry; it clears item identity, display name, SKU, aliases, Cost, and Sell
// Price. Common Item is an item-specific curation flag, not a session preset, so it resets too.
function resetForAddAnother(prev: FormState): FormState {
  return {
    ...prev,
    displayName: "",
    externalKey: "",
    isCommonItem: false,
    aliasText: "",
    cost: "",
    sellPrice: "",
  };
}

// build-log/112: after client or server validation, move focus to the first invalid field in
// visual/tab order (Name; Type + Category; UOM; Codes & search; pricing).
const FIELD_FOCUS_ORDER: (keyof FormState)[] = [
  "displayName",
  "unitOfMeasure",
  "externalKey",
  "aliasText",
  "cost",
  "sellPrice",
];

function isDirty(form: FormState): boolean {
  return (
    form.displayName.trim() !== "" ||
    form.externalKey.trim() !== "" ||
    form.unitOfMeasure.trim() !== "each" ||
    form.categoryId !== null ||
    form.isCommonItem ||
    form.aliasText.trim() !== "" ||
    form.cost.trim() !== "" ||
    form.sellPrice.trim() !== "" ||
    form.pricingMode !== "StandalonePrice"
  );
}

type FieldErrors = Partial<Record<keyof FormState, string>>;

// build-log/112: server/client validation errors render next to their relevant fields.
function mapErrorToField(code: string | undefined): keyof FormState | null {
  switch (code) {
    case "CatalogItem.DisplayNameRequired":
    case "CatalogItem.DisplayNameTooLong":
      return "displayName";
    case "CatalogItem.UnitOfMeasureRequired":
    case "CatalogItem.UnitOfMeasureTooLong":
      return "unitOfMeasure";
    case "CatalogItem.ExternalKeyAlreadyExists":
    case "CatalogItem.InvalidExternalKey":
      return "externalKey";
    case "CatalogItem.AliasTextTooLong":
    case "CatalogItem.AliasAlreadyExists":
      return "aliasText";
    case "PriceBookVersion.CostSnapshotMustNotBeNegative":
      return "cost";
    case "PriceBookVersion.SellPriceSnapshotMustNotBeNegative":
    case "PriceBookVersion.StandalonePriceRequiresSellPrice":
    case "PriceBookVersion.NoStandalonePriceRequiresNullSellPrice":
      return "sellPrice";
    default:
      return null;
  }
}

const FIELD_ERROR_MESSAGES: Partial<Record<string, string>> = {
  "CatalogItem.DisplayNameRequired": "Name is required.",
  "CatalogItem.DisplayNameTooLong": "Name must not exceed 200 characters.",
  "CatalogItem.UnitOfMeasureRequired": "Unit of measure is required.",
  "CatalogItem.UnitOfMeasureTooLong": "Unit of measure must not exceed 50 characters.",
  "CatalogItem.ExternalKeyAlreadyExists": "This SKU is already in use.",
  "CatalogItem.InvalidExternalKey": "This SKU isn't valid — use letters or numbers.",
  "CatalogItem.AliasTextTooLong": "Alias must not exceed 200 characters.",
  "CatalogItem.AliasAlreadyExists": "This alias is already used on this item.",
  "PriceBookVersion.CostSnapshotMustNotBeNegative": "Cost can't be negative.",
  "PriceBookVersion.SellPriceSnapshotMustNotBeNegative": "Sell price can't be negative.",
  "PriceBookVersion.StandalonePriceRequiresSellPrice": "Enter a sell price, or choose No standalone price.",
  "PriceBookVersion.NoStandalonePriceRequiresNullSellPrice": "Clear the sell price for No standalone price.",
};

/**
 * Catalog item creation drawer (Session 2e.5/2e.7b, build-log/113/114): the responsive New Item
 * drawer — desktop side drawer / mobile full screen. Single creation outcome (atomic Save &
 * activate, no Save Draft), dirty-dismiss protection, a shared searchable/creatable category
 * combobox (`CategoryCombobox`) with race-safe fallback to an existing category, and a below-cost
 * confirmation gate. Save & add another resets the form and keeps the drawer open.
 */
export function CatalogItemDrawer({ categories, onCategoriesChanged, onClose, onCreated }: CatalogItemDrawerProps) {
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  // Reported by CategoryCombobox: true from the start of a category-create attempt until it
  // resolves (success, or an error/conflict still awaiting retry) — blocks item save so it can
  // never fire against an uncommitted category intent.
  const [categoryPending, setCategoryPending] = useState(false);
  const [belowCostConfirmed, setBelowCostConfirmed] = useState(false);
  const [savedAndAddedAnotherMessage, setSavedAndAddedAnotherMessage] = useState<string | null>(null);
  const saveAndAddAnotherRef = useRef(false);
  const fieldRefs = useRef<Partial<Record<keyof FormState, HTMLInputElement | null>>>({});
  const belowCostCheckboxRef = useRef<HTMLInputElement>(null);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);

  function focusFirstInvalidField(errors: FieldErrors) {
    const key = FIELD_FOCUS_ORDER.find((k) => errors[k]);
    if (key) fieldRefs.current[key]?.focus();
  }

  const dirty = isDirty(form);

  const costValue = form.cost.trim() === "" ? null : Number(form.cost);
  const sellPriceValue = form.sellPrice.trim() === "" ? null : Number(form.sellPrice);
  const showBelowCostWarning =
    form.pricingMode === "StandalonePrice" &&
    costValue !== null &&
    sellPriceValue !== null &&
    !Number.isNaN(costValue) &&
    !Number.isNaN(sellPriceValue) &&
    sellPriceValue < costValue;

  const createItemMutation = useMutation({
    mutationFn: () =>
      api.createCatalogItem({
        type: form.type,
        displayName: form.displayName.trim(),
        unitOfMeasure: form.unitOfMeasure.trim(),
        currency: ACCOUNT_CURRENCY,
        externalKey: form.externalKey.trim() || null,
        categoryId: form.categoryId,
        isCommonItem: form.isCommonItem,
        initialAliasTexts: form.aliasText.trim() ? [form.aliasText.trim()] : [],
        pricingMode: form.pricingMode,
        cost: costValue,
        sellPrice: form.pricingMode === "StandalonePrice" ? sellPriceValue : null,
      }),
    onSuccess: (result) => {
      onCreated(result);
      if (saveAndAddAnotherRef.current) {
        setSavedAndAddedAnotherMessage(`${result.item.displayName} added.`);
        setForm(resetForAddAnother);
        setFieldErrors({});
        setGeneralError(null);
        setBelowCostConfirmed(false);
        fieldRefs.current.displayName?.focus();
      } else {
        onClose();
      }
    },
    onError: (err) => {
      setFieldErrors({});
      setGeneralError(null);
      if (err instanceof ApiError && err.code) {
        const field = mapErrorToField(err.code);
        const message = FIELD_ERROR_MESSAGES[err.code] ?? "Something went wrong. Try again.";
        if (field) {
          const errors = { [field]: message };
          setFieldErrors(errors);
          focusFirstInvalidField(errors);
          return;
        }
      }
      setGeneralError("Something went wrong. Try again.");
    },
  });

  function updateField<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
    setSavedAndAddedAnotherMessage(null);
  }

  // P2 fix: the discard confirm is a nested alertdialog inside KeepModal's own dialog. KeepModal's
  // Tab trap spans its whole panel (form fields included), so without this the background form
  // stays reachable and focused-behind while the confirmation is up. `inert` on the form (above)
  // removes it from the tab order and hit-testing; this effect owns initial focus, Tab wrapping
  // between the two confirm buttons, and Escape — registered on the capture phase so it runs and
  // can stopPropagation before KeepModal's own bubble-phase Escape listener sees the event.
  useEffect(() => {
    if (!showDiscardConfirm) return;
    previousFocusRef.current = document.activeElement;
    keepEditingRef.current?.focus();

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        setShowDiscardConfirm(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = keepEditingRef.current;
      const last = discardRef.current;
      if (!first || !last) return;
      // Exactly two focusable elements in this sub-dialog — Tab and Shift+Tab both just toggle.
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      const prior = previousFocusRef.current;
      if (prior instanceof HTMLElement) prior.focus();
    };
  }, [showDiscardConfirm]);

  function attemptClose() {
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  function validateBeforeSubmit(): boolean {
    const errors: FieldErrors = {};
    if (!form.displayName.trim()) errors.displayName = "Name is required.";
    if (!form.unitOfMeasure.trim()) errors.unitOfMeasure = "Unit of measure is required.";
    if (form.pricingMode === "StandalonePrice" && form.sellPrice.trim() === "") {
      errors.sellPrice = "Enter a sell price, or choose No standalone price.";
    }
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      focusFirstInvalidField(errors);
      return false;
    }
    if (showBelowCostWarning && !belowCostConfirmed) {
      belowCostCheckboxRef.current?.focus();
      return false;
    }
    return true;
  }

  function submit(addAnother: boolean) {
    if (createItemMutation.isPending) return;
    // The Save buttons are already disabled while categoryPending, but Ctrl/Cmd+Enter calls
    // submit() directly — guard here too so a fast keyboard shortcut can't race an in-flight or
    // stuck category creation.
    if (categoryPending) return;
    if (!validateBeforeSubmit()) return;
    saveAndAddAnotherRef.current = addAnother;
    createItemMutation.mutate();
  }

  function handleSubmit(e: React.FormEvent, addAnother: boolean) {
    e.preventDefault();
    submit(addAnother);
  }

  // build-log/112: Ctrl+Enter/Cmd+Enter triggers Save & add another from within the drawer.
  function handleFormKeyDown(e: React.KeyboardEvent<HTMLFormElement>) {
    if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
      e.preventDefault();
      submit(true);
    }
  }

  const isPending = createItemMutation.isPending;

  return (
    <KeepModal
      onClose={attemptClose}
      label="New catalog item"
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 top-0 right-0 h-[100dvh] max-h-[100dvh] w-full sm:w-[480px] bg-[var(--ophalo-card)] shadow-xl flex flex-col"
    >
      <form
        onSubmit={(e) => handleSubmit(e, false)}
        onKeyDown={handleFormKeyDown}
        className="h-full min-h-0 flex flex-col"
        {...(showDiscardConfirm ? { inert: true } : {})}
      >
        <div className="shrink-0 px-4 sm:px-6 py-4 border-b border-[var(--ophalo-border)] flex items-center justify-between">
          <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">New catalog item</h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
          >
            Close
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-4 sm:px-6 py-4 flex flex-col gap-4">
          {savedAndAddedAnotherMessage && (
            <p className="text-sm rounded-lg px-3 py-2 text-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)]">
              {savedAndAddedAnotherMessage}
            </p>
          )}

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-display-name">
              Name
            </label>
            <input
              id="ci-display-name"
              ref={(el) => {
                fieldRefs.current.displayName = el;
              }}
              type="text"
              value={form.displayName}
              onChange={(e) => updateField("displayName", e.target.value)}
              placeholder="As it should appear on a quote, e.g. Condensate Pump"
              maxLength={200}
              className={`${INPUT_CLS} ${fieldErrors.displayName ? ERROR_INPUT_CLS : ""}`}
              disabled={isPending}
            />
            {fieldErrors.displayName && (
              <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.displayName}</span>
            )}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="flex flex-col gap-1.5">
              <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-type">
                Type
              </label>
              <select
                id="ci-type"
                value={form.type}
                onChange={(e) => updateField("type", e.target.value)}
                className={INPUT_CLS}
                disabled={isPending}
              >
                {TYPE_OPTIONS.map((t) => (
                  <option key={t.value} value={t.value}>
                    {t.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="flex flex-col gap-1.5">
              <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-category">
                Category <span className="text-[var(--ophalo-muted)] font-normal">(optional)</span>
              </label>
              <CategoryCombobox
                id="ci-category"
                categories={categories}
                currentCategoryId={form.categoryId}
                onSelect={(categoryId) => setForm((prev) => ({ ...prev, categoryId }))}
                creatable
                disabled={isPending}
                onCategoriesChanged={onCategoriesChanged}
                onPendingChange={setCategoryPending}
              />
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-uom">
              Unit of measure
            </label>
            <input
              id="ci-uom"
              ref={(el) => {
                fieldRefs.current.unitOfMeasure = el;
              }}
              type="text"
              value={form.unitOfMeasure}
              onChange={(e) => updateField("unitOfMeasure", e.target.value)}
              placeholder="e.g. each"
              maxLength={50}
              className={`${INPUT_CLS} ${fieldErrors.unitOfMeasure ? ERROR_INPUT_CLS : ""}`}
              disabled={isPending}
            />
            <div className="flex flex-wrap gap-1.5" role="group" aria-label="Unit of measure quick fill">
              {UOM_SUGGESTIONS.map((u) => (
                <button
                  key={u}
                  type="button"
                  onClick={() => updateField("unitOfMeasure", u)}
                  disabled={isPending}
                  className={`px-2 py-1 rounded-full text-xs font-medium border border-[var(--ophalo-border)] ${FOCUS_RING} ${
                    form.unitOfMeasure === u
                      ? "bg-[var(--ophalo-navy)] text-white border-[var(--ophalo-navy)]"
                      : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                  }`}
                >
                  {u}
                </button>
              ))}
            </div>
            {fieldErrors.unitOfMeasure && (
              <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.unitOfMeasure}</span>
            )}
          </div>

          <fieldset className="flex flex-col gap-1.5 pt-2 border-t border-[var(--ophalo-border)]">
            <legend className="text-sm font-medium text-[var(--ophalo-ink)] px-0">
              Codes &amp; search <span className="text-[var(--ophalo-muted)] font-normal">(optional)</span>
            </legend>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="flex flex-col gap-1.5">
                <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-sku">
                  SKU / internal code
                </label>
                <input
                  id="ci-sku"
                  ref={(el) => {
                    fieldRefs.current.externalKey = el;
                  }}
                  type="text"
                  value={form.externalKey}
                  onChange={(e) => updateField("externalKey", e.target.value)}
                  placeholder="e.g. CP20"
                  className={`${INPUT_CLS} ${fieldErrors.externalKey ? ERROR_INPUT_CLS : ""}`}
                  disabled={isPending}
                />
                {fieldErrors.externalKey && (
                  <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.externalKey}</span>
                )}
              </div>

              <div className="flex flex-col gap-1.5">
                <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-alias">
                  Search keyword / shorthand
                </label>
                <input
                  id="ci-alias"
                  ref={(el) => {
                    fieldRefs.current.aliasText = el;
                  }}
                  type="text"
                  value={form.aliasText}
                  onChange={(e) => updateField("aliasText", e.target.value)}
                  placeholder="e.g. condensate pump"
                  maxLength={200}
                  className={`${INPUT_CLS} ${fieldErrors.aliasText ? ERROR_INPUT_CLS : ""}`}
                  disabled={isPending}
                />
                {fieldErrors.aliasText && (
                  <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.aliasText}</span>
                )}
              </div>
            </div>
          </fieldset>

          <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
            <input
              type="checkbox"
              checked={form.isCommonItem}
              onChange={(e) => updateField("isCommonItem", e.target.checked)}
              disabled={isPending}
              className={`rounded border-[var(--ophalo-border)] ${FOCUS_RING}`}
            />
            Common item
          </label>

          <div className="flex flex-col gap-1 pt-2 border-t border-[var(--ophalo-border)]">
            <span className="text-sm font-medium text-[var(--ophalo-ink)]">Pricing</span>
            <span className="text-xs text-[var(--ophalo-muted)]">Prices in {ACCOUNT_CURRENCY}</span>
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-cost">
              Cost <span className="text-[var(--ophalo-muted)] font-normal">(optional)</span>
            </label>
            <div className="relative">
              <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-[var(--ophalo-muted)]">
                $
              </span>
              <input
                id="ci-cost"
                ref={(el) => {
                  fieldRefs.current.cost = el;
                }}
                type="number"
                inputMode="decimal"
                min={0}
                step="0.01"
                value={form.cost}
                onChange={(e) => {
                  updateField("cost", e.target.value);
                  setBelowCostConfirmed(false);
                }}
                className={`${INPUT_CLS} pl-6 ${fieldErrors.cost ? ERROR_INPUT_CLS : ""}`}
                disabled={isPending}
              />
            </div>
            {fieldErrors.cost && <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.cost}</span>}
          </div>

          <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
            <input
              type="checkbox"
              checked={form.pricingMode === "NoStandalonePrice"}
              onChange={(e) => {
                if (e.target.checked) {
                  updateField("pricingMode", "NoStandalonePrice");
                  updateField("sellPrice", "");
                  setBelowCostConfirmed(false);
                } else {
                  updateField("pricingMode", "StandalonePrice");
                }
              }}
              disabled={isPending}
              className={`rounded border-[var(--ophalo-border)] ${FOCUS_RING}`}
            />
            This item doesn&apos;t have its own sell price
          </label>

          {form.pricingMode === "StandalonePrice" && (
            <div className="flex flex-col gap-1.5">
              <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-sell-price">
                Sell price
              </label>
              <div className="relative">
                <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-[var(--ophalo-muted)]">
                  $
                </span>
                <input
                  id="ci-sell-price"
                  ref={(el) => {
                    fieldRefs.current.sellPrice = el;
                  }}
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="0.01"
                  value={form.sellPrice}
                  onChange={(e) => {
                    updateField("sellPrice", e.target.value);
                    setBelowCostConfirmed(false);
                  }}
                  className={`${INPUT_CLS} pl-6 ${fieldErrors.sellPrice ? ERROR_INPUT_CLS : ""}`}
                  disabled={isPending}
                />
              </div>
              {fieldErrors.sellPrice && (
                <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.sellPrice}</span>
              )}
            </div>
          )}

          {showBelowCostWarning && (
            <div className="rounded-lg px-3 py-2 bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)] flex flex-col gap-2">
              <p className="text-sm">Sell price is below cost. This is permitted, but confirm it's intentional.</p>
              <label className="flex items-center gap-2 text-sm">
                <input
                  ref={belowCostCheckboxRef}
                  type="checkbox"
                  checked={belowCostConfirmed}
                  onChange={(e) => setBelowCostConfirmed(e.target.checked)}
                  className={`rounded border-[var(--ophalo-border)] ${FOCUS_RING}`}
                />
                I understand this item is priced below cost
              </label>
            </div>
          )}

          {generalError && (
            <p className="text-sm rounded-lg px-3 py-2 text-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)]">
              {generalError}
            </p>
          )}
        </div>

        <div className="shrink-0 px-4 sm:px-6 py-4 border-t border-[var(--ophalo-border)] flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={(e) => handleSubmit(e, true)}
            disabled={isPending || categoryPending}
            className={`text-sm font-medium text-[var(--keep-accent)] hover:underline disabled:opacity-50 rounded ${FOCUS_RING}`}
          >
            Save &amp; add another
          </button>
          <button
            type="submit"
            disabled={isPending || categoryPending}
            className={`px-4 py-2 rounded-lg text-sm font-medium bg-[var(--ophalo-navy)] text-white
              hover:opacity-90 transition-opacity disabled:opacity-50 disabled:cursor-not-allowed ${FOCUS_RING}`}
          >
            {isPending ? "Saving…" : "Save & activate"}
          </button>
        </div>
      </form>

      {showDiscardConfirm && (
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Discard changes"
          className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
        >
          <div className="max-w-xs w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
            <p className="text-sm text-[var(--ophalo-ink)]">Discard your changes to this item?</p>
            <div className="flex items-center justify-end gap-3">
              <button
                ref={keepEditingRef}
                type="button"
                onClick={() => setShowDiscardConfirm(false)}
                className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
              >
                Keep editing
              </button>
              <button
                ref={discardRef}
                type="button"
                onClick={onClose}
                className={`px-3 py-1.5 rounded-lg text-sm font-medium bg-[var(--ophalo-danger)] text-white hover:opacity-90 ${FOCUS_RING}`}
              >
                Discard changes
              </button>
            </div>
          </div>
        </div>
      )}
    </KeepModal>
  );
}
