import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useActualWorkHistory } from "../useActualWorkHistory";
import { ApiError } from "../../../lib/apiClient";
import type { ActualWorkHistoryResult } from "../../../lib/apiClient";

const mockGetActualWorkHistoryForRequest = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getActualWorkHistoryForRequest: (...args: unknown[]) => mockGetActualWorkHistoryForRequest(...args),
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

describe("useActualWorkHistory", () => {
  it("hides the card on a 403 probe response", async () => {
    mockGetActualWorkHistoryForRequest.mockRejectedValueOnce(new ApiError(403, "Forbidden", "forbidden"));
    const { result } = renderHook(() => useActualWorkHistory("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "hidden" }));
  });

  it("renders a compact error state on a non-403 failure", async () => {
    mockGetActualWorkHistoryForRequest.mockRejectedValueOnce(new Error("network down"));
    const { result } = renderHook(() => useActualWorkHistory("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "error" }));
  });

  it("loads submitted visits for a Viewer-style success response (canCaptureActualWork: false)", async () => {
    const submittedVisits = [
      { id: "v1", status: "SubmittedToOffice", outcome: "DiagnosticOnly", completionNote: "Checked unit.", submittedAtUtc: "2026-01-01T12:00:00Z", lines: [] },
    ];
    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(
      history({ canCaptureActualWork: false, submittedVisits }),
    );
    const { result } = renderHook(() => useActualWorkHistory("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "loaded", submittedVisits }));
  });

  it("retry re-probes after an error", async () => {
    mockGetActualWorkHistoryForRequest.mockRejectedValueOnce(new Error("network down"));
    const { result } = renderHook(() => useActualWorkHistory("request-1"));
    await waitFor(() => expect(result.current.state).toEqual({ status: "error" }));

    mockGetActualWorkHistoryForRequest.mockResolvedValueOnce(history());
    await result.current.retry();

    await waitFor(() => expect(result.current.state).toEqual({ status: "loaded", submittedVisits: [] }));
  });
});
