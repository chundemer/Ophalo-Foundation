import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import {
  useActualWorkCapture,
  ACTUAL_WORK_CONFLICT_NOTICE,
  ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE,
  ACTUAL_WORK_TRANSFER_STALE_NOTICE,
} from "../useActualWorkCapture";
import { ApiError } from "../../../lib/apiClient";
import type { ActualWorkHistoryResult } from "../../../lib/apiClient";

const mockGetActualWorkHistoryForRequest = vi.fn();
const mockCreateActualWork = vi.fn();
const mockTransferActualWorkDraftRecorder = vi.fn();
const mockSetActualWorkDefaultPerformer = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getActualWorkHistoryForRequest: (...args: unknown[]) => mockGetActualWorkHistoryForRequest(...args),
      createActualWork: (...args: unknown[]) => mockCreateActualWork(...args),
      transferActualWorkDraftRecorder: (...args: unknown[]) => mockTransferActualWorkDraftRecorder(...args),
      setActualWorkDefaultPerformer: (...args: unknown[]) => mockSetActualWorkDefaultPerformer(...args),
    },
  };
});

const DRAFT_NO_DEFAULT = {
  id: "draft-1",
  status: "Draft",
  outcome: null,
  completionNote: null,
  submittedAtUtc: null,
  concurrencyVersion: "v1",
  isRecorder: true,
  defaultPerformedByAccountUserId: null,
  defaultPerformerDisplayName: null,
  lines: [],
};

