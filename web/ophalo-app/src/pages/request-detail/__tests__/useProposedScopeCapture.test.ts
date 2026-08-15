import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { useProposedScopeCapture } from "../useProposedScopeCapture";
import { ApiError } from "../../../lib/apiClient";
import type { ProposedScopeDetailResult } from "../../../lib/apiClient";

const mockGetCurrentProposedScopeForRequest = vi.fn();
const mockGetProposedScope = vi.fn();
const mockCreateProposedScope = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCurrentProposedScopeForRequest: (...args: unknown[]) => mockGetCurrentProposedScopeForRequest(...args),
      getProposedScope: (...args: unknown[]) => mockGetProposedScope(...args),
      createProposedScope: (...args: unknown[]) => mockCreateProposedScope(...args),
    },
  };
});

function draftScope(overrides: Partial<ProposedScopeDetailResult> = {}): ProposedScopeDetailResult {
  return {
    id: "scope-1",
    requestId: "request-1",
    status: "Draft",
    concurrencyVersion: "v1",
    lines: [],
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useProposedScopeCapture", () => {
  it("hides the entry point on a 403 probe response", async () => {
    mockGetCurrentProposedScopeForRequest.mockRejectedValueOnce(new ApiError(403, "Forbidden", "forbidden"));
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));

    await waitFor(() => expect(result.current.state.status).toBe("hidden"));
  });

  it("reports no-scope when the probe returns null", async () => {
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "NoScopeYet", scope: null });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));

    await waitFor(() => expect(result.current.state.status).toBe("no-scope"));
  });

  it("reports draft with the existing scope when one is in progress", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "draft", scope }));
  });

  it("reports submitted for SubmittedToOffice/OfficeReviewed scopes", async () => {
    const scope = draftScope({ status: "SubmittedToOffice" });
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "SubmittedToOffice", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));

    await waitFor(() => expect(result.current.state).toEqual({ status: "submitted", scope }));
  });

  it("startCapture resumes an existing draft by opening the modal without creating one", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    act(() => {
      void result.current.startCapture();
    });

    expect(result.current.isModalOpen).toBe(true);
    expect(mockCreateProposedScope).not.toHaveBeenCalled();
  });

  it("startCapture creates a new draft when none exists, then opens the modal", async () => {
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "NoScopeYet", scope: null });
    const created = { id: "scope-2", requestId: "request-1", status: "Draft", concurrencyVersion: "v1" };
    mockCreateProposedScope.mockResolvedValueOnce(created);
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-scope"));

    await act(async () => {
      await result.current.startCapture();
    });

    expect(mockCreateProposedScope).toHaveBeenCalledWith({ requestId: "request-1" });
    expect(result.current.isModalOpen).toBe(true);
    expect(result.current.state).toEqual({
      status: "draft",
      scope: { id: "scope-2", requestId: "request-1", status: "Draft", concurrencyVersion: "v1", lines: [] },
    });
  });

  it("refetchScope replaces state with the authoritative snapshot from the server", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    const refreshed = draftScope({
      concurrencyVersion: "v2",
      lines: [
        {
          id: "line-1",
          lineType: "OffCatalogItem",
          catalogItemId: null,
          offeringAssemblyId: null,
          quantity: 1,
          isException: false,
          offCatalogDescription: "Custom part",
          offCatalogQuantity: 1,
          note: null,
          displayOrder: 10,
          displayNameSnapshot: "Custom part",
          unitOfMeasureSnapshot: null,
          offeringAssemblyNameSnapshot: null,
          defaultQuantitySnapshot: null,
        },
      ],
    });
    mockGetProposedScope.mockResolvedValueOnce(refreshed);

    await act(async () => {
      await result.current.refetchScope();
    });

    expect(mockGetProposedScope).toHaveBeenCalledWith("scope-1");
    expect(result.current.state).toEqual({ status: "draft", scope: refreshed });
  });

  it("startView opens the modal read-only for a submitted scope without creating anything", async () => {
    const scope = draftScope({ status: "SubmittedToOffice" });
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "SubmittedToOffice", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("submitted"));

    act(() => {
      result.current.startView();
    });

    expect(result.current.isModalOpen).toBe(true);
    expect(mockCreateProposedScope).not.toHaveBeenCalled();
  });

  it("startView is a no-op outside the submitted state", async () => {
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "NoScopeYet", scope: null });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("no-scope"));

    act(() => {
      result.current.startView();
    });

    expect(result.current.isModalOpen).toBe(false);
  });
});
