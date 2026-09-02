import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Tag } from "lucide-react";
import { api, ApiError, type AccountRole } from "../lib/apiClient";
import { CatalogItemPricePublishForm } from "./CatalogItemPricePublishForm";
import {
  CatalogItemEditDrawer,
  type CatalogItemHeaderDraft,
} from "../components/keep/CatalogItemEditDrawer";

const INPUT_CLS =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base text-[var(--ophalo-ink)] px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

interface CatalogItemDetailProps {
  catalogItemId: string;
  role: AccountRole;
  entitled: boolean;
  entitlementLoading: boolean;
  entitlementError: boolean;
  onRetryEntitlement: () => void;
  onBack: () => void;
  /** Repair-loop return context (Step 2 Batch 2, 2026-08-13): "Back to Price Book" when arrived
   * normally, "Back to assembly" when arrived via an assembly's pricing/margin review link. No
   * catalog-item editing is added here — the existing publish form (Internal cost/Sell price
   * fields) is the correct mutation path; this page only relabels its CTA and adds contextual
   * copy pointing at it. */
  backLabel?: string;
  /** Set only alongside a recognized returnToAssembly — which review link (price vs. margin)
   * brought the operator here, so the contextual banner names the right fix. */
  returnToAssemblyReason?: "price" | "margin";
}

const TYPE_LABELS: Record<string, string> = {
  Material: "Material",
  Equipment: "Equipment",
  Service: "Service",
  Fee: "Fee",
};

