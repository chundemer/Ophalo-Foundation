import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Package } from "lucide-react";
import { api, ApiError, type AccountRole } from "../lib/apiClient";
import { CatalogItemPicker } from "../components/keep/CatalogItemPicker";
import { KeepBadge } from "../components/keep/KeepBadge";
import {
  OfferingAssemblyHeaderEditDrawer,
  type OfferingAssemblyHeaderDraft,
} from "../components/keep/OfferingAssemblyHeaderEditDrawer";

const INPUT_CLS =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base text-[var(--ophalo-ink)] px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const ELIGIBILITY_REASON_LABELS: Record<string, string> = {
  AssemblyInactive: "This assembly is inactive.",
  PrimaryItemInactive: "The primary catalog item is inactive.",
  PrimaryItemMissingStandalonePrice: "The primary catalog item has no standalone price.",
  ComponentInactive: "An associated item is inactive.",
  ComponentMissingStandalonePrice: "An associated item has no standalone price.",
};

// Step 2 Batch 2 (2026-08-13): server-authoritative pricing/margin summary presentation. The
// frontend only labels and links the reasons the server already computed — it never derives
// price, cost, counts, or review status itself.
const PRICE_REASON_LABELS: Record<string, string> = {
  PrimaryMissingStandaloneSellPrice: "The primary item has no standalone sell price.",
  RequiredComponentMissingStandaloneSellPrice: "A required associated item has no standalone sell price.",
};

const MARGIN_REASON_LABELS: Record<string, string> = {
  PrimaryMissingBusinessCost: "The primary item has no business cost on file.",
  RequiredComponentMissingBusinessCost: "A required associated item has no business cost on file.",
};

