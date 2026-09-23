import { forwardRef, useEffect, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Check, Minus, Plus, RefreshCw, Search, X } from "lucide-react";
import { KeepButton } from "../../components/keep/KeepButton";
import {
  api,
  ApiError,
  type ActualWorkAddLineBody,
  type ActualWorkNudgeSuggestionFieldRowResponse,
  type FieldScopeSearchResultResponse,
} from "../../lib/apiClient";

// Maintainability review item 7: split out of ActualWorkComposer.tsx (see build-log for the
// composer-family split). No behavior change. FOCUS_RING/INPUT_CLS are redeclared locally rather
// than imported, matching the existing convention in this directory (ComposerSearchAndAdd.tsx,
// ComposerDraftList.tsx, etc. each keep their own copy).
//
// ActualWorkNudgeChips moves here too even though it wasn't separately named for this slice: it is
// used exclusively inside ActualWorkSearchAndAdd (its only call site) and moving it here avoids a
// circular import between this file and ActualWorkComposer.tsx that exporting it in place would
// require, without changing its behavior or its single consumer.

const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2";

const INPUT_CLS =
  `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-sm ` +
  `text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] px-3 py-2 ${FOCUS_RING}`;

interface ActualWorkSearchAndAddProps {
  actualWorkId: string;
  version: string;
  // ADR-494 D2 (4c-iii): the resolved name of the Draft's ticket-default performer, shown as the
  // default option in the add panel's performer picker. A new line inherits the ticket default
  // unless the recorder picks a different technician; existing lines cannot be re-attributed (no
  // backend route).
  defaultPerformerName: string | null;
  onCommitted: () => Promise<void>;
  onConflict: (message?: string) => void;
  onConnectionFailure: (message: string, retry: () => void) => void;
  onConnectionRecovered: () => void;
  /** When true (inline/drawer host only), a first Escape with results showing clears them instead
   *  of falling straight through to the drawer's Escape-to-close. Arrow/Enter navigation is
   *  unaffected by this flag and works in both presentations. */
  dismissResultsOnEscape?: boolean;
}

type Selection = { kind: "catalog"; item: FieldScopeSearchResultResponse } | { kind: "custom" };

