import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockRequestSummaries, mockViewCounts } from "../../mocks/fixtures";
import type { KeepRequestListResult, KeepSetupResult } from "../../lib/apiClient";

// ADR-449: within "All work" (the "default" view), the already server-ranked page
// splits into a quiet Needs attention / Open work pair. rowContext is server-authoritative;
// the client only partitions the existing order, it never resorts or invents membership.

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
const mockGetMe = vi.fn();
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

function renderRequests(role: "owner" | "admin" | "operator" = "owner") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Requests
        role={role}
        viewCounts={null}
        onViewCountsUpdate={() => {}}
        onSelectRequest={() => {}}
        onNavigateSettings={() => {}}
        onStartCapture={() => {}}
      />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetMe.mockReset();
  mockGetActualWorkReviewQueueCount.mockReset();
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 0 });
  mockGetMe.mockResolvedValue({
    accountUserId: "mock-user-1",
    accountId: "mock-account-1",
    isAuthenticated: true,
    isVerified: true,
    accountRole: "owner",
    businessName: "Acme Plumbing",
  });
});

describe("Requests — ADR-449 Owner/Admin work-queue hierarchy", () => {
  it("renders a business-name heading and the locked All work subtitle for Owner/Admin", async () => {
    mockGetRequests.mockResolvedValue(listResult([]));
    renderRequests("owner");

    expect(await screen.findByRole("heading", { name: "Requests for Acme Plumbing" })).toBeInTheDocument();
    // UI-004 amendment: new-session landing tab is Needs Attention — select All Work explicitly.
    fireEvent.click(await screen.findByRole("tab", { name: /All Work/ }));
    expect(
      await screen.findByText(
        "Open requests and feedback requiring review, ranked with customer promises needing attention first.",
      ),
    ).toBeInTheDocument();
  });

  it("splits a mixed page into Needs attention (first) and Open work (second), preserving server order", async () => {
    // mock-req-002 → rowContext "needs_attention"; mock-req-001 → "active_work".
    const needsAttentionRow = mockRequestSummaries.find((r) => r.id === "mock-req-002")!;
    const openWorkRow = mockRequestSummaries.find((r) => r.id === "mock-req-001")!;
    mockGetRequests.mockResolvedValue(listResult([needsAttentionRow, openWorkRow]));
    renderRequests("owner");

    // UI-004 amendment: new-session landing tab is Needs Attention — select All Work explicitly.
    fireEvent.click(await screen.findByRole("tab", { name: /All Work/ }));
    await screen.findByText(needsAttentionRow.customerName);
    const region = screen.getByRole("region", { name: "All Work requests" });
    // First h2 in the region is GAP-043's stable page-range heading ("Showing 1–2"), not a section label.
    const headings = within(region).getAllByRole("heading", { level: 2 }).slice(1);
    expect(headings.map((h) => h.textContent)).toEqual(["Needs attention", "Open work"]);

    const needsSection = headings[0].parentElement!;
    const openSection = headings[1].parentElement!;
    expect(within(needsSection).getByText(needsAttentionRow.customerName)).toBeInTheDocument();
    expect(within(openSection).getByText(openWorkRow.customerName)).toBeInTheDocument();
  });

  it("omits the Needs attention section header entirely when no row qualifies", async () => {
    const openWorkRow = mockRequestSummaries.find((r) => r.id === "mock-req-001")!;
    mockGetRequests.mockResolvedValue(listResult([openWorkRow]));
    renderRequests("owner");

    // UI-004 amendment: new-session landing tab is Needs Attention — select All Work explicitly.
    fireEvent.click(await screen.findByRole("tab", { name: /All Work/ }));
    await screen.findByText("Open work");
    expect(screen.queryByText("Needs attention")).not.toBeInTheDocument();
  });

  it("does not render section headers or the locked subtitle on a non-default tab", async () => {
    const needsAttentionRow = mockRequestSummaries.find((r) => r.id === "mock-req-002")!;
    mockGetRequests.mockResolvedValue(listResult([needsAttentionRow]));
    renderRequests("owner");

    await screen.findByRole("heading", { name: "Requests for Acme Plumbing" });
    const needsAttentionTab = await screen.findByRole("tab", { name: /Needs Attention/ });
    fireEvent.click(needsAttentionTab);

    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ view: "needs_attention" }),
    ));
    expect(screen.queryByRole("heading", { level: 2, name: "Needs attention" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 2, name: "Open work" })).not.toBeInTheDocument();
    expect(
      screen.queryByText(
        "Open requests and feedback requiring review, ranked with customer promises needing attention first.",
      ),
    ).not.toBeInTheDocument();
  });

  it("shows the business-name heading for an Operator too, sourced from [\"me\"], never fetches setup (GAP-042)", async () => {
    mockGetRequests.mockResolvedValue(listResult([]));
    renderRequests("operator");

    expect(await screen.findByRole("heading", { name: "Requests for Acme Plumbing" })).toBeInTheDocument();
    expect(mockGetSetup).not.toHaveBeenCalled();
  });
});
