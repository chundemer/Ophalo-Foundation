import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError, type ScopeNudgeSuggestionFieldRowResponse } from "../../lib/apiClient";
import type { ProposedScopeNudgeState } from "./useProposedScopeCapture";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

interface ComposerNudgePanelProps {
  proposedScopeId: string;
  version: string;
  nudge: ProposedScopeNudgeState;
  onAccepted: () => void;
  onAcceptConflict: () => void;
  onDismiss: () => void;
}

/**
 * Session 5, build-log/125: the single price-blind chip panel between the add controls and the
 * Draft list. Accepting a chip dispatches the same field-select/expand-assembly mutations the rest
 * of the composer uses (default quantity 1, no note/optional-item exclusions); only success retires
 * the whole rule and clears the panel. A 409 clears the panel without retiring it (the caller's
 * `onAcceptConflict` handles reconciliation); any other failure preserves the panel so the
 * technician can retry.
 */
export function ComposerNudgePanel({ proposedScopeId, version, nudge, onAccepted, onAcceptConflict, onDismiss }: ComposerNudgePanelProps) {
  const [error, setError] = useState<string | null>(null);

  const acceptMutation = useMutation({
    mutationFn: (suggestion: ScopeNudgeSuggestionFieldRowResponse): Promise<unknown> => {
      if (suggestion.catalogItemId !== null) {
        return api.fieldSelectProposedScopeLine(
          proposedScopeId,
          { lineType: "KnownCatalogItem", catalogItemId: suggestion.catalogItemId, quantity: 1 },
          version,
        );
      }
      return api.expandProposedScopeAssembly(
        proposedScopeId,
        { offeringAssemblyId: suggestion.offeringAssemblyId!, excludedOptionalItemIds: [] },
        version,
      );
    },
    onSuccess: () => {
      setError(null);
      onAccepted();
    },
    onError: (err) => {
      // The target-unavailable codes must be checked before generic status handling —
      // ExpandAssemblyNotOperationallyEligible is itself an actual 409, so a status-first check
      // would misroute it to conflict reconciliation instead of the unavailable notice (matches
      // ComposerQuickActions' existing ordering).
      if (
        err instanceof ApiError &&
        (err.code === "ProposedScope.LineCatalogItemNotFound" || err.code === "ProposedScope.ExpandAssemblyNotOperationallyEligible")
      ) {
        setError("The office recently updated this item and it's no longer available.");
        return;
      }
      if (err instanceof ApiError && err.status === 409) {
        onAcceptConflict();
        return;
      }
      setError("Something went wrong. Try again.");
    },
  });

  return (
    <div className="rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] p-3 space-y-2">
      <p className="text-xs font-medium text-[var(--ophalo-muted)]">Often added together</p>
      <div className="flex flex-wrap gap-2">
        {nudge.suggestions.map((suggestion) => (
          <button
            key={suggestion.id}
            type="button"
            disabled={acceptMutation.isPending}
            onClick={() => acceptMutation.mutate(suggestion)}
            className={`min-h-[44px] rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-3 py-2 text-sm font-medium text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-50 ${FOCUS_RING}`}
          >
            {suggestion.displayName}
          </button>
        ))}
        <button
          type="button"
          disabled={acceptMutation.isPending}
          onClick={onDismiss}
          className={`min-h-[44px] rounded-lg px-3 py-2 text-sm font-medium text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-50 ${FOCUS_RING}`}
        >
          Dismiss
        </button>
      </div>
      {error && (
        <p role="alert" className="text-sm text-[var(--ophalo-danger)]">
          {error}
        </p>
      )}
    </div>
  );
}
