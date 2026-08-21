import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestWorkbenchShell } from "../RequestWorkbenchShell";
import { mockViewCounts, mockRequestSummaries } from "../../../mocks/fixtures";
import type { KeepRequestListResult } from "../../../lib/apiClient";

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

function renderShell(onSelectRequest: (requestId: string, navContext?: { requestIds: string[] }) => void = () => {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RequestWorkbenchShell
        role="owner"
        viewCounts={null}
        onViewCountsUpdate={() => {}}
        onSelectRequest={onSelectRequest}
        onNavigateSettings={() => {}}
        onStartCapture={() => {}}
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
});
