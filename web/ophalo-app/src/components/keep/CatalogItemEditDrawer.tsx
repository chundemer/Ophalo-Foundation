import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { KeepModal } from "./KeepModal";
import { CategoryCombobox } from "./CategoryCombobox";
import {
  api,
  ApiError,
  type CatalogCategoryResponse,
  type CatalogItemResponse,
} from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

/** Header/identity-only draft for a catalog item — the subset this drawer edits. Pricing,
 *  cost, and aliases stay on the detail page (Price Book editing model, session-log item 2). */
export interface CatalogItemHeaderDraft {
  displayName: string;
  externalKey: string;
  categoryId: string;
  isCommonItem: boolean;
}

export function toCatalogItemHeaderDraft(item: CatalogItemResponse): CatalogItemHeaderDraft {
  return {
    displayName: item.displayName,
    externalKey: item.externalKey ?? "",
    categoryId: item.categoryId ?? "",
    isCommonItem: item.isCommonItem,
  };
}

type FieldErrors = { displayName?: string; externalKey?: string; categoryId?: string };

function mapErrorToField(code: string | undefined): keyof FieldErrors | null {
  switch (code) {
    case "CatalogItem.DisplayNameRequired":
    case "CatalogItem.DisplayNameTooLong":
      return "displayName";
    case "CatalogItem.InvalidExternalKey":
    case "CatalogItem.ExternalKeyAlreadyExists":
      return "externalKey";
    case "CatalogCategory.NotFound":
    case "CatalogCategory.NotActive":
      return "categoryId";
    default:
      return null;
  }
}

const FIELD_ERROR_MESSAGES: Record<string, string> = {
  "CatalogItem.DisplayNameRequired": "Display name is required.",
  "CatalogItem.DisplayNameTooLong": "Display name must not exceed 200 characters.",
  "CatalogItem.InvalidExternalKey": "SKU must contain at least one letter or number.",
  "CatalogItem.ExternalKeyAlreadyExists": "A catalog item with this SKU already exists.",
  "CatalogCategory.NotFound": "This category no longer exists. Reload and pick another.",
  "CatalogCategory.NotActive": "This category is no longer active. Reload and pick another.",
};

interface CatalogItemEditDrawerProps {
  item: CatalogItemResponse;
  /** The item's current category, if any — kept selectable even when inactive. */
  currentCategory: CatalogCategoryResponse | null;
  categories: CatalogCategoryResponse[];
  /** A prior unsaved draft to restore after a version conflict; otherwise seed from `item`. */
  initialDraft?: CatalogItemHeaderDraft | null;
  onCategoriesChanged: () => void;
  /** Plain dismiss with no save (Cancel / Escape / backdrop / discard-confirm). */
  onClose: () => void;
  /** Save succeeded — the detail page owns cache invalidation and closing the drawer. */
  onSaved: () => void;
  /** Save hit CatalogItem.VersionMismatch — hand the draft back so the detail page owns
   *  refetch, Edit-disable-during-refresh, and draft restoration on the next deliberate Edit. */
  onVersionConflict: (draft: CatalogItemHeaderDraft) => void;
}

/**
 * Dedicated catalog item header/identity edit drawer (session-log item 2): the focused
 * responsive side drawer for display name, SKU, category, and Common Item. Owns its own form
 * state, validation presentation, dirty-dismiss protection, and field-level API errors. The
 * detail page keeps refresh/invalidation and version-conflict recovery. Deliberately does not
 * reuse `CatalogItemDrawer` — that component's scope (pricing, aliases, below-cost gate,
 * Save & add another) is intentionally different from an edit of the identity subset.
 */
