import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestWorkbenchShell } from "../RequestWorkbenchShell";
import { mockViewCounts, mockRequestSummaries } from "../../../mocks/fixtures";
import type { KeepRequestListResult } from "../../../lib/apiClient";

// Step 5: the shell's own routing/live-navigation-bridge logic is under test here, not
// RequestDetail's internals (network fetch, modals, capture flows — covered elsewhere). Stub it
// to a prop-recording node so pane-mode wiring (requestId, paneMode, prevId/nextId, onNavigate)
// is directly assertable.
vi.mock("../../../pages/RequestDetail", () => ({
  RequestDetail: (props: {
    requestId: string;
    paneMode?: boolean;
    prevId?: string;
    nextId?: string;
    onNavigate?: (id: string) => void;
  }) => (
    <div
      data-testid="request-detail-stub"
      data-request-id={props.requestId}
      data-pane-mode={String(!!props.paneMode)}
      data-prev-id={props.prevId ?? ""}
      data-next-id={props.nextId ?? ""}
    >
      <button type="button" onClick={() => props.onNavigate?.("mock-req-003")}>
        stub-navigate
      </button>
    </div>
  ),
}));

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
const mockGetActualWorkReviewQueueCount = vi.fn();

vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>(
    "../../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getRequests: (...args: unknown[]) => mockGetRequests(...args),
      getAvailableRequests: (...args: unknown[]) => mockGetAvailableRequests(...args),
      getGuidedSetup: (...args: unknown[]) => mockGetGuidedSetup(...args),
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      getActualWorkReviewQueueCount: (...args: unknown[]) => mockGetActualWorkReviewQueueCount(...args),
    },
  };
});

function listResult(): KeepRequestListResult {
  return {
    requests: mockRequestSummaries.slice(0, 1),
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
  };
}

function listResultMulti(): KeepRequestListResult {
  return {
    requests: mockRequestSummaries.slice(0, 3),
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
  };
}

// Protected-minimum measurement (build-log 133, locked in keep-ui-design-model-v2.md §13) is a
// container-width rule via ResizeObserver, not a CSS media query — jsdom has no ResizeObserver,
// so this stub captures the registered callback and lets each test drive it directly.
let roCallback: ResizeObserverCallback | null = null;
class StubResizeObserver {
  constructor(cb: ResizeObserverCallback) {
    roCallback = cb;
  }
  observe() {}
  disconnect() {}
  unobserve() {}
}

function fireWidth(width: number) {
  act(() => {
    roCallback?.(
      [{ contentRect: { width } } as ResizeObserverEntry],
      null as unknown as ResizeObserver,
    );
  });
}

function renderShell(
  onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }) => void = () => {},
  route?: { page: "requests" } | { page: "detail"; requestId: string; focusPanel?: string },
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RequestWorkbenchShell
        role="owner"
        route={route}
        viewCounts={null}
        onViewCountsUpdate={() => {}}
        onSelectRequest={onSelectRequest}
        onNavigateSettings={() => {}}
        onStartCapture={() => {}}
        onBack={() => {}}
        narrowPrevId={undefined}
        narrowNextId={undefined}
        onNarrowNavigate={() => {}}
      />
    </QueryClientProvider>,
  );
}