function history(overrides: Partial<ActualWorkHistoryResult> = {}): ActualWorkHistoryResult {
  return {
    canCaptureActualWork: true,
    openDraft: null,
    openDraftHeldByOther: false,
    submittedVisits: [],
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useActualWorkCapture", () => {
  it("hides the entry point when canCaptureActualWork is false (visible non-Responsible watcher)", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ canCaptureActualWork: false }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));

    await waitFor(() => expect(result.current.state.status).toBe("hidden"));
  });

  it("hides the entry point on a 403 probe response", async () => {
    mockGetActualWorkHistoryForRequest.mockRejectedValueOnce(new ApiError(403, "Forbidden", "forbidden"));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));

    await waitFor(() => expect(result.current.state.status).toBe("hidden"));
  });

  it("reports no-draft with the submitted count when the Responsible has no open Draft", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(
      history({ submittedVisits: [{ id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01", lines: [] }] }),
    );
    const { result } = renderHook(() => useActualWorkCapture("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "no-draft", submittedCount: 1 }));
  });

  it("reports draft with the existing open Draft", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "draft", draft: openDraft, submittedCount: 0 }));
  });

  it("startCapture resumes an existing draft by opening the modal without creating one", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    act(() => {
      void result.current.startCapture();
    });

    expect(result.current.isModalOpen).toBe(true);
    expect(mockCreateActualWork).not.toHaveBeenCalled();
  });

  it("startCapture creates a new Draft when none exists, then opens the modal", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    const created = { id: "draft-2", requestId: "request-1", status: "Draft", concurrencyVersion: "v1" };
    mockCreateActualWork.mockResolvedValueOnce(created);
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-draft"));

    await act(async () => {
      await result.current.startCapture();
    });

    expect(mockCreateActualWork).toHaveBeenCalledWith({ requestId: "request-1" });
    expect(result.current.isModalOpen).toBe(true);
    expect(result.current.state).toEqual({
      status: "draft",
      draft: {
        id: "draft-2",
        status: "Draft",
        outcome: null,
        completionNote: null,
        submittedAtUtc: null,
        concurrencyVersion: "v1",
        isRecorder: true,
        defaultPerformedByAccountUserId: null,
        defaultPerformerDisplayName: null,
        lines: [],
      },
      submittedCount: 0,
    });
  });

  it("startCapture('record-mine') creates the Draft with the caller as its ticket-default performer", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    mockCreateActualWork.mockResolvedValueOnce({
      id: "draft-3",
      requestId: "request-1",
      status: "Draft",
      concurrencyVersion: "v1",
    });
    const { result } = renderHook(() => useActualWorkCapture("request-1", "me-au-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-draft"));

    await act(async () => {
      await result.current.startCapture("record-mine");
    });

    expect(mockCreateActualWork).toHaveBeenCalledWith({
      requestId: "request-1",
      defaultPerformedByAccountUserId: "me-au-1",
    });
    expect(result.current.state).toMatchObject({
      status: "draft",
      draft: { defaultPerformedByAccountUserId: "me-au-1" },
    });
  });

  it("startCapture('transcribe') creates the Draft with no default performer", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    mockCreateActualWork.mockResolvedValueOnce({
      id: "draft-4",
      requestId: "request-1",
      status: "Draft",
      concurrencyVersion: "v1",
    });
    const { result } = renderHook(() => useActualWorkCapture("request-1", "me-au-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-draft"));

    await act(async () => {
      await result.current.startCapture("transcribe");
    });

    expect(mockCreateActualWork).toHaveBeenCalledWith({ requestId: "request-1" });
    expect(result.current.state).toMatchObject({
      status: "draft",
      draft: { defaultPerformedByAccountUserId: null },
    });
  });

  it("reconcileAfterConflict reloads the draft and surfaces the shared conflict notice", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    const refreshedDraft = { ...openDraft, concurrencyVersion: "v2" };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft: refreshedDraft }));

    await act(async () => {
      await result.current.reconcileAfterConflict();
    });

    expect(result.current.conflictNotice).toBe(ACTUAL_WORK_CONFLICT_NOTICE);
    expect(result.current.state).toEqual({ status: "draft", draft: refreshedDraft, submittedCount: 0 });
  });

  it("reconcileAfterConflict surfaces the reload-failure notice and leaves state untouched when the reload itself fails", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetActualWorkHistoryForRequest.mockRejectedValueOnce(new Error("network down"));

    await act(async () => {
      await result.current.reconcileAfterConflict();
    });

    expect(result.current.conflictNotice).toBe(ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE);
    expect(result.current.state).toEqual({ status: "draft", draft: openDraft, submittedCount: 0 });
  });

  it("onDraftDiscarded closes the modal and reprobes to no-draft", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));
    act(() => void result.current.startCapture());
    expect(result.current.isModalOpen).toBe(true);

    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());

    act(() => {
      result.current.onDraftDiscarded();
    });

    expect(result.current.isModalOpen).toBe(false);
    await waitFor(() => expect(result.current.state).toEqual({ status: "no-draft", submittedCount: 0 }));
  });

  it("markSubmitted does not reprobe while the composer's submitted confirmation is still showing", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));
    act(() => void result.current.startCapture());

    act(() => {
      result.current.markSubmitted();
    });

    // No reprobe fired yet — state (and therefore the composer's mount condition in
    // RequestDetailContent) must stay "draft" so the submitted confirmation stays visible.
    expect(mockGetActualWorkHistoryForRequest).toHaveBeenCalledTimes(1);
    expect(result.current.state.status).toBe("draft");
    expect(result.current.isModalOpen).toBe(true);
  });

  it("closeModal reprobes to no-draft with the incremented submitted count once the user dismisses a submitted confirmation", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));
    act(() => void result.current.startCapture());
    act(() => result.current.markSubmitted());

    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(
      history({ submittedVisits: [{ id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01", lines: [] }] }),
    );

    act(() => {
      result.current.closeModal();
    });

    expect(result.current.isModalOpen).toBe(false);
    await waitFor(() => expect(result.current.state).toEqual({ status: "no-draft", submittedCount: 1 }));
  });

  it("closeModal without a pending submission does not reprobe", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));
    act(() => void result.current.startCapture());

    act(() => {
      result.current.closeModal();
    });

    expect(result.current.isModalOpen).toBe(false);
    expect(mockGetActualWorkHistoryForRequest).toHaveBeenCalledTimes(1);
    expect(result.current.state).toEqual({ status: "draft", draft: openDraft, submittedCount: 0 });
  });

  it("startCapture reconciles a create-time 409 onto the now-authoritative Draft and opens it with a conflict notice", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    mockCreateActualWork.mockRejectedValueOnce(new ApiError(409, "ActualWork.DraftAlreadyOpenForRequest", "conflict"));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-draft"));

    const raceDraft = {
      id: "draft-race",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v9",
      isRecorder: true,
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft: raceDraft }));

    await act(async () => {
      await result.current.startCapture();
    });

    expect(mockCreateActualWork).toHaveBeenCalledWith({ requestId: "request-1" });
    expect(result.current.state).toEqual({ status: "draft", draft: raceDraft, submittedCount: 0 });
    expect(result.current.isModalOpen).toBe(true);
    expect(result.current.conflictNotice).toBe(ACTUAL_WORK_CONFLICT_NOTICE);
  });

  it("startCapture surfaces an error when a create-time 409 cannot be reconciled", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    mockCreateActualWork.mockRejectedValueOnce(new ApiError(409, "ActualWork.DraftAlreadyOpenForRequest", "conflict"));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-draft"));

    mockGetActualWorkHistoryForRequest.mockRejectedValueOnce(new Error("network down"));

    await act(async () => {
      await result.current.startCapture();
    });

    expect(result.current.state).toEqual({ status: "error", message: "Unable to start a visit." });
    expect(result.current.isModalOpen).toBe(false);
  });

  it("reports held-by-other from the presence-only signal (qualified non-recorder)", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(
      history({ openDraftHeldByOther: true, submittedVisits: [{ id: "v1", status: "SubmittedToOffice", outcome: null, completionNote: null, submittedAtUtc: "2026-01-01", lines: [] }] }),
    );
    const { result } = renderHook(() => useActualWorkCapture("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "held-by-other", submittedCount: 1 }));
  });

  it("routes an Owner/Admin non-editable openDraft (isRecorder false) to owner-recovery, retaining the draft", async () => {
    const openDraft = {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v1",
      isRecorder: false,
      recorderAccountUserId: "au-current",
      recorderDisplayName: "Sam Field",
      lines: [],
    };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft }));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));

    await waitFor(() =>
      expect(result.current.state).toEqual({ status: "owner-recovery", draft: openDraft, submittedCount: 0 }),
    );
  });

  it("startCapture 409 lands on held-by-other with no modal and no conflict notice when someone else holds the Draft", async () => {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    mockCreateActualWork.mockRejectedValueOnce(new ApiError(409, "ActualWork.DraftAlreadyOpenForRequest", "conflict"));
    const { result } = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-draft"));

    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraftHeldByOther: true }));

    await act(async () => {
      await result.current.startCapture();
    });

    expect(result.current.state).toEqual({ status: "held-by-other", submittedCount: 0 });
    expect(result.current.isModalOpen).toBe(false);
    expect(result.current.conflictNotice).toBeNull();
  });
});

