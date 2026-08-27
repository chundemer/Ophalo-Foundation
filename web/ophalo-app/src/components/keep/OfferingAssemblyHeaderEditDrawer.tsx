import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { KeepModal } from "./KeepModal";
import { CatalogItemPicker } from "./CatalogItemPicker";
import { api, ApiError, type OfferingAssemblyDetailResult } from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

/** Header-only draft for an offering/assembly — the subset this drawer edits. Associated items,
 *  pricing/eligibility, and activation stay on the detail page (session-log item 2). */
export interface OfferingAssemblyHeaderDraft {
  primaryCatalogItemId: string;
  primaryCatalogItemDisplayName: string;
  name: string;
  priceTreatment: "Summed" | "AllInclusive";
}

export function toOfferingAssemblyHeaderDraft(
  detail: OfferingAssemblyDetailResult,
): OfferingAssemblyHeaderDraft {
  return {
    primaryCatalogItemId: detail.primaryCatalogItemId,
    primaryCatalogItemDisplayName: detail.primaryCatalogItemDisplayName,
    name: detail.name,
    priceTreatment: detail.priceTreatment as "Summed" | "AllInclusive",
  };
}

type FieldErrors = { name?: string; primary?: string };

const FIELD_ERROR_MESSAGES: Record<string, string> = {
  "OfferingAssembly.NameRequired": "Name is required.",
  "OfferingAssembly.NameTooLong": "Name must not exceed 200 characters.",
  "OfferingAssembly.PrimaryCatalogItemRequired": "A primary catalog item is required.",
  "OfferingAssembly.PrimaryCatalogItemAlreadyClaimed":
    "Another active offering/assembly already uses this primary catalog item.",
  "OfferingAssembly.PrimaryCatalogItemAlreadyAssociated":
    "The primary catalog item cannot also be an associated item.",
};

interface OfferingAssemblyHeaderEditDrawerProps {
  assembly: OfferingAssemblyDetailResult;
  /** A prior unsaved draft to restore after a version conflict; otherwise seed from `assembly`. */
  initialDraft?: OfferingAssemblyHeaderDraft | null;
  /** Plain dismiss with no save (Cancel / Escape / discard-confirm). */
  onClose: () => void;
  /** Save succeeded — the detail page owns cache invalidation and closing the drawer. */
  onSaved: () => void;
  /** Save hit OfferingAssembly.VersionMismatch — hand the draft back so the detail page owns
   *  refetch, Edit-disable-during-refresh, and one-time draft restoration. */
  onVersionConflict: (draft: OfferingAssemblyHeaderDraft) => void;
}

/**
 * Dedicated offering/assembly header edit drawer (session-log item 2): the focused responsive
 * side drawer for name, primary catalog item, and price treatment. Owns its own form state,
 * validation presentation, dirty-dismiss protection, and field-level API errors. The detail page
 * keeps refresh/invalidation and version-conflict recovery. Deliberately not a mode on
 * `OfferingAssemblyDrawer` — that component also owns associated-item rows at create time, an
 * intentionally different scope.
 */
