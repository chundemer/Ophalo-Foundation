import { useState } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockRequestSummaries, mockViewCounts } from "../../mocks/fixtures";
import type { KeepRequestListResult, KeepSetupResult, KeepRequestViewCounts } from "../../lib/apiClient";

// App.tsx owns viewCounts as external state and feeds it back via onViewCountsUpdate (Session
// 3.5 first-visit count continuity) — Office Review's readyToClose/feedbackReview inputs read
// this same prop, so tests exercising it need the same round trip, not a static null/no-op.
function RequestsHarness({
  role,
  onSelectRequest = () => {},
}: {
  role: "owner" | "admin" | "operator";
  onSelectRequest?: (requestId: string) => void;
}) {
  const [viewCounts, setViewCounts] = useState<KeepRequestViewCounts | null>(null);
  return (
    <Requests
      role={role}
      viewCounts={viewCounts}
      onViewCountsUpdate={setViewCounts}
      onSelectRequest={onSelectRequest}
      onNavigateSettings={() => {}}
      onStartCapture={() => {}}
    />
  );
}

// GAP-041: a first-time queue selection must keep the header/tab bar/search row stable
// and show a fixed queue-agnostic skeleton, never blank the whole region or reuse the
// previous queue's real rows. The tab bar also needs a real roving-tabindex keyboard
// pattern (Left/Right/Home/End move focus+selection; Enter/Space stay native).
// GAP-026: the search box needs a visible, keyboard-usable clear affordance.

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
const mockGetMe = vi.fn();
const mockGetActualWorkReviewQueue = vi.fn();
const mockGetActualWorkReviewQueueCount = vi.fn();

vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>(
    "../../lib/apiClient",
  );
  return {
    ...actual,
    api: {
      ...actual.api,
      getRequests: (...args: unknown[]) => mockGetRequests(...args),
      getAvailableRequests: (...args: unknown[]) => mockGetAvailableRequests(...args),
      getGuidedSetup: (...args: unknown[]) => mockGetGuidedSetup(...args),
      getSetup: (...args: unknown[]) => mockGetSetup(...args),
      getMe: (...args: unknown[]) => mockGetMe(...args),
      getActualWorkReviewQueue: (...args: unknown[]) => mockGetActualWorkReviewQueue(...args),
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

const mockBusinessSetup: KeepSetupResult = {
  businessName: "Acme Plumbing",
  timeZone: "America/Chicago",
  customerFacingPhone: null,
  customerFacingEmail: null,
  logoUrl: null,
  websiteUrl: null,
  responsePolicy: {
    firstResponseTargetMinutes: 60,
    standardResponseTargetMinutes: 240,
    priorityResponseTargetMinutes: 30,
    statusCheckThresholdDays: 3,
  },
};

function listResult(requests: KeepRequestListResult["requests"]): KeepRequestListResult {
  return {
    requests,
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
  };
}

function renderRequests(
  role: "owner" | "admin" | "operator" = "owner",
  onSelectRequest: (requestId: string) => void = () => {},
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RequestsHarness role={role} onSelectRequest={onSelectRequest} />
    </QueryClientProvider>,
  );
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => { resolve = r; });
  return { promise, resolve };
}

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetMe.mockReset();
  mockGetActualWorkReviewQueue.mockReset();
  mockGetActualWorkReviewQueueCount.mockReset();
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetActualWorkReviewQueue.mockResolvedValue([]);
  mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 0 });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  // GAP-042: ["me"] resolves independently of the list response — this is what proves the
  // GAP-041 first-load skeleton contract still holds when the title source moves off the list.
  mockGetMe.mockResolvedValue({
    accountUserId: "mock-user-1",
    accountId: "mock-account-1",
    isAuthenticated: true,
    isVerified: true,
    accountRole: "owner",
    businessName: "Acme Plumbing",
  });
});