function formatCurrency(value: number, currency: string): string {
  return value.toLocaleString(undefined, { style: "currency", currency });
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
function EconomicsPanel({
  cost,
  sellPrice,
  noStandalonePrice,
  currency,
}: {
  cost: number | null;
  sellPrice: number | null;
  noStandalonePrice: boolean;
  currency: string;
}) {
  const grossProfit = cost != null && sellPrice != null ? sellPrice - cost : null;
  const hasCompleteEconomics = grossProfit != null;
  const margin = grossProfit != null && sellPrice != null && sellPrice !== 0 ? grossProfit / sellPrice : null;
  const markup = grossProfit != null && cost != null && cost !== 0 ? grossProfit / cost : null;
  const marginTone = margin == null
    ? "border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] text-[var(--ophalo-ink)]"
    : margin < 0
      ? "border-[var(--ophalo-danger)] bg-[var(--ophalo-danger-bg)] text-[var(--ophalo-danger)]"
      : margin < 0.15
        ? "border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] text-[var(--ophalo-attention)]"
        : "border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] text-[var(--ophalo-success)]";

  return (
    <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm" aria-labelledby="economics-heading">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--ophalo-border)] px-4 py-3">
        <div>
          <h2 id="economics-heading" className="text-sm font-semibold text-[var(--ophalo-ink)]">Economics &amp; profitability</h2>
          <p className="mt-0.5 text-xs text-[var(--ophalo-muted)]">Current internal price and cost for future use.</p>
        </div>
      </div>
      <dl className="grid grid-cols-1 gap-3 p-4 sm:grid-cols-3">
        <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] px-4 py-3">
          <dt className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">Sell price</dt>
          <dd className="mt-1 text-lg font-semibold text-[var(--ophalo-ink)]">
            {noStandalonePrice ? "No standalone price" : sellPrice != null ? formatCurrency(sellPrice, currency) : "Unavailable"}
          </dd>
        </div>
        <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] px-4 py-3">
          <dt className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">Direct cost</dt>
          <dd className="mt-1 text-lg font-semibold text-[var(--ophalo-ink)]">
            {cost != null ? formatCurrency(cost, currency) : "Unavailable"}
          </dd>
        </div>
        <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] px-4 py-3">
          <dt className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">Gross profit</dt>
          <dd className="mt-1 text-lg font-semibold text-[var(--ophalo-ink)]">
            {grossProfit != null ? formatCurrency(grossProfit, currency) : "Unavailable"}
          </dd>
        </div>
      </dl>
      <dl className="grid grid-cols-1 gap-3 border-t border-[var(--ophalo-border)] px-4 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <div className={`rounded-lg border px-4 py-3 ${marginTone}`}>
          <dt className="text-[10px] font-bold uppercase tracking-[0.1em] opacity-80">Margin %</dt>
          <dd className="mt-1 text-lg font-semibold">{margin != null ? formatPercent(margin) : "Unavailable"}</dd>
        </div>
        <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] px-4 py-3 text-[var(--ophalo-ink)]">
          <dt className="text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--ophalo-muted)]">Markup %</dt>
          <dd className="mt-1 text-lg font-semibold">{markup != null ? formatPercent(markup) : "Unavailable"}</dd>
        </div>
      </dl>
      {!hasCompleteEconomics && (
        <p className="border-t border-[var(--ophalo-border)] px-4 py-3 text-sm text-[var(--ophalo-muted)]">
          Profitability is unavailable until both Cost and Sell Price are set.
        </p>
      )}
    </section>
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
  backLabel = "Back to Price Book",
  returnToAssemblyReason,
}: CatalogItemDetailProps) {
  const isOwnerOrAdmin = role === "owner" || role === "admin";
  const queryClient = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["catalogItem", catalogItemId],
    queryFn: () => api.getCatalogItem(catalogItemId),
    enabled: isOwnerOrAdmin && entitled,
    retry: false,
  });

  const categoriesQuery = useQuery({
    queryKey: ["catalogCategories"],
    queryFn: () => api.getCatalogCategories(),
    enabled: isOwnerOrAdmin && entitled,
  });

  const [isEditing, setIsEditing] = useState(false);
  // Set when a save hits a version conflict (build-log/113, review 2026-08-07): the drawer
  // closes and the read-only view refreshes to the concurrent editor's latest values, so a
  // resave can't silently overwrite them. Consumed once into `editSessionDraft` when the user
  // deliberately reopens Edit; a later Edit re-seeds fresh from the refreshed item.
  const [conflictDraft, setConflictDraft] = useState<CatalogItemHeaderDraft | null>(null);
  // The draft handed to the edit drawer for the current edit session (the restored conflict
  // draft, or null to seed from the item). Cleared on cancel or successful save.
  const [editSessionDraft, setEditSessionDraft] = useState<CatalogItemHeaderDraft | null>(null);
  // True from the moment a conflict is detected until the refetch it triggers lands. Edit stays
  // disabled for this window so a fast double-click can't reopen the drawer and resave against
  // the still-stale `data.item.concurrencyVersion` before the refreshed item is rendered.
  const [conflictRefreshPending, setConflictRefreshPending] = useState(false);

  // The read-only "Edit" trigger and the version-conflict banner: after the drawer closes, focus
  // returns to the trigger on a normal cancel/save, or to the banner when a conflict sent the
  // user back to review the concurrent editor's values (WCAG 2.4.3).
  const editTriggerRef = useRef<HTMLButtonElement>(null);
  const conflictBannerRef = useRef<HTMLDivElement>(null);
  const restoreEditFocusRef = useRef(false);

  function startEditing() {
    if (!data || itemBusy) return;
    setEditSessionDraft(conflictDraft);
    setConflictDraft(null);
    restoreEditFocusRef.current = true;
    setIsEditing(true);
  }

  function cancelEditing() {
    setIsEditing(false);
    setEditSessionDraft(null);
  }

  useEffect(() => {
    if (isEditing || conflictRefreshPending) return;
    if (conflictDraft) {
      restoreEditFocusRef.current = false;
      conflictBannerRef.current?.focus();
      return;
    }
    if (restoreEditFocusRef.current) {
      restoreEditFocusRef.current = false;
      editTriggerRef.current?.focus();
    }
  }, [isEditing, conflictRefreshPending, conflictDraft]);

  // Session 2e.6c, build-log/113: reactivate and alias-management wiring. Both stay pending
  // (blocking Edit and every alias control) until the refetch they trigger lands, for the same
  // reason as conflictRefreshPending above — the next action must never fire against the
  // now-stale `data.item.concurrencyVersion`.
  const [reactivatePending, setReactivatePending] = useState(false);
  const [reactivateError, setReactivateError] = useState<string | null>(null);
  const [inactivatePending, setInactivatePending] = useState(false);
  const [inactivateError, setInactivateError] = useState<string | null>(null);
  const [confirmInactivate, setConfirmInactivate] = useState(false);
  const [aliasActionPending, setAliasActionPending] = useState(false);
  const [aliasFieldError, setAliasFieldError] = useState<string | null>(null);
  const [newAliasText, setNewAliasText] = useState("");
  const itemBusy = conflictRefreshPending || reactivatePending || inactivatePending || aliasActionPending;

  // Session 3.2d: which active offerings/assemblies reference this item, fetched only while the
  // inline inactivate confirmation is open. A failed read must not allow a blind inactivation, so
  // Confirm inactivate stays disabled (not just unwarned) until this resolves successfully.
  const assemblyDependenciesQuery = useQuery({
    queryKey: ["catalogItemActiveAssemblyDependencies", catalogItemId],
    queryFn: () => api.getActiveAssemblyDependencies(catalogItemId),
    enabled: confirmInactivate,
  });

  const reactivateMutation = useMutation({
    mutationFn: () => {
      if (!data) throw new Error("Catalog item not loaded.");
      return api.reactivateCatalogItem(catalogItemId, data.item.concurrencyVersion);
    },
    onMutate: () => {
      setReactivateError(null);
      setReactivatePending(true);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
        setReactivatePending(false);
      });
      void queryClient.invalidateQueries({ queryKey: ["catalogItems"] });
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && (err.code === "CatalogItem.VersionMismatch" || err.code === "CatalogItem.AlreadyActive")) {
        setReactivateError(
          err.code === "CatalogItem.AlreadyActive"
            ? "This item is already active."
            : "This item was changed elsewhere. Refreshing…",
        );
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
          setReactivatePending(false);
        });
        return;
      }
      setReactivatePending(false);
      setReactivateError("Could not reactivate this item. Try again.");
    },
  });

  // Removes the item from future selection (search/create-quote pickers) without deleting its
  // history; Reactivate above is the only way back. Requires an explicit inline confirmation
  // (matches TeamSection's suspend/remove pattern) since it's a one-click action with real
  // consequence for anyone currently searching the catalog.
  const inactivateMutation = useMutation({
    mutationFn: () => {
      if (!data) throw new Error("Catalog item not loaded.");
      return api.inactivateCatalogItem(catalogItemId, data.item.concurrencyVersion);
    },
    onMutate: () => {
      setInactivateError(null);
      setConfirmInactivate(false);
      setInactivatePending(true);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
        setInactivatePending(false);
      });
      void queryClient.invalidateQueries({ queryKey: ["catalogItems"] });
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && (err.code === "CatalogItem.VersionMismatch" || err.code === "CatalogItem.NotActive")) {
        setInactivateError(
          err.code === "CatalogItem.NotActive"
            ? "This item is already inactive."
            : "This item was changed elsewhere. Refreshing…",
        );
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
          setInactivatePending(false);
        });
        return;
      }
      setInactivatePending(false);
      setInactivateError("Could not inactivate this item. Try again.");
    },
  });

  const addAliasMutation = useMutation({
    mutationFn: (aliasText: string) => {
      if (!data) throw new Error("Catalog item not loaded.");
      return api.addCatalogItemAlias(catalogItemId, { aliasText }, data.item.concurrencyVersion);
    },
    onMutate: () => {
      setAliasFieldError(null);
      setAliasActionPending(true);
    },
    onSuccess: () => {
      setNewAliasText("");
      void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
        setAliasActionPending(false);
      });
    },
    onError: (err: unknown) => {
      // Deliberately does not clear newAliasText: the user's typed alias is preserved so a
      // transient failure (or a version conflict) does not force them to retype it.
      if (err instanceof ApiError && err.code === "CatalogItem.VersionMismatch") {
        setAliasFieldError("This item was changed elsewhere. Refreshing…");
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
          setAliasActionPending(false);
        });
        return;
      }
      setAliasActionPending(false);
      if (err instanceof ApiError && err.code === "CatalogItem.AliasTextRequired") {
        setAliasFieldError("Alias text is required.");
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogItem.AliasTextTooLong") {
        setAliasFieldError("Alias text must not exceed 200 characters.");
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogItem.AliasAlreadyExists") {
        setAliasFieldError("This catalog item already has an alias with this text.");
        return;
      }
      setAliasFieldError("Could not add this alias. Try again.");
    },
  });

  const aliasTransitionMutation = useMutation({
    mutationFn: (vars: { aliasId: string; action: "activate" | "inactivate" }) => {
      if (!data) throw new Error("Catalog item not loaded.");
      return vars.action === "activate"
        ? api.activateCatalogItemAlias(catalogItemId, vars.aliasId, data.item.concurrencyVersion)
        : api.inactivateCatalogItemAlias(catalogItemId, vars.aliasId, data.item.concurrencyVersion);
    },
    onMutate: () => {
      setAliasFieldError(null);
      setAliasActionPending(true);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
        setAliasActionPending(false);
      });
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && err.code === "CatalogItem.VersionMismatch") {
        setAliasFieldError("This item was changed elsewhere. Refreshing…");
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
          setAliasActionPending(false);
        });
        return;
      }
      setAliasActionPending(false);
      setAliasFieldError("Could not update this alias. Try again.");
    },
  });

  function handleAddAlias(e: React.FormEvent) {
    e.preventDefault();
    if (!data || itemBusy || addAliasMutation.isPending || newAliasText.trim() === "") return;
    addAliasMutation.mutate(newAliasText.trim());
  }

  // Session 2e.6d, build-log/113: later price publish. The form itself (draft state, validation,
  // below-cost confirmation, mutation/conflict handling, cache invalidation) lives in
  // CatalogItemPricePublishForm — only the trigger and open/closed state stay here.
  const [showPublishForm, setShowPublishForm] = useState(false);

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
      <div className="flex min-h-screen items-center justify-center bg-[var(--keep-workspace-canvas)]">
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
      <div className="flex min-h-screen items-center justify-center bg-[var(--keep-workspace-canvas)]">
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
      <div className="flex min-h-screen items-center justify-center bg-[var(--keep-workspace-canvas)]">
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
        <div className="mx-auto w-full max-w-6xl">
          <button
            type="button"
            onClick={onBack}
            className="inline-flex items-center gap-1.5 text-sm font-medium text-[var(--keep-accent)] hover:underline"
          >
            <ArrowLeft className="h-4 w-4" />
            {backLabel}
          </button>
        </div>
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

        {/* Keep the read-only detail mounted while the identity editor is open. The editor is a
            modal side drawer, so unmounting this surface leaves the operator staring at an
            empty, dimmed page instead of retaining the item context behind the drawer. */}
        {!isLoading && !isError && data && (
          <div className="mx-auto w-full max-w-6xl space-y-4">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="min-w-0">
                <h1 className="keep-page-title tracking-tight">{data.item.displayName}</h1>
                <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-[var(--ophalo-muted)]">
                  <span className="rounded-md border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-2 py-0.5 font-medium text-[var(--ophalo-ink)]">
                    {TYPE_LABELS[data.item.type] ?? data.item.type}
                  </span>
                  <span className={`rounded-md px-2 py-0.5 font-medium ${data.item.activeState === "Active" ? "bg-[var(--ophalo-success-bg)] text-[var(--ophalo-success)]" : "bg-[var(--ophalo-surface-muted)] text-[var(--ophalo-muted)]"}`}>
                    {data.item.activeState}
                  </span>
                  {data.category && <span>{data.category.name}</span>}
                  {data.item.isCommonItem && <span>Common item</span>}
                </div>
                <span className="sr-only">
                  {TYPE_LABELS[data.item.type] ?? data.item.type}{data.category ? ` · ${data.category.name}` : ""}
                </span>
                <p className="mt-2 text-sm text-[var(--ophalo-muted)]">
                  SKU: {data.item.externalKey ?? "—"} <span aria-hidden="true">·</span> Unit of measure: {data.item.unitOfMeasure}
                </p>
              </div>
              <div className="flex shrink-0 flex-wrap items-center justify-end gap-2">
                {data.item.activeState !== "Active" && (
                  <button
                    type="button"
                    onClick={() => reactivateMutation.mutate()}
                    disabled={itemBusy || reactivateMutation.isPending}
                    className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                  >
                    {reactivatePending ? "Reactivating…" : "Reactivate"}
                  </button>
                )}
                {data.item.activeState === "Active" && !showPublishForm && (
                  <button
                    type="button"
                    onClick={() => setShowPublishForm(true)}
                    disabled={itemBusy}
                    className="rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--keep-accent-hover)] disabled:opacity-60"
                  >
                    Update pricing &amp; cost
                  </button>
                )}
                <button
                  ref={editTriggerRef}
                  type="button"
                  onClick={startEditing}
                  disabled={itemBusy}
                  className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                >
                  {itemBusy ? "Refreshing…" : "Edit item details"}
                </button>
                {data.item.activeState === "Active" && !confirmInactivate && (
                  <button
                    type="button"
                    onClick={() => setConfirmInactivate(true)}
                    disabled={itemBusy}
                    className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                  >
                    Inactivate
                  </button>
                )}
                {data.item.activeState === "Active" && confirmInactivate && (
                  <div className="flex items-center gap-2">
                    <span className="text-sm text-[var(--ophalo-muted)]">
                      {assemblyDependenciesQuery.isFetching
                        ? "Checking offerings/assemblies…"
                        : "Remove from selection?"}
                    </span>
                    <button
                      type="button"
                      onClick={() => inactivateMutation.mutate()}
                      disabled={
                        itemBusy ||
                        inactivateMutation.isPending ||
                        assemblyDependenciesQuery.isFetching ||
                        assemblyDependenciesQuery.isError
                      }
                      className="rounded-lg border border-[var(--ophalo-danger)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                    >
                      {inactivatePending ? "Inactivating…" : "Confirm inactivate"}
                    </button>
                    <button
                      type="button"
                      onClick={() => setConfirmInactivate(false)}
                      disabled={itemBusy}
                      className="text-sm text-[var(--ophalo-muted)] hover:underline disabled:opacity-60"
                    >
                      Cancel
                    </button>
                  </div>
                )}
              </div>
            </div>

            {reactivateError && (
              <div className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)]">
                {reactivateError}
              </div>
            )}

            {inactivateError && (
              <div className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)]">
                {inactivateError}
              </div>
            )}

            {confirmInactivate && assemblyDependenciesQuery.isError && (
              <div className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)]">
                Couldn't check whether this item is used by any offerings/assemblies.{" "}
                <button
                  type="button"
                  onClick={() => void assemblyDependenciesQuery.refetch()}
                  className="font-medium underline"
                >
                  Try again
                </button>{" "}
                before inactivating.
              </div>
            )}

            {confirmInactivate &&
              !assemblyDependenciesQuery.isLoading &&
              !assemblyDependenciesQuery.isError &&
              (assemblyDependenciesQuery.data?.assemblies.length ?? 0) > 0 && (
                <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3 text-sm text-[var(--ophalo-ink)]">
                  This item is used by{" "}
                  {assemblyDependenciesQuery.data!.assemblies.map((a, i) => (
                    <span key={a.id}>
                      {i > 0 ? ", " : ""}
                      <span className="font-medium">{a.name}</span>
                    </span>
                  ))}
                  . Inactivating it will make{" "}
                  {assemblyDependenciesQuery.data!.assemblies.length === 1 ? "that assembly" : "those assemblies"}{" "}
                  unavailable for new selection.
                </div>
              )}

            {conflictDraft && (
              <div
                ref={conflictBannerRef}
                tabIndex={-1}
                className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
              >
                This item was changed by someone else while you were editing. We kept your unsaved
                edits — review the latest values below, then Edit to re-apply them.
              </div>
            )}

            {/* Repair-loop contextual guidance (Step 2 Batch 2, 2026-08-13): points at the
                existing publish form's Internal cost / Sell price fields — no new mutation path,
                no inline editing here. */}
            {returnToAssemblyReason && !showPublishForm && (
              <div className="rounded-lg border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] p-3 text-sm text-[var(--ophalo-attention)]">
                {returnToAssemblyReason === "margin"
                  ? "This item needs an internal cost to complete the assembly's margin review."
                  : "This item needs a standalone sell price to complete the assembly's price review."}{" "}
                <button
                  type="button"
                  onClick={() => setShowPublishForm(true)}
                  disabled={itemBusy || data.item.activeState !== "Active"}
                  className="font-medium underline disabled:opacity-60"
                >
                  Update pricing &amp; cost
                </button>
              </div>
            )}

            <EconomicsPanel
              cost={data.currentCost}
              sellPrice={data.currentPricingMode === "NoStandalonePrice" ? null : data.currentSellPrice}
              noStandalonePrice={data.currentPricingMode === "NoStandalonePrice"}
              currency={data.item.currency}
            />

            {/* The repair/edit form is the first actionable content once opened — directly after
                the header/summary, before Profitability and Aliases — so an owner arriving via
                the assembly cost-repair link (or clicking Update pricing & cost normally) doesn't
                have to scroll past unrelated alias management to reach it (2026-08-13). */}
            {showPublishForm && (
              <CatalogItemPricePublishForm
                catalogItemId={catalogItemId}
                currency={data.item.currency}
                currentCost={data.currentCost}
                currentSellPrice={data.currentSellPrice}
                currentPricingMode={data.currentPricingMode}
                onClose={() => setShowPublishForm(false)}
              />
            )}

            <section className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] p-4 shadow-sm" aria-labelledby="aliases-heading">
              <h2 id="aliases-heading" className="text-sm font-semibold text-[var(--ophalo-ink)]">Search aliases</h2>
              <p className="mt-1 text-xs text-[var(--ophalo-muted)]">Helps technicians find this item in field search using alternate terms or shorthand.</p>
              {data.aliases.length === 0 ? (
                <p className="mt-4 text-sm text-[var(--ophalo-muted)]">No search aliases yet.</p>
              ) : (
                <ul className="mt-4 flex flex-wrap gap-2">
                  {data.aliases.map((alias) => {
                    const isThisAliasPending =
                      aliasTransitionMutation.isPending && aliasTransitionMutation.variables?.aliasId === alias.id;
                    return (
                      <li key={alias.id} className="inline-flex items-center gap-2 rounded-full border border-[var(--ophalo-border)] bg-[var(--ophalo-surface-muted)] py-1 pl-3 pr-2 text-sm text-[var(--ophalo-ink)]">
                        <span>{alias.aliasText}{alias.activeState !== "Active" && <span className="text-[var(--ophalo-muted)]"> (inactive)</span>}</span>
                        <button
                          type="button"
                          onClick={() =>
                            aliasTransitionMutation.mutate({
                              aliasId: alias.id,
                              action: alias.activeState === "Active" ? "inactivate" : "activate",
                            })
                          }
                          disabled={itemBusy || isThisAliasPending}
                          className="shrink-0 text-xs font-medium text-[var(--keep-accent)] hover:underline disabled:opacity-60 disabled:no-underline"
                        >
                          {isThisAliasPending
                            ? "Working…"
                            : alias.activeState === "Active"
                              ? "Deactivate"
                              : "Activate"}
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}

              <form onSubmit={handleAddAlias} className="mt-4 flex items-start gap-2 border-t border-[var(--ophalo-border)] pt-4">
                <div className="flex-1">
                  <label htmlFor="new-alias-text" className="sr-only">
                    New alias
                  </label>
                  <input
                    id="new-alias-text"
                    type="text"
                    placeholder="Add a search alias"
                    value={newAliasText}
                    onChange={(e) => setNewAliasText(e.target.value)}
                    disabled={itemBusy || addAliasMutation.isPending}
                    className={INPUT_CLS}
                  />
                  {aliasFieldError && (
                    <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{aliasFieldError}</p>
                  )}
                </div>
                <button
                  type="submit"
                  disabled={itemBusy || addAliasMutation.isPending || newAliasText.trim() === ""}
                  className="shrink-0 rounded-lg border border-[var(--ophalo-border)] px-3 py-2 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-surface-muted)] disabled:opacity-60"
                >
                  {addAliasMutation.isPending ? "Adding…" : "Add"}
                </button>
              </form>
            </section>
          </div>
        )}

        {!isLoading && !isError && data && isEditing && (
          <CatalogItemEditDrawer
            item={data.item}
            currentCategory={data.category}
            categories={categoriesQuery.data?.categories ?? []}
            initialDraft={editSessionDraft}
            onCategoriesChanged={() =>
              void queryClient.invalidateQueries({ queryKey: ["catalogCategories"] })
            }
            onClose={cancelEditing}
            onSaved={() => {
              void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] });
              setConflictDraft(null);
              setEditSessionDraft(null);
              setIsEditing(false);
            }}
            onVersionConflict={(draft) => {
              setConflictDraft(draft);
              setEditSessionDraft(null);
              setIsEditing(false);
              setConflictRefreshPending(true);
              void queryClient
                .invalidateQueries({ queryKey: ["catalogItem", catalogItemId] })
                .then(() => setConflictRefreshPending(false));
            }}
          />
        )}
      </div>
    </div>
  );
}
