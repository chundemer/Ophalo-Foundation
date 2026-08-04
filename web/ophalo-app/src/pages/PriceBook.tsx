import { useQuery } from "@tanstack/react-query";
import { Tag } from "lucide-react";
import { api, ApiError, type AccountRole } from "../lib/apiClient";

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

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ["catalogItems"],
    queryFn: () => api.getCatalogItems({}),
    enabled: isOwnerOrAdmin && entitled,
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

  return (
    <div className="flex-1 min-w-0 flex flex-col">
      <div className="px-4 pt-5 pb-4 sm:px-6 sm:pt-6">
        <h1 className="keep-page-title tracking-tight">Price Book</h1>
        <p className="mt-1 keep-page-subtitle">Catalog items for quotes and materials.</p>
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
          <div className="flex flex-col items-center justify-center py-16 text-center">
            <Tag className="mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
            <p className="text-[var(--ophalo-ink)] text-sm font-medium mb-1">No catalog items yet</p>
            <p className="text-[var(--ophalo-muted)] text-sm">
              Materials, equipment, services, and fees you price will show up here.
            </p>
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
    </div>
  );
}
