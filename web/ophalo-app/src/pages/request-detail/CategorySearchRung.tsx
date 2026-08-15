import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiError, type FieldCatalogCategoryResponse, type FieldCatalogItemResponse } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ScopeCaptureRungProps } from "./ProposedScopeCaptureModal";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const CONFLICT_NOTICE = "This proposed scope changed elsewhere — refreshed with the latest scope. Try again.";

/**
 * ADR-461 rung 3: browse the account-owned, client-named categories, then the common items filed
 * under one — never a fixed trade taxonomy. Same price-free field-catalog read as
 * {@link CommonItemsRung}, scoped by categoryId.
 */
export function CategorySearchRung({ proposedScopeId, version, onCommitted, onConflict }: ScopeCaptureRungProps) {
  const [category, setCategory] = useState<FieldCatalogCategoryResponse | null>(null);
  const [selectedItem, setSelectedItem] = useState<FieldCatalogItemResponse | null>(null);
  const [quantity, setQuantity] = useState("1");
  const [error, setError] = useState<string | null>(null);

  const { data: categoryList, isLoading: isCategoryLoading } = useQuery({
    queryKey: ["fieldCatalogCategories"],
    queryFn: () => api.getFieldCatalogCategories(),
  });

  const { data: itemPage, isLoading: isItemLoading } = useQuery({
    queryKey: ["fieldCatalogItems", "byCategory", category?.id],
    queryFn: () => api.getFieldCatalogItems({ categoryId: category!.id, limit: 50 }),
    enabled: category !== null,
  });

  const addMutation = useMutation({
    mutationFn: () =>
      api.fieldSelectProposedScopeLine(
        proposedScopeId,
        { lineType: "KnownCatalogItem", catalogItemId: selectedItem!.id, quantity: Number(quantity) },
        version,
      ),
    onSuccess: () => {
      setError(null);
      setSelectedItem(null);
      setQuantity("1");
      onCommitted();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        onConflict(CONFLICT_NOTICE);
        setSelectedItem(null);
        return;
      }
      if (!(err instanceof ApiError)) {
        onConflict(CONFLICT_NOTICE);
        setSelectedItem(null);
        return;
      }
      if (err.code === "ProposedScope.LineCatalogItemNotFound") {
        setError("This item is no longer available.");
        setSelectedItem(null);
        return;
      }
      if (err.code === "ProposedScope.LineQuantityMustBePositive") {
        setError("Quantity must be greater than zero.");
        return;
      }
      setError("Something went wrong. Try again.");
    },
  });

  if (selectedItem !== null) {
    return (
      <div className="space-y-3">
        <button
          type="button"
          onClick={() => {
            setSelectedItem(null);
            setError(null);
          }}
          className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
        >
          ← Choose a different item
        </button>
        <p className="text-sm font-medium text-[var(--ophalo-ink)]">{selectedItem.displayName}</p>
        <div>
          <label htmlFor="category-item-quantity" className="block text-xs text-[var(--ophalo-muted)] mb-1">
            Quantity ({selectedItem.unitOfMeasure})
          </label>
          <input
            id="category-item-quantity"
            type="number"
            min="0"
            step="any"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            className={INPUT_CLS}
          />
        </div>
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

  if (category !== null) {
    const items = itemPage?.items ?? [];
    return (
      <div className="space-y-2">
        <button
          type="button"
          onClick={() => setCategory(null)}
          className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
        >
          ← Choose a different category
        </button>
        <p className="text-sm font-medium text-[var(--ophalo-ink)]">{category.name}</p>
        {isItemLoading && <p className="text-sm text-[var(--ophalo-muted)]">Loading…</p>}
        {!isItemLoading && items.length === 0 && (
          <p className="text-sm text-[var(--ophalo-muted)]">No common items in this category.</p>
        )}
        <ul className="max-h-48 overflow-y-auto space-y-1">
          {items.map((row) => (
            <li key={row.item.id}>
              <button
                type="button"
                onClick={() => setSelectedItem(row.item)}
                className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
              >
                {row.item.displayName}
              </button>
            </li>
          ))}
        </ul>
      </div>
    );
  }

  const categories = categoryList?.categories ?? [];

  return (
    <div className="space-y-2">
      {isCategoryLoading && <p className="text-sm text-[var(--ophalo-muted)]">Loading…</p>}
      {!isCategoryLoading && categories.length === 0 && (
        <p className="text-sm text-[var(--ophalo-muted)]">No categories configured.</p>
      )}
      <ul className="max-h-48 overflow-y-auto space-y-1">
        {categories.map((cat) => (
          <li key={cat.id}>
            <button
              type="button"
              onClick={() => setCategory(cat)}
              className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            >
              {cat.name}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
