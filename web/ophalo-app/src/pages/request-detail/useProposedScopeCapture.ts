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

  // Session 5A, build-log/120: the single reusable 409/ambiguous-failure path every composer
  // mutation will call instead of duplicating notice-plus-refetch handling per surface. It only
  // reloads the authoritative scope and surfaces one shared notice — it never retries the mutation.
  const reconcileAfterConflict = useCallback(
    async (message: string = PROPOSED_SCOPE_CONFLICT_NOTICE) => {
      setConflictNotice(message);
      await refetchScope();
    },
    [refetchScope],
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
    clearConflictNotice,
  };
}
