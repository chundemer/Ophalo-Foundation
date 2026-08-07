import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Tag } from "lucide-react";
import { api, ApiError, type AccountRole } from "../lib/apiClient";
import { CatalogItemDrawer } from "../components/keep/CatalogItemDrawer";

interface PriceBookProps {
  role: AccountRole;
  entitled: boolean;
  entitlementLoading: boolean;
  entitlementError: boolean;
  onRetryEntitlement: () => void;
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
 * Price Book workspace shell (Session 2e.4, build-log/113): route, entitled nav, unavailable
 * direct-access handling, and the list shell (default active items only — search/filter/pager
 * controls are 2e.7). No creation drawer, no item detail, no actions column yet.
 */
export function PriceBook({ role, entitled, entitlementLoading, entitlementError, onRetryEntitlement }: PriceBookProps) {
  const isOwnerOrAdmin = role === "owner" || role === "admin";
  const queryClient = useQueryClient();
  const [drawerOpen, setDrawerOpen] = useState(false);

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ["catalogItems"],
    queryFn: () => api.getCatalogItems({}),
    enabled: isOwnerOrAdmin && entitled,
  });

  const { data: categoriesData } = useQuery({
    queryKey: ["catalogCategories"],
    queryFn: () => api.getCatalogCategories(),
    enabled: isOwnerOrAdmin && entitled && drawerOpen,
  });

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
  // successful response proves the catalog is empty — loading/error states keep showing it, since
  // we can't yet claim there's nothing to add to.
  const showHeaderCta = !(data && data.items.length === 0);

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

      <div className="flex-1 min-w-0 px-4 sm:px-6 pb-6">
        {isLoading && (
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

        {!isLoading && !isError && data && data.items.length === 0 && (
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
                  <tr key={row.item.id} className="border-b border-[var(--ophalo-border)] last:border-0">
                    <td className="py-2.5 pr-4 text-[var(--ophalo-ink)] font-medium">{row.item.displayName}</td>
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
