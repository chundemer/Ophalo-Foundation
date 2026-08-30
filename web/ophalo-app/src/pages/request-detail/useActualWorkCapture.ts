import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, type ActualWorkHistoryResult } from "../../lib/apiClient";

/**
 * ADR-494 D2 (4c-i): the UI-only entry intent chosen on the card before a Draft is created. Not a
 * persisted `EntrySource` — only the interaction branch:
 * - `record-mine` — the caller is the technician; the Draft is created with the caller as its
 *   explicit ticket-default performer, shown preset in the composer.
 * - `transcribe` — an office user recording a paper ticket; the Draft is created with no default,
 *   and the composer's entire add region stays disabled until a performer is selected and
 *   persisted via `setActualWorkDefaultPerformer` (survives reload).
 */
export type ActualWorkEntryIntent = "record-mine" | "transcribe";

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

/** Outcome of a recorder-initiated "hand off to office" attempt (Slice 4d), returned to the
 * composer so it can keep the picker open on a recoverable failure or close on success. */
export type ActualWorkHandoffOutcome = "handed-off" | "ineligible" | "stale" | "failed";

export const ACTUAL_WORK_HANDOFF_STALE_NOTICE =
  "This visit changed elsewhere — refreshed with the latest state.";

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

export function useActualWorkCapture(requestId: string, currentAccountUserId?: string) {
  const [state, setState] = useState<ActualWorkCaptureState>({ status: "loading" });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [conflictNotice, setConflictNotice] = useState<string | null>(null);
  const [pendingReconcileMessage, setPendingReconcileMessage] = useState<string | null>(null);
  const [recoveryNotice, setRecoveryNotice] = useState<ActualWorkRecoveryNotice | null>(null);
  // BL136 4e-iii: session-scoped flag — set only after a confirmed replacement-copy correction that
  // this session auto-opened, so the composer can show "this replaces a superseded visit" guidance.
  // UI-only: the durable lineage lives on the history record, so a hard reload simply loses the
  // banner (the Draft still opens normally). Cleared when the composer closes or submits.
  const [replacementCorrection, setReplacementCorrection] = useState(false);

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

  const startCapture = useCallback(async (intent: ActualWorkEntryIntent = "transcribe") => {
    if (state.status === "draft") {
      setIsModalOpen(true);
      return;
    }
    if (state.status !== "no-draft") return;
    // "Record my work" seeds the Draft with the caller as its ticket-default performer; "Transcribe
    // work" (and the legacy no-arg call) creates with no default and the composer gates its add
    // region until one is persisted.
    const presetPerformerId =
      intent === "record-mine" && currentAccountUserId ? currentAccountUserId : null;
    try {
      const created = await api.createActualWork(
        presetPerformerId
          ? { requestId, defaultPerformedByAccountUserId: presetPerformerId }
          : { requestId },
      );
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
          defaultPerformedByAccountUserId: presetPerformerId,
          defaultPerformerDisplayName: null,
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
  }, [state, requestId, currentAccountUserId, refetchDraft]);

  // BL136 4f-i: the wide-screen capture entry point navigates to the dedicated workspace route
  // instead of opening the in-page modal, so it needs the Draft created (or confirmed already
  // open) *without* the `setIsModalOpen(true)` side effect `startCapture` carries. Mirrors
  // `startCapture`'s create + 409 reconcile, minus the modal. The workspace route then mounts its
  // own capture hook, which re-probes and lands on the Draft.
  const createDraft = useCallback(
    async (
      intent: ActualWorkEntryIntent = "transcribe",
    ): Promise<"created" | "exists" | "held-by-other" | "failed"> => {
      if (state.status === "draft") return "exists";
      if (state.status !== "no-draft") return "failed";
      const presetPerformerId =
        intent === "record-mine" && currentAccountUserId ? currentAccountUserId : null;
      try {
        const created = await api.createActualWork(
          presetPerformerId
            ? { requestId, defaultPerformedByAccountUserId: presetPerformerId }
            : { requestId },
        );
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
            defaultPerformedByAccountUserId: presetPerformerId,
            defaultPerformerDisplayName: null,
            lines: [],
          },
          submittedCount: state.submittedCount,
        });
        return "created";
      } catch (err) {
        if (err instanceof ApiError && err.status === 409) {
          try {
            const next = await refetchDraft();
            if (next.status === "draft") return "exists";
            if (next.status === "held-by-other") return "held-by-other";
            return "failed";
          } catch {
            setState({ status: "error", message: "Unable to start a visit." });
            return "failed";
          }
        }
        setState({ status: "error", message: "Unable to start a visit." });
        return "failed";
      }
    },
    [state, requestId, currentAccountUserId, refetchDraft],
  );

  // Session-scoped: set when a submit succeeds while the composer is showing its own submitted
  // confirmation (see markSubmitted below). closeModal only reprobes card state (draft ->
  // no-draft/submittedCount) once the user actually dismisses that confirmation — reprobing
  // immediately on submit would flip hook state away from "draft" and unmount the composer out
  // from under the confirmation it is supposed to show.
  const submittedPendingRef = useRef(false);

  const closeModal = useCallback(() => {
    setIsModalOpen(false);
    setReplacementCorrection(false);
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

  // ADR-494 D2 (4c-i): the office-transcription path persists the selected technician as the Draft's
  // ticket default. Recorder-only, Draft-only, existing version protocol. On success the rotated
  // version + resolved performer name are pulled back through `refetchDraft` so the composer un-gates
  // its add region from the authoritative projection (and stays un-gated across a reload). A stale
  // version / non-Draft collapses onto the reconcile path; a 422 (`PerformerIneligible`) is returned
  // so the selector can surface it without disturbing state.
  const setDefaultPerformer = useCallback(
    async (performerId: string | null): Promise<"set" | "ineligible" | "stale" | "failed"> => {
      if (state.status !== "draft") return "stale";
      try {
        await api.setActualWorkDefaultPerformer(
          state.draft.id,
          performerId,
          state.draft.concurrencyVersion,
        );
        await refetchDraft();
        return "set";
      } catch (err) {
        if (err instanceof ApiError && err.code === "ActualWork.PerformerIneligible") {
          return "ineligible";
        }
        if (
          err instanceof ApiError &&
          (err.code === "ActualWork.VersionMismatch" || err.code === "ActualWork.NotDraft")
        ) {
          await reconcileAfterConflict();
          return "stale";
        }
        return "failed";
      }
    },
    [state, refetchDraft, reconcileAfterConflict],
  );

  // ADR-494 D5 (4c-ii): recorder-only, Draft-only visit note. Mirrors `setDefaultPerformer` — the
  // composer autosaves on blur, so on success the rotated version + trimmed value are pulled back
  // through `refetchDraft` (the textarea reflects the server's trim and survives a reload). A stale
  // version / non-Draft collapses onto the shared reconcile path; a >2000 rejection (400) is
  // returned as `"too-long"` so the textarea can surface it without disturbing state.
  const setVisitNote = useCallback(
    async (visitNote: string | null): Promise<"set" | "too-long" | "stale" | "failed"> => {
      if (state.status !== "draft") return "stale";
      try {
        await api.setActualWorkVisitNote(
          state.draft.id,
          visitNote,
          state.draft.concurrencyVersion,
        );
        await refetchDraft();
        return "set";
      } catch (err) {
        if (err instanceof ApiError && err.code === "ActualWork.VisitNoteTooLong") {
          return "too-long";
        }
        if (
          err instanceof ApiError &&
          (err.code === "ActualWork.VersionMismatch" || err.code === "ActualWork.NotDraft")
        ) {
          await reconcileAfterConflict();
          return "stale";
        }
        return "failed";
      }
    },
    [state, refetchDraft, reconcileAfterConflict],
  );

  // BL136 §4e-iii: recorder-only, Draft-only zero-line disposition (outcome + completion note).
  // Mirrors `setVisitNote` — the composer footer autosaves on blur once a valid outcome exists, so
  // on success the rotated version + server-trimmed values are pulled back through `refetchDraft`
  // (the fields survive a reload). A stale version / non-Draft collapses onto the shared reconcile
  // path; an enum-invalid outcome (400) is returned as `"invalid"` without disturbing state.
  const setZeroLineDisposition = useCallback(
    async (
      outcome: string,
      completionNote: string | null,
    ): Promise<"set" | "invalid" | "stale" | "failed"> => {
      if (state.status !== "draft") return "stale";
      try {
        await api.setActualWorkZeroLineDisposition(
          state.draft.id,
          outcome,
          completionNote,
          state.draft.concurrencyVersion,
        );
        await refetchDraft();
        return "set";
      } catch (err) {
        if (err instanceof ApiError && err.code === "ActualWork.InvalidOutcome") {
          return "invalid";
        }
        if (
          err instanceof ApiError &&
          (err.code === "ActualWork.VersionMismatch" || err.code === "ActualWork.NotDraft")
        ) {
          await reconcileAfterConflict();
          return "stale";
        }
        return "failed";
      }
    },
    [state, refetchDraft, reconcileAfterConflict],
  );

  // After a successful submit the draft is gone (Draft -> Submitted), but the composer keeps
  // showing its own submitted confirmation until the user closes it (mirrors
  // ProposedScopeComposer). Marks the pending reprobe for closeModal to run instead of running it
  // here, which would unmount the composer mid-confirmation (RequestDetailContent only mounts it
  // while hook state is "draft").
  const markSubmitted = useCallback(() => {
    submittedPendingRef.current = true;
    // The composer stays mounted for its submitted confirmation, but the visit is no longer a
    // replacement Draft — clear the banner now (lifecycle: clear on close, submit, or discard).
    setReplacementCorrection(false);
  }, []);

  // After a successful discard the draft is gone with nothing left to show — close the composer
  // and reprobe in one step.
  const onDraftDiscarded = useCallback(() => {
    setIsModalOpen(false);
    // Symmetric with closeModal: discarding is a composer exit, so the session-scoped replacement
    // banner must not survive it onto a later same-session Draft opened via startCapture.
    setReplacementCorrection(false);
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

  // Slice 4d: the current recorder hands their own unsubmitted Draft to a chosen office member.
  // Same `transfer-recorder` endpoint as the Owner/Admin recovery path, with the reason omitted —
  // the server records a fixed system reason. On success the composer closes and the re-probe
  // lands the (now former) recorder on `held-by-other`; a transient banner shows over it. A
  // recoverable failure (`ineligible` / `failed`) keeps the composer's picker open for a retry.
  const handOffToOffice = useCallback(
    async (newRecorderAccountUserId: string): Promise<ActualWorkHandoffOutcome> => {
      if (state.status !== "draft") return "stale";
      try {
        await api.transferActualWorkDraftRecorder(
          state.draft.id,
          { newRecorderAccountUserId },
          state.draft.concurrencyVersion,
        );
        setRecoveryNotice({ tone: "success", text: "Visit handed off to the office." });
        setIsModalOpen(false);
        await probe();
        return "handed-off";
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
          setRecoveryNotice({ tone: "warning", text: ACTUAL_WORK_HANDOFF_STALE_NOTICE });
          setIsModalOpen(false);
          await probe();
          return "stale";
        }
        return "failed";
      }
    },
    [state, probe],
  );

  const clearRecoveryNotice = useCallback(() => setRecoveryNotice(null), []);

  // BL136 4e-iii: after an Owner/Admin replacement-copy correction the successor Draft already
  // exists server-side. Reload the authoritative history and open the composer on it only when the
  // read confirms (a) the caller may capture Actual Work — Owner/Admin history reads expose their
  // own replacement Draft even without `ActualWorkCapture`, but every Draft mutation requires it,
  // so auto-opening a non-capturer would only yield 403s — and (b) the open Draft is exactly the
  // successor that was just created (guards a race where another session opens a different Draft
  // first). Any other outcome returns false so the caller shows an explicit recovery affordance.
  const openReplacementDraft = useCallback(async (successorId: string): Promise<boolean> => {
    try {
      const result = await api.getActualWorkHistoryForRequest(requestId);
      if (!result.canCaptureActualWork) {
        setState({ status: "hidden" });
        return false;
      }
      const next = routeHistory(result);
      setState(next);
      if (next.status !== "draft" || next.draft.id !== successorId) return false;
      setReplacementCorrection(true);
      setIsModalOpen(true);
      return true;
    } catch {
      return false;
    }
  }, [requestId]);

  return {
    state,
    isModalOpen,
    startCapture,
    createDraft,
    closeModal,
    refetchDraft,
    setDefaultPerformer,
    setVisitNote,
    setZeroLineDisposition,
    conflictNotice,
    reconcileAfterConflict,
    retryReconciliation,
    clearConflictNotice,
    markSubmitted,
    onDraftDiscarded,
    transferRecorder,
    handOffToOffice,
    recoveryNotice,
    clearRecoveryNotice,
    openReplacementDraft,
    replacementCorrection,
  };
}
