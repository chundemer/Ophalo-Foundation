import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, type ProposedScopeDetailResult, type ScopeNudgeSuggestionFieldRowResponse } from "../../lib/apiClient";

// Session 5, build-log/125: the two commit shapes that are eligible to fire a nudge read — a
// resolved catalog-item add or an assembly expansion. Off-catalog adds, edits, removes, Undo, and
// submit use the plain no-trigger commit form and never carry one of these.
export type ScopeNudgeTrigger = { catalogItemId: string } | { offeringAssemblyId: string };

export interface ProposedScopeNudgeState {
  ruleId: string;
  suggestions: ScopeNudgeSuggestionFieldRowResponse[];
}

/**
 * Session 3.4f-1, build-log/118: hoisted at RequestDetailContent so the simultaneously-mounted
 * desktop/mobile layouts (CSS-toggled, not conditionally mounted) share one probe/draft/modal
 * state instead of double-probing or creating duplicate drafts. A 403 on the availability probe
 * hides the entry point entirely (locked decision, build-log/118) rather than rendering an error.
 */
export type ProposedScopeCaptureState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error"; message: string }
  | { status: "no-scope" }
  | { status: "draft"; scope: ProposedScopeDetailResult }
  | { status: "submitted"; scope: ProposedScopeDetailResult };

// Session 5A, build-log/120: the one notice every composer mutation surfaces on a 409 or an
// ambiguous (non-ApiError) failure — never retried automatically.
export const PROPOSED_SCOPE_CONFLICT_NOTICE =
  "This proposed scope changed elsewhere — refreshed with the latest scope. Try again.";

// Session 5C review fix, build-log/120: the reconciliation reload itself can fail (e.g. the
// technician's connection drops between the conflict and the re-fetch). This is distinct from the
// conflict notice above — it means the client does not yet know the authoritative scope state.
export const PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE =
  "Unable to refresh scope. Check your connection and try again.";

