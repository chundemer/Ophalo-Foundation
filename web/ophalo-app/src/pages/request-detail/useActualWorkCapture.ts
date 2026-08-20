import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, type ActualWorkHistoryResult } from "../../lib/apiClient";

/**
 * Batch 5b, build-log/129: mirrors useProposedScopeCapture's probe/draft/modal state machine.
 * The only read path is the visit-history endpoint (Batch 5a) — its `canCaptureActualWork` flag
 * (true only for the request's active Responsible recorder) disambiguates a null `openDraft`
 * from "not allowed to capture here", so `hidden` covers both a 403 and a visible-but-not-
 * Responsible caller.
 */
export type ActualWorkCaptureState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error"; message: string }
  | { status: "no-draft"; submittedCount: number }
  | { status: "draft"; draft: NonNullable<ActualWorkHistoryResult["openDraft"]>; submittedCount: number };

export const ACTUAL_WORK_CONFLICT_NOTICE =
  "This visit changed elsewhere — refreshed with the latest draft. Try again.";

export const ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE =
  "Unable to refresh this visit. Check your connection and try again.";

export function useActualWorkCapture(requestId: string) {
  const [state, setState] = useState<ActualWorkCaptureState>({ status: "loading" });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [conflictNotice, setConflictNotice] = useState<string | null>(null);
  const [pendingReconcileMessage, setPendingReconcileMessage] = useState<string | null>(null);

  const probe = useCallback(async () => {
    setState({ status: "loading" });
    try {
      const result = await api.getActualWorkHistoryForRequest(requestId);
      if (!result.canCaptureActualWork) {
        setState({ status: "hidden" });
        return;
      }
      if (result.openDraft) {
        setState({ status: "draft", draft: result.openDraft, submittedCount: result.submittedVisits.length });
      } else {
        setState({ status: "no-draft", submittedCount: result.submittedVisits.length });
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ status: "hidden" });
        return;
      }
      setState({ status: "error", message: "Unable to load actual work." });
    }
  }, [requestId]);

  useEffect(() => {
    void probe();
  }, [probe]);

  // Every composer mutation calls this to replace local state with the authoritative snapshot
  // (mutation responses carry only ids/versions, never the full line set).
  const refetchDraft = useCallback(async () => {
    const result = await api.getActualWorkHistoryForRequest(requestId);
    if (result.openDraft) {
      setState({ status: "draft", draft: result.openDraft, submittedCount: result.submittedVisits.length });
    } else {
      setState({ status: "no-draft", submittedCount: result.submittedVisits.length });
    }
  }, [requestId]);

  const startCapture = useCallback(async () => {
    if (state.status === "draft") {
      setIsModalOpen(true);
      return;
    }
    if (state.status !== "no-draft") return;
    try {
      const created = await api.createActualWork({ requestId });
      setState({
        status: "draft",
        draft: {
          id: created.id,
          status: created.status,
          outcome: null,
          completionNote: null,
          submittedAtUtc: null,
          concurrencyVersion: created.concurrencyVersion,
          lines: [],
        },
        submittedCount: state.submittedCount,
      });
      setIsModalOpen(true);
    } catch (err) {
      // Another session/tab may have opened this request's one Draft between the probe and this
      // create call (ActualWork.DraftAlreadyOpenForRequest, 409) — reconcile onto the now-
      // authoritative Draft and open it, rather than reporting a fabricated "unable to start"
      // error for what is actually a resumable Draft.
      if (err instanceof ApiError && err.status === 409) {
        try {
          await refetchDraft();
          setConflictNotice(ACTUAL_WORK_CONFLICT_NOTICE);
          setIsModalOpen(true);
        } catch {
          setState({ status: "error", message: "Unable to start a visit." });
        }
        return;
      }
      setState({ status: "error", message: "Unable to start a visit." });
    }
  }, [state, requestId, refetchDraft]);

  // Session-scoped: set when a submit succeeds while the composer is showing its own submitted
  // confirmation (see markSubmitted below). closeModal only reprobes card state (draft ->
  // no-draft/submittedCount) once the user actually dismisses that confirmation — reprobing
  // immediately on submit would flip hook state away from "draft" and unmount the composer out
  // from under the confirmation it is supposed to show.
  const submittedPendingRef = useRef(false);

  const closeModal = useCallback(() => {
    setIsModalOpen(false);
    if (submittedPendingRef.current) {
      submittedPendingRef.current = false;
      void probe();
    }
  }, [probe]);

  // The one shared 409/ambiguous-failure path every composer mutation calls instead of
  // duplicating notice-plus-refetch handling per surface (mirrors ProposedScope's
  // reconcileAfterConflict). It only reloads the authoritative draft and surfaces a notice — it
  // never retries the mutation. A failed reload leaves current state untouched and remembers the
  // intended notice so retryReconciliation can re-attempt the same reload.
  const reconcileAfterConflict = useCallback(
    async (message: string = ACTUAL_WORK_CONFLICT_NOTICE) => {
      try {
        await refetchDraft();
        setConflictNotice(message);
        setPendingReconcileMessage(null);
      } catch {
        setConflictNotice(ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE);
        setPendingReconcileMessage(message);
      }
    },
    [refetchDraft],
  );

  const retryReconciliation = useCallback(
    () => reconcileAfterConflict(pendingReconcileMessage ?? undefined),
    [reconcileAfterConflict, pendingReconcileMessage],
  );

  const clearConflictNotice = useCallback(() => setConflictNotice(null), []);

  // After a successful submit the draft is gone (Draft -> Submitted), but the composer keeps
  // showing its own submitted confirmation until the user closes it (mirrors
  // ProposedScopeComposer). Marks the pending reprobe for closeModal to run instead of running it
  // here, which would unmount the composer mid-confirmation (RequestDetailContent only mounts it
  // while hook state is "draft").
  const markSubmitted = useCallback(() => {
    submittedPendingRef.current = true;
  }, []);

  // After a successful discard the draft is gone with nothing left to show — close the composer
  // and reprobe in one step.
  const onDraftDiscarded = useCallback(() => {
    setIsModalOpen(false);
    void probe();
  }, [probe]);

  return {
    state,
    isModalOpen,
    startCapture,
    closeModal,
    refetchDraft,
    conflictNotice,
    reconcileAfterConflict,
    retryReconciliation,
    clearConflictNotice,
    markSubmitted,
    onDraftDiscarded,
  };
}