describe("Requests — GAP-041 queue-transition stability", () => {
  it("shows a fixed skeleton on first load without blanking the header, tabs, or search row", async () => {
    const gate = deferred<KeepRequestListResult>();
    mockGetRequests.mockReturnValue(gate.promise);
    renderRequests();

    expect(await screen.findByRole("heading", { name: "Requests for Acme Plumbing" })).toBeInTheDocument();
    // UI-004 amendment: Owner/Admin's new-session landing tab is Needs Attention.
    expect(screen.getByRole("tab", { name: /Needs Attention/ })).toBeInTheDocument();
    expect(screen.getByLabelText("Search requests")).toBeInTheDocument();
    const region = screen.getByRole("region", { name: "Needs Attention requests" });
    expect(region).toHaveAttribute("aria-busy", "true");
    // Five fixed skeleton placeholders, not the "Loading…" blob.
    expect(region.querySelectorAll('[aria-hidden="true"] > div').length).toBe(5);
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();

    gate.resolve(listResult([mockRequestSummaries[0]]));
    await screen.findByText(mockRequestSummaries[0].customerName);
    expect(region).toHaveAttribute("aria-busy", "false");
  });

  it("returns cached tab content immediately with no skeleton on revisit", async () => {
    mockGetRequests.mockImplementation((query: { view: string }) =>
      Promise.resolve(
        query.view === "needs_attention"
          ? listResult([mockRequestSummaries[1]])
          : listResult([mockRequestSummaries[0]]),
      ),
    );
    // UI-004 amendment: new-session landing tab is Needs Attention.
    renderRequests();
    await screen.findByText(mockRequestSummaries[1].customerName);

    fireEvent.click(screen.getByRole("tab", { name: /All Work/ }));
    await screen.findByText(mockRequestSummaries[0].customerName);

    fireEvent.click(screen.getByRole("tab", { name: /Needs Attention/ }));
    // Cached — must be present synchronously, no skeleton frame in between.
    expect(screen.getByText(mockRequestSummaries[1].customerName)).toBeInTheDocument();
  });

  it("moves focus and selection with ArrowRight/ArrowLeft/Home/End without navigation", async () => {
    mockGetRequests.mockResolvedValue(listResult([]));
    renderRequests();

    const tabs = await screen.findAllByRole("tab");
    expect(tabs[0]).toHaveAttribute("aria-selected", "true");
    expect(tabs[0]).toHaveAttribute("tabindex", "0");
    expect(tabs[1]).toHaveAttribute("tabindex", "-1");

    tabs[0].focus();
    fireEvent.keyDown(tabs[0], { key: "ArrowRight" });
    await waitFor(() => expect(tabs[1]).toHaveAttribute("aria-selected", "true"));
    expect(document.activeElement).toBe(tabs[1]);
    expect(tabs[1]).toHaveAttribute("tabindex", "0");
    expect(tabs[0]).toHaveAttribute("tabindex", "-1");

    fireEvent.keyDown(tabs[1], { key: "ArrowLeft" });
    await waitFor(() => expect(tabs[0]).toHaveAttribute("aria-selected", "true"));
    expect(document.activeElement).toBe(tabs[0]);

    fireEvent.keyDown(tabs[0], { key: "End" });
    const last = tabs[tabs.length - 1];
    await waitFor(() => expect(last).toHaveAttribute("aria-selected", "true"));
    expect(document.activeElement).toBe(last);

    fireEvent.keyDown(last, { key: "Home" });
    await waitFor(() => expect(tabs[0]).toHaveAttribute("aria-selected", "true"));
    expect(document.activeElement).toBe(tabs[0]);
  });

  it("activates a focused tab with Enter and Space", async () => {
    mockGetRequests.mockResolvedValue(listResult([]));
    const user = userEvent.setup();
    renderRequests();

    const tabs = await screen.findAllByRole("tab");
    tabs[1].focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(tabs[1]).toHaveAttribute("aria-selected", "true"));

    tabs[2].focus();
    await user.keyboard(" ");
    await waitFor(() => expect(tabs[2]).toHaveAttribute("aria-selected", "true"));
  });
});