export const ActualWorkSearchAndAdd = forwardRef<HTMLInputElement, ActualWorkSearchAndAddProps>(function ActualWorkSearchAndAdd(
  {
    actualWorkId,
    version,
    defaultPerformerName,
    onCommitted,
    onConflict,
    onConnectionFailure,
    onConnectionRecovered,
    dismissResultsOnEscape = false,
  },
  ref,
) {
  const inputRef = useRef<HTMLInputElement>(null);
  // Merge the forwarded ref (used by the drawer's `initialFocus`) with a local handle so the clear
  // button can return focus to the input. Callback-ref keeps the existing install timing exactly.
  const setInputRef = (node: HTMLInputElement | null) => {
    inputRef.current = node;
    if (typeof ref === "function") ref(node);
    else if (ref) ref.current = node;
  };

  const [searchText, setSearchText] = useState("");
  const [debouncedText, setDebouncedText] = useState("");
  const [selection, setSelection] = useState<Selection | null>(null);
  const [customDescription, setCustomDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [note, setNote] = useState("");
  // "" => inherit the Draft's ticket default; any other value is an explicit per-line override.
  const [performerId, setPerformerId] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: performerCandidates } = useQuery({
    queryKey: ["actualWorkPerformerCandidates"],
    queryFn: () => api.getActualWorkPerformerCandidates(),
    enabled: selection !== null,
  });

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedText(searchText.trim()), 250);
    return () => clearTimeout(handle);
  }, [searchText]);

  const { data: results, isLoading, isFetching } = useQuery({
    queryKey: ["fieldScopeSearch", "actualWork", debouncedText],
    queryFn: () => api.getFieldScopeSearch({ search: debouncedText, limit: 20 }),
    enabled: selection === null && debouncedText.length > 0,
  });

  const catalogResults = (results?.items ?? []).filter((item) => item.kind === "CatalogItem");
  const assemblyResults = (results?.items ?? []).filter((item) => item.kind === "OfferingAssembly");
  // In-drawer success feedback for both add paths (direct catalog/custom line and assembly expand).
  // Kept open across adds; cleared when the recorder starts a new search or picks a new result.
  const [addNotice, setAddNotice] = useState<string | null>(null);

  // Build Log 129, 5d-ii-d: session-only Paired Nudges state, mirroring
  // useProposedScopeCapture's nudge shape (build-log/125) but kept inline here since this
  // composer's mutation logic already lives in this component rather than an extracted hook.
  // Retirement and the read generation live in refs so they reflect the latest value inside async
  // continuations without waiting for a re-render.
  const [nudge, setNudge] = useState<{ ruleId: string; suggestions: ActualWorkNudgeSuggestionFieldRowResponse[] } | null>(null);
  const retiredRuleIdsRef = useRef<Set<string>>(new Set());
  const nudgeGenerationRef = useRef(0);

  async function fetchNudge(trigger: { triggerCatalogItemId: string } | { triggerOfferingAssemblyId: string }) {
    const myGeneration = ++nudgeGenerationRef.current;
    try {
      const result = await api.getActualWorkNudgeFieldSuggestions(actualWorkId, trigger);
      if (myGeneration !== nudgeGenerationRef.current) return;
      if (result.ruleId && result.suggestions.length > 0 && !retiredRuleIdsRef.current.has(result.ruleId)) {
        setNudge({ ruleId: result.ruleId, suggestions: result.suggestions });
      }
    } catch {
      // Silent by design (build-log/125 precedent): a nudge-read failure never surfaces to the technician.
    }
  }

  function resetAfterSuccess() {
    setError(null);
    setSelection(null);
    setSearchText("");
    setDebouncedText("");
    setCustomDescription("");
    setQuantity("1");
    setNote("");
    setPerformerId("");
  }

  // The mutation takes an explicit, click-time snapshot of the payload/trigger rather than reading
  // `selection`/`quantity`/`note`/`customDescription` state — a technician can edit those fields
  // after a connection failure before pressing Retry, and the retry closure must replay the exact
  // operation that failed, not whatever the fields currently hold.
  type AddLineVariables = { body: ActualWorkAddLineBody; trigger: { triggerCatalogItemId: string } | null; label: string };

  const addMutation = useMutation({
    mutationFn: (variables: AddLineVariables) => api.addActualWorkLine(actualWorkId, variables.body, version),
    onSuccess: async (_data, variables) => {
      resetAfterSuccess();
      setAddNotice(`Added ${variables.label}.`);
      onConnectionRecovered();
      await onCommitted();
      if (variables.trigger) void fetchNudge(variables.trigger);
    },
    onError: (err, variables) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't add actual work.", () => addMutation.mutate(variables));
        return;
      }
      if (err.status === 409) {
        onConflict();
        return;
      }
      // 400 (validation) and 422 (`ActualWork.PerformerIneligible` — a stale per-line performer
      // pick) both surface inline so the recorder can correct the field without a reconcile churn.
      if (err.status !== 400 && err.status !== 422) {
        onConflict();
        return;
      }
      setError(err.message);
    },
  });

  const expandAssemblyMutation = useMutation({
    mutationFn: (assembly: FieldScopeSearchResultResponse) =>
      api.expandActualWorkAssembly(actualWorkId, { offeringAssemblyId: assembly.id, includedOptionalItemIds: [] }, version),
    onSuccess: async (result, assembly) => {
      const added = `Added ${assembly.displayName} (${result.lineIds.length} item${result.lineIds.length === 1 ? "" : "s"}).`;
      setAddNotice(
        result.skippedCatalogItemIds.length === 0
          ? added
          : `${added} ${result.skippedCatalogItemIds.length} already on this visit.`,
      );
      setError(null);
      onConnectionRecovered();
      await onCommitted();
      void fetchNudge({ triggerOfferingAssemblyId: assembly.id });
    },
    onError: (err, assembly) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't add assembly items.", () => expandAssemblyMutation.mutate(assembly));
        return;
      }
      if (err.status !== 400) {
        onConflict();
        return;
      }
      setError(err.message);
    },
  });

  const canAdd =
    selection !== null &&
    Number(quantity) > 0 &&
    (selection.kind === "catalog" || customDescription.trim().length > 0);

  // Combobox keyboard navigation over the actionable results. Section headers and the trailing
  // "Add as custom item" action are deliberately excluded — Arrow keys walk only the assembly and
  // catalog options, in that display order.
  type NavResult = { kind: "assembly" | "catalog"; item: FieldScopeSearchResultResponse };
  const navigableResults: NavResult[] = [
    ...assemblyResults.map((item): NavResult => ({ kind: "assembly", item })),
    ...catalogResults.map((item): NavResult => ({ kind: "catalog", item })),
  ];
  const resultOptionId = (r: NavResult) => `aw-result-${r.kind}-${r.item.id}`;

  const [activeResultIndex, setActiveResultIndex] = useState(-1);
  const navResultsRef = useRef(navigableResults);
  navResultsRef.current = navigableResults;
  const activeResultIndexRef = useRef(activeResultIndex);
  activeResultIndexRef.current = activeResultIndex;
  const expandPendingRef = useRef(expandAssemblyMutation.isPending);
  expandPendingRef.current = expandAssemblyMutation.isPending;
  const expandMutateRef = useRef(expandAssemblyMutation.mutate);
  expandMutateRef.current = expandAssemblyMutation.mutate;
  const dismissResultsOnEscapeRef = useRef(dismissResultsOnEscape);
  dismissResultsOnEscapeRef.current = dismissResultsOnEscape;

  // Drop the highlight whenever the result set (or the query behind it) changes.
  useEffect(() => {
    setActiveResultIndex(-1);
  }, [results, debouncedText]);

  // Keep the highlighted option scrolled into view as the selection moves.
  useEffect(() => {
    if (activeResultIndex < 0) return;
    const r = navResultsRef.current[activeResultIndex];
    if (r) document.getElementById(resultOptionId(r))?.scrollIntoView({ block: "nearest" });
  }, [activeResultIndex]);

  // Capture-phase key handler (mirrors the discard-confirm pattern above): while the search input
  // holds focus, Arrow/Enter drive the listbox in every presentation. Escape is only intercepted
  // when `dismissResultsOnEscape` is set (inline/drawer host) — there a first Escape clears the
  // results and only a subsequent one reaches KeepModal's Escape-to-close; the modal composer
  // keeps its unchanged one-Escape-to-close behavior.
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (document.activeElement !== inputRef.current) return;
      const options = navResultsRef.current;
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        if (options.length === 0) return;
        e.preventDefault();
        e.stopPropagation();
        setActiveResultIndex((i) =>
          e.key === "ArrowDown"
            ? (i + 1) % options.length
            : i <= 0
              ? options.length - 1
              : i - 1,
        );
      } else if (e.key === "Enter") {
        const r = options[activeResultIndexRef.current];
        if (!r) return;
        e.preventDefault();
        e.stopPropagation();
        if (r.kind === "assembly") {
          if (!expandPendingRef.current) expandMutateRef.current(r.item);
        } else {
          setAddNotice(null);
          setSelection({ kind: "catalog", item: r.item });
        }
      } else if (e.key === "Escape") {
        if (!dismissResultsOnEscapeRef.current) return;
        if (options.length === 0 && activeResultIndexRef.current < 0) return;
        e.preventDefault();
        e.stopPropagation();
        setActiveResultIndex(-1);
        setSearchText("");
        setDebouncedText("");
        setAddNotice(null);
      }
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => document.removeEventListener("keydown", onKeyDown, true);
  }, []);

  return (
    <div className="space-y-2">
      {selection === null ? (
        <>
          <div className="relative">
            <Search
              aria-hidden="true"
              className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--ophalo-muted)]"
            />
            <input
              ref={setInputRef}
              type="text"
              value={searchText}
              onChange={(e) => {
                setSearchText(e.target.value);
                setAddNotice(null);
              }}
              placeholder="Search by name or SKU..."
              role="combobox"
              aria-autocomplete="list"
              aria-expanded={navigableResults.length > 0}
              aria-controls="aw-results-listbox"
              aria-activedescendant={
                activeResultIndex >= 0 && activeResultIndex < navigableResults.length
                  ? resultOptionId(navigableResults[activeResultIndex])
                  : undefined
              }
              className={`${INPUT_CLS.replace("px-3", "pl-9 pr-16")}`}
            />
            <div className="absolute right-2 top-1/2 flex -translate-y-1/2 items-center gap-1">
              {isFetching && (
                <RefreshCw
                  aria-hidden="true"
                  className="h-4 w-4 animate-spin text-[var(--ophalo-muted)]"
                />
              )}
              {searchText.length > 0 && (
                <button
                  type="button"
                  aria-label="Clear search"
                  onClick={() => {
                    setSearchText("");
                    setDebouncedText("");
                    setAddNotice(null);
                    inputRef.current?.focus();
                  }}
                  className={`rounded-md p-1 text-[var(--ophalo-muted)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                >
                  <X className="h-4 w-4" />
                </button>
              )}
            </div>
          </div>
          {debouncedText.length > 0 && (
            <div className="rounded-lg border border-[var(--ophalo-border)] p-1">
              {isLoading && <p className="px-3 py-2 text-xs text-[var(--ophalo-muted)]">Searching...</p>}
              {!isLoading && (
                <>
                  <ul
                    id="aw-results-listbox"
                    role="listbox"
                    aria-label="Search results"
                    className="max-h-48 overflow-y-auto space-y-1"
                  >
                    {assemblyResults.length > 0 && (
                      <li
                        role="presentation"
                        className="rounded-lg border-l-4 border-[var(--keep-accent)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]"
                      >
                        Matching assemblies
                      </li>
                    )}
                    {assemblyResults.map((item, i) => {
                      const active = activeResultIndex === i;
                      return (
                        <li
                          key={item.id}
                          id={`aw-result-assembly-${item.id}`}
                          role="option"
                          aria-selected={active}
                          aria-disabled={expandAssemblyMutation.isPending || undefined}
                          onMouseMove={() => setActiveResultIndex(i)}
                          onClick={() => {
                            if (!expandAssemblyMutation.isPending) expandAssemblyMutation.mutate(item);
                          }}
                          className={`cursor-pointer rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] ${active ? "bg-[var(--ophalo-canvas)]" : "hover:bg-[var(--ophalo-canvas)]"} ${expandAssemblyMutation.isPending ? "opacity-50" : ""}`}
                        >
                          <span>{item.displayName}</span>
                          <span className="ml-2 rounded bg-[var(--ophalo-canvas)] px-1.5 py-0.5 text-xs font-medium text-[var(--ophalo-muted)]">
                            Assembly
                          </span>
                          {item.defaultItemCount !== null && (
                            <span className="ml-2 text-xs text-[var(--ophalo-muted)]">Expands {item.defaultItemCount} items</span>
                          )}
                        </li>
                      );
                    })}
                    {catalogResults.length > 0 && (
                      <li
                        role="presentation"
                        className="mt-3 rounded-lg border-l-4 border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-xs font-bold uppercase tracking-wide text-[var(--ophalo-ink)]"
                      >
                        Matching catalog items
                      </li>
                    )}
                    {catalogResults.map((item, i) => {
                      const index = assemblyResults.length + i;
                      const active = activeResultIndex === index;
                      return (
                        <li
                          key={item.id}
                          id={`aw-result-catalog-${item.id}`}
                          role="option"
                          aria-selected={active}
                          onMouseMove={() => setActiveResultIndex(index)}
                          onClick={() => {
                            setAddNotice(null);
                            setSelection({ kind: "catalog", item });
                          }}
                          className={`cursor-pointer rounded-lg px-3 py-2 text-sm text-[var(--ophalo-ink)] ${active ? "bg-[var(--ophalo-canvas)]" : "hover:bg-[var(--ophalo-canvas)]"}`}
                        >
                          {item.displayName}
                        </li>
                      );
                    })}
                  </ul>
                  <button
                    type="button"
                    onClick={() => {
                      setAddNotice(null);
                      setSelection({ kind: "custom" });
                    }}
                    className={`mt-1 w-full text-left rounded-lg px-3 py-2 text-sm font-medium text-[var(--keep-accent)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
                  >
                    Add as custom item
                  </button>
                </>
              )}
            </div>
          )}
          {addNotice && (
            <p role="status" aria-live="polite" className="flex items-center gap-1.5 px-3 py-2 text-xs font-medium text-[var(--ophalo-ink)]">
              <Check aria-hidden="true" className="h-3.5 w-3.5 text-[var(--keep-accent)]" />
              {addNotice}
            </p>
          )}
          {error && <p className="px-3 text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
          {nudge && (
            <ActualWorkNudgeChips
              actualWorkId={actualWorkId}
              version={version}
              nudge={nudge}
              onAccepted={async () => {
                retiredRuleIdsRef.current.add(nudge.ruleId);
                setNudge(null);
                await onCommitted();
              }}
              onConflict={(message) => {
                setNudge(null);
                onConflict(message);
              }}
              onConnectionFailure={onConnectionFailure}
              onConnectionRecovered={onConnectionRecovered}
              onDismiss={() => {
                retiredRuleIdsRef.current.add(nudge.ruleId);
                setNudge(null);
              }}
            />
          )}
        </>
      ) : (
        <div className="rounded-lg border border-[var(--ophalo-border)] p-3 space-y-2">
          {selection.kind === "catalog" ? (
            <p className="text-sm font-medium text-[var(--ophalo-ink)]">{selection.item.displayName}</p>
          ) : (
            <input
              type="text"
              value={customDescription}
              onChange={(e) => setCustomDescription(e.target.value)}
              placeholder="Describe the item"
              className={INPUT_CLS}
            />
          )}
          <div className="flex gap-2">
            <div className="flex shrink-0 items-stretch">
              <button
                type="button"
                aria-label="Decrease quantity"
                disabled={!(Number.isFinite(Number(quantity)) && Number(quantity) > 1)}
                onClick={() => {
                  const n = Number(quantity);
                  setQuantity(String(Math.max(1, (Number.isFinite(n) ? n : 1) - 1)));
                }}
                className={`flex w-9 items-center justify-center rounded-l-lg border border-[var(--ophalo-border)] text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] disabled:opacity-40 ${FOCUS_RING}`}
              >
                <Minus className="h-4 w-4" />
              </button>
              <input
                type="number"
                min="0"
                step="any"
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
                className={`${INPUT_CLS.replace("w-full ", "").replace("rounded-lg", "rounded-none")} w-16 border-x-0 text-center`}
                aria-label="Quantity"
              />
              <button
                type="button"
                aria-label="Increase quantity"
                onClick={() => {
                  const n = Number(quantity);
                  setQuantity(String((Number.isFinite(n) && n > 0 ? n : 0) + 1));
                }}
                className={`flex w-9 items-center justify-center rounded-r-lg border border-[var(--ophalo-border)] text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] ${FOCUS_RING}`}
              >
                <Plus className="h-4 w-4" />
              </button>
            </div>
            <input
              type="text"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Note (optional)"
              className={INPUT_CLS}
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="actual-work-line-performer" className="text-xs font-medium text-[var(--ophalo-muted)]">
              Performed by
            </label>
            <select
              id="actual-work-line-performer"
              value={performerId}
              onChange={(e) => setPerformerId(e.target.value)}
              className={INPUT_CLS}
            >
              <option value="">
                Ticket default{defaultPerformerName ? ` (${defaultPerformerName})` : ""}
              </option>
              {(performerCandidates?.candidates ?? []).map((c) => (
                <option key={c.accountUserId} value={c.accountUserId}>
                  {c.displayName}
                </option>
              ))}
            </select>
          </div>
          {error && <p className="text-xs text-[var(--ophalo-danger,#c0392b)]">{error}</p>}
          <div className="flex gap-2">
            <KeepButton
              variant="teal"
              disabled={!canAdd || addMutation.isPending}
              onClick={() => {
                const performer = performerId ? { performedByAccountUserId: performerId } : {};
                const body: ActualWorkAddLineBody =
                  selection?.kind === "catalog"
                    ? { catalogItemId: selection.item.id, actualQuantity: Number(quantity), note: note.trim() || null, ...performer }
                    : { offCatalogDescription: customDescription, actualQuantity: Number(quantity), note: note.trim() || null, ...performer };
                const trigger = selection?.kind === "catalog" ? { triggerCatalogItemId: selection.item.id } : null;
                const label = selection?.kind === "catalog" ? selection.item.displayName : customDescription.trim();
                addMutation.mutate({ body, trigger, label });
              }}
              className="flex-1"
            >
              Add item
            </KeepButton>
            <KeepButton
              variant="secondary"
              onClick={() => {
                setSelection(null);
                setError(null);
              }}
              className="flex-1"
            >
              Cancel
            </KeepButton>
          </div>
        </div>
      )}
    </div>
  );
});

