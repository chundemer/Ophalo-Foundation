import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import {
  useProposedScopeCapture,
  PROPOSED_SCOPE_CONFLICT_NOTICE,
  PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE,
} from "../useProposedScopeCapture";
import { api, ApiError } from "../../../lib/apiClient";
import type { ProposedScopeDetailResult } from "../../../lib/apiClient";

const mockGetCurrentProposedScopeForRequest = vi.fn();
const mockGetProposedScope = vi.fn();
const mockCreateProposedScope = vi.fn();
const mockGetScopeNudgeFieldSuggestions = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getCurrentProposedScopeForRequest: (...args: unknown[]) => mockGetCurrentProposedScopeForRequest(...args),
      getProposedScope: (...args: unknown[]) => mockGetProposedScope(...args),
      createProposedScope: (...args: unknown[]) => mockCreateProposedScope(...args),
      getScopeNudgeFieldSuggestions: (...args: unknown[]) => mockGetScopeNudgeFieldSuggestions(...args),
    },
  };
});

function emptyNudgeResult() {
  return { ruleId: null, triggerCatalogItemId: null, triggerOfferingAssemblyId: null, suggestions: [] };
}

function nudgeResult(ruleId: string, displayName = "Drain pan") {
  return {
    ruleId,
    triggerCatalogItemId: "item-1",
    triggerOfferingAssemblyId: null,
    suggestions: [{ id: "sugg-1", order: 0, catalogItemId: "item-9", offeringAssemblyId: null, displayName, targetKind: "CatalogItem" }],
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

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

  it("refetchScope without a trigger never requests a nudge read", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));
    mockGetProposedScope.mockResolvedValueOnce(scope);

    await act(async () => {
      await result.current.refetchScope();
    });

    expect(mockGetScopeNudgeFieldSuggestions).not.toHaveBeenCalled();
    expect(result.current.nudge).toBeNull();
  });

  it("a trigger-carrying refetchScope requests a nudge read after the reload and shows a non-empty result", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));
    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1"));

    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-1" });
    });

    expect(mockGetScopeNudgeFieldSuggestions).toHaveBeenCalledWith("scope-1", { triggerCatalogItemId: "item-1" });
    expect(result.current.nudge).toEqual({ ruleId: "rule-1", suggestions: nudgeResult("rule-1").suggestions });
  });

  it("an empty nudge-read result leaves an existing panel unchanged", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-1" });
    });
    expect(result.current.nudge).not.toBeNull();

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(emptyNudgeResult());
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-2" });
    });

    expect(result.current.nudge).toEqual({ ruleId: "rule-1", suggestions: nudgeResult("rule-1").suggestions });
  });

  it("a nudge-read failure is silent and leaves an existing panel unchanged", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-1" });
    });
    expect(result.current.nudge).not.toBeNull();

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockRejectedValueOnce(new Error("network down"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-2" });
    });

    expect(result.current.nudge).toEqual({ ruleId: "rule-1", suggestions: nudgeResult("rule-1").suggestions });
  });

  it("retireNudge excludes that rule from surfacing again even if its trigger returns a non-empty result", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    act(() => result.current.retireNudge("rule-1"));

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-1" });
    });

    expect(result.current.nudge).toBeNull();
  });

  it("a later non-empty trigger result replaces the visible panel", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1", "First"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-1" });
    });
    expect(result.current.nudge?.ruleId).toBe("rule-1");

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-2", "Second"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-2" });
    });

    expect(result.current.nudge?.ruleId).toBe("rule-2");
  });

  it("a late response from an older trigger/read generation is discarded", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    const firstReload = deferred<typeof scope>();
    const secondReload = deferred<typeof scope>();
    mockGetProposedScope.mockReturnValueOnce(firstReload.promise).mockReturnValueOnce(secondReload.promise);
    // secondCall's reload (and therefore its nudge-read call) resolves first below, so its mocked
    // response must be queued first — the mock queue follows call order, not trigger-issue order.
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-2", "Second"));
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1", "First"));

    let firstCall: Promise<void> | undefined;
    let secondCall: Promise<void> | undefined;
    act(() => {
      firstCall = result.current.refetchScope({ catalogItemId: "item-1" });
    });
    act(() => {
      secondCall = result.current.refetchScope({ catalogItemId: "item-2" });
    });

    await act(async () => {
      secondReload.resolve(scope);
      await secondCall;
      firstReload.resolve(scope);
      await firstCall;
    });

    expect(result.current.nudge?.ruleId).toBe("rule-2");
  });

  it("closeModal clears the visible panel and retirement, and discards a pending trigger read", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockResolvedValueOnce(scope);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1"));
    await act(async () => {
      await result.current.refetchScope({ catalogItemId: "item-1" });
    });
    expect(result.current.nudge).not.toBeNull();
    act(() => result.current.retireNudge("rule-1"));

    act(() => result.current.closeModal());
    expect(result.current.nudge).toBeNull();
    expect(result.current.isModalOpen).toBe(false);

    const pendingReload = deferred<typeof scope>();
    mockGetProposedScope.mockReturnValueOnce(pendingReload.promise);
    mockGetScopeNudgeFieldSuggestions.mockResolvedValueOnce(nudgeResult("rule-1"));
    let call: Promise<void> | undefined;
    act(() => {
      call = result.current.refetchScope({ catalogItemId: "item-1" });
    });
    act(() => result.current.closeModal());

    await act(async () => {
      pendingReload.resolve(scope);
      await call;
    });

    // rule-1 was retired before close; the same trigger firing again after close/reopen (a new
    // session) is allowed to surface, but this response's generation was invalidated by closeModal.
    expect(result.current.nudge).toBeNull();
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

  it("reconcileAfterConflict shows a reload-failure notice and preserves scope state when the authoritative reload fails", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockRejectedValueOnce(new Error("network down"));

    await act(async () => {
      await result.current.reconcileAfterConflict();
    });

    expect(result.current.conflictNotice).toBe(PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE);
    expect(result.current.state).toEqual({ status: "draft", scope });
  });

  it("retryReconciliation re-attempts the same authoritative reload and, on success, surfaces the original notice", async () => {
    const scope = draftScope();
    mockGetCurrentProposedScopeForRequest.mockResolvedValueOnce({ state: "Draft", scope });
    const { result } = renderHook(() => useProposedScopeCapture("request-1"));
    await waitFor(() => expect(result.current.state.status).toBe("draft"));

    mockGetProposedScope.mockRejectedValueOnce(new Error("network down"));
    await act(async () => {
      await result.current.reconcileAfterConflict("Custom notice");
    });
    expect(result.current.conflictNotice).toBe(PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE);
    expect(result.current.state).toEqual({ status: "draft", scope });

    const refreshed = draftScope({ concurrencyVersion: "v2" });
    mockGetProposedScope.mockResolvedValueOnce(refreshed);
    await act(async () => {
      await result.current.retryReconciliation();
    });

    expect(mockGetProposedScope).toHaveBeenCalledTimes(2);
    expect(result.current.conflictNotice).toBe("Custom notice");
    expect(result.current.state).toEqual({ status: "draft", scope: refreshed });
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