export function CatalogItemEditDrawer({
  item,
  currentCategory,
  categories,
  initialDraft,
  onCategoriesChanged,
  onClose,
  onSaved,
  onVersionConflict,
}: CatalogItemEditDrawerProps) {
  // Baseline is always the item as loaded, so a restored conflict draft reads as dirty and is
  // protected by the discard confirmation — an abandoned re-apply never drops silently.
  const baselineRef = useRef<CatalogItemHeaderDraft>(toCatalogItemHeaderDraft(item));
  // Frozen at mount: Save targets the version whose values the drawer is showing, so a background
  // refetch can't let a save land against a version the user never saw.
  const versionRef = useRef(item.concurrencyVersion);
  const [form, setForm] = useState<CatalogItemHeaderDraft>(
    () => initialDraft ?? baselineRef.current,
  );
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  // Reported by CategoryCombobox: true from the start of a category-create attempt until it
  // resolves — blocks Save so it can never fire against an uncommitted category intent.
  const [categoryPending, setCategoryPending] = useState(false);

  const displayNameRef = useRef<HTMLInputElement>(null);
  const externalKeyRef = useRef<HTMLInputElement>(null);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);

  const baseline = baselineRef.current;
  const dirty =
    form.displayName !== baseline.displayName ||
    form.externalKey !== baseline.externalKey ||
    form.categoryId !== baseline.categoryId ||
    form.isCommonItem !== baseline.isCommonItem;

  const mutation = useMutation({
    mutationFn: () =>
      api.updateCatalogItemHeader(
        item.id,
        {
          displayName: form.displayName.trim(),
          externalKey: form.externalKey.trim() === "" ? null : form.externalKey.trim(),
          categoryId: form.categoryId === "" ? null : form.categoryId,
          isCommonItem: form.isCommonItem,
        },
        versionRef.current,
      ),
    onSuccess: () => onSaved(),
    onError: (err: unknown) => {
      setFieldErrors({});
      setFormError(null);
      if (err instanceof ApiError && err.code === "CatalogItem.VersionMismatch") {
        onVersionConflict(form);
        return;
      }
      if (err instanceof ApiError && err.code) {
        const field = mapErrorToField(err.code);
        const message = FIELD_ERROR_MESSAGES[err.code] ?? "Could not save changes. Try again.";
        if (field) {
          setFieldErrors({ [field]: message });
          if (field === "displayName") displayNameRef.current?.focus();
          else if (field === "externalKey") externalKeyRef.current?.focus();
          return;
        }
      }
      setFormError("Could not save changes. Try again.");
    },
  });

  // Discard-confirm is a nested alertdialog inside KeepModal's own dialog. KeepModal's Tab trap
  // spans its whole panel (form fields included), so `inert` on the form removes it from the tab
  // order while the confirm is up; this effect owns initial focus, Tab wrapping between the two
  // confirm buttons, and Escape — on the capture phase so it stops propagation before KeepModal's
  // bubble-phase Escape handler runs. Mirrors CatalogItemDrawer's proven handling.
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
    if (mutation.isPending) return;
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  function validateBeforeSubmit(): boolean {
    if (!form.displayName.trim()) {
      setFieldErrors({ displayName: "Display name is required." });
      displayNameRef.current?.focus();
      return false;
    }
    return true;
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (mutation.isPending || categoryPending) return;
    setFieldErrors({});
    setFormError(null);
    if (!validateBeforeSubmit()) return;
    mutation.mutate();
  }

  const isPending = mutation.isPending;
  const comboboxCategories = categories.filter(
    (c) => c.activeState === "Active" || c.id === currentCategory?.id,
  );

  return (
    <KeepModal
      onClose={attemptClose}
      backdropClosable={false}
      label="Edit item"
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 top-0 right-0 h-[100dvh] max-h-[100dvh] w-full sm:w-[480px] bg-[var(--ophalo-card)] shadow-xl flex flex-col"
    >
      <form
        onSubmit={handleSubmit}
        className="h-full min-h-0 flex flex-col"
        {...(showDiscardConfirm ? { inert: true } : {})}
      >
        <div className="shrink-0 px-4 sm:px-6 py-4 border-b border-[var(--ophalo-border)] flex items-center justify-between">
          <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">Edit item</h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
          >
            Close
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-4 sm:px-6 py-4 flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-edit-display-name">
              Name
            </label>
            <input
              id="ci-edit-display-name"
              ref={displayNameRef}
              type="text"
              value={form.displayName}
              onChange={(e) => setForm((f) => ({ ...f, displayName: e.target.value }))}
              maxLength={200}
              disabled={isPending}
              className={`${INPUT_CLS} ${fieldErrors.displayName ? ERROR_INPUT_CLS : ""}`}
              aria-describedby={fieldErrors.displayName ? "ci-edit-display-name-error" : undefined}
            />
            {fieldErrors.displayName && (
              <span id="ci-edit-display-name-error" className="text-sm text-[var(--ophalo-danger)]">
                {fieldErrors.displayName}
              </span>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-edit-sku">
              SKU
            </label>
            <input
              id="ci-edit-sku"
              ref={externalKeyRef}
              type="text"
              value={form.externalKey}
              onChange={(e) => setForm((f) => ({ ...f, externalKey: e.target.value }))}
              disabled={isPending}
              className={`${INPUT_CLS} ${fieldErrors.externalKey ? ERROR_INPUT_CLS : ""}`}
              aria-describedby={fieldErrors.externalKey ? "ci-edit-sku-error" : undefined}
            />
            {fieldErrors.externalKey && (
              <span id="ci-edit-sku-error" className="text-sm text-[var(--ophalo-danger)]">
                {fieldErrors.externalKey}
              </span>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-[var(--ophalo-ink)]" htmlFor="ci-edit-category">
              Category
            </label>
            <CategoryCombobox
              id="ci-edit-category"
              categories={comboboxCategories}
              currentCategoryId={form.categoryId === "" ? null : form.categoryId}
              onSelect={(categoryId) => setForm((f) => ({ ...f, categoryId: categoryId ?? "" }))}
              creatable
              disabled={isPending}
              invalid={!!fieldErrors.categoryId}
              onCategoriesChanged={onCategoriesChanged}
              onPendingChange={setCategoryPending}
            />
            {fieldErrors.categoryId && (
              <span className="text-sm text-[var(--ophalo-danger)]">{fieldErrors.categoryId}</span>
            )}
          </div>

          <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
            <input
              type="checkbox"
              checked={form.isCommonItem}
              onChange={(e) => setForm((f) => ({ ...f, isCommonItem: e.target.checked }))}
              disabled={isPending}
              className={`rounded border-[var(--ophalo-border)] ${FOCUS_RING}`}
            />
            Common item
          </label>

          {formError && (
            <p className="text-sm rounded-lg px-3 py-2 text-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)]">
              {formError}
            </p>
          )}
        </div>

        <div className="shrink-0 px-4 sm:px-6 py-4 border-t border-[var(--ophalo-border)] flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={attemptClose}
            disabled={isPending}
            className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60 ${FOCUS_RING}`}
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={isPending || categoryPending}
            className={`px-4 py-2 rounded-lg text-sm font-medium bg-[var(--keep-accent)] text-white
              hover:opacity-90 transition-opacity disabled:opacity-60 disabled:cursor-not-allowed ${FOCUS_RING}`}
          >
            {isPending ? "Saving…" : "Save"}
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
