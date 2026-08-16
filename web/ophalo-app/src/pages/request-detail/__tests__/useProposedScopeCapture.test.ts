import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { useProposedScopeCapture, PROPOSED_SCOPE_CONFLICT_NOTICE } from "../useProposedScopeCapture";
import { api, ApiError } from "../../../lib/apiClient";
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

  it("reconcileAfterConflict sets the shared notice and reloads the authoritative scope without retrying", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    const refreshed = draftScope({ concurrencyVersion: "v2" });
    mockGetProposedScope.mockResolvedValueOnce(refreshed);

    await act(async () => {
      await result.current.reconcileAfterConflict();
    });

    expect(mockGetProposedScope).toHaveBeenCalledTimes(1);
    expect(mockGetProposedScope).toHaveBeenCalledWith("scope-1");
    expect(result.current.conflictNotice).toBe(PROPOSED_SCOPE_CONFLICT_NOTICE);
    expect(result.current.state).toEqual({ status: "draft", scope: refreshed });
  });

  it("clearConflictNotice clears a previously surfaced notice", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockResolvedValueOnce(scope);
    await act(async () => {
      await result.current.reconcileAfterConflict("Custom notice");
    });
    expect(result.current.conflictNotice).toBe("Custom notice");

    act(() => result.current.clearConflictNotice());
    expect(result.current.conflictNotice).toBeNull();
  });
});

describe("apiClient proposed-scope contract (Session 5A, build-log/120)", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("getFieldQuickScopeActions returns a price-blind action shape", async () => {
    const body = {
      actions: [
        {
          id: "action-1",
          order: 1,
          catalogItemId: "item-1",
          offeringAssemblyId: null,
          targetDisplayName: "Filter",
        },
      ],
    };
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => body });
    globalThis.fetch = fetchMock as unknown as typeof fetch;

    const result = await api.getFieldQuickScopeActions();

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/keep/pricebook/field/quick-scope-actions"),
      expect.any(Object),
    );
    expect(Object.keys(result.actions[0]!).sort()).toEqual(
      ["catalogItemId", "id", "offeringAssemblyId", "order", "targetDisplayName"].sort(),
    );
  });

  it("restoreProposedScopeLine posts the version header to the versioned restore route", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ concurrencyVersion: "v3" }) });
    globalThis.fetch = fetchMock as unknown as typeof fetch;

    const result = await api.restoreProposedScopeLine("scope-1", "line-1", "v2");

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/keep/pricebook/proposed-scopes/scope-1/lines/line-1/restore"),
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({ "X-Keep-ProposedScope-Version": "v2" }),
      }),
    );
    expect(result).toEqual({ concurrencyVersion: "v3" });
  });
});