// UI-004 amendment: Actual Work Review is an Office Review member, not a primary tab — it's
// reachable by opening the Office Review disclosure, and its count comes from the
// authoritative GET .../review-queue/count endpoint, never the review queue list's `.length`.
describe("Requests — Slice 8A / UI-004 amendment Actual Work Review (Office Review member)", () => {
  it("shows Office Review with Actual Work Review as a member for Owner/Admin", async () => {
    mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 1 });
    mockGetRequests.mockResolvedValue(listResult([]));
    renderRequests("owner");

    const officeReviewTrigger = await screen.findByRole("button", { name: /Office Review/ });
    fireEvent.click(officeReviewTrigger);
    expect(await screen.findByRole("button", { name: /Actual Work Review/ })).toBeInTheDocument();
  });

  it("never renders Office Review for Operator, regardless of counts", async () => {
    mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 1 });
    mockGetRequests.mockResolvedValue(listResult([]));
    renderRequests("operator");
    await screen.findByRole("heading", { name: "Requests for Acme Plumbing" });
    expect(screen.queryByRole("button", { name: /Office Review/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Actual Work Review/)).not.toBeInTheDocument();
  });

  it("selecting Actual Work Review from Office Review renders queue rows and navigates on selection", async () => {
    mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 1 });
    mockGetRequests.mockResolvedValue(listResult([]));
    const gate = deferred<unknown[]>();
    mockGetActualWorkReviewQueue.mockReturnValue(gate.promise);
    const onSelectRequest = vi.fn();
    renderRequests("owner", onSelectRequest);

    const officeReviewTrigger = await screen.findByRole("button", { name: /Office Review/ });
    fireEvent.click(officeReviewTrigger);
    const memberButton = await screen.findByRole("button", { name: /Actual Work Review/ });
    fireEvent.click(memberButton);
    // Selecting closes the disclosure and returns focus to its own trigger.
    await waitFor(() => expect(document.activeElement).toBe(officeReviewTrigger));

    await screen.findByText("Loading review queue…");

    gate.resolve([
      {
        actualWorkId: "aw-1",
        requestId: "req-1",
        referenceCode: "REQ-001",
        customerName: "Marcus Reyes",
        submittedAtUtc: "2026-08-20T12:00:00Z",
        hasIncompleteFinancialData: false,
        incompleteLineCount: 0,
        totalSalesPrice: 100,
        totalStandardExpectedDirectCost: 40,
        totalMargin: 60,
      },
    ]);

    const row = await screen.findByText("Marcus Reyes");
    fireEvent.click(row);
    expect(onSelectRequest).toHaveBeenCalledWith("req-1");
  });

  it("Office Review shows a Retry affordance (not a perpetual loading placeholder) when the count query fails", async () => {
    mockGetActualWorkReviewQueueCount.mockRejectedValue(new Error("network error"));
    mockGetRequests.mockResolvedValue(listResult([]));
    renderRequests("owner");

    const retryButton = await screen.findByRole("button", { name: /couldn.t load counts/i });
    expect(retryButton).toBeInTheDocument();

    mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 2 });
    fireEvent.click(retryButton);

    await waitFor(() => expect(screen.getByRole("button", { name: /Office Review/ })).toBeInTheDocument());
  });
});

describe("Requests — GAP-026 search clear affordance", () => {
  it("shows a clear control only while the search box has text, and resets query/cursor/focus", async () => {
    mockGetRequests.mockResolvedValue(listResult([]));
    const user = userEvent.setup();
    renderRequests();

    const input = await screen.findByLabelText("Search requests");
    expect(screen.queryByLabelText("Clear search")).not.toBeInTheDocument();

    await user.type(input, "Marcus");
    const clearButton = await screen.findByLabelText("Clear search");
    expect(clearButton.tagName).toBe("BUTTON");

    await user.click(clearButton);
    expect(input).toHaveValue("");
    expect(screen.queryByLabelText("Clear search")).not.toBeInTheDocument();
    expect(document.activeElement).toBe(input);
  });
});
