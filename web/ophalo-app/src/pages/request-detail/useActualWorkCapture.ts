import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, type ActualWorkHistoryResult } from "../../lib/apiClient";

/**
 * Batch 5b, build-log/129: mirrors useProposedScopeCapture's probe/draft/modal state machine.
 * The only read path is the visit-history endpoint (Batch 5a). `canCaptureActualWork` (GAP-055:
 * `RequestsOperate` + `ActualWorkCapture`, not active-Responsible participation) separates a
 * qualified caller from a visible-but-not-permitted one, so `hidden` covers both a 403 and a
 * Viewer.
 *
 * GAP-055 recorder ownership: only the Draft's current recorder may edit it. `openDraft` comes
 * back populated for the recorder (`isRecorder: true`) and, read-only, for Owner/Admin
 * (`isRecorder: false`); `openDraftHeldByOther` is the presence-only signal for a qualified
 * non-recorder who is not an Owner/Admin.
 *
 * A qualified non-Owner/Admin lands in `held-by-other`, a non-actionable informational state.
 * An Owner/Admin who is not the recorder lands in `owner-recovery` (1a-ii-b): the populated
 * (read-only) Draft is retained — version, lines, and current-recorder identity — so the
 * reason-required, immutable-audited recorder-transfer control can submit against the exact
 * version and exclude the current recorder from its candidate list.
 */
export type ActualWorkCaptureState =
  | { status: "loading" }
  | { status: "hidden" }
  | { status: "error"; message: string }
  | { status: "no-draft"; submittedCount: number }
  | { status: "held-by-other"; submittedCount: number }
  | { status: "owner-recovery"; draft: NonNullable<ActualWorkHistoryResult["openDraft"]>; submittedCount: number }
  | { status: "draft"; draft: NonNullable<ActualWorkHistoryResult["openDraft"]>; submittedCount: number };

/** A transient banner shown on the Actual Work card after a recorder transfer resolves — kept in
 * hook state (not the drawer) so it survives the drawer unmounting and displays over whichever
 * post-transfer card state the re-probe lands on (the new recorder's `draft`, or `held-by-other`
 * when the Owner/Admin handed it to someone else). */
export type ActualWorkRecoveryNotice = { tone: "success" | "warning"; text: string };

/** The outcome of a recorder-transfer attempt, returned to the drawer so it can react: close on a
 * settled outcome, or stay open and let the Owner/Admin retry on a recoverable one. */
export type ActualWorkTransferOutcome = "transferred" | "ineligible" | "stale" | "failed";

export const ACTUAL_WORK_TRANSFER_STALE_NOTICE =
  "This draft changed elsewhere — refreshed with the latest state. Review before reassigning again.";

/** Routes a history read into the resume/recovery/informational/empty states. `hidden` and `error`
 * are handled by the caller (they depend on how the read failed), not here. */
function routeHistory(result: ActualWorkHistoryResult): ActualWorkCaptureState {
  const submittedCount = result.submittedVisits.length;
  if (result.openDraft && result.openDraft.isRecorder) {
    return { status: "draft", draft: result.openDraft, submittedCount };
  }
  if (result.openDraft) {
    return { status: "owner-recovery", draft: result.openDraft, submittedCount };
  }
  if (result.openDraftHeldByOther) {
    return { status: "held-by-other", submittedCount };
  }
  return { status: "no-draft", submittedCount };
}

export const ACTUAL_WORK_CONFLICT_NOTICE =
  "This visit changed elsewhere — refreshed with the latest draft. Try again.";

export const ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE =
  "Unable to refresh this visit. Check your connection and try again.";

export function useActualWorkCapture(requestId: string) {
  const [state, setState] = useState<ActualWorkCaptureState>({ status: "loading" });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [conflictNotice, setConflictNotice] = useState<string | null>(null);
  const [pendingReconcileMessage, setPendingReconcileMessage] = useState<string | null>(null);
  const [recoveryNotice, setRecoveryNotice] = useState<ActualWorkRecoveryNotice | null>(null);

  const probe = useCallback(async () => {
    setState({ status: "loading" });
    try {
      const result = await api.getActualWorkHistoryForRequest(requestId);
      if (!result.canCaptureActualWork) {
        setState({ status: "hidden" });
        return;
      }
      setState(routeHistory(result));
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
  // (mutation responses carry only ids/versions, never the full line set). Returns the state it
  // applied so callers can react to a Draft that is gone or has been transferred away.
  const refetchDraft = useCallback(async () => {
    const result = await api.getActualWorkHistoryForRequest(requestId);
    const next = routeHistory(result);
    setState(next);
    return next;
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
          isRecorder: true,
          lines: [],
        },
        submittedCount: state.submittedCount,
      });
      setIsModalOpen(true);
    } catch (err) {
      // Another session/tab opened this request's one Draft between the probe and this create
      // call (ActualWork.DraftAlreadyOpenForRequest, 409). Reconcile onto the authoritative read:
      // if this caller turns out to be the recorder, resume it with a conflict notice; if someone
      // else holds it (GAP-055), land on the non-actionable informational card with no modal and
      // no generic conflict notice — the card itself is the recovery outcome.
      if (err instanceof ApiError && err.status === 409) {
        try {
          const next = await refetchDraft();
          if (next.status === "draft") {
            setConflictNotice(ACTUAL_WORK_CONFLICT_NOTICE);
            setIsModalOpen(true);
          }
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

  // 1a-ii-b: Owner/Admin recorder-transfer recovery. Submits against the exact retained Draft
  // version; on success re-probes (the caller may have handed it to someone else -> `held-by-other`,
  // or to themselves -> `draft`) and records a transient confirmation the card shows over either
  // state. `ineligible` (422) / `failed` keep the drawer open for a retry; `stale` (the Draft moved
  // or was already reviewed) re-probes and surfaces a warning so the Owner/Admin reorients.
  const transferRecorder = useCallback(
    async (
      newRecorderAccountUserId: string,
      newRecorderDisplayName: string,
      reason: string,
    ): Promise<ActualWorkTransferOutcome> => {
      if (state.status !== "owner-recovery") return "stale";
      try {
        await api.transferActualWorkDraftRecorder(
          state.draft.id,
          { newRecorderAccountUserId, reason },
          state.draft.concurrencyVersion,
        );
        setRecoveryNotice({ tone: "success", text: `Recording handed to ${newRecorderDisplayName}.` });
        await probe();
        return "transferred";
      } catch (err) {
        if (err instanceof ApiError && err.code === "ActualWork.RecorderTransferTargetIneligible") {
          return "ineligible";
        }
        if (
          err instanceof ApiError &&
          (err.code === "ActualWork.VersionMismatch" ||
            err.code === "ActualWork.AlreadyReviewed" ||
            err.code === "ActualWork.NotDraft")
        ) {
          setRecoveryNotice({ tone: "warning", text: ACTUAL_WORK_TRANSFER_STALE_NOTICE });
          await probe();
          return "stale";
        }
        return "failed";
      }
    },
    [state, probe],
  );

  const clearRecoveryNotice = useCallback(() => setRecoveryNotice(null), []);

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
    transferRecorder,
    recoveryNotice,
    clearRecoveryNotice,
  };
}
