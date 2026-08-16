import { forwardRef, useEffect, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiError, type FieldCatalogItemResponse } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const DESCRIPTION_MAX_LENGTH = 200;

interface ComposerSearchAndAddProps {
  proposedScopeId: string;
  version: string;
  onCommitted: () => void;
  onConflict: () => void;
}

type Selection = { kind: "catalog"; item: FieldCatalogItemResponse } | { kind: "custom" };

/**
 * Session 5B, build-log/120: the single unified Name/SKU/Alias search input. Catalog results and
 * the explicit "Add as custom item" action render from the same entry surface — typing alone never
 * writes a line; a line is only written after an explicit pick and an explicit "Add to scope".
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
  const [quantity, setQuantity] = useState("1");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [descriptionError, setDescriptionError] = useState<string | null>(null);

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedText(searchText.trim()), 250);
    return () => clearTimeout(handle);
  }, [searchText]);

  const { data: listPage, isLoading } = useQuery({
    queryKey: ["fieldCatalogItems", "search", debouncedText],
    queryFn: () => api.getFieldCatalogItems({ search: debouncedText, limit: 20 }),
    enabled: selection === null && debouncedText.length > 0,
  });

  function resetAfterSuccess() {
    setError(null);
    setDescriptionError(null);
    setSelection(null);
    setSearchText("");
    setDebouncedText("");
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
        { lineType: "OffCatalogItem", offCatalogDescription: debouncedText, quantity: Number(quantity), note: note.trim() || null },
        version,
      );
    },
    onSuccess: () => {
      resetAfterSuccess();
      onCommitted();
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

  if (selection !== null) {
    const label = selection.kind === "catalog" ? selection.item.displayName : `“${debouncedText}” (custom item)`;
    const unitLabel = selection.kind === "catalog" ? ` (${selection.item.unitOfMeasure})` : "";
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
        <p className="text-sm font-medium text-[var(--ophalo-ink)]">{label}</p>
        <div>
          <label htmlFor="composer-add-quantity" className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Quantity{unitLabel}
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
        </div>
        <div>
          <label htmlFor="composer-add-note" className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Note
          </label>
          <input id="composer-add-note" type="text" value={note} onChange={(e) => setNote(e.target.value)} className={INPUT_CLS} />
        </div>
        {descriptionError && <p className="text-sm text-[var(--ophalo-danger)]">{descriptionError}</p>}
        {error && <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>}
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

  return (
    <div className="space-y-2">
      <input
        ref={searchInputRef}
        type="text"
        value={searchText}
        onChange={(e) => setSearchText(e.target.value)}
        placeholder="Search by name, SKU, or alias…"
        maxLength={DESCRIPTION_MAX_LENGTH}
        className={INPUT_CLS}
      />
      {debouncedText.length === 0 && (
        <p className="text-sm text-[var(--ophalo-muted)]">Type to search catalog items.</p>
      )}
      {debouncedText.length > 0 && isLoading && <p className="text-sm text-[var(--ophalo-muted)]">Searching…</p>}
      {debouncedText.length > 0 && (
        <ul className="max-h-48 overflow-y-auto space-y-1">
          {results.map((row) => (
            <li key={row.item.id}>
              <button
                type="button"
                onClick={() => setSelection({ kind: "catalog", item: row.item })}
                className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
              >
                {row.item.displayName}
              </button>
            </li>
          ))}
          <li>
            <button
              type="button"
              onClick={() => setSelection({ kind: "custom" })}
              className={`w-full text-left rounded-lg px-3 py-2 text-sm font-medium text-[var(--keep-accent)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            >
              Add “{debouncedText}” as custom item
            </button>
          </li>
        </ul>
      )}
    </div>
  );
});
