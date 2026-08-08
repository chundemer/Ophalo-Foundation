import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Tag } from "lucide-react";
import { api, ApiError, type AccountRole, type GetCatalogItemsParams } from "../lib/apiClient";
import { CatalogItemDrawer } from "../components/keep/CatalogItemDrawer";
import { CategoryCombobox } from "../components/keep/CategoryCombobox";

const SEARCH_DEBOUNCE_MS = 300;

interface PriceBookProps {
  role: AccountRole;
  entitled: boolean;
  entitlementLoading: boolean;
  entitlementError: boolean;
  onRetryEntitlement: () => void;
  onSelectItem: (catalogItemId: string) => void;
}

const TYPE_LABELS: Record<string, string> = {
  Material: "Material",
  Equipment: "Equipment",
  Service: "Service",
  Fee: "Fee",
};

function formatPrice(row: { currentPricingMode: string | null; currentSellPrice: number | null }): string {
  // build-log/112: an item with NoStandalonePrice renders "No standalone price", never $0.00
  // or a blank cell.
  if (row.currentPricingMode === "NoStandalonePrice" || row.currentSellPrice == null) {
    return "No standalone price";
  }
  return row.currentSellPrice.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

/**
 * Price Book workspace shell (Session 2e.4/2e.7, build-log/113): route, entitled nav, unavailable
 * direct-access handling, and the list with debounced search, category/status filters, and
 * Prev/Next pagination against the already-server-supported query params.
 */
export function PriceBook({
  role,
  entitled,
  entitlementLoading,
  entitlementError,
  onRetryEntitlement,
  onSelectItem,
}: PriceBookProps) {
  const isOwnerOrAdmin = role === "owner" || role === "admin";
  const queryClient = useQueryClient();
  const [drawerOpen, setDrawerOpen] = useState(false);

  const [searchInput, setSearchInput] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"Active" | "Inactive">("Active");
  // Cursor visited to reach each page; index 0's cursor is always undefined (first page).
  const [pageCursors, setPageCursors] = useState<(string | undefined)[]>([undefined]);
  const [pageIndex, setPageIndex] = useState(0);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchInput), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchInput]);

  // A filter/search change invalidates the page cursors gathered under the old query, so it
  // always resets back to page one rather than silently reusing a stale cursor.
  useEffect(() => {
    setPageCursors([undefined]);
    setPageIndex(0);
  }, [debouncedSearch, categoryFilter, statusFilter]);

  const hasActiveFilters =
    debouncedSearch.trim() !== "" || categoryFilter !== null || statusFilter !== "Active";

  const clearFilters = () => {
    setSearchInput("");
    setDebouncedSearch("");
    setCategoryFilter(null);
    setStatusFilter("Active");
  };

  const queryParams: GetCatalogItemsParams = {};
  const trimmedSearch = debouncedSearch.trim();
  if (trimmedSearch) queryParams.search = trimmedSearch;
  if (categoryFilter) queryParams.categoryId = categoryFilter;
  if (statusFilter !== "Active") queryParams.status = statusFilter;
  const activeCursor = pageCursors[pageIndex];
  if (activeCursor) queryParams.cursor = activeCursor;

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ["catalogItems", trimmedSearch, categoryFilter, statusFilter, activeCursor ?? null],
    queryFn: () => api.getCatalogItems(queryParams),
    enabled: isOwnerOrAdmin && entitled,
  });

  const { data: categoriesData } = useQuery({
    queryKey: ["catalogCategories"],
    queryFn: () => api.getCatalogCategories(),
    enabled: isOwnerOrAdmin && entitled,
  });

  const activeCategories = (categoriesData?.categories ?? []).filter((c) => c.activeState === "Active");

  // The unfiltered list defaults to status=Active server-side, so a zero-item result there does
  // not by itself mean the catalog is empty — it could hold only inactive items. Only probe for
  // that once the unfiltered active list has actually come back empty, so we don't fire an extra
  // request on every normal load.
  const zeroStateCheckEnabled =
    isOwnerOrAdmin && entitled && !!data && data.items.length === 0 && !hasActiveFilters;
  const { data: inactiveCheckData, isLoading: inactiveCheckLoading } = useQuery({
    queryKey: ["catalogItems", "inactiveCheck"],
    queryFn: () => api.getCatalogItems({ status: "Inactive", limit: 1 }),
    enabled: zeroStateCheckEnabled,
  });
  const zeroStateResolving = zeroStateCheckEnabled && inactiveCheckLoading;
  const hasOnlyInactiveItems =
    zeroStateCheckEnabled && !inactiveCheckLoading && (inactiveCheckData?.items.length ?? 0) > 0;

  const handleNextPage = () => {
    if (!data?.nextCursor) return;
    setPageCursors((prev) => [...prev.slice(0, pageIndex + 1), data.nextCursor ?? undefined]);
    setPageIndex((i) => i + 1);
  };

  const handlePrevPage = () => {
    setPageIndex((i) => Math.max(0, i - 1));
  };

  if (role === "unknown") {
    return (
      <div className="flex flex-1 items-center justify-center">
        <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
      </div>
    );
  }

  if (!isOwnerOrAdmin) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[var(--ophalo-canvas)]">
        <div className="max-w-sm text-center px-6">
          <Tag className="mx-auto mb-4 h-8 w-8 text-[var(--ophalo-muted)]" />
          <h1 className="font-serif text-xl font-semibold text-[var(--ophalo-ink)] mb-2">
            Price Book isn't available for your role
          </h1>
          <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed">
            Contact your account owner if you need access to pricing.
          </p>
        </div>
      </div>
    );
  }

  if (entitlementLoading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
      </div>
    );
  }

  if (entitlementError) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[var(--ophalo-canvas)]">
        <div className="max-w-sm text-center px-6">
          <Tag className="mx-auto mb-4 h-8 w-8 text-[var(--ophalo-muted)]" />
          <h1 className="font-serif text-xl font-semibold text-[var(--ophalo-ink)] mb-2">
            Couldn't check Price Book access
          </h1>
          <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed mb-4">
            We weren't able to confirm your plan's Price Book access. This is usually temporary.
          </p>
          <button
            type="button"
            onClick={onRetryEntitlement}
            className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
          >
            Try again
          </button>
        </div>
      </div>
    );
  }

  if (!entitled) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[var(--ophalo-canvas)]">
        <div className="max-w-sm text-center px-6">
          <Tag className="mx-auto mb-4 h-8 w-8 text-[var(--ophalo-muted)]" />
          <h1 className="font-serif text-xl font-semibold text-[var(--ophalo-ink)] mb-2">
            Price Book isn't included in your plan
          </h1>
          <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed">
            Talk to your account owner about enabling Price Book, Quotes &amp; Materials.
          </p>
        </div>
      </div>
    );
  }

  // build-log/112/113 (2e.5 zero-state CTA refinement): the header CTA is suppressed only once a
  // successful response proves the catalog itself (not just the current filters, and not just its
  // default-Active view) is empty — loading/error/still-resolving states keep showing it, since we
  // can't yet claim there's nothing to add to.
  const isTrulyEmpty = zeroStateCheckEnabled && !zeroStateResolving && !hasOnlyInactiveItems;
  const isFilteredEmpty = !!data && data.items.length === 0 && hasActiveFilters;
  const showHeaderCta = !isTrulyEmpty;

  return (
    <div className="flex-1 min-w-0 flex flex-col">
      <div className="px-4 pt-5 pb-4 sm:px-6 sm:pt-6 flex items-start justify-between gap-4">
        <div>
          <h1 className="keep-page-title tracking-tight">Price Book</h1>
          <p className="mt-1 keep-page-subtitle">
            Build your catalog of materials, equipment, services, and fees.
          </p>
        </div>
        {showHeaderCta && (
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            className="shrink-0 inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium
              bg-[var(--ophalo-navy)] text-white hover:opacity-90 transition-opacity
              focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
          >
            <Plus className="h-4 w-4" />
            Add catalog item
          </button>
        )}
      </div>

      <div className="px-4 sm:px-6 pb-4 flex flex-wrap items-center gap-3">
        <label className="sr-only" htmlFor="catalog-search">
          Search catalog
        </label>
        <input
          id="catalog-search"
          type="search"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search by name, SKU, or keyword"
          className="w-full sm:w-64 px-3 py-1.5 rounded-lg border border-[var(--ophalo-border)] text-sm
            focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
        />

        <label className="sr-only" htmlFor="catalog-category-filter">
          Filter by category
        </label>
        <div className="w-full sm:w-56">
          <CategoryCombobox
            id="catalog-category-filter"
            categories={activeCategories}
            currentCategoryId={categoryFilter}
            onSelect={setCategoryFilter}
            noneLabel="All categories"
            placeholder="All categories"
          />
        </div>

        <div className="inline-flex rounded-lg border border-[var(--ophalo-border)] p-0.5" role="group" aria-label="Filter by status">
          {(["Active", "Inactive"] as const).map((option) => (
            <button
              key={option}
              type="button"
              aria-pressed={statusFilter === option}
              onClick={() => setStatusFilter(option)}
              className={`px-3 py-1 rounded-md text-sm font-medium transition-colors ${
                statusFilter === option
                  ? "bg-[var(--ophalo-navy)] text-white"
                  : "text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              {option}
            </button>
          ))}
        </div>

        {hasActiveFilters && (
          <button
            type="button"
            onClick={clearFilters}
            className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
          >
            Clear filters
          </button>
        )}
      </div>

      <div className="flex-1 min-w-0 px-4 sm:px-6 pb-6">
        {(isLoading || zeroStateResolving) && (
          <div className="flex flex-1 items-center justify-center py-16">
            <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <div className="flex flex-col items-center justify-center py-16 text-center">
            <p className="text-[var(--ophalo-muted)] text-sm mb-3">
              {error instanceof ApiError ? "Couldn't load your catalog." : "Something went wrong."}
            </p>
            <button
              type="button"
              onClick={() => void refetch()}
              className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
            >
              Try again
            </button>
          </div>
        )}

        {!isLoading && !isError && isTrulyEmpty && (
          <div className="flex flex-1 items-center justify-center py-16">
            <div className="max-w-sm w-full rounded-xl border border-[var(--ophalo-border)] px-6 py-8 text-center">
              <Tag className="mx-auto mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
              <h2 className="text-[var(--ophalo-ink)] text-base font-semibold mb-1">Your catalog is empty</h2>
              <p className="text-[var(--ophalo-muted)] text-sm mb-4">
                Start with the parts, services, and fees you use most.
              </p>
              <button
                type="button"
                onClick={() => setDrawerOpen(true)}
                className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium
                  bg-[var(--ophalo-navy)] text-white hover:opacity-90 transition-opacity
                  focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
              >
                <Plus className="h-4 w-4" />
                Add your first catalog item
              </button>
            </div>
          </div>
        )}

        {!isLoading && !isError && hasOnlyInactiveItems && (
          <div className="flex flex-1 items-center justify-center py-16">
            <div className="max-w-sm w-full rounded-xl border border-[var(--ophalo-border)] px-6 py-8 text-center">
              <Tag className="mx-auto mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
              <h2 className="text-[var(--ophalo-ink)] text-base font-semibold mb-1">No active items</h2>
              <p className="text-[var(--ophalo-muted)] text-sm mb-4">
                Every item in your catalog is currently inactive.
              </p>
              <button
                type="button"
                onClick={() => setStatusFilter("Inactive")}
                className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
              >
                View inactive items
              </button>
            </div>
          </div>
        )}

        {!isLoading && !isError && isFilteredEmpty && (
          <div className="flex flex-1 items-center justify-center py-16">
            <div className="max-w-sm w-full rounded-xl border border-[var(--ophalo-border)] px-6 py-8 text-center">
              <Tag className="mx-auto mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
              <h2 className="text-[var(--ophalo-ink)] text-base font-semibold mb-1">No items match your filters</h2>
              <p className="text-[var(--ophalo-muted)] text-sm mb-4">
                Try a different search term, or clear your filters to see the full catalog.
              </p>
              <button
                type="button"
                onClick={clearFilters}
                className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
              >
                Clear filters
              </button>
            </div>
          </div>
        )}

        {!isLoading && !isError && data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs font-medium text-[var(--ophalo-muted)] border-b border-[var(--ophalo-border)]">
                  <th className="py-2 pr-4">Name</th>
                  <th className="py-2 pr-4">SKU</th>
                  <th className="py-2 pr-4">Type</th>
                  <th className="py-2 pr-4">UOM</th>
                  <th className="py-2 pr-4">Sell price</th>
                  <th className="py-2 pr-4">Status</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((row) => (
                  <tr key={row.item.id} className="border-b border-[var(--ophalo-border)] last:border-0 hover:bg-[var(--ophalo-canvas)]">
                    <td className="py-2.5 pr-4 text-[var(--ophalo-ink)] font-medium">
                      <button
                        type="button"
                        onClick={() => onSelectItem(row.item.id)}
                        className="text-left hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
                      >
                        {row.item.displayName}
                      </button>
                    </td>
                    <td className="py-2.5 pr-4 text-[var(--ophalo-muted)]">{row.item.externalKey ?? "—"}</td>
                    <td className="py-2.5 pr-4 text-[var(--ophalo-muted)]">
                      {TYPE_LABELS[row.item.type] ?? row.item.type}
                    </td>
                    <td className="py-2.5 pr-4 text-[var(--ophalo-muted)]">{row.item.unitOfMeasure}</td>
                    <td className="py-2.5 pr-4 text-[var(--ophalo-ink)]">{formatPrice(row)}</td>
                    <td className="py-2.5 pr-4 text-[var(--ophalo-muted)]">{row.item.activeState}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!isLoading && !isError && data && data.items.length > 0 && (pageIndex > 0 || data.hasMore) && (
          <div className="flex items-center justify-end gap-3 pt-4">
            <button
              type="button"
              onClick={handlePrevPage}
              disabled={pageIndex === 0}
              className="px-3 py-1.5 rounded-lg text-sm font-medium border border-[var(--ophalo-border)]
                disabled:opacity-40 disabled:cursor-not-allowed hover:bg-[var(--ophalo-canvas)]
                focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
            >
              Previous
            </button>
            <button
              type="button"
              onClick={handleNextPage}
              disabled={!data.hasMore}
              className="px-3 py-1.5 rounded-lg text-sm font-medium border border-[var(--ophalo-border)]
                disabled:opacity-40 disabled:cursor-not-allowed hover:bg-[var(--ophalo-canvas)]
                focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
            >
              Next
            </button>
          </div>
        )}
      </div>

      {drawerOpen && (
        <CatalogItemDrawer
          categories={categoriesData?.categories ?? []}
          onCategoriesChanged={() => void queryClient.invalidateQueries({ queryKey: ["catalogCategories"] })}
          onClose={() => setDrawerOpen(false)}
          onCreated={() => void queryClient.invalidateQueries({ queryKey: ["catalogItems"] })}
        />
      )}
    </div>
  );
}
