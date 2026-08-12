import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronRight, Keyboard, Package, Plus, Search, Tag } from "lucide-react";
import { api, ApiError, type AccountRole, type GetCatalogItemsParams } from "../lib/apiClient";
import { CatalogItemDrawer } from "../components/keep/CatalogItemDrawer";
import { OfferingAssemblyDrawer } from "../components/keep/OfferingAssemblyDrawer";
import { CategoryCombobox } from "../components/keep/CategoryCombobox";
import { KeepModal } from "../components/keep/KeepModal";

const SEARCH_DEBOUNCE_MS = 300;

const INTERACTIVE_ROLES = new Set(["button", "link", "textbox", "combobox", "listbox", "menu", "dialog", "slider", "spinbutton"]);

// 2e.7c: "/" only needs to avoid stealing a keystroke someone is actually typing/editing — it's
// fine to fire from a focused button or link (e.g. right after closing the shortcuts dialog).
function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return !!target.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"])');
}

// "n" stays conservative: it also backs off from any interactive control (buttons, links,
// interactive-role elements) so a stray "n" over a button can never accidentally create an item.
function isEditableOrInteractiveTarget(target: EventTarget | null): boolean {
  if (isTypingTarget(target)) return true;
  if (!(target instanceof Element)) return false;
  const el = target.closest("button, a[href]");
  if (el) return true;
  const roleEl = target.closest("[role]");
  const role = roleEl?.getAttribute("role");
  return !!role && INTERACTIVE_ROLES.has(role);
}

