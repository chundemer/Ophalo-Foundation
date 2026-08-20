import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import {
  useActualWorkCapture,
  ACTUAL_WORK_CONFLICT_NOTICE,
  ACTUAL_WORK_RECONCILE_RELOAD_FAILURE_NOTICE,
} from "../useActualWorkCapture";
import { ApiError } from "../../../lib/apiClient";
import type { ActualWorkHistoryResult } from "../../../lib/apiClient";

const mockGetActualWorkHistoryForRequest = vi.fn();
const mockCreateActualWork = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getActualWorkHistoryForRequest: (...args: unknown[]) => mockGetActualWorkHistoryForRequest(...args),
      createActualWork: (...args: unknown[]) => mockCreateActualWork(...args),
    },
  };
});

function history(overrides: Partial<ActualWorkHistoryResult> = {}): ActualWorkHistoryResult {
  return {
    canCaptureActualWork: true,
    openDraft: null,
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
        lines: [],
      },
      submittedCount: 0,
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
});
