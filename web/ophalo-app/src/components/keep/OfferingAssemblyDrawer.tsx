import { useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { KeepModal } from "./KeepModal";
import { CatalogItemPicker } from "./CatalogItemPicker";
import {
  api,
  ApiError,
  type CatalogItemListRowResponse,
  type OfferingAssemblyResponse,
} from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

interface DraftItem {
  key: string;
  catalogItemId: string;
  displayName: string;
  defaultQuantity: string;
  isOptional: boolean;
}

interface OfferingAssemblyDrawerProps {
  onClose: () => void;
  onCreated: (result: OfferingAssemblyResponse) => void;
}

const FIELD_ERROR_MESSAGES: Record<string, string> = {
  "OfferingAssembly.NameRequired": "Name is required.",
  "OfferingAssembly.NameTooLong": "Name must not exceed 200 characters.",
  "OfferingAssembly.PrimaryCatalogItemRequired": "A primary catalog item is required.",
  "OfferingAssembly.PrimaryCatalogItemAlreadyClaimed":
    "Another active offering/assembly already uses this primary catalog item.",
  "OfferingAssembly.PrimaryCatalogItemAlreadyAssociated":
    "The primary catalog item cannot also be an associated item.",
  "OfferingAssembly.ItemCatalogItemRequired": "Each associated item needs a catalog item.",
  "OfferingAssembly.ItemCannotBePrimary": "An associated item cannot be the same as the primary.",
  "OfferingAssembly.ItemAlreadyExists": "This catalog item is already an associated item.",
  "OfferingAssembly.ItemQuantityMustBePositive": "Default quantity must be greater than zero.",
  "OfferingAssembly.ItemDisplayOrderMustNotBeNegative": "Display order must not be negative.",
};

function isDirty(name: string, primaryItemId: string | null, items: DraftItem[]): boolean {
  return name.trim() !== "" || primaryItemId !== null || items.length > 0;
}

/**
 * Offering/Assembly creation drawer (Session 3.2c): mirrors CatalogItemDrawer's responsive
 * side-drawer shell and dirty-dismiss protection. Single creation outcome — atomic
 * create-with-items, no separate draft/activate step (matches ADR-479/3.2a.1's no-eligibility-
 * check-at-create posture). Reorder isn't offered here — DisplayOrder is assigned by add order and
 * can be changed later on the detail page via sequential item-update calls (no bulk reorder
 * endpoint exists).
 */
export function OfferingAssemblyDrawer({ onClose, onCreated }: OfferingAssemblyDrawerProps) {
  const [name, setName] = useState("");
  const [primaryItem, setPrimaryItem] = useState<CatalogItemListRowResponse | null>(null);
  const [priceTreatment, setPriceTreatment] = useState<"Summed" | "AllInclusive">("Summed");
  const [items, setItems] = useState<DraftItem[]>([]);
  const [nameError, setNameError] = useState<string | null>(null);
  const [primaryError, setPrimaryError] = useState<string | null>(null);
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  const nameFieldRef = useRef<HTMLInputElement>(null);

  const dirty = isDirty(name, primaryItem?.item.id ?? null, items);

  const createMutation = useMutation({
    mutationFn: () =>
      api.createOfferingAssembly({
        primaryCatalogItemId: primaryItem!.item.id,
        name: name.trim(),
        priceTreatment,
        items: items.map((it, index) => ({
          catalogItemId: it.catalogItemId,
          defaultQuantity: Number(it.defaultQuantity),
          isOptional: it.isOptional,
          displayOrder: index,
        })),
      }),
    onSuccess: (result) => {
      onCreated(result);
      onClose();
    },
    onError: (err) => {
      setNameError(null);
      setPrimaryError(null);
      setGeneralError(null);
      if (err instanceof ApiError && err.code) {
        const message = FIELD_ERROR_MESSAGES[err.code] ?? "Something went wrong. Try again.";
        if (err.code.startsWith("OfferingAssembly.Name")) {
          setNameError(message);
          nameFieldRef.current?.focus();
          return;
        }
        if (err.code.startsWith("OfferingAssembly.Primary")) {
          setPrimaryError(message);
          return;
        }
        setGeneralError(message);
        return;
      }
      setGeneralError("Something went wrong. Try again.");
    },
  });

  function attemptClose() {
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  function addItemRow() {
    setItems((prev) => [
      ...prev,
      { key: `${Date.now()}-${prev.length}`, catalogItemId: "", displayName: "", defaultQuantity: "1", isOptional: false },
    ]);
  }

  function updateItemRow(key: string, patch: Partial<DraftItem>) {
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, ...patch } : it)));
  }

  function removeItemRow(key: string) {
    setItems((prev) => prev.filter((it) => it.key !== key));
  }

  function validateBeforeSubmit(): boolean {
    let ok = true;
    if (!name.trim()) {
      setNameError("Name is required.");
      nameFieldRef.current?.focus();
      ok = false;
    } else {
      setNameError(null);
    }
    if (!primaryItem) {
      setPrimaryError("A primary catalog item is required.");
      ok = false;
    } else {
      setPrimaryError(null);
    }
    return ok;
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (createMutation.isPending) return;
    if (!validateBeforeSubmit()) return;
    createMutation.mutate();
  }

  function excludeIdsForRow(rowKey: string): string[] {
    const primaryId = primaryItem ? [primaryItem.item.id] : [];
    const otherRowIds = items.filter((it) => it.key !== rowKey && it.catalogItemId).map((it) => it.catalogItemId);
    return [...primaryId, ...otherRowIds];
  }

  return (
    <KeepModal
      onClose={attemptClose}
      label="New offering/assembly"
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 top-0 right-0 h-[100dvh] max-h-[100dvh] w-full sm:w-[520px] bg-[var(--ophalo-card)] shadow-xl flex flex-col"
    >
      <form onSubmit={handleSubmit} className="h-full min-h-0 flex flex-col" {...(showDiscardConfirm ? { inert: true } : {})}>
        <div className="shrink-0 px-4 sm:px-6 py-4 border-b border-[var(--ophalo-border)] flex items-center justify-between">
          <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">New offering/assembly</h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`rounded-lg px-2 py-1 text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            aria-label="Close"
          >
            ×
          </button>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto px-4 sm:px-6 py-4 space-y-4">
          {generalError && (
            <div className="rounded-lg bg-[var(--ophalo-danger-bg)] px-3 py-2 text-sm text-[var(--ophalo-danger)]">
              {generalError}
            </div>
          )}

          <div>
            <label htmlFor="assembly-name" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              Name
            </label>
            <input
              ref={nameFieldRef}
              id="assembly-name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className={`${INPUT_CLS} ${nameError ? ERROR_INPUT_CLS : ""}`}
            />
            {nameError && <p className="mt-1 text-sm text-[var(--ophalo-danger)]">{nameError}</p>}
          </div>

          <div>
            <label htmlFor="assembly-primary" className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              Primary catalog item
            </label>
            <CatalogItemPicker
              id="assembly-primary"
              selectedItemId={primaryItem?.item.id ?? null}
              selectedItemDisplayName={primaryItem?.item.displayName ?? null}
              onSelect={(row) => setPrimaryItem(row)}
              invalid={!!primaryError}
            />
            {primaryError && <p className="mt-1 text-sm text-[var(--ophalo-danger)]">{primaryError}</p>}
          </div>

          <div>
            <span className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">Price treatment</span>
            <div className="flex gap-4">
              {(["Summed", "AllInclusive"] as const).map((option) => (
                <label key={option} className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
                  <input
                    type="radio"
                    name="priceTreatment"
                    checked={priceTreatment === option}
                    onChange={() => setPriceTreatment(option)}
                  />
                  {option === "Summed" ? "Summed (component prices add up)" : "All-inclusive (primary's own price)"}
                </label>
              ))}
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-1">
              <span className="block text-sm font-medium text-[var(--ophalo-ink)]">Associated items</span>
              <button
                type="button"
                onClick={addItemRow}
                className={`text-sm font-medium text-[var(--keep-accent)] hover:underline ${FOCUS_RING}`}
              >
                + Add item
              </button>
            </div>
            <p className="mb-2 text-xs text-[var(--ophalo-muted)]">
              Required items are included in the base assembly and added to Actual Work by default. Optional items are
              excluded from the base price and added only when needed.
            </p>
            <div className="space-y-2">
              {items.map((it) => (
                <div key={it.key} className="rounded-lg border border-[var(--ophalo-border)] p-2 space-y-2">
                  <CatalogItemPicker
                    id={`assembly-item-${it.key}`}
                    selectedItemId={it.catalogItemId || null}
                    selectedItemDisplayName={it.displayName || null}
                    excludeIds={excludeIdsForRow(it.key)}
                    onSelect={(row) =>
                      updateItemRow(it.key, { catalogItemId: row.item.id, displayName: row.item.displayName })
                    }
                  />
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                    <label className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)]">
                      Qty
                      <input
                        type="number"
                        min="0.01"
                        step="any"
                        value={it.defaultQuantity}
                        onChange={(e) => updateItemRow(it.key, { defaultQuantity: e.target.value })}
                        className={`${INPUT_CLS} w-20`}
                      />
                    </label>
                    <label className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)] whitespace-normal">
                      <input
                        type="checkbox"
                        checked={it.isOptional}
                        onChange={(e) => updateItemRow(it.key, { isOptional: e.target.checked })}
                        aria-label="Optional — add only when needed"
                      />
                      <span>
                        Optional <span className="text-[var(--ophalo-muted)]">— add only when needed</span>
                      </span>
                    </label>
                    <button
                      type="button"
                      onClick={() => removeItemRow(it.key)}
                      className={`ml-auto text-sm text-[var(--ophalo-danger)] hover:underline ${FOCUS_RING}`}
                    >
                      Remove
                    </button>
                  </div>
                </div>
              ))}
              {items.length === 0 && (
                <p className="text-sm text-[var(--ophalo-muted)]">No associated items yet.</p>
              )}
            </div>
          </div>
        </div>

        <div className="shrink-0 px-4 sm:px-6 py-4 border-t border-[var(--ophalo-border)] flex justify-end gap-2">
          <button
            type="button"
            onClick={attemptClose}
            className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={createMutation.isPending}
            className={`rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-60 ${FOCUS_RING}`}
          >
            {createMutation.isPending ? "Creating…" : "Create"}
          </button>
        </div>

        {showDiscardConfirm && (
          <div className="absolute inset-0 z-10 flex items-center justify-center bg-black/20">
            <div
              role="alertdialog"
              aria-modal="true"
              className="rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 max-w-sm w-full mx-4 space-y-3"
            >
              <p className="text-sm text-[var(--ophalo-ink)]">Discard this new offering/assembly?</p>
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setShowDiscardConfirm(false)}
                  className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                >
                  Keep editing
                </button>
                <button
                  type="button"
                  onClick={onClose}
                  className={`rounded-lg border border-[var(--ophalo-danger)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                >
                  Discard
                </button>
              </div>
            </div>
          </div>
        )}
      </form>
    </KeepModal>
  );
}