interface PriceBookProps {
  role: AccountRole;
  entitled: boolean;
  entitlementLoading: boolean;
  entitlementError: boolean;
  onRetryEntitlement: () => void;
  onSelectItem: (catalogItemId: string) => void;
  onSelectAssembly: (offeringAssemblyId: string) => void;
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

function isStandalonePrice(row: { currentPricingMode: string | null; currentSellPrice: number | null }): boolean {
  return row.currentPricingMode !== "NoStandalonePrice" && row.currentSellPrice != null;
}

function StatusBadge({ status }: { status: string }) {
  const isActive = status === "Active";
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium ${
        isActive
          ? "border-[color:var(--keep-accent)]/20 bg-[var(--keep-accent-bg)] text-[var(--keep-accent-hover)]"
          : "border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-[var(--ophalo-muted)]"
      }`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${isActive ? "bg-[var(--keep-accent)]" : "bg-[var(--ophalo-muted)]"}`} />
      {status}
    </span>
  );
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
  onSelectAssembly,
}: PriceBookProps) {
  const isOwnerOrAdmin = role === "owner" || role === "admin";
  const queryClient = useQueryClient();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [assemblyDrawerOpen, setAssemblyDrawerOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  // Session 3.2c: one Owner/Admin price-book workspace, tabbed rather than a separate route —
  // build-log/117's "Offering & Packages tab" placeholder, now built.
  const [activeTab, setActiveTab] = useState<"items" | "assemblies">("items");
  const [assemblyStatusFilter, setAssemblyStatusFilter] = useState<"Active" | "Inactive">("Active");
  // Independent cursor/page state per status — switching the Active/Inactive filter must not
  // lose the other status's page position, unlike the catalog-items list which resets on any
  // filter change.
  const [assemblyPagination, setAssemblyPagination] = useState<
    Record<"Active" | "Inactive", { cursors: (string | undefined)[]; index: number }>
  >({
    Active: { cursors: [undefined], index: 0 },
    Inactive: { cursors: [undefined], index: 0 },
  });
  const assemblyPage = assemblyPagination[assemblyStatusFilter];
  const assemblyActiveCursor = assemblyPage.cursors[assemblyPage.index];

  const assembliesQuery = useQuery({
    queryKey: ["offeringAssemblies", assemblyStatusFilter, assemblyActiveCursor ?? null],
    queryFn: () => api.getOfferingAssemblies({ status: assemblyStatusFilter, cursor: assemblyActiveCursor }),
    enabled: isOwnerOrAdmin && entitled && activeTab === "assemblies",
  });

  const handleAssemblyNextPage = () => {
    if (!assembliesQuery.data?.nextCursor) return;
    const nextCursor = assembliesQuery.data.nextCursor;
    setAssemblyPagination((prev) => {
      const cur = prev[assemblyStatusFilter];
      return {
        ...prev,
        [assemblyStatusFilter]: {
          cursors: [...cur.cursors.slice(0, cur.index + 1), nextCursor],
          index: cur.index + 1,
        },
      };
    });
  };

  const handleAssemblyPrevPage = () => {
    setAssemblyPagination((prev) => {
      const cur = prev[assemblyStatusFilter];
      return { ...prev, [assemblyStatusFilter]: { ...cur, index: Math.max(0, cur.index - 1) } };
    });
  };

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

  // 2e.7c: list-level "/" (focus search) and "n" (new item) shortcuts. Both stay silent while
  // the create drawer or the shortcuts dialog is open (each owns its own keyboard handling) or a
  // modifier key is held. "/" only backs off while actually typing/editing — it's fine to fire
  // from a focused button or link, e.g. right after closing the shortcuts dialog. "n" stays
  // conservative and also backs off from any interactive control, so it can never accidentally
  // create an item.
  useEffect(() => {
    if (drawerOpen || shortcutsOpen) return;
    function onKeyDown(e: KeyboardEvent) {
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.key === "/") {
        if (isTypingTarget(e.target)) return;
        e.preventDefault();
        document.getElementById("catalog-search")?.focus();
      } else if (e.key === "n") {
        if (isEditableOrInteractiveTarget(e.target)) return;
        e.preventDefault();
        setDrawerOpen(true);
      }
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [drawerOpen, shortcutsOpen]);

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
      <div className="mx-auto w-full max-w-[1440px] px-4 pt-6 pb-5 sm:px-6 sm:pt-8 flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
        <div>
          <h1 className="keep-page-title tracking-tight">Price Book</h1>
          <p className="mt-1.5 keep-page-subtitle">
            Build your catalog of materials, equipment, services, and fees.
          </p>
        </div>
        <div className="shrink-0 flex items-center gap-2">
          <div className="relative group">
            <button
              type="button"
              onClick={() => setShortcutsOpen(true)}
              aria-label="Keyboard shortcuts"
              className="inline-flex items-center justify-center h-9 w-9 rounded-lg border border-[var(--ophalo-border)]
                text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors
                focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              <Keyboard className="h-4 w-4" />
            </button>
            {/* Visible on hover AND keyboard focus — a title attribute only shows on mouse
                hover, which leaves keyboard/touch users without the label the icon alone can't
                convey. */}
            <span
              role="tooltip"
              className="pointer-events-none absolute left-1/2 top-full z-10 mt-1.5 -translate-x-1/2 whitespace-nowrap
                rounded-md bg-[var(--ophalo-ink)] px-2 py-1 text-xs font-medium text-white opacity-0
                transition-opacity group-hover:opacity-100 group-focus-within:opacity-100"
            >
              Keyboard shortcuts
            </span>
          </div>
          {activeTab === "items" && showHeaderCta && (
            <button
              type="button"
              onClick={() => setDrawerOpen(true)}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium
                bg-[var(--ophalo-navy)] text-white hover:opacity-90 transition-opacity
                focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              <Plus className="h-4 w-4" />
              Add catalog item
            </button>
          )}
          {activeTab === "assemblies" && (
            <button
              type="button"
              onClick={() => setAssemblyDrawerOpen(true)}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium
                bg-[var(--ophalo-navy)] text-white hover:opacity-90 transition-opacity
                focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              <Plus className="h-4 w-4" />
              Add assembly
            </button>
          )}
        </div>
      </div>

      <div className="mx-auto w-full max-w-[1440px] px-4 sm:px-6" role="tablist" aria-label="Price Book sections">
        <div className="flex gap-5 border-b border-[var(--ophalo-border)]" >
          {(
            [
              { key: "items", label: "Catalog Items" },
              { key: "assemblies", label: "Offerings & Assemblies" },
            ] as const
          ).map((tab) => (
            <button
              key={tab.key}
              type="button"
              role="tab"
              aria-selected={activeTab === tab.key}
              onClick={() => setActiveTab(tab.key)}
              className={`relative -mb-px px-0.5 py-3 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2 ${
                activeTab === tab.key
                  ? "border-b-2 border-[var(--keep-accent)] text-[var(--ophalo-navy)]"
                  : "border-b-2 border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>
      </div>

      {activeTab === "items" && (
      <>
      <div className="mx-auto w-full max-w-[1440px] px-4 pt-4 pb-3 sm:px-6">
      <div className="flex flex-wrap items-center gap-3 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-3 shadow-sm">
        <label className="sr-only" htmlFor="catalog-search">
          Search catalog
        </label>
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--ophalo-muted)]" />
          <input
            id="catalog-search"
            type="search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Search by name, SKU, or keyword"
            className="w-full rounded-lg border border-[var(--ophalo-border)] py-2 pl-9 pr-3 text-sm text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)]
              focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
          />
        </div>

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

        <div className="inline-flex rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-0.5" role="group" aria-label="Filter by status">
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
      </div>

      <div className="mx-auto flex-1 min-w-0 w-full max-w-[1440px] px-4 sm:px-6 pb-8">
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
          <>
            {/* 2e.7c: the desktop table is unreadable at mobile widths (values wrap into
                fragments), so mobile gets a compact card list — Name, Type/UOM, Sell price or
                "No standalone price", and Status. Desktop table is unchanged. */}
            <div className="hidden sm:block overflow-x-auto rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm">
              <table className="w-full text-sm">
                <colgroup>
                  <col className="w-[32%]" />
                  <col className="w-[12%]" />
                  <col className="w-[13%]" />
                  <col className="w-[10%]" />
                  <col className="w-[20%]" />
                  <col className="w-[13%]" />
                  <col className="w-10" />
                </colgroup>
                <thead>
                  <tr className="border-b border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-left text-[11px] font-semibold uppercase tracking-wider text-[var(--ophalo-muted)]">
                    <th className="px-4 py-3">Name</th>
                    <th className="px-4 py-3">SKU</th>
                    <th className="px-4 py-3">Type</th>
                    <th className="px-4 py-3">UOM</th>
                    <th className="px-4 py-3">Sell price</th>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-3 py-3"><span className="sr-only">Open item</span></th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((row) => (
                    <tr key={row.item.id} className="group border-b border-[var(--ophalo-border)] last:border-0 transition-colors hover:bg-[var(--keep-accent-bg)]/35">
                      <td className="px-4 py-3.5 text-[var(--ophalo-ink)] font-semibold">
                        <button
                          type="button"
                          onClick={() => onSelectItem(row.item.id)}
                          className="text-left hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
                        >
                          {row.item.displayName}
                        </button>
                      </td>
                      <td className="px-4 py-3.5 text-[var(--ophalo-muted)]">{row.item.externalKey ?? "—"}</td>
                      <td className="px-4 py-3.5 text-[var(--ophalo-muted)]">
                        {TYPE_LABELS[row.item.type] ?? row.item.type}
                      </td>
                      <td className="px-4 py-3.5 text-[var(--ophalo-muted)]">{row.item.unitOfMeasure}</td>
                      <td className={`px-4 py-3.5 ${isStandalonePrice(row) ? "font-semibold text-[var(--ophalo-ink)]" : "italic text-[var(--ophalo-muted)]"}`}>
                        {formatPrice(row)}
                      </td>
                      <td className="px-4 py-3.5"><StatusBadge status={row.item.activeState} /></td>
                      <td className="px-3 py-3.5 text-right">
                        <button
                          type="button"
                          onClick={() => onSelectItem(row.item.id)}
                          aria-label={`Open ${row.item.displayName}`}
                          className="inline-flex h-8 w-8 items-center justify-center rounded-md text-[var(--ophalo-muted)] opacity-0 transition-all hover:bg-[var(--ophalo-card)] hover:text-[var(--ophalo-navy)] group-hover:opacity-100 focus:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                        >
                          <ChevronRight className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <ul className="sm:hidden flex flex-col gap-2.5">
              {data.items.map((row) => (
                <li key={row.item.id}>
                  <button
                    type="button"
                    onClick={() => onSelectItem(row.item.id)}
                    aria-label={`View ${row.item.displayName}`}
                    className="w-full text-left rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3.5 py-3.5 shadow-sm
                      flex flex-col gap-2 hover:bg-[var(--keep-accent-bg)]/35
                      focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <span className="text-[var(--ophalo-ink)] font-medium">{row.item.displayName}</span>
                      <StatusBadge status={row.item.activeState} />
                    </div>
                    <div className="flex items-center justify-between gap-2 text-sm">
                      <span className="text-[var(--ophalo-muted)]">
                        {TYPE_LABELS[row.item.type] ?? row.item.type} · {row.item.unitOfMeasure}
                      </span>
                      <span className={`text-right ${isStandalonePrice(row) ? "font-semibold text-[var(--ophalo-ink)]" : "italic text-[var(--ophalo-muted)]"}`}>{formatPrice(row)}</span>
                    </div>
                  </button>
                </li>
              ))}
            </ul>
          </>
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
      </>
      )}

      {activeTab === "assemblies" && (
        <div className="mx-auto w-full max-w-[1440px] px-4 pt-4 pb-3 sm:px-6">
          <div className="flex flex-wrap items-center gap-3 rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-3 shadow-sm">
          <div className="inline-flex rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-0.5" role="group" aria-label="Filter by status">
            {(["Active", "Inactive"] as const).map((option) => (
              <button
                key={option}
                type="button"
                aria-pressed={assemblyStatusFilter === option}
                onClick={() => setAssemblyStatusFilter(option)}
                className={`px-3 py-1 rounded-md text-sm font-medium transition-colors ${
                  assemblyStatusFilter === option
                    ? "bg-[var(--ophalo-navy)] text-white"
                    : "text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
                }`}
              >
                {option}
              </button>
            ))}
          </div>
          </div>
        </div>
      )}

      {activeTab === "assemblies" && (
        <div className="mx-auto flex-1 min-w-0 w-full max-w-[1440px] px-4 sm:px-6 pb-8">
          {assembliesQuery.isLoading && (
            <div className="flex flex-1 items-center justify-center py-16">
              <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
            </div>
          )}

          {assembliesQuery.isError && (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <p className="text-[var(--ophalo-muted)] text-sm mb-3">Couldn't load your offerings/assemblies.</p>
              <button
                type="button"
                onClick={() => void assembliesQuery.refetch()}
                className="text-sm font-medium text-[var(--keep-accent)] hover:underline"
              >
                Try again
              </button>
            </div>
          )}

          {!assembliesQuery.isLoading && !assembliesQuery.isError && (assembliesQuery.data?.items.length ?? 0) === 0 && (
            <div className="flex flex-1 items-center justify-center py-16">
              <div className="max-w-sm w-full rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-6 py-8 text-center shadow-sm">
                <Package className="mx-auto mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
                <h2 className="text-[var(--ophalo-ink)] text-base font-semibold mb-1">
                  {assemblyStatusFilter === "Active" ? "No active offerings/assemblies" : "No inactive offerings/assemblies"}
                </h2>
                <p className="text-[var(--ophalo-muted)] text-sm mb-4">
                  {assemblyStatusFilter === "Active"
                    ? "Build a static bundle of catalog items around one primary offering."
                    : "Nothing has been inactivated."}
                </p>
                {assemblyStatusFilter === "Active" && (
                  <button
                    type="button"
                    onClick={() => setAssemblyDrawerOpen(true)}
                    className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium
                      bg-[var(--ophalo-navy)] text-white hover:opacity-90 transition-opacity
                      focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
                  >
                    <Plus className="h-4 w-4" />
                    Add your first assembly
                  </button>
                )}
              </div>
            </div>
          )}

          {!assembliesQuery.isLoading && !assembliesQuery.isError && (assembliesQuery.data?.items.length ?? 0) > 0 && (
            <>
              <div className="hidden sm:block overflow-x-auto rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] text-left text-[11px] font-semibold uppercase tracking-wider text-[var(--ophalo-muted)]">
                      <th className="px-4 py-3">Name</th>
                      <th className="px-4 py-3">Primary item</th>
                      <th className="px-4 py-3">Price treatment</th>
                      <th className="px-4 py-3">Status</th>
                      <th className="px-4 py-3">Eligibility</th>
                      <th className="px-3 py-3"><span className="sr-only">Open assembly</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    {assembliesQuery.data!.items.map((row) => (
                      <tr
                        key={row.id}
                        className="group border-b border-[var(--ophalo-border)] last:border-0 transition-colors hover:bg-[var(--keep-accent-bg)]/35"
                      >
                        <td className="px-4 py-3.5 text-[var(--ophalo-ink)] font-semibold">
                          <button
                            type="button"
                            onClick={() => onSelectAssembly(row.id)}
                            className="text-left hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] rounded"
                          >
                            {row.name}
                          </button>
                        </td>
                        <td className="px-4 py-3.5 text-[var(--ophalo-muted)]">{row.primaryCatalogItemDisplayName}</td>
                        <td className="px-4 py-3.5 text-[var(--ophalo-muted)]">
                          {row.priceTreatment === "Summed" ? "Summed" : "All-inclusive"}
                        </td>
                        <td className="px-4 py-3.5"><StatusBadge status={row.activeState} /></td>
                        <td className="px-4 py-3.5">
                          {row.isOperationallyEligible ? (
                            <span className="text-[var(--ophalo-success)]">Ready to use</span>
                          ) : (
                            <span className="inline-flex rounded-full bg-[var(--ophalo-attention-bg)] px-2 py-0.5 text-xs font-medium text-[var(--ophalo-attention)]">
                              Needs review
                            </span>
                          )}
                        </td>
                        <td className="px-3 py-3.5 text-right">
                          <button
                            type="button"
                            onClick={() => onSelectAssembly(row.id)}
                            aria-label={`Open ${row.name}`}
                            className="inline-flex h-8 w-8 items-center justify-center rounded-md text-[var(--ophalo-muted)] opacity-0 transition-all hover:bg-[var(--ophalo-card)] hover:text-[var(--ophalo-navy)] group-hover:opacity-100 focus:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                          >
                            <ChevronRight className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <ul className="sm:hidden flex flex-col gap-2.5">
                {assembliesQuery.data!.items.map((row) => (
                  <li key={row.id}>
                    <button
                      type="button"
                      onClick={() => onSelectAssembly(row.id)}
                      aria-label={`View ${row.name}`}
                      className="w-full text-left rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3.5 py-3.5 shadow-sm
                        flex flex-col gap-2 hover:bg-[var(--keep-accent-bg)]/35
                        focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <span className="text-[var(--ophalo-ink)] font-semibold">{row.name}</span>
                        <StatusBadge status={row.activeState} />
                      </div>
                      <div className="flex items-center justify-between gap-2 text-sm">
                        <span className="text-[var(--ophalo-muted)]">{row.primaryCatalogItemDisplayName}</span>
                        {!row.isOperationallyEligible && (
                          <span className="rounded-full bg-[var(--ophalo-attention-bg)] px-2 py-0.5 text-xs font-medium text-[var(--ophalo-attention)]">
                            Needs review
                          </span>
                        )}
                      </div>
                    </button>
                  </li>
                ))}
              </ul>
            </>
          )}

          {!assembliesQuery.isLoading &&
            !assembliesQuery.isError &&
            (assembliesQuery.data?.items.length ?? 0) > 0 &&
            (assemblyPage.index > 0 || assembliesQuery.data?.hasMore) && (
              <div className="flex items-center justify-end gap-3 pt-4">
                <button
                  type="button"
                  onClick={handleAssemblyPrevPage}
                  disabled={assemblyPage.index === 0}
                  className="px-3 py-1.5 rounded-lg text-sm font-medium border border-[var(--ophalo-border)]
                    disabled:opacity-40 disabled:cursor-not-allowed hover:bg-[var(--ophalo-canvas)]
                    focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                >
                  Previous
                </button>
                <button
                  type="button"
                  onClick={handleAssemblyNextPage}
                  disabled={!assembliesQuery.data?.hasMore}
                  className="px-3 py-1.5 rounded-lg text-sm font-medium border border-[var(--ophalo-border)]
                    disabled:opacity-40 disabled:cursor-not-allowed hover:bg-[var(--ophalo-canvas)]
                    focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
                >
                  Next
                </button>
              </div>
            )}
        </div>
      )}

      {shortcutsOpen && (
        <KeepModal
          onClose={() => setShortcutsOpen(false)}
          label="Keyboard shortcuts"
          overlayClassName="flex items-center justify-center px-4"
          backdropClassName="bg-black/30"
          panelClassName="max-w-sm w-full rounded-xl bg-[var(--ophalo-card)] shadow-xl p-5 flex flex-col gap-4"
        >
          <div className="flex items-center justify-between">
            <h2 className="font-serif text-lg font-semibold text-[var(--ophalo-ink)]">Keyboard shortcuts</h2>
            <button
              type="button"
              onClick={() => setShortcutsOpen(false)}
              className="text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded
                focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1"
            >
              Close
            </button>
          </div>
          <dl className="flex flex-col gap-3 text-sm">
            {[
              { keys: ["Ctrl/Cmd", "Enter"], description: "Save & add another (in the item form)" },
              { keys: ["Esc"], description: "Close or cancel safely" },
              { keys: ["/"], description: "Focus catalog search" },
              { keys: ["n"], description: "Open New item (when not typing in a field)" },
            ].map(({ keys, description }) => (
              <div key={description} className="flex items-center justify-between gap-4">
                <dt className="flex items-center gap-1">
                  {keys.map((k) => (
                    <kbd
                      key={k}
                      className="px-1.5 py-0.5 rounded border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)]
                        text-xs font-medium text-[var(--ophalo-ink)]"
                    >
                      {k}
                    </kbd>
                  ))}
                </dt>
                <dd className="text-[var(--ophalo-muted)] text-right">{description}</dd>
              </div>
            ))}
          </dl>
        </KeepModal>
      )}

      {drawerOpen && (
        <CatalogItemDrawer
          categories={categoriesData?.categories ?? []}
          onCategoriesChanged={() => void queryClient.invalidateQueries({ queryKey: ["catalogCategories"] })}
          onClose={() => setDrawerOpen(false)}
          onCreated={() => void queryClient.invalidateQueries({ queryKey: ["catalogItems"] })}
        />
      )}

      {assemblyDrawerOpen && (
        <OfferingAssemblyDrawer
          onClose={() => setAssemblyDrawerOpen(false)}
          onCreated={() => void queryClient.invalidateQueries({ queryKey: ["offeringAssemblies"] })}
        />
      )}
    </div>
  );
}