interface ActualWorkNudgeChipsProps {
  actualWorkId: string;
  version: string;
  nudge: { ruleId: string; suggestions: ActualWorkNudgeSuggestionFieldRowResponse[] };
  onAccepted: () => void;
  onConflict: (message?: string) => void;
  onConnectionFailure: (message: string, retry: () => void) => void;
  onConnectionRecovered: () => void;
  onDismiss: () => void;
}

/**
 * Build Log 129, 5d-ii-d: the price-blind "Often added together" chip panel — mirrors
 * ComposerNudgePanel's (build-log/125) UX exactly (session-only chips, client-side Dismiss,
 * default quantity 1/no note/no optional-item inclusions on accept) but stays inline in this file
 * rather than an extracted panel component, matching this composer's established shape. Accepting
 * a chip dispatches the same add-line/expand-assembly mutations the rest of the composer uses;
 * only success retires the panel. A 409 clears the panel without retiring the rule — the caller's
 * `onConflict` handles reconciliation — and any other failure keeps the panel up so the technician
 * can retry.
 */
function ActualWorkNudgeChips({
  actualWorkId,
  version,
  nudge,
  onAccepted,
  onConflict,
  onConnectionFailure,
  onConnectionRecovered,
  onDismiss,
}: ActualWorkNudgeChipsProps) {
  const [error, setError] = useState<string | null>(null);

  const acceptMutation = useMutation({
    mutationFn: (suggestion: ActualWorkNudgeSuggestionFieldRowResponse): Promise<unknown> => {
      if (suggestion.catalogItemId !== null) {
        return api.addActualWorkLine(
          actualWorkId,
          { catalogItemId: suggestion.catalogItemId, actualQuantity: 1, note: null },
          version,
        );
      }
      return api.expandActualWorkAssembly(
        actualWorkId,
        { offeringAssemblyId: suggestion.offeringAssemblyId!, includedOptionalItemIds: [] },
        version,
      );
    },
    onSuccess: () => {
      setError(null);
      onConnectionRecovered();
      onAccepted();
    },
    onError: (err, suggestion) => {
      if (!(err instanceof ApiError)) {
        onConnectionFailure("Couldn't add suggested item.", () => acceptMutation.mutate(suggestion));
        return;
      }
      if (err.status === 409) {
        onConflict();
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


