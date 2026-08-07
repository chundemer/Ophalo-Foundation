import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Tag } from "lucide-react";
import { api, ApiError, type AccountRole } from "../lib/apiClient";

interface CatalogItemDetailProps {
  catalogItemId: string;
  role: AccountRole;
  entitled: boolean;
  entitlementLoading: boolean;
  entitlementError: boolean;
  onRetryEntitlement: () => void;
  onBack: () => void;
}

const TYPE_LABELS: Record<string, string> = {
  Material: "Material",
  Equipment: "Equipment",
  Service: "Service",
  Fee: "Fee",
};

function formatCurrency(value: number): string {
  return value.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

function formatPercent(value: number): string {
  return `${(value * 100).toLocaleString(undefined, { maximumFractionDigits: 1 })}%`;
}

/**
 * Owner/Admin-only derived profitability display (Build Log 114 item 5): Gross profit =
 * SellPrice - Cost; Margin % = Gross profit / SellPrice; Markup % = Gross profit / Cost. Absent
 * Cost or SellPrice makes all three unavailable. A zero Cost keeps gross profit/margin valid but
 * makes markup unavailable (division by zero); a zero SellPrice makes margin unavailable.
 */
function ProfitabilityPanel({ cost, sellPrice }: { cost: number | null; sellPrice: number | null }) {
  if (cost == null || sellPrice == null) {
    return (
      <p className="text-sm text-[var(--ophalo-muted)]">
        Profitability is unavailable until both Cost and Sell Price are set.
      </p>
    );
  }

  const grossProfit = sellPrice - cost;
  const margin = sellPrice !== 0 ? grossProfit / sellPrice : null;
  const markup = cost !== 0 ? grossProfit / cost : null;

  return (
    <dl className="grid grid-cols-3 gap-4">
      <div>
        <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Gross profit</dt>
        <dd className="text-sm text-[var(--ophalo-ink)] font-medium">{formatCurrency(grossProfit)}</dd>
      </div>
      <div>
        <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Margin</dt>
        <dd className="text-sm text-[var(--ophalo-ink)] font-medium">
          {margin != null ? formatPercent(margin) : "Unavailable"}
        </dd>
      </div>
      <div>
        <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Markup</dt>
        <dd className="text-sm text-[var(--ophalo-ink)] font-medium">
          {markup != null ? formatPercent(markup) : "Unavailable"}
        </dd>
      </div>
    </dl>
  );
}

/**
 * Read-only catalog item detail (Session 2e.6a, build-log/113): header, category, current price,
 * aliases, and owner/admin-only derived profitability from the existing immutable price snapshot.
 * No mutation yet — header edit, alias management, republish, and reactivate are later 2e.6
 * slices.
 */
export function CatalogItemDetail({
  catalogItemId,
  role,
  entitled,
  entitlementLoading,
  entitlementError,
  onRetryEntitlement,
  onBack,
}: CatalogItemDetailProps) {
  const isOwnerOrAdmin = role === "owner" || role === "admin";

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["catalogItem", catalogItemId],
    queryFn: () => api.getCatalogItem(catalogItemId),
    enabled: isOwnerOrAdmin && entitled,
    retry: false,
  });

  // Mirrors PriceBook's guard order (build-log/113): a direct #/pricebook/:id URL must resolve
  // the same role/entitlement gates as arriving via the list, not fall through to the query and
  // surface a raw server 403 as a generic load failure.
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
        <button
          type="button"
          onClick={onBack}
          className="inline-flex items-center gap-1.5 text-sm font-medium text-[var(--keep-accent)] hover:underline mb-3"
        >
          <ArrowLeft className="h-4 w-4" />
          Back to Price Book
        </button>
      </div>

      <div className="flex-1 min-w-0 px-4 sm:px-6 pb-6">
        {isLoading && (
          <div className="flex flex-1 items-center justify-center py-16">
            <span className="text-[var(--ophalo-muted)] text-sm">Loading…</span>
          </div>
        )}

        {isError && error instanceof ApiError && error.status === 404 && (
          <div className="flex flex-col items-center justify-center py-16 text-center">
            <Tag className="mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
            <p className="text-[var(--ophalo-ink)] text-sm font-medium">This catalog item couldn't be found.</p>
          </div>
        )}

        {isError && !(error instanceof ApiError && error.status === 404) && (
          <div className="flex flex-col items-center justify-center py-16 text-center">
            <p className="text-[var(--ophalo-muted)] text-sm">Couldn't load this catalog item.</p>
          </div>
        )}

        {!isLoading && !isError && data && (
          <div className="max-w-2xl space-y-6">
            <div>
              <h1 className="keep-page-title tracking-tight">{data.item.displayName}</h1>
              <p className="mt-1 keep-page-subtitle">
                {TYPE_LABELS[data.item.type] ?? data.item.type}
                {data.category ? ` · ${data.category.name}` : ""}
              </p>
            </div>

            <dl className="grid grid-cols-2 sm:grid-cols-3 gap-4 rounded-xl border border-[var(--ophalo-border)] p-4">
              <div>
                <dt className="text-xs font-medium text-[var(--ophalo-muted)]">SKU</dt>
                <dd className="text-sm text-[var(--ophalo-ink)]">{data.item.externalKey ?? "—"}</dd>
              </div>
              <div>
                <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Unit of measure</dt>
                <dd className="text-sm text-[var(--ophalo-ink)]">{data.item.unitOfMeasure}</dd>
              </div>
              <div>
                <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Status</dt>
                <dd className="text-sm text-[var(--ophalo-ink)]">{data.item.activeState}</dd>
              </div>
              <div>
                <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Sell price</dt>
                <dd className="text-sm text-[var(--ophalo-ink)]">
                  {data.currentPricingMode === "NoStandalonePrice" || data.currentSellPrice == null
                    ? "No standalone price"
                    : formatCurrency(data.currentSellPrice)}
                </dd>
              </div>
              <div>
                <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Cost</dt>
                <dd className="text-sm text-[var(--ophalo-ink)]">
                  {data.currentCost != null ? formatCurrency(data.currentCost) : "—"}
                </dd>
              </div>
            </dl>

            <div>
              <h2 className="text-sm font-semibold text-[var(--ophalo-ink)] mb-2">Profitability</h2>
              <ProfitabilityPanel cost={data.currentCost} sellPrice={data.currentSellPrice} />
            </div>

            <div>
              <h2 className="text-sm font-semibold text-[var(--ophalo-ink)] mb-2">Aliases</h2>
              {data.aliases.length === 0 ? (
                <p className="text-sm text-[var(--ophalo-muted)]">No search aliases yet.</p>
              ) : (
                <ul className="text-sm text-[var(--ophalo-ink)] space-y-1">
                  {data.aliases.map((alias) => (
                    <li key={alias.id}>
                      {alias.aliasText}
                      {alias.activeState !== "Active" && (
                        <span className="text-[var(--ophalo-muted)]"> (inactive)</span>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
