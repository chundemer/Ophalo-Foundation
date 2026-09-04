import { useCallback, useRef, useState } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockViewCounts, mockRequestSummaries } from "../../mocks/fixtures";
import type {
  AccountRole,
  KeepRequestListResult,
  KeepRequestViewCounts,
} from "../../lib/apiClient";

// GAP-057: an empty Needs Attention queue must not imply the system has no active work. The
// Attention queue remains selected, describes its empty state truthfully, and offers All Work as
// an explicit choice rather than silently redirecting the owner.

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
const mockGetActualWorkReviewQueueCount = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
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

const completeGuidedSetup = {
  businessInfoComplete: true,
  addFirstRequestComplete: true,
  reviewCustomerPageComplete: true,
  createIntakePageComplete: true,
  shareIntakePageComplete: true,
  buildTeamComplete: true,
  useMobileComplete: true,
  deferredSteps: [],
  intendedTeamSize: null,
};

function listResult(
  view: string,
  requests: KeepRequestListResult["requests"],
  viewCounts: KeepRequestViewCounts,
): KeepRequestListResult {
  return {
    requests,
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts,
    listContext: { view, isDefaultCommandCenter: view === "default", isHistory: false, isSearch: false },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => { resolve = r; });
  return { promise, resolve };
}

function RequestsHarness({ role }: { role: AccountRole }) {
  const [viewCounts, setViewCounts] = useState<KeepRequestViewCounts | null>(null);
  const onUpdate = useRef(setViewCounts);
  const handle = useCallback((c: KeepRequestViewCounts | null) => onUpdate.current(c), []);
  return (
    <Requests
      role={role}
      viewCounts={viewCounts}
      onViewCountsUpdate={handle}
      onSelectRequest={() => {}}
      onNavigateSettings={() => {}}
      onStartCapture={() => {}}
    />
  );
}

function renderRequests(role: AccountRole = "owner") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RequestsHarness role={role} />
    </QueryClientProvider>,
  );
}

const zeroAttention: KeepRequestViewCounts = { ...mockViewCounts, needsAttention: 0, default: 4 };
const emptyEverything: KeepRequestViewCounts = { ...mockViewCounts, needsAttention: 0, default: 0 };
const withAttention: KeepRequestViewCounts = { ...mockViewCounts, needsAttention: 2, default: 4 };

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset().mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockReset().mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockReset().mockResolvedValue(null);
  mockGetActualWorkReviewQueueCount.mockReset().mockResolvedValue({ count: 0 });
});

describe("Requests — GAP-057 empty Attention queue", () => {
  it("zero Attention + active All work: stays on Attention and offers All Work without a redirect", async () => {
    mockGetRequests.mockImplementation((q: { view: string }) =>
      Promise.resolve(
        q.view === "needs_attention"
          ? listResult("needs_attention", [], zeroAttention)
          : listResult("default", mockRequestSummaries.slice(0, 2), zeroAttention),
      ),
    );
    renderRequests("owner");

    await waitFor(() =>
      expect(screen.getByRole("tab", { name: /Needs Attention/ })).toHaveAttribute("aria-selected", "true"),
    );
    expect((await screen.findAllByText("Nothing needs attention")).length).toBeGreaterThan(0);
    expect(await screen.findByRole("button", { name: /View all 4 active requests/ })).toBeInTheDocument();
    expect(screen.queryByText("No active requests")).not.toBeInTheDocument();
  });

  it("nonzero Attention: keeps the attention-first landing", async () => {
    mockGetRequests.mockImplementation((q: { view: string }) =>
      Promise.resolve(listResult(q.view, q.view === "needs_attention" ? mockRequestSummaries.slice(0, 1) : [], withAttention)),
    );
    renderRequests("owner");

    await waitFor(() =>
      expect(screen.getByRole("tab", { name: /Needs Attention/ })).toHaveAttribute("aria-selected", "true"),
    );
    expect(await screen.findByText(mockRequestSummaries[0].customerName)).toBeInTheDocument();
  });

  it("truly empty work: keeps Attention selected and does not offer a false All Work recovery", async () => {
    mockGetRequests.mockImplementation((q: { view: string }) =>
      Promise.resolve(listResult(q.view, [], emptyEverything)),
    );
    renderRequests("owner");

    await waitFor(() =>
      expect(screen.getByRole("tab", { name: /Needs Attention/ })).toHaveAttribute("aria-selected", "true"),
    );
    expect((await screen.findAllByText("Nothing needs attention")).length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /View all .* active requests/i })).not.toBeInTheDocument();
    expect(screen.queryByText("No active requests")).not.toBeInTheDocument();
  });

  it("does not override an explicit selection when counts arrive later", async () => {
    const attentionGate = deferred<KeepRequestListResult>();
    mockGetRequests.mockImplementation((q: { view: string }) =>
      q.view === "needs_attention"
        ? attentionGate.promise
        : Promise.resolve(listResult("default", mockRequestSummaries.slice(0, 1), zeroAttention)),
    );
    renderRequests("owner");

    // User explicitly picks All Work before the landing counts resolve.
    fireEvent.click(await screen.findByRole("tab", { name: /All Work/ }));
    await screen.findByText(mockRequestSummaries[0].customerName);

    // Now the attention landing query resolves with a zero count — must NOT bounce selection.
    attentionGate.resolve(listResult("needs_attention", [], zeroAttention));
    await new Promise((r) => setTimeout(r, 0));
    expect(screen.getByRole("tab", { name: /All Work/ })).toHaveAttribute("aria-selected", "true");
  });

  it("deliberately selecting an empty Attention queue: truthful state plus View all action, never 'No active requests'", async () => {
    const counts = { ...mockViewCounts, needsAttention: 0, default: 3 };
    mockGetRequests.mockImplementation((q: { view: string }) =>
      Promise.resolve(
        q.view === "needs_attention"
          ? listResult("needs_attention", [], counts)
          : listResult("default", mockRequestSummaries.slice(0, 3), counts),
      ),
    );
    renderRequests("owner");

    expect((await screen.findAllByText("Nothing needs attention")).length).toBeGreaterThan(0);
    const viewAll = await screen.findByRole("button", { name: /View all 3 active requests/ });
    expect(screen.queryByText("No active requests")).not.toBeInTheDocument();

    fireEvent.click(viewAll);
    await waitFor(() =>
      expect(screen.getByRole("tab", { name: /All Work/ })).toHaveAttribute("aria-selected", "true"),
    );
  });
});