export function useProposedScopeCapture(requestId: string) {
  const [state, setState] = useState<ProposedScopeCaptureState>({ status: "loading" });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [conflictNotice, setConflictNotice] = useState<string | null>(null);

  // Session 5, build-log/125: session-only nudge state — at most one visible suggestion set, and
  // the rule IDs retired (accepted/dismissed) for the currently open composer session. Retirement
  // and the read generation live in refs, not state, because they must reflect the latest value
  // inside async continuations without waiting for a re-render.
  const [nudge, setNudge] = useState<ProposedScopeNudgeState | null>(null);
  const retiredRuleIdsRef = useRef<Set<string>>(new Set());
  const nudgeGenerationRef = useRef(0);

  const probe = useCallback(async () => {
    setState({ status: "loading" });
    try {
      const result = await api.getCurrentProposedScopeForRequest(requestId);
      if (result.scope === null) {
        setState({ status: "no-scope" });
      } else if (result.scope.status === "Draft") {
        setState({ status: "draft", scope: result.scope });
      } else {
        setState({ status: "submitted", scope: result.scope });
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ status: "hidden" });
        return;
      }
      setState({ status: "error", message: "Unable to load proposed scope." });
    }
  }, [requestId]);

  useEffect(() => {
    void probe();
  }, [probe]);

  // 3.4f-2 calls this after every field-select/expand-assembly commit (and on 409/timeout) to
  // replace local state with the authoritative snapshot — mutation responses carry only
  // {id, status, version}, never updated lines.
  //
  // Session 5, build-log/125: an optional trigger marks this commit as nudge-eligible. The
  // authoritative reload always runs first; only after it succeeds does a trigger-carrying call
  // fire the nudge read. `nudgeGenerationRef` is bumped for every trigger-carrying call (and by
  // `closeModal`) so a result can only update state when it is still the newest outstanding read —
  // an older trigger's late response, or any response after close, is discarded.
  const refetchScope = useCallback(
    async (trigger?: ScopeNudgeTrigger) => {
      if (state.status !== "draft" && state.status !== "submitted") return;
      const myGeneration = trigger ? ++nudgeGenerationRef.current : nudgeGenerationRef.current;
      const scope = await api.getProposedScope(state.scope.id);
      setState({ status: scope.status === "Draft" ? "draft" : "submitted", scope });
      if (!trigger) return;
      try {
        const result = await api.getScopeNudgeFieldSuggestions(
          scope.id,
          "catalogItemId" in trigger
            ? { triggerCatalogItemId: trigger.catalogItemId }
            : { triggerOfferingAssemblyId: trigger.offeringAssemblyId },
        );
        if (myGeneration !== nudgeGenerationRef.current) return;
        if (result.ruleId && result.suggestions.length > 0 && !retiredRuleIdsRef.current.has(result.ruleId)) {
          setNudge({ ruleId: result.ruleId, suggestions: result.suggestions });
        }
      } catch {
        // Silent by design (build-log/125): a nudge-read failure never surfaces to the technician.
      }
    },
    [state],
  );

  // Accept (after the field-select/expand-assembly mutation succeeds) and Dismiss both retire the
  // rule for the rest of this open session and clear the visible panel. A 409 during accept clears
  // the panel without retiring it — the Draft state it was computed against is stale, but the rule
  // may legitimately fire again once the office/technician state settles.
  const retireNudge = useCallback((ruleId: string) => {
    retiredRuleIdsRef.current.add(ruleId);
    setNudge(null);
  }, []);

  const clearNudge = useCallback(() => setNudge(null), []);

  const startCapture = useCallback(async () => {
    if (state.status === "draft") {
      setIsModalOpen(true);
      return;
    }
    if (state.status !== "no-scope" && state.status !== "submitted") return;
    try {
      const created = await api.createProposedScope({ requestId });
      setState({
        status: "draft",
        scope: {
          id: created.id,
          requestId: created.requestId,
          status: created.status,
          concurrencyVersion: created.concurrencyVersion,
          lines: [],
        },
      });
      setIsModalOpen(true);
    } catch {
      setState({ status: "error", message: "Unable to start a proposed scope." });
    }
  }, [state, requestId]);

  // Session 3.4g: opens the modal read-only against an already-submitted/reviewed scope — never
  // creates anything, unlike startCapture's no-scope/submitted branches.
  const startView = useCallback(() => {
    if (state.status !== "submitted") return;
    setIsModalOpen(true);
  }, [state]);

  // Session 5, build-log/125: closing the modal ends the nudge "session" — retirement and the
  // visible panel are session-scoped, not hook-lifetime-scoped, and closeModal (not unmount) is the
  // boundary. Bumping the generation here also discards any nudge-read still in flight so it cannot
  // revive a panel after the composer reopens.
  const closeModal = useCallback(() => {
    nudgeGenerationRef.current++;
    retiredRuleIdsRef.current = new Set();
    setNudge(null);
    setIsModalOpen(false);
  }, []);

  // Session 5C review fix: reconcileAfterConflict's own state is what the last reload attempt
  // needs to retry with — kept separate from conflictNotice so a reload-failure notice can
  // temporarily replace it without losing what the eventual successful reload should say.
  const [pendingReconcileMessage, setPendingReconcileMessage] = useState<string | null>(null);

  // Session 5A, build-log/120: the single reusable 409/ambiguous-failure path every composer
  // mutation will call instead of duplicating notice-plus-refetch handling per surface. It only
  // reloads the authoritative scope and surfaces one shared notice — it never retries the mutation.
  //
  // Session 5C review fix: the authoritative reload itself can fail. The original mutation is
  // still never retried, but a failed reload must not claim the scope was refreshed — it leaves
  // the current (pre-conflict) scope state untouched, surfaces a distinct reload-failure notice,
  // and remembers the intended notice so `retryReconciliation` can re-attempt the same reload.
  const reconcileAfterConflict = useCallback(
    async (message: string = PROPOSED_SCOPE_CONFLICT_NOTICE) => {
      try {
        await refetchScope();
        setConflictNotice(message);
        setPendingReconcileMessage(null);
      } catch {
        setConflictNotice(PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE);
        setPendingReconcileMessage(message);
      }
    },
    [refetchScope],
  );

  // Session 5C review fix: explicit retry of a failed reconciliation reload, using the same
  // authoritative read and the notice the failed attempt was trying to show.
  const retryReconciliation = useCallback(
    () => reconcileAfterConflict(pendingReconcileMessage ?? undefined),
    [reconcileAfterConflict, pendingReconcileMessage],
  );

  const clearConflictNotice = useCallback(() => setConflictNotice(null), []);

  return {
    state,
    isModalOpen,
    startCapture,
    startView,
    closeModal,
    refetchScope,
    conflictNotice,
    reconcileAfterConflict,
    retryReconciliation,
    clearConflictNotice,
    nudge,
    retireNudge,
    clearNudge,
  };
}
