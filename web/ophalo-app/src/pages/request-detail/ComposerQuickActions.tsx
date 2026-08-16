import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiError, type QuickScopeActionFieldRowResponse } from "../../lib/apiClient";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const QUICK_ACTION_UNAVAILABLE_NOTICE =
  "The office recently updated this item and it's no longer available — refreshed with the latest scope.";

interface ComposerQuickActionsProps {
  proposedScopeId: string;
  version: string;
  onCommitted: () => void;
  onConflict: (message?: string) => void;
}

/**
 * Session 5C, build-log/120: the office-curated, at-most-six Quick action set (ADR-485). Each
 * action is a resolved catalog-item or assembly target — tapping it dispatches directly through the
 * same field-select/default-only expand-assembly mutations the search surface uses, with no
 * intermediate quantity or optional-item chooser. A target that went unavailable since the field
 * read resolves through the normal selection error and reconciliation path (build-log/119, ADR-485).
 */
export function ComposerQuickActions({ proposedScopeId, version, onCommitted, onConflict }: ComposerQuickActionsProps) {
  const { data } = useQuery({
    queryKey: ["fieldQuickScopeActions"],
    queryFn: () => api.getFieldQuickScopeActions(),
  });

  const dispatchMutation = useMutation({
    mutationFn: (action: QuickScopeActionFieldRowResponse): Promise<unknown> => {
      if (action.catalogItemId !== null) {
        return api.fieldSelectProposedScopeLine(
          proposedScopeId,
          { lineType: "KnownCatalogItem", catalogItemId: action.catalogItemId, quantity: 1 },
          version,
        );
      }
      return api.expandProposedScopeAssembly(
        proposedScopeId,
        { offeringAssemblyId: action.offeringAssemblyId!, excludedOptionalItemIds: [] },
        version,
      );
    },
    onSuccess: () => onCommitted(),
    onError: (err) => {
      // Session 5C review fix: the target-unavailable codes must be checked before generic status
      // handling — ExpandAssemblyNotOperationallyEligible is itself an actual 409, so a status-first
      // check would misroute it to the generic conflict notice instead of the unavailable one.
      if (
        err instanceof ApiError &&
        (err.code === "ProposedScope.LineCatalogItemNotFound" ||
          err.code === "ProposedScope.ExpandAssemblyNotOperationallyEligible")
      ) {
        onConflict(QUICK_ACTION_UNAVAILABLE_NOTICE);
        return;
      }
      onConflict();
    },
  });

  const actions = data?.actions ?? [];
  if (actions.length === 0) return null;

  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((action) => (
        <button
          key={action.id}
          type="button"
          disabled={dispatchMutation.isPending}
          onClick={() => dispatchMutation.mutate(action)}
          className={`min-h-[44px] rounded-lg border border-[var(--ophalo-border)] px-3 py-2 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-50 ${FOCUS_RING}`}
        >
          {action.targetDisplayName}
        </button>
      ))}
    </div>
  );
}
