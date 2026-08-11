import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Tag } from "lucide-react";
import { api, ApiError, type AccountRole, type CatalogItemResponse } from "../lib/apiClient";
import { CatalogItemPricePublishForm } from "./CatalogItemPricePublishForm";
import { CategoryCombobox } from "../components/keep/CategoryCombobox";

const INPUT_CLS =
  "w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-base text-[var(--ophalo-ink)] px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const ERROR_INPUT_CLS = "border-[var(--ophalo-danger)]";

interface HeaderFormState {
  displayName: string;
  externalKey: string;
  categoryId: string;
  isCommonItem: boolean;
}

function toFormState(item: CatalogItemResponse): HeaderFormState {
  return {
    displayName: item.displayName,
    externalKey: item.externalKey ?? "",
    categoryId: item.categoryId ?? "",
    isCommonItem: item.isCommonItem,
  };
}

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
function ProfitabilityPanel({
  cost,
  sellPrice,
  currency,
}: {
  cost: number | null;
  sellPrice: number | null;
  currency: string;
}) {
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
        <dd className="text-sm text-[var(--ophalo-ink)] font-medium">{formatCurrency(grossProfit, currency)}</dd>
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
  const [form, setForm] = useState<HeaderFormState | null>(null);
  // Set instead of the form itself when a save hits a version conflict (build-log/113, review
  // 2026-08-07): the form unmounts and the read-only view refreshes to the concurrent editor's
  // latest values, so a resave can't silently overwrite them. Re-entering Edit restores this draft
  // as the deliberate, explicit retry.
  const [conflictDraft, setConflictDraft] = useState<HeaderFormState | null>(null);
  // True from the moment a conflict is detected until the refetch it triggers lands. Edit stays
  // disabled for this window so a fast double-click can't reopen the form and resave against the
  // still-stale `data.item.concurrencyVersion` before the refreshed item is actually rendered.
  const [conflictRefreshPending, setConflictRefreshPending] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<{ displayName?: string; externalKey?: string; categoryId?: string }>({});
  // Reported by CategoryCombobox (Session 2e.7b, build-log/114): true from the start of a
  // category-create attempt until it resolves — blocks Save so it can never fire against an
  // uncommitted category intent, matching the create-drawer's contract.
  const [categoryPending, setCategoryPending] = useState(false);

  function startEditing() {
    if (!data || itemBusy) return;
    setForm(conflictDraft ?? toFormState(data.item));
    setConflictDraft(null);
    setFormError(null);
    setFieldErrors({});
    setCategoryPending(false);
    setIsEditing(true);
  }

  function cancelEditing() {
    setIsEditing(false);
    setForm(null);
    setFormError(null);
    setFieldErrors({});
    setCategoryPending(false);
  }

  const updateHeaderMutation = useMutation({
    mutationFn: (input: HeaderFormState) => {
      if (!data) throw new Error("Catalog item not loaded.");
      return api.updateCatalogItemHeader(
        catalogItemId,
        {
          displayName: input.displayName.trim(),
          externalKey: input.externalKey.trim() === "" ? null : input.externalKey.trim(),
          categoryId: input.categoryId === "" ? null : input.categoryId,
          isCommonItem: input.isCommonItem,
        },
        data.item.concurrencyVersion,
      );
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] });
      setConflictDraft(null);
      cancelEditing();
    },
    onError: (err: unknown, input) => {
      if (err instanceof ApiError && err.code === "CatalogItem.VersionMismatch") {
        setConflictDraft(input);
        setIsEditing(false);
        setForm(null);
        setFormError(null);
        setFieldErrors({});
        setConflictRefreshPending(true);
        void queryClient.invalidateQueries({ queryKey: ["catalogItem", catalogItemId] }).then(() => {
          setConflictRefreshPending(false);
        });
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogItem.DisplayNameRequired") {
        setFieldErrors({ displayName: "Display name is required." });
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogItem.DisplayNameTooLong") {
        setFieldErrors({ displayName: "Display name must not exceed 200 characters." });
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogItem.InvalidExternalKey") {
        setFieldErrors({ externalKey: "SKU must contain at least one letter or number." });
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogItem.ExternalKeyAlreadyExists") {
        setFieldErrors({ externalKey: "A catalog item with this SKU already exists." });
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogCategory.NotFound") {
        setFieldErrors({ categoryId: "This category no longer exists. Reload and pick another." });
        return;
      }
      if (err instanceof ApiError && err.code === "CatalogCategory.NotActive") {
        setFieldErrors({ categoryId: "This category is no longer active. Reload and pick another." });
        return;
      }
      setFormError("Could not save changes. Try again.");
    },
  });

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

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!form || updateHeaderMutation.isPending || categoryPending) return;
    setFieldErrors({});
    setFormError(null);
    updateHeaderMutation.mutate(form);
  }

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

        {!isLoading && !isError && data && !isEditing && (
          <div className="max-w-2xl space-y-6">
            <div className="flex items-start justify-between gap-4">
              <div>
                <h1 className="keep-page-title tracking-tight">{data.item.displayName}</h1>
                <p className="mt-1 keep-page-subtitle">
                  {TYPE_LABELS[data.item.type] ?? data.item.type}
                  {data.category ? ` · ${data.category.name}` : ""}
                  {data.item.isCommonItem ? " · Common item" : ""}
                </p>
              </div>
              <div className="flex shrink-0 items-center gap-2">
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
                {data.item.activeState === "Active" && !showPublishForm && (
                  <button
                    type="button"
                    onClick={() => setShowPublishForm(true)}
                    disabled={itemBusy}
                    className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                  >
                    Update price
                  </button>
                )}
                <button
                  type="button"
                  onClick={startEditing}
                  disabled={itemBusy}
                  className="rounded-lg border border-[var(--ophalo-border)] px-3 py-1.5 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                >
                  {itemBusy ? "Refreshing…" : "Edit"}
                </button>
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
              <div className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)]">
                This item was changed by someone else while you were editing. We kept your unsaved
                edits — review the latest values below, then Edit to re-apply them.
              </div>
            )}

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
                    : formatCurrency(data.currentSellPrice, data.item.currency)}
                </dd>
              </div>
              <div>
                <dt className="text-xs font-medium text-[var(--ophalo-muted)]">Cost</dt>
                <dd className="text-sm text-[var(--ophalo-ink)]">
                  {data.currentCost != null ? formatCurrency(data.currentCost, data.item.currency) : "—"}
                </dd>
              </div>
            </dl>

            <div>
              <h2 className="text-sm font-semibold text-[var(--ophalo-ink)] mb-2">Profitability</h2>
              <ProfitabilityPanel cost={data.currentCost} sellPrice={data.currentSellPrice} currency={data.item.currency} />
            </div>

            <div>
              <h2 className="text-sm font-semibold text-[var(--ophalo-ink)] mb-2">Aliases</h2>
              {data.aliases.length === 0 ? (
                <p className="text-sm text-[var(--ophalo-muted)]">No search aliases yet.</p>
              ) : (
                <ul className="text-sm text-[var(--ophalo-ink)] space-y-1">
                  {data.aliases.map((alias) => {
                    const isThisAliasPending =
                      aliasTransitionMutation.isPending && aliasTransitionMutation.variables?.aliasId === alias.id;
                    return (
                      <li key={alias.id} className="flex items-center justify-between gap-3">
                        <span>
                          {alias.aliasText}
                          {alias.activeState !== "Active" && (
                            <span className="text-[var(--ophalo-muted)]"> (inactive)</span>
                          )}
                        </span>
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

              <form onSubmit={handleAddAlias} className="mt-3 flex items-start gap-2">
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
                  className="shrink-0 rounded-lg border border-[var(--ophalo-border)] px-3 py-2 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-60"
                >
                  {addAliasMutation.isPending ? "Adding…" : "Add"}
                </button>
              </form>
            </div>

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
          </div>
        )}

        {!isLoading && !isError && data && isEditing && form && (
          <form onSubmit={handleSubmit} className="max-w-2xl space-y-6">
            <div className="rounded-xl border border-[var(--ophalo-border)] p-4 space-y-4">
              <div>
                <label htmlFor="header-display-name" className="text-xs font-medium text-[var(--ophalo-muted)]">
                  Name
                </label>
                <input
                  id="header-display-name"
                  type="text"
                  value={form.displayName}
                  onChange={(e) => setForm({ ...form, displayName: e.target.value })}
                  disabled={updateHeaderMutation.isPending}
                  className={`mt-1 ${INPUT_CLS} ${fieldErrors.displayName ? ERROR_INPUT_CLS : ""}`}
                />
                {fieldErrors.displayName && (
                  <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{fieldErrors.displayName}</p>
                )}
              </div>

              <div>
                <label htmlFor="header-external-key" className="text-xs font-medium text-[var(--ophalo-muted)]">
                  SKU
                </label>
                <input
                  id="header-external-key"
                  type="text"
                  value={form.externalKey}
                  onChange={(e) => setForm({ ...form, externalKey: e.target.value })}
                  disabled={updateHeaderMutation.isPending}
                  className={`mt-1 ${INPUT_CLS} ${fieldErrors.externalKey ? ERROR_INPUT_CLS : ""}`}
                />
                {fieldErrors.externalKey && (
                  <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{fieldErrors.externalKey}</p>
                )}
              </div>

              <div>
                <label htmlFor="header-category" className="text-xs font-medium text-[var(--ophalo-muted)]">
                  Category
                </label>
                <div className="mt-1">
                  <CategoryCombobox
                    id="header-category"
                    categories={(categoriesQuery.data?.categories ?? []).filter(
                      (c) => c.activeState === "Active" || c.id === data.category?.id,
                    )}
                    currentCategoryId={form.categoryId === "" ? null : form.categoryId}
                    onSelect={(categoryId) => setForm({ ...form, categoryId: categoryId ?? "" })}
                    creatable
                    disabled={updateHeaderMutation.isPending}
                    invalid={!!fieldErrors.categoryId}
                    onCategoriesChanged={() => void queryClient.invalidateQueries({ queryKey: ["catalogCategories"] })}
                    onPendingChange={setCategoryPending}
                  />
                </div>
                {fieldErrors.categoryId && (
                  <p className="mt-1 text-xs text-[var(--ophalo-danger)]">{fieldErrors.categoryId}</p>
                )}
              </div>

              <label className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
                <input
                  type="checkbox"
                  checked={form.isCommonItem}
                  onChange={(e) => setForm({ ...form, isCommonItem: e.target.checked })}
                  disabled={updateHeaderMutation.isPending}
                />
                Common item
              </label>
            </div>

            {formError && (
              <div className="rounded-lg border border-[var(--ophalo-danger)] p-3 text-sm text-[var(--ophalo-danger)]">
                {formError}
              </div>
            )}

            <div className="flex items-center gap-3">
              <button
                type="submit"
                disabled={updateHeaderMutation.isPending || categoryPending}
                className="rounded-lg bg-[var(--keep-accent)] px-4 py-2 text-sm font-medium text-white disabled:opacity-60"
              >
                {updateHeaderMutation.isPending ? "Saving…" : "Save"}
              </button>
              <button
                type="button"
                onClick={cancelEditing}
                disabled={updateHeaderMutation.isPending}
                className="rounded-lg border border-[var(--ophalo-border)] px-4 py-2 text-sm font-medium text-[var(--ophalo-ink)]"
              >
                Cancel
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