function formatCurrency(value: number): string {
  return value.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

interface OfferingAssemblyDetailProps {
  offeringAssemblyId: string;
  role: AccountRole;
  entitled: boolean;
  entitlementLoading: boolean;
  entitlementError: boolean;
  onRetryEntitlement: () => void;
  onBack: () => void;
  /** Repair-loop navigation (Step 2 Batch 2, 2026-08-13): routes to the affected catalog item
   * with return context, never edits the catalog item inline here. The reason kind lets the
   * catalog item page show cost- vs. price-specific contextual guidance without re-deriving it. */
  onSelectCatalogItem: (catalogItemId: string, reasonKind: "price" | "margin") => void;
}

/**
 * Offering/Assembly detail page (Session 3.2c): view/edit header, activate/inactivate, and
 * item add/update/remove, wired to the existing 3.2a/3.2b API. Mirrors CatalogItemDetail.tsx's
 * shell (entitlement gates, version-conflict recovery pattern) — no new backend work in this
 * slice.
 */
export function OfferingAssemblyDetail({
  offeringAssemblyId,
  role,
  entitled,
  entitlementLoading,
  entitlementError,
  onRetryEntitlement,
  onBack,
  onSelectCatalogItem,
}: OfferingAssemblyDetailProps) {
  const isOwnerOrAdmin = role === "owner" || role === "admin";
  const queryClient = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["offeringAssembly", offeringAssemblyId],
    queryFn: () => api.getOfferingAssembly(offeringAssemblyId),
    enabled: isOwnerOrAdmin && entitled,
    retry: false,
  });

  // The list row shows name, primary item, price treatment, active state, and eligibility — all
  // of which a header edit, item add/update/remove, or activate/inactivate can change, so every
  // mutation here invalidates both queries together rather than leaving the list to go stale.
  function invalidateDetail() {
    return Promise.all([
      queryClient.invalidateQueries({ queryKey: ["offeringAssembly", offeringAssemblyId] }),
      queryClient.invalidateQueries({ queryKey: ["offeringAssemblies"] }),
    ]);
  }

  const [isEditing, setIsEditing] = useState(false);
  // Conflict recovery stays page-owned (mirrors CatalogItemDetail). The draft is consumed once
  // into `editSessionDraft` when the user deliberately reopens Edit; a later Edit re-seeds fresh.
  const [conflictDraft, setConflictDraft] = useState<OfferingAssemblyHeaderDraft | null>(null);
  const [editSessionDraft, setEditSessionDraft] = useState<OfferingAssemblyHeaderDraft | null>(null);
  const [conflictRefreshPending, setConflictRefreshPending] = useState(false);

  const [activatePending, setActivatePending] = useState(false);
  const [activateError, setActivateError] = useState<string | null>(null);
  const [inactivatePending, setInactivatePending] = useState(false);
  const [inactivateError, setInactivateError] = useState<string | null>(null);
  const [confirmInactivate, setConfirmInactivate] = useState(false);
  const [itemActionPending, setItemActionPending] = useState(false);
  const [itemActionError, setItemActionError] = useState<string | null>(null);
  const [showAddItem, setShowAddItem] = useState(false);
  const [newItemCatalogItemId, setNewItemCatalogItemId] = useState<string | null>(null);
  const [newItemDisplayName, setNewItemDisplayName] = useState<string | null>(null);
  const [newItemQuantity, setNewItemQuantity] = useState("1");
  const [newItemOptional, setNewItemOptional] = useState(false);

  const itemBusy = conflictRefreshPending || activatePending || inactivatePending || itemActionPending;

  // After the drawer closes, return focus to the "Edit" trigger on a normal cancel/save, or to
  // the version-conflict banner when a conflict sent the user back to review (WCAG 2.4.3).
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

  const activateMutation = useMutation({
    mutationFn: () => {
      if (!data) throw new Error("Offering/assembly not loaded.");
      return api.activateOfferingAssembly(offeringAssemblyId, data.concurrencyVersion);
    },
    onMutate: () => {
      setActivateError(null);
      setActivatePending(true);
    },
    onSuccess: () => {
      void invalidateDetail().then(() => setActivatePending(false));
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && (err.code === "OfferingAssembly.VersionMismatch" || err.code === "OfferingAssembly.AlreadyActive")) {
        setActivateError(err.code === "OfferingAssembly.AlreadyActive" ? "This assembly is already active." : "This assembly was changed elsewhere. Refreshing…");
        void invalidateDetail().then(() => setActivatePending(false));
        return;
      }
      setActivatePending(false);
      setActivateError("Could not activate this assembly. Try again.");
    },
  });

  const inactivateMutation = useMutation({
    mutationFn: () => {
      if (!data) throw new Error("Offering/assembly not loaded.");
      return api.inactivateOfferingAssembly(offeringAssemblyId, data.concurrencyVersion);
    },
    onMutate: () => {
      setInactivateError(null);
      setConfirmInactivate(false);
      setInactivatePending(true);
    },
    onSuccess: () => {
      void invalidateDetail().then(() => setInactivatePending(false));
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && (err.code === "OfferingAssembly.VersionMismatch" || err.code === "OfferingAssembly.NotActive")) {
        setInactivateError(err.code === "OfferingAssembly.NotActive" ? "This assembly is already inactive." : "This assembly was changed elsewhere. Refreshing…");
        void invalidateDetail().then(() => setInactivatePending(false));
        return;
      }
      setInactivatePending(false);
      setInactivateError("Could not inactivate this assembly. Try again.");
    },
  });

  const addItemMutation = useMutation({
    mutationFn: () => {
      if (!data || !newItemCatalogItemId) throw new Error("No catalog item selected.");
      return api.addOfferingAssemblyItem(
        offeringAssemblyId,
        {
          catalogItemId: newItemCatalogItemId,
          defaultQuantity: Number(newItemQuantity),
          isOptional: newItemOptional,
          displayOrder: data.items.length,
        },
        data.concurrencyVersion,
      );
    },
    onMutate: () => {
      setItemActionError(null);
      setItemActionPending(true);
    },
    onSuccess: () => {
      void invalidateDetail().then(() => setItemActionPending(false));
      setShowAddItem(false);
      setNewItemCatalogItemId(null);
      setNewItemDisplayName(null);
      setNewItemQuantity("1");
      setNewItemOptional(false);
    },
    onError: (err: unknown) => {
      if (err instanceof ApiError && err.code === "OfferingAssembly.VersionMismatch") {
        setItemActionError("This assembly was changed elsewhere. Refreshing…");
        void invalidateDetail().then(() => setItemActionPending(false));
        return;
      }
      setItemActionPending(false);
      setItemActionError(err instanceof ApiError ? err.message : "Could not add this item. Try again.");
    },
  });

  const updateItemMutation = useMutation({
    mutationFn: (input: { itemId: string; defaultQuantity: number; isOptional: boolean; displayOrder: number }) => {
      if (!data) throw new Error("Offering/assembly not loaded.");
      return api.updateOfferingAssemblyItem(
        offeringAssemblyId,
        input.itemId,
        { defaultQuantity: input.defaultQuantity, isOptional: input.isOptional, displayOrder: input.displayOrder },
        data.concurrencyVersion,
      );
    },
    onMutate: () => {
      setItemActionError(null);
      setItemActionPending(true);
    },
    onSuccess: () => void invalidateDetail().then(() => setItemActionPending(false)),
    onError: (err: unknown) => {
      if (err instanceof ApiError && err.code === "OfferingAssembly.VersionMismatch") {
        setItemActionError("This assembly was changed elsewhere. Refreshing…");
        void invalidateDetail().then(() => setItemActionPending(false));
        return;
      }
      setItemActionPending(false);
      setItemActionError(err instanceof ApiError ? err.message : "Could not update this item. Try again.");
    },
  });

  const removeItemMutation = useMutation({
    mutationFn: (itemId: string) => {
      if (!data) throw new Error("Offering/assembly not loaded.");
      return api.removeOfferingAssemblyItem(offeringAssemblyId, itemId, data.concurrencyVersion);
    },
    onMutate: () => {
      setItemActionError(null);
      setItemActionPending(true);
    },
    onSuccess: () => void invalidateDetail().then(() => setItemActionPending(false)),
    onError: (err: unknown) => {
      if (err instanceof ApiError && err.code === "OfferingAssembly.VersionMismatch") {
        setItemActionError("This assembly was changed elsewhere. Refreshing…");
        void invalidateDetail().then(() => setItemActionPending(false));
        return;
      }
      setItemActionPending(false);
      setItemActionError(err instanceof ApiError ? err.message : "Could not remove this item. Try again.");
    },
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
          <Package className="mx-auto mb-4 h-8 w-8 text-[var(--ophalo-muted)]" />
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
          <Package className="mx-auto mb-4 h-8 w-8 text-[var(--ophalo-muted)]" />
          <h1 className="font-serif text-xl font-semibold text-[var(--ophalo-ink)] mb-2">
            Couldn't check Price Book access
          </h1>
          <p className="text-[var(--ophalo-muted)] text-sm leading-relaxed mb-4">
            We weren't able to confirm your plan's Price Book access. This is usually temporary.
          </p>
          <button type="button" onClick={onRetryEntitlement} className="text-sm font-medium text-[var(--keep-accent)] hover:underline">
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
          <Package className="mx-auto mb-4 h-8 w-8 text-[var(--ophalo-muted)]" />
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
        <button type="button" onClick={onBack} className="inline-flex items-center gap-1.5 text-sm font-medium text-[var(--keep-accent)] hover:underline mb-3">
          <ArrowLeft className="h-4 w-4" />
          Back to Assemblies
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
            <Package className="mb-3 h-8 w-8 text-[var(--ophalo-muted)]" />
            <p className="text-[var(--ophalo-ink)] text-sm font-medium">This offering/assembly couldn't be found.</p>
          </div>
        )}

        {isError && !(error instanceof ApiError && error.status === 404) && (
          <div className="flex flex-col items-center justify-center py-16 text-center">
            <p className="text-[var(--ophalo-muted)] text-sm">Couldn't load this offering/assembly.</p>
          </div>
        )}

        {/* Keep the assembly detail mounted behind its edit drawer. This matches the nudge
            editor and preserves context while the modal owns focus and interaction. */}
        {!isLoading && !isError && data && (
          <div className="max-w-2xl space-y-6">
            <div className="flex items-start justify-between gap-4">
              <div>
                <h1 className="keep-page-title tracking-tight">{data.name}</h1>
                <p className="mt-1 keep-page-subtitle">
                  Primary: {data.primaryCatalogItemDisplayName} · {data.priceTreatment === "Summed" ? "Summed" : "All-inclusive"}
                </p>
                {!data.isOperationallyEligible && (
                  <div className="mt-2 inline-flex items-center gap-1 rounded-full bg-[var(--ophalo-attention-bg)] px-2 py-0.5 text-xs font-medium text-[var(--ophalo-attention)]">
                    Needs review
                  </div>
                )}
              </div>
              <div className="flex shrink-0 items-center gap-2">
                {data.activeState !== "Active" && !activatePending && (
                  <button
                    type="button"
                    onClick={() => activateMutation.mutate()}
                    disabled={itemBusy}
                    className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                  >
                    Activate
                  </button>
                )}
                {activatePending && <span className="text-sm text-[var(--ophalo-muted)]">Activating…</span>}
                {data.activeState === "Active" && !confirmInactivate && (
                  <button
                    type="button"
                    onClick={() => setConfirmInactivate(true)}
                    disabled={itemBusy}
                    className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                  >
                    Inactivate
                  </button>
                )}
                {data.activeState === "Active" && confirmInactivate && (
                  <div className="flex items-center gap-2">
                    <span className="text-sm text-[var(--ophalo-muted)]">Remove from selection?</span>
                    <button
                      type="button"
                      onClick={() => inactivateMutation.mutate()}
                      disabled={itemBusy || inactivateMutation.isPending}
                      className="rounded-lg border border-[var(--ophalo-danger)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-danger)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                    >
                      {inactivatePending ? "Inactivating…" : "Confirm inactivate"}
                    </button>
                    <button type="button" onClick={() => setConfirmInactivate(false)} disabled={itemBusy} className="text-sm text-[var(--ophalo-muted)] hover:underline disabled:opacity-60">
                      Cancel
                    </button>
                  </div>
                )}
                {!confirmInactivate && (
                  <button
                    ref={editTriggerRef}
                    type="button"
                    onClick={startEditing}
                    disabled={itemBusy}
                    className="rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-60"
                  >
                    Edit
                  </button>
                )}
              </div>
            </div>

            {conflictDraft && (
              <div
                ref={conflictBannerRef}
                tabIndex={-1}
                className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)]"
              >
                This offering/assembly was changed by someone else while you were editing. We kept
                your unsaved edits — review the latest values below, then Edit to re-apply them.
              </div>
            )}

            {activateError && <p className="text-sm text-[var(--ophalo-danger)]">{activateError}</p>}
            {inactivateError && <p className="text-sm text-[var(--ophalo-danger)]">{inactivateError}</p>}

            {/* Owner/Admin pricing/margin header context (Step 2, 2026-08-13): server-authoritative
                only — this reads data.pricing verbatim, it never computes a price, cost, count, or
                review status itself. */}
            <div className="flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border border-[var(--ophalo-border)] p-3">
              <div>
                <p className="text-xs font-medium text-[var(--ophalo-muted)]">Calculated sell price</p>
                {data.pricing.priceStatus === "Priced" && data.pricing.calculatedSellPrice !== null ? (
                  <p className="text-lg font-semibold text-[var(--ophalo-ink)]">{formatCurrency(data.pricing.calculatedSellPrice)}</p>
                ) : (
                  <p className="text-sm font-medium text-[var(--ophalo-attention)]">Price needs review</p>
                )}
              </div>
              <div>
                <p className="text-xs font-medium text-[var(--ophalo-muted)]">Margin readiness</p>
                {data.pricing.marginStatus === "Ready" ? (
                  <p className="text-sm font-medium text-[var(--ophalo-ink)]">Ready</p>
                ) : (
                  <p className="text-sm font-medium text-[var(--ophalo-attention)]">
                    Margin needs cost review ({data.pricing.missingCostLineCount})
                  </p>
                )}
              </div>
            </div>

            {data.eligibilityReasons.length > 0 && (
              <div className="rounded-lg border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] p-3 space-y-1">
                <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-attention)]">Lifecycle</p>
                {data.eligibilityReasons.map((reason, idx) => (
                  <p key={idx} className="text-sm text-[var(--ophalo-attention)]">
                    {ELIGIBILITY_REASON_LABELS[reason.code] ?? reason.code}
                  </p>
                ))}
              </div>
            )}

            {/* Price and margin issues are kept as separate groups from each other and from
                lifecycle eligibility above — a missing cost is never presented as a price error,
                and neither collapses into one generic warning (locked contract). */}
            {data.pricing.priceReasons.length > 0 && (
              <div className="rounded-lg border border-[var(--ophalo-attention)] bg-[var(--ophalo-attention-bg)] p-3 space-y-1">
                <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-attention)]">Price</p>
                {data.pricing.priceReasons.map((reason, idx) => (
                  <div key={idx} className="flex items-center justify-between gap-2">
                    <p className="text-sm text-[var(--ophalo-attention)]">
                      {PRICE_REASON_LABELS[reason.code] ?? reason.code} ({reason.catalogItemDisplayName})
                    </p>
                    <button
                      type="button"
                      onClick={() => onSelectCatalogItem(reason.catalogItemId, "price")}
                      className="shrink-0 text-sm font-medium text-[var(--keep-accent)] hover:underline"
                    >
                      Review price
                    </button>
                  </div>
                ))}
              </div>
            )}

            {data.pricing.marginReasons.length > 0 && (
              <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3 space-y-1">
                <p className="text-xs font-semibold uppercase tracking-wide text-[var(--ophalo-muted)]">Margin</p>
                {data.pricing.marginReasons.map((reason, idx) => (
                  <div key={idx} className="flex items-center justify-between gap-2">
                    <p className="text-sm text-[var(--ophalo-muted)]">
                      {MARGIN_REASON_LABELS[reason.code] ?? reason.code} ({reason.catalogItemDisplayName})
                    </p>
                    <button
                      type="button"
                      onClick={() => onSelectCatalogItem(reason.catalogItemId, "margin")}
                      className="shrink-0 text-sm font-medium text-[var(--keep-accent)] hover:underline"
                    >
                      Review cost
                    </button>
                  </div>
                ))}
              </div>
            )}

            <section>
              <div className="flex items-center justify-between mb-2">
                <h2 className="text-sm font-medium text-[var(--ophalo-ink)]">Associated items</h2>
                {!showAddItem && (
                  <button
                    type="button"
                    onClick={() => setShowAddItem(true)}
                    disabled={itemBusy}
                    className="text-sm font-medium text-[var(--keep-accent)] hover:underline disabled:opacity-60"
                  >
                    + Add item
                  </button>
                )}
              </div>

              {itemActionError && <p className="text-sm text-[var(--ophalo-danger)] mb-2">{itemActionError}</p>}

              <div className="space-y-2">
                {data.items.map((it) => {
                  const hasPriceIssue = data.pricing.priceReasons.some((r) => r.catalogItemId === it.catalogItemId);
                  const hasMarginIssue = data.pricing.marginReasons.some((r) => r.catalogItemId === it.catalogItemId);
                  return (
                  <div key={it.id} className="flex items-center gap-3 rounded-lg border border-[var(--ophalo-border)] p-2">
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-[var(--ophalo-ink)] truncate">{it.catalogItemDisplayName}</p>
                      {(hasPriceIssue || hasMarginIssue) && (
                        <div className="mt-1 flex flex-wrap gap-1">
                          {hasPriceIssue && <KeepBadge variant="attention">Price needs review</KeepBadge>}
                          {hasMarginIssue && <KeepBadge variant="attention">Margin needs cost review</KeepBadge>}
                        </div>
                      )}
                    </div>
                    <label className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)]">
                      Qty
                      <input
                        type="number"
                        min="0.01"
                        step="any"
                        defaultValue={it.defaultQuantity}
                        disabled={itemBusy}
                        onBlur={(e) => {
                          const value = Number(e.target.value);
                          if (!Number.isNaN(value) && value !== it.defaultQuantity) {
                            updateItemMutation.mutate({ itemId: it.id, defaultQuantity: value, isOptional: it.isOptional, displayOrder: it.displayOrder });
                          }
                        }}
                        className={`${INPUT_CLS} w-20`}
                      />
                    </label>
                    <label className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)]">
                      <input
                        type="checkbox"
                        checked={it.isOptional}
                        disabled={itemBusy}
                        onChange={(e) =>
                          updateItemMutation.mutate({ itemId: it.id, defaultQuantity: it.defaultQuantity, isOptional: e.target.checked, displayOrder: it.displayOrder })
                        }
                      />
                      Optional
                    </label>
                    <button
                      type="button"
                      onClick={() => removeItemMutation.mutate(it.id)}
                      disabled={itemBusy}
                      className="text-sm text-[var(--ophalo-danger)] hover:underline disabled:opacity-60"
                    >
                      Remove
                    </button>
                  </div>
                  );
                })}
                {data.items.length === 0 && !showAddItem && <p className="text-sm text-[var(--ophalo-muted)]">No associated items yet.</p>}
              </div>

              {showAddItem && (
                <div className="mt-2 rounded-lg border border-[var(--ophalo-border)] p-2 space-y-2">
                  <CatalogItemPicker
                    id="assembly-add-item"
                    selectedItemId={newItemCatalogItemId}
                    selectedItemDisplayName={newItemDisplayName}
                    excludeIds={[data.primaryCatalogItemId, ...data.items.map((it) => it.catalogItemId)]}
                    onSelect={(row) => {
                      setNewItemCatalogItemId(row.item.id);
                      setNewItemDisplayName(row.item.displayName);
                    }}
                  />
                  <div className="flex items-center gap-3">
                    <label className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)]">
                      Qty
                      <input type="number" min="0.01" step="any" value={newItemQuantity} onChange={(e) => setNewItemQuantity(e.target.value)} className={`${INPUT_CLS} w-20`} />
                    </label>
                    <label className="flex items-center gap-1 text-sm text-[var(--ophalo-muted)]">
                      <input type="checkbox" checked={newItemOptional} onChange={(e) => setNewItemOptional(e.target.checked)} />
                      Optional
                    </label>
                    <button
                      type="button"
                      onClick={() => addItemMutation.mutate()}
                      disabled={!newItemCatalogItemId || itemBusy}
                      className="ml-auto rounded-lg bg-[var(--keep-accent)] px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-60"
                    >
                      Add
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setShowAddItem(false);
                        setNewItemCatalogItemId(null);
                        setNewItemDisplayName(null);
                      }}
                      className="text-sm text-[var(--ophalo-muted)] hover:underline"
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              )}
            </section>
          </div>
        )}

        {!isLoading && !isError && data && isEditing && (
          <OfferingAssemblyHeaderEditDrawer
            assembly={data}
            initialDraft={editSessionDraft}
            onClose={cancelEditing}
            onSaved={() => {
              void invalidateDetail();
              setConflictDraft(null);
              setEditSessionDraft(null);
              setIsEditing(false);
            }}
            onVersionConflict={(draft) => {
              setConflictDraft(draft);
              setEditSessionDraft(null);
              setIsEditing(false);
              setConflictRefreshPending(true);
              void invalidateDetail().then(() => setConflictRefreshPending(false));
            }}
          />
        )}
      </div>
    </div>
  );
}