export function OfferingAssemblyHeaderEditDrawer({
  assembly,
  initialDraft,
  onClose,
  onSaved,
  onVersionConflict,
}: OfferingAssemblyHeaderEditDrawerProps) {
  // Baseline is always the assembly as loaded, so a restored conflict draft reads as dirty and
  // is protected by the discard confirmation — an abandoned re-apply never drops silently.
  const baselineRef = useRef<OfferingAssemblyHeaderDraft>(toOfferingAssemblyHeaderDraft(assembly));
  // Frozen at mount: Save targets the version whose values the drawer is showing, so a background
  // refetch can't let a save land against a version the user never saw.
  const versionRef = useRef(assembly.concurrencyVersion);
  const [form, setForm] = useState<OfferingAssemblyHeaderDraft>(
    () => initialDraft ?? baselineRef.current,
  );
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);

  const nameRef = useRef<HTMLInputElement>(null);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);

  const baseline = baselineRef.current;
  const dirty =
    form.name !== baseline.name ||
    form.primaryCatalogItemId !== baseline.primaryCatalogItemId ||
    form.priceTreatment !== baseline.priceTreatment;

  const mutation = useMutation({
    mutationFn: () =>
      api.updateOfferingAssemblyHeader(
        assembly.id,
        {
          primaryCatalogItemId: form.primaryCatalogItemId,
          name: form.name.trim(),
          priceTreatment: form.priceTreatment,
        },
        versionRef.current,
      ),
    onSuccess: () => onSaved(),
    onError: (err: unknown) => {
      setFieldErrors({});
      setFormError(null);
      if (err instanceof ApiError && err.code === "OfferingAssembly.VersionMismatch") {
        onVersionConflict(form);
        return;
      }
      if (err instanceof ApiError && err.code) {
        const message = FIELD_ERROR_MESSAGES[err.code] ?? "Could not save changes. Try again.";
        if (err.code.startsWith("OfferingAssembly.Name")) {
          setFieldErrors({ name: message });
          nameRef.current?.focus();
          return;
        }
        if (err.code.startsWith("OfferingAssembly.PrimaryCatalogItem")) {
          setFieldErrors({ primary: message });
          return;
        }
      }
      setFormError("Could not save changes. Try again.");
    },
  });

  // Nested discard-confirm inside KeepModal's dialog — see CatalogItemEditDrawer for the rationale
  // (capture-phase Escape/Tab, `inert` form). Duplicated deliberately: no shared helper this slice.
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
    let ok = true;
    const errors: FieldErrors = {};
    if (!form.name.trim()) {
      errors.name = "Name is required.";
      ok = false;
    }
    if (!form.primaryCatalogItemId) {
      errors.primary = "A primary catalog item is required.";
      ok = false;
    }
    setFieldErrors(errors);
    if (errors.name) nameRef.current?.focus();
    return ok;
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (mutation.isPending) return;
    setFieldErrors({});
    setFormError(null);
    if (!validateBeforeSubmit()) return;
    mutation.mutate();
  }

  const isPending = mutation.isPending;

  return (
    <KeepModal
      onClose={attemptClose}
      backdropClosable={false}
      label="Edit offering/assembly"
      backdropClassName="bg-black/30"
      panelClassName="fixed z-50 top-0 right-0 h-[100dvh] max-h-[100dvh] w-full sm:w-[520px] bg-[var(--ophalo-card)] shadow-xl flex flex-col"
    >
      <form
        onSubmit={handleSubmit}
        className="h-full min-h-0 flex flex-col"
        {...(showDiscardConfirm ? { inert: true } : {})}
      >
        <div className="shrink-0 px-4 sm:px-6 py-4 border-b border-[var(--ophalo-border)] flex items-center justify-between">
          <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">
            Edit offering/assembly
          </h2>
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
          {formError && (
            <div className="rounded-lg bg-[var(--ophalo-danger-bg)] px-3 py-2 text-sm text-[var(--ophalo-danger)]">
              {formError}
            </div>
          )}

          <div>
            <label
              htmlFor="assembly-header-edit-name"
              className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1"
            >
              Name
            </label>
            <input
              id="assembly-header-edit-name"
              ref={nameRef}
              type="text"
              value={form.name}
              onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
              disabled={isPending}
              className={`${INPUT_CLS} ${fieldErrors.name ? ERROR_INPUT_CLS : ""}`}
            />
            {fieldErrors.name && (
              <p className="mt-1 text-sm text-[var(--ophalo-danger)]">{fieldErrors.name}</p>
            )}
          </div>

          <div>
            <label
              htmlFor="assembly-header-edit-primary"
              className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1"
            >
              Primary catalog item
            </label>
            <CatalogItemPicker
              id="assembly-header-edit-primary"
              selectedItemId={form.primaryCatalogItemId || null}
              selectedItemDisplayName={form.primaryCatalogItemDisplayName || null}
              onSelect={(row) =>
                setForm((f) => ({
                  ...f,
                  primaryCatalogItemId: row.item.id,
                  primaryCatalogItemDisplayName: row.item.displayName,
                }))
              }
              disabled={isPending}
              invalid={!!fieldErrors.primary}
            />
            {fieldErrors.primary && (
              <p className="mt-1 text-sm text-[var(--ophalo-danger)]">{fieldErrors.primary}</p>
            )}
          </div>

          <div>
            <span className="block text-sm font-medium text-[var(--ophalo-ink)] mb-1">
              Price treatment
            </span>
            <div className="flex gap-4">
              {(["Summed", "AllInclusive"] as const).map((option) => (
                <label
                  key={option}
                  className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]"
                >
                  <input
                    type="radio"
                    name="priceTreatmentHeaderEdit"
                    checked={form.priceTreatment === option}
                    onChange={() => setForm((f) => ({ ...f, priceTreatment: option }))}
                    disabled={isPending}
                  />
                  {option === "Summed" ? "Summed" : "All-inclusive"}
                </label>
              ))}
            </div>
          </div>
        </div>

        <div className="shrink-0 px-4 sm:px-6 py-4 border-t border-[var(--ophalo-border)] flex justify-end gap-2">
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
            disabled={isPending}
            className={`rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-60 ${FOCUS_RING}`}
          >
            {isPending ? "Saving…" : "Save"}
          </button>
        </div>
      </form>

      {/* Sibling of the form, never a descendant — the form carries `inert` while this is open,
          which would also disable these buttons if they lived inside it. */}
      {showDiscardConfirm && (
        <div className="absolute inset-0 z-10 flex items-center justify-center bg-black/20">
          <div
            role="alertdialog"
            aria-modal="true"
            aria-label="Discard changes"
            className="rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 max-w-sm w-full mx-4 space-y-3"
          >
            <p className="text-sm text-[var(--ophalo-ink)]">
              Discard your changes to this offering/assembly?
            </p>
            <div className="flex justify-end gap-2">
              <button
                ref={keepEditingRef}
                type="button"
                onClick={() => setShowDiscardConfirm(false)}
                className={`rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
              >
                Keep editing
              </button>
              <button
                ref={discardRef}
                type="button"
                onClick={onClose}
                className={`rounded-lg border border-[var(--ophalo-danger)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
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