describe("RequestWorkbenchShell", () => {
  beforeEach(() => {
    roCallback = null;
    mockGetRequests.mockReset().mockResolvedValue(listResult());
    mockGetAvailableRequests.mockReset();
    mockGetGuidedSetup.mockReset().mockResolvedValue({
      businessInfoComplete: true,
      addFirstRequestComplete: true,
      reviewCustomerPageComplete: true,
      createIntakePageComplete: true,
      shareIntakePageComplete: true,
      buildTeamComplete: true,
      useMobileComplete: true,
      deferredSteps: [],
      intendedTeamSize: null,
    });
    mockGetSetup.mockReset().mockResolvedValue(null);
    mockGetActualWorkReviewQueueCount.mockReset().mockResolvedValue({ count: 0 });
    vi.stubGlobal("ResizeObserver", StubResizeObserver);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders only the one-pane Requests fallback below the protected minimum width", async () => {
    renderShell();
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    fireWidth(1000);
    await waitFor(() => expect(screen.getByText(mockRequestSummaries[0].customerName)).toBeInTheDocument());
    expect(screen.queryByText(/loading priority preview/i)).not.toBeInTheDocument();
  });

  it("renders the Queue pane plus Priority Preview at/above the protected minimum width", async () => {
    renderShell();
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    fireWidth(1001);
    await waitFor(() => {
      expect(screen.getAllByText(mockRequestSummaries[0].customerName).length).toBeGreaterThan(0);
    });
    // Priority Preview mounts as a second, independent presentation of the applied snapshot —
    // distinguishable here by its "Open request" action, which the Queue-pane row does not render.
    await waitFor(() => expect(screen.getByRole("button", { name: /open request/i })).toBeInTheDocument());
  });

  it("Open request preserves the applied queue's request-ID navigation context", async () => {
    const onSelectRequest = vi.fn();
    renderShell(onSelectRequest);
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    fireWidth(1001);
    await waitFor(() => expect(screen.getByRole("button", { name: /open request/i })).toBeInTheDocument());

    act(() => {
      screen.getByRole("button", { name: /open request/i }).click();
    });

    expect(onSelectRequest).toHaveBeenCalledWith(mockRequestSummaries[0].id, {
      requestIds: [mockRequestSummaries[0].id],
    });
  });

  it("Backlog item 3: auto-selects the first eligible request once, on initial wide entry with a settled ranked queue", async () => {
    mockGetRequests.mockResolvedValue(listResultMulti());
    const onSelectRequest = vi.fn();
    renderShell(onSelectRequest);
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    fireWidth(1001);

    await waitFor(() =>
      expect(onSelectRequest).toHaveBeenCalledWith(mockRequestSummaries[0].id, {
        requestIds: mockRequestSummaries.slice(0, 3).map((r) => r.id),
      }),
    );
    expect(onSelectRequest).toHaveBeenCalledTimes(1);
  });

  it("Backlog item 3: does not auto-select again once a detail route has already been entered", async () => {
    const onSelectRequest = vi.fn();
    renderShell(onSelectRequest, { page: "detail", requestId: mockRequestSummaries[0].id });
    fireWidth(1001);
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByTestId("request-detail-stub")).toBeInTheDocument());

    expect(onSelectRequest).not.toHaveBeenCalled();
  });

  it("Backlog item 3: an explicit Requests entry selects again without remounting the Queue", async () => {
    mockGetRequests.mockResolvedValue(listResultMulti());
    const onSelectRequest = vi.fn();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const props = {
      role: "owner" as const,
      route: { page: "requests" as const },
      viewCounts: null,
      onViewCountsUpdate: () => {},
      onSelectRequest,
      onNavigateSettings: () => {},
      onStartCapture: () => {},
      onBack: () => {},
    };
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <RequestWorkbenchShell {...props} requestEntryIntent={0} />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    fireWidth(1001);
    await waitFor(() => expect(onSelectRequest).toHaveBeenCalledTimes(1));

    rerender(
      <QueryClientProvider client={queryClient}>
        <RequestWorkbenchShell {...props} requestEntryIntent={1} />
      </QueryClientProvider>,
    );

    await waitFor(() => expect(onSelectRequest).toHaveBeenCalledTimes(2));
    expect(mockGetRequests).toHaveBeenCalledTimes(1);
  });

  it("falls back to the one-pane presentation in History mode, even when wide", async () => {
    mockGetRequests.mockImplementation((args: { view: string }) =>
      Promise.resolve(
        args.view === "needs_attention"
          ? listResult()
          : {
              requests: [],
              pageInfo: { limit: 50, hasMore: false, nextCursor: null },
              viewCounts: mockViewCounts,
              listContext: { view: args.view, isDefaultCommandCenter: false, isHistory: true, isSearch: false },
            },
      ),
    );
    renderShell();
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
    fireWidth(1001);
    await waitFor(() => expect(screen.getByRole("button", { name: /open request/i })).toBeInTheDocument());

    act(() => {
      screen.getByRole("button", { name: "History" }).click();
    });

    // History is a closed/cancelled result set, not an active queue UI-003's branches describe —
    // Priority Preview must not render "no active requests" + New Request for it.
    await waitFor(() => expect(screen.getByRole("button", { name: /back to queues/i })).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /open request/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /new request/i })).not.toBeInTheDocument();
  });

  describe("detail route (Step 5)", () => {
    it("keeps the Queue pane mounted (no refetch) when navigating from requests to a wide detail route", async () => {
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { rerender } = render(
        <QueryClientProvider client={queryClient}>
          <RequestWorkbenchShell
            role="owner"
            route={{ page: "requests" }}
            viewCounts={null}
            onViewCountsUpdate={() => {}}
            onSelectRequest={() => {}}
            onNavigateSettings={() => {}}
            onStartCapture={() => {}}
            onBack={() => {}}
          />
        </QueryClientProvider>,
      );
      await waitFor(() => expect(mockGetRequests).toHaveBeenCalled());
      fireWidth(1001);
      await waitFor(() =>
        expect(screen.getAllByText(mockRequestSummaries[0].customerName).length).toBeGreaterThan(0),
      );
      const callsBeforeNavigate = mockGetRequests.mock.calls.length;

      rerender(
        <QueryClientProvider client={queryClient}>
          <RequestWorkbenchShell
            role="owner"
            route={{ page: "detail", requestId: mockRequestSummaries[0].id }}
            viewCounts={null}
            onViewCountsUpdate={() => {}}
            onSelectRequest={() => {}}
            onNavigateSettings={() => {}}
            onStartCapture={() => {}}
            onBack={() => {}}
          />
        </QueryClientProvider>,
      );

      // The Queue pane (its row content, filters, scroll) survives the route change because
      // `Requests` occupies the same position in the shell's JSX for both routes — React
      // reconciles it as an update, not an unmount/remount, so no additional fetch fires.
      expect(screen.getByText(mockRequestSummaries[0].customerName)).toBeInTheDocument();
      expect(screen.getByTestId("request-detail-stub")).toBeInTheDocument();
      expect(mockGetRequests.mock.calls.length).toBe(callsBeforeNavigate);
    });

    it("computes pane-mode Prev/Next from the live applied snapshot, not frozen navContext", async () => {
      mockGetRequests.mockResolvedValue(listResultMulti());
      renderShell(() => {}, { page: "detail", requestId: mockRequestSummaries[1].id });
      fireWidth(1001);
      await waitFor(() =>
        expect(screen.getAllByText(mockRequestSummaries[0].customerName).length).toBeGreaterThan(0),
      );

      const stub = screen.getByTestId("request-detail-stub");
      expect(stub).toHaveAttribute("data-pane-mode", "true");
      expect(stub).toHaveAttribute("data-prev-id", mockRequestSummaries[0].id);
      expect(stub).toHaveAttribute("data-next-id", mockRequestSummaries[2].id);
    });

    it("hides Prev/Next when the open request has fallen out of the applied snapshot", async () => {
      mockGetRequests.mockResolvedValue(listResultMulti());
      renderShell(() => {}, { page: "detail", requestId: "not-in-the-queue" });
      fireWidth(1001);
      await waitFor(() =>
        expect(screen.getAllByText(mockRequestSummaries[0].customerName).length).toBeGreaterThan(0),
      );

      const stub = screen.getByTestId("request-detail-stub");
      expect(stub).toHaveAttribute("data-prev-id", "");
      expect(stub).toHaveAttribute("data-next-id", "");
    });

    it("pane-mode navigation calls onSelectRequest without the frozen navContext", async () => {
      mockGetRequests.mockResolvedValue(listResultMulti());
      const onSelectRequest = vi.fn();
      renderShell(onSelectRequest, { page: "detail", requestId: mockRequestSummaries[1].id });
      fireWidth(1001);
      await waitFor(() =>
        expect(screen.getAllByText(mockRequestSummaries[0].customerName).length).toBeGreaterThan(0),
      );

      screen.getByRole("button", { name: "stub-navigate" }).click();

      expect(onSelectRequest).toHaveBeenCalledWith("mock-req-003");
    });

    it("falls back to the narrow one-pane detail presentation below the protected minimum, without mounting the Queue", async () => {
      renderShell(() => {}, { page: "detail", requestId: mockRequestSummaries[0].id });
      fireWidth(1000);
      await waitFor(() => expect(screen.getByTestId("request-detail-stub")).toBeInTheDocument());

      expect(mockGetRequests).not.toHaveBeenCalled();
      const stub = screen.getByTestId("request-detail-stub");
      expect(stub).toHaveAttribute("data-pane-mode", "false");
    });
  });
});
