import { forwardRef, useEffect, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiError, type FieldScopeSearchResultResponse } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import type { ScopeNudgeTrigger } from "./useProposedScopeCapture";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const DESCRIPTION_MAX_LENGTH = 200;

interface ComposerSearchAndAddProps {
  proposedScopeId: string;
  version: string;
  onCommitted: (trigger?: ScopeNudgeTrigger) => void;
  onConflict: () => void;
}

type Selection =
  | { kind: "catalog"; item: FieldScopeSearchResultResponse }
  | { kind: "custom" };

/**
 * Build Log 121, ADR-486: the single unified Name/SKU/Alias search input. Results render grouped
 * as Matching assemblies, then Matching catalog items, then the always-last "Add as custom item"
 * action — typing alone never writes a line. An assembly pick dispatches `expand-assembly`
 * immediately; a catalog pick or custom item goes through the existing quantity/note confirmation
 * and `field-select`.
 *
 * The forwarded ref targets the search input itself so the composer shell can hand it to
 * `KeepModal`'s `initialFocus` — the modal's default first-focusable pick would be the close
 * button, not this input.
 */
export const ComposerSearchAndAdd = forwardRef<HTMLInputElement, ComposerSearchAndAddProps>(function ComposerSearchAndAdd(
  { proposedScopeId, version, onCommitted, onConflict },
  searchInputRef,
) {
  const [searchText, setSearchText] = useState("");
  const [debouncedText, setDebouncedText] = useState("");
  const [selection, setSelection] = useState<Selection | null>(null);
  const [customDescription, setCustomDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [descriptionError, setDescriptionError] = useState<string | null>(null);

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedText(searchText.trim()), 250);
    return () => clearTimeout(handle);
  }, [searchText]);

  const {
    data: listPage,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ["fieldScopeSearch", "search", debouncedText],
    queryFn: () => api.getFieldScopeSearch({ search: debouncedText, limit: 20 }),
    enabled: selection === null && debouncedText.length > 0,
  });

  function resetAfterSuccess() {
    setError(null);
    setDescriptionError(null);
    setSelection(null);
    setSearchText("");
    setDebouncedText("");
    setCustomDescription("");
    setQuantity("1");
    setNote("");
  }

  const addMutation = useMutation({
    mutationFn: () => {
      if (selection?.kind === "catalog") {
        return api.fieldSelectProposedScopeLine(
          proposedScopeId,
          { lineType: "KnownCatalogItem", catalogItemId: selection.item.id, quantity: Number(quantity), note: note.trim() || null },
          version,
        );
      }
      return api.fieldSelectProposedScopeLine(
        proposedScopeId,
        { lineType: "OffCatalogItem", offCatalogDescription: customDescription, quantity: Number(quantity), note: note.trim() || null },
        version,
      );
    },
    onSuccess: () => {
      const trigger: ScopeNudgeTrigger | undefined =
        selection?.kind === "catalog" ? { catalogItemId: selection.item.id } : undefined;
      resetAfterSuccess();
      onCommitted(trigger);
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        onConflict();
        return;
      }
      if (!(err instanceof ApiError)) {
        onConflict();
        return;
      }
      if (err.code === "ProposedScope.LineCatalogItemNotFound") {
        setError("This item is no longer available.");
        return;
      }
      if (err.code === "ProposedScope.LineOffCatalogDescriptionInvalidCharacters") {
        setDescriptionError("This description contains characters that aren't allowed.");
        return;
      }
      if (err.code === "ProposedScope.LineQuantityMustBePositive") {
        setError("Quantity must be greater than zero.");
        return;
      }
      setError("Something went wrong. Try again.");
    },
  });

  const expandMutation = useMutation({
    mutationFn: (row: FieldScopeSearchResultResponse) =>
      api.expandProposedScopeAssembly(proposedScopeId, { offeringAssemblyId: row.id, excludedOptionalItemIds: [] }, version),
    onSuccess: (_data, row) => {
      resetAfterSuccess();
      onCommitted({ offeringAssemblyId: row.id });
    },
    onError: () => onConflict(),
  });

  if (selection !== null) {
    return (
      <div className="space-y-3">
        <button
          type="button"
          onClick={() => {
            setSelection(null);
            setError(null);
            setDescriptionError(null);
          }}
          className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
        >
          ← Choose a different item
        </button>
        {selection.kind === "catalog" ? (
          <p className="text-sm font-medium text-[var(--ophalo-ink)]">{selection.item.displayName}</p>
        ) : (
          <div>
            <label htmlFor="composer-custom-description" className="block text-xs text-[var(--ophalo-muted)] mb-1">
              Custom item description
            </label>
            <input
              id="composer-custom-description"
              type="text"
              value={customDescription}
              onChange={(e) => setCustomDescription(e.target.value)}
              maxLength={DESCRIPTION_MAX_LENGTH}
              aria-invalid={descriptionError ? true : undefined}
              aria-describedby={descriptionError ? "composer-custom-description-error" : undefined}
              className={INPUT_CLS}
            />
            {descriptionError && (
              <p id="composer-custom-description-error" role="alert" className="text-sm text-[var(--ophalo-danger)] mt-1">
                {descriptionError}
              </p>
            )}
          </div>
        )}
        <div>
          <label htmlFor="composer-add-quantity" className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Quantity
          </label>
          <input
            id="composer-add-quantity"
            type="number"
            min="0"
            step="any"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            className={INPUT_CLS}
          />
          {/* `error` also covers non-quantity failures (item unavailable, generic mutation failure),
              so it is announced via role="alert" rather than aria-invalid/aria-describedby on this
              field — that pairing would incorrectly mark quantity itself as invalid in those cases. */}
          {error && (
            <p role="alert" className="text-sm text-[var(--ophalo-danger)] mt-1">
              {error}
            </p>
          )}
        </div>
        <div>
          <label htmlFor="composer-add-note" className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Note
          </label>
          <input id="composer-add-note" type="text" value={note} onChange={(e) => setNote(e.target.value)} className={INPUT_CLS} />
        </div>
        <KeepButton
          type="button"
          variant="teal"
          disabled={addMutation.isPending}
          onClick={() => addMutation.mutate()}
          className="w-full"
        >
          Add to scope
        </KeepButton>
      </div>
    );
  }

  const results = listPage?.items ?? [];
  const assemblyResults = results.filter((row) => row.kind === "OfferingAssembly");
  const catalogResults = results.filter((row) => row.kind === "CatalogItem");
  const hasResults = assemblyResults.length > 0 || catalogResults.length > 0;

  return (
    <div className="space-y-2">
      <label htmlFor="composer-search-input" className="sr-only">
        Search catalog items and assemblies
      </label>
      <input
        ref={searchInputRef}
        id="composer-search-input"
        type="text"
        value={searchText}
        onChange={(e) => setSearchText(e.target.value)}
        placeholder="Search by name, SKU, or alias…"
        maxLength={DESCRIPTION_MAX_LENGTH}
        className={INPUT_CLS}
      />
      {debouncedText.length === 0 && (
        <p className="text-sm text-[var(--ophalo-muted)]">Type to search catalog items and assemblies.</p>
      )}
      {debouncedText.length > 0 && isLoading && <p className="text-sm text-[var(--ophalo-muted)]">Searching…</p>}
      {debouncedText.length > 0 && !isLoading && isError && (
        <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
          Search failed. Try again.
        </p>
      )}
      {debouncedText.length > 0 && !isLoading && !isError && (
        <>
          {!hasResults && <p className="text-sm text-[var(--ophalo-muted)]">No Price Book matches</p>}
          <ul className="max-h-48 overflow-y-auto space-y-1">
            {assemblyResults.length > 0 && (
              <li className="rounded-lg border-l-4 border-[var(--keep-accent)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]">
                Matching assemblies
              </li>
            )}
            {assemblyResults.map((row) => (
              <li key={row.id}>
                <button
                  type="button"
                  disabled={expandMutation.isPending}
                  onClick={() => expandMutation.mutate(row)}
                  className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-50 ${FOCUS_RING}`}
                >
                  <span>{row.displayName}</span>
                  <span className="ml-2 rounded bg-[var(--ophalo-canvas)] px-1.5 py-0.5 text-xs font-medium text-[var(--ophalo-muted)]">
                    Assembly
                  </span>
                  {row.defaultItemCount !== null && (
                    <span className="ml-2 text-xs text-[var(--ophalo-muted)]">Expands {row.defaultItemCount} items</span>
                  )}
                </button>
              </li>
            ))}
            {catalogResults.length > 0 && (
              <li className="mt-3 rounded-lg border-l-4 border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]">
                Matching catalog items
              </li>
            )}
            {catalogResults.map((row) => (
              <li key={row.id}>
                <button
                  type="button"
                  onClick={() => setSelection({ kind: "catalog", item: row })}
                  className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                >
                  <span>{row.displayName}</span>
                  {row.catalogItemType && (
                    <span className="ml-2 rounded bg-[var(--ophalo-canvas)] px-1.5 py-0.5 text-xs font-medium text-[var(--ophalo-muted)]">
                      {row.catalogItemType}
                    </span>
                  )}
                </button>
              </li>
            ))}
            <li>
              <button
                type="button"
                onClick={() => {
                  setCustomDescription(debouncedText);
                  setSelection({ kind: "custom" });
                }}
                className={`w-full text-left rounded-lg px-3 py-2 text-sm font-medium text-[var(--keep-accent)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
              >
                Add “{debouncedText}” as custom item
              </button>
            </li>
          </ul>
        </>
      )}
    </div>
  );
});