describe("useActualWorkCapture — recorder transfer (1a-ii-b)", () => {
  function ownerRecoveryDraft() {
    return {
      id: "draft-1",
      status: "Draft",
      outcome: null,
      completionNote: null,
      submittedAtUtc: null,
      concurrencyVersion: "v3",
      isRecorder: false,
      recorderAccountUserId: "au-current",
      recorderDisplayName: "Sam Field",
      lines: [],
    };
  }

  async function renderInOwnerRecovery() {
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft: ownerRecoveryDraft() }));
    const hook = renderHook(() => useActualWorkCapture("request-1"));
    await waitFor(() => expect(hook.result.current.state.status).toBe("owner-recovery"));
    return hook;
  }

  it("submits the exact retained version, re-probes, and records a success notice on transfer", async () => {
    const { result } = await renderInOwnerRecovery();
    mockTransferActualWorkDraftRecorder.mockResolvedValueOnce({ concurrencyVersion: "v4" });
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraftHeldByOther: true }));

    let outcome: string | undefined;
    await act(async () => {
      outcome = await result.current.transferRecorder("au-next", "Jordan Lead", "Sam went home sick");
    });

    expect(outcome).toBe("transferred");
    expect(mockTransferActualWorkDraftRecorder).toHaveBeenCalledWith(
      "draft-1",
      { newRecorderAccountUserId: "au-next", reason: "Sam went home sick" },
      "v3",
    );
    expect(result.current.state.status).toBe("held-by-other");
    expect(result.current.recoveryNotice).toEqual({ tone: "success", text: "Recording handed to Jordan Lead." });
  });

  it("routes an Owner/Admin self-assignment back to the editable draft state", async () => {
    const { result } = await renderInOwnerRecovery();
    mockTransferActualWorkDraftRecorder.mockResolvedValueOnce({ concurrencyVersion: "v4" });
    const selfDraft = { ...ownerRecoveryDraft(), isRecorder: true, concurrencyVersion: "v4" };
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft: selfDraft }));

    await act(async () => {
      await result.current.transferRecorder("au-me", "Me", "Taking this over");
    });

    expect(result.current.state).toMatchObject({ status: "draft" });
    expect(result.current.recoveryNotice).toEqual({ tone: "success", text: "Recording handed to Me." });
  });

  it("returns 'ineligible' without changing state on a 422", async () => {
    const { result } = await renderInOwnerRecovery();
    mockTransferActualWorkDraftRecorder.mockRejectedValueOnce(
      new ApiError(422, "ActualWork.RecorderTransferTargetIneligible", "nope"),
    );

    let outcome: string | undefined;
    await act(async () => {
      outcome = await result.current.transferRecorder("au-next", "Jordan Lead", "reason");
    });

    expect(outcome).toBe("ineligible");
    expect(result.current.state.status).toBe("owner-recovery");
    expect(result.current.recoveryNotice).toBeNull();
  });

  it.each([
    ["ActualWork.VersionMismatch"],
    ["ActualWork.AlreadyReviewed"],
    ["ActualWork.NotDraft"],
  ])("returns 'stale' and re-probes with a warning notice on %s", async (code) => {
    const { result } = await renderInOwnerRecovery();
    mockTransferActualWorkDraftRecorder.mockRejectedValueOnce(new ApiError(409, code, "stale"));
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraftHeldByOther: true }));

    let outcome: string | undefined;
    await act(async () => {
      outcome = await result.current.transferRecorder("au-next", "Jordan Lead", "reason");
    });

    expect(outcome).toBe("stale");
    expect(mockGetActualWorkHistoryForRequest).toHaveBeenCalledTimes(2);
    expect(result.current.state.status).toBe("held-by-other");
    expect(result.current.recoveryNotice).toEqual({
      tone: "warning",
      text: ACTUAL_WORK_TRANSFER_STALE_NOTICE,
    });
  });

  it("returns 'failed' on an unclassified error without changing state", async () => {
    const { result } = await renderInOwnerRecovery();
    mockTransferActualWorkDraftRecorder.mockRejectedValueOnce(new Error("network down"));

    let outcome: string | undefined;
    await act(async () => {
      outcome = await result.current.transferRecorder("au-next", "Jordan Lead", "reason");
    });

    expect(outcome).toBe("failed");
    expect(result.current.state.status).toBe("owner-recovery");
  });

  describe("setDefaultPerformer (office-transcription path)", () => {
    async function renderInTranscribeDraft() {
      mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft: DRAFT_NO_DEFAULT }));
      const hook = renderHook(() => useActualWorkCapture("request-1", "me-au-1"));
      await waitFor(() => expect(hook.result.current.state.status).toBe("draft"));
      return hook;
    }

    it("persists the performer, then applies the refreshed projection (rotated version + name)", async () => {
      const { result } = await renderInTranscribeDraft();
      mockSetActualWorkDefaultPerformer.mockResolvedValueOnce({ concurrencyVersion: "v2" });
      mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(
        history({
          openDraft: {
            ...DRAFT_NO_DEFAULT,
            concurrencyVersion: "v2",
            defaultPerformedByAccountUserId: "tech-au-9",
            defaultPerformerDisplayName: "Sam Tech",
          },
        }),
      );

      let outcome: string | undefined;
      await act(async () => {
        outcome = await result.current.setDefaultPerformer("tech-au-9");
      });

      expect(outcome).toBe("set");
      expect(mockSetActualWorkDefaultPerformer).toHaveBeenCalledWith("draft-1", "tech-au-9", "v1");
      expect(result.current.state).toMatchObject({
        status: "draft",
        draft: {
          concurrencyVersion: "v2",
          defaultPerformedByAccountUserId: "tech-au-9",
          defaultPerformerDisplayName: "Sam Tech",
        },
      });
    });

    it("returns 'ineligible' on a 422 without disturbing state", async () => {
      const { result } = await renderInTranscribeDraft();
      mockSetActualWorkDefaultPerformer.mockRejectedValueOnce(
        new ApiError(422, "ActualWork.PerformerIneligible", "That team member can't be recorded as the performer."),
      );

      let outcome: string | undefined;
      await act(async () => {
        outcome = await result.current.setDefaultPerformer("tech-au-9");
      });

      expect(outcome).toBe("ineligible");
      expect(result.current.state).toMatchObject({ draft: { defaultPerformedByAccountUserId: null } });
    });

    it("returns 'stale' and reconciles on a version mismatch", async () => {
      const { result } = await renderInTranscribeDraft();
      mockSetActualWorkDefaultPerformer.mockRejectedValueOnce(
        new ApiError(409, "ActualWork.VersionMismatch", "changed by someone else"),
      );
      mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history({ openDraft: DRAFT_NO_DEFAULT }));

      let outcome: string | undefined;
      await act(async () => {
        outcome = await result.current.setDefaultPerformer("tech-au-9");
      });

      expect(outcome).toBe("stale");
      expect(result.current.conflictNotice).toBe(ACTUAL_WORK_CONFLICT_NOTICE);
    });
  });
});
