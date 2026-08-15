import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../lib/apiClient";
import { KeepButton } from "../../components/keep/KeepButton";
import { type ScopeCaptureRungProps } from "./ProposedScopeCaptureModal";

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-1";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

const CONFLICT_NOTICE = "This proposed scope changed elsewhere — refreshed with the latest scope. Try again.";

/**
 * ADR-461 rung 1: browse the account's Active, operationally-eligible offering assemblies
 * (server-filtered — FieldOfferingAssemblyReadApiService, Session 3.4c) and expand one into
 * required + opted-in-optional lines via expand-assembly (Session 3.4e). No server search on this
 * list endpoint (limit/cursor only), so filtering here is client-side over the loaded page.
 */
export function PrimaryOfferingRung({ proposedScopeId, version, onCommitted, onConflict }: ScopeCaptureRungProps) {
  const [filterText, setFilterText] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [excludedItemIds, setExcludedItemIds] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);

  const { data: listPage, isLoading: isListLoading } = useQuery({
    queryKey: ["fieldOfferingAssemblies"],
    queryFn: () => api.getFieldOfferingAssemblies({ limit: 50 }),
  });

  const { data: detail, isLoading: isDetailLoading } = useQuery({
    queryKey: ["fieldOfferingAssembly", selectedId],
    queryFn: () => api.getFieldOfferingAssembly(selectedId!),
    enabled: selectedId !== null,
  });

  const expandMutation = useMutation({
    mutationFn: () =>
      api.expandProposedScopeAssembly(
        proposedScopeId,
        { offeringAssemblyId: selectedId!, excludedOptionalItemIds: Array.from(excludedItemIds) },
        version,
      ),
    onSuccess: () => {
      setError(null);
      setSelectedId(null);
      setExcludedItemIds(new Set());
      onCommitted();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        onConflict(CONFLICT_NOTICE);
        setSelectedId(null);
        return;
      }
      if (!(err instanceof ApiError)) {
        onConflict(CONFLICT_NOTICE);
        setSelectedId(null);
        return;
      }
      setError(
        err.code === "ProposedScope.ExpandAssemblyNotOperationallyEligible"
          ? "This offering/assembly can no longer be selected."
          : "Something went wrong. Try again.",
      );
    },
  });

  function toggleOptional(itemId: string, include: boolean) {
    setExcludedItemIds((prev) => {
      const next = new Set(prev);
      if (include) next.delete(itemId);
      else next.add(itemId);
      return next;
    });
  }

  if (selectedId !== null) {
    return (
      <div className="space-y-3">
        <button
          type="button"
          onClick={() => {
            setSelectedId(null);
            setExcludedItemIds(new Set());
            setError(null);
          }}
          className={`text-xs font-medium text-[var(--keep-accent)] ${FOCUS_RING}`}
        >
          ← Choose a different offering
        </button>
        {isDetailLoading && <p className="text-sm text-[var(--ophalo-muted)]">Loading…</p>}
        {detail && (
          <div className="space-y-2">
            <p className="text-sm font-medium text-[var(--ophalo-ink)]">{detail.name}</p>
            {detail.items.length > 0 && (
              <ul className="space-y-1">
                {detail.items.map((item) => (
                  <li key={item.id} className="flex items-center gap-2 text-sm text-[var(--ophalo-ink)]">
                    {item.isOptional ? (
                      <input
                        type="checkbox"
                        id={`primary-item-${item.id}`}
                        checked={!excludedItemIds.has(item.id)}
                        onChange={(e) => toggleOptional(item.id, e.target.checked)}
                        className={FOCUS_RING}
                      />
                    ) : (
                      <span className="w-4 text-center text-[var(--ophalo-muted)]" aria-hidden="true">
                        •
                      </span>
                    )}
                    <label htmlFor={item.isOptional ? `primary-item-${item.id}` : undefined}>
                      {item.catalogItemDisplayName}
                      {item.isOptional && <span className="text-[var(--ophalo-muted)]"> (optional)</span>}
                    </label>
                  </li>
                ))}
              </ul>
            )}
            {error && <p className="text-sm text-[var(--ophalo-danger)]">{error}</p>}
            <KeepButton
              type="button"
              variant="teal"
              disabled={expandMutation.isPending}
              onClick={() => expandMutation.mutate()}
              className="w-full"
            >
              Add to scope
            </KeepButton>
          </div>
        )}
      </div>
    );
  }

  const filtered = (listPage?.items ?? []).filter((row) =>
    row.name.toLowerCase().includes(filterText.trim().toLowerCase()),
  );

  return (
    <div className="space-y-2">
      <input
        type="text"
        value={filterText}
        onChange={(e) => setFilterText(e.target.value)}
        placeholder="Filter offerings…"
        className={INPUT_CLS}
      />
      {isListLoading && <p className="text-sm text-[var(--ophalo-muted)]">Loading…</p>}
      {!isListLoading && filtered.length === 0 && (
        <p className="text-sm text-[var(--ophalo-muted)]">No offerings match.</p>
      )}
      <ul className="max-h-48 overflow-y-auto space-y-1">
        {filtered.map((row) => (
          <li key={row.id}>
            <button
              type="button"
              onClick={() => setSelectedId(row.id)}
              className={`w-full text-left rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
            >
              {row.name}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
