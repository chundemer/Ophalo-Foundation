import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type ProposedScopeDetailResult } from "../../lib/apiClient";

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
  const refetchScope = useCallback(async () => {
    if (state.status !== "draft" && state.status !== "submitted") return;
    const scope = await api.getProposedScope(state.scope.id);
    setState({ status: scope.status === "Draft" ? "draft" : "submitted", scope });
  }, [state]);

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

  const closeModal = useCallback(() => setIsModalOpen(false), []);

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
  };
}
