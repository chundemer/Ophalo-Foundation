import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockViewCounts } from "../../mocks/fixtures";
import type { KeepRequestListResult, KeepSetupResult, GetRequestsParams } from "../../lib/apiClient";

// GAP-044: a demoted, non-competing Owner/Admin entry point into the already-implemented
// closed_history/cancelled_history/all_history contract. Closed/Cancelled/All scope and
// Today/Yesterday/This week/All time date scope; search and pagination must retain the
// selected history view/date scope and never silently drop back to an active queue.

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();

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

const HISTORY_VIEWS = new Set(["closed_history", "cancelled_history", "all_history"]);

function listResult(requests: KeepRequestListResult["requests"], isHistory: boolean): KeepRequestListResult {
  return {
    requests,
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: !isHistory, isHistory, isSearch: false },
  };
}

// Request Queue header consolidation (locked 2026-08-24): History moved from a standalone
// header button into Views as "History Log".
async function enterHistory(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Views" }));
  await user.click(screen.getByRole("button", { name: "History Log" }));
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
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  // UI-004 amendment: the landing view is Needs Attention, not "default" — history is
  // determined by the actual history views, not by "any non-default view".
  mockGetRequests.mockImplementation((query: GetRequestsParams) =>
    Promise.resolve(listResult([], HISTORY_VIEWS.has(query.view ?? ""))),
  );
});

describe("Requests — GAP-044 history entry point", () => {
  it("is not offered to Operators, and only the active-queue contract is ever requested", async () => {
    renderRequests("operator");

    await screen.findByRole("heading", { name: "Requests" });
    await userEvent.setup().click(screen.getByRole("button", { name: "Views" }));
    expect(screen.queryByRole("button", { name: "History Log" })).not.toBeInTheDocument();
    expect(mockGetRequests.mock.calls.every(([q]) => q.view !== "closed_history"
      && q.view !== "cancelled_history" && q.view !== "all_history")).toBe(true);
  });

  it("enters history mode on all_history/All time by default, with the demoted heading and search placeholder", async () => {
    const user = userEvent.setup();
    renderRequests("owner");

    await enterHistory(user);

    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ view: "all_history" }),
    ));
    const allTimeCall = mockGetRequests.mock.calls[mockGetRequests.mock.calls.length - 1][0] as GetRequestsParams;
    expect(allTimeCall.closedFrom).toBeUndefined();
    expect(allTimeCall.closedTo).toBeUndefined();
    expect(allTimeCall.closedShortcut).toBeUndefined();
    expect(screen.getByText("Closed and cancelled work — not part of your active queues.")).toBeInTheDocument();
    expect(screen.getByLabelText("Search requests")).toHaveAttribute("placeholder", "Search closed & cancelled history…");
    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Views/ })).not.toBeInTheDocument();
  });

  it("switches Closed/Cancelled scope, retaining history mode", async () => {
    const user = userEvent.setup();
    renderRequests("owner");

    await enterHistory(user);
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(expect.objectContaining({ view: "all_history" })));

    await user.click(screen.getByRole("button", { name: "Closed" }));
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(expect.objectContaining({ view: "closed_history" })));
    expect(screen.getByText("Closed and cancelled work — not part of your active queues.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Cancelled" }));
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(expect.objectContaining({ view: "cancelled_history" })));
  });

  it("maps Yesterday/This week to closedShortcut and Today to explicit UTC closedFrom/closedTo, never both", async () => {
    const user = userEvent.setup();
    renderRequests("owner");
    await enterHistory(user);

    await user.click(screen.getByRole("button", { name: "Yesterday" }));
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ closedShortcut: "yesterday" }),
    ));
    let call = mockGetRequests.mock.calls[mockGetRequests.mock.calls.length - 1][0] as GetRequestsParams;
    expect(call.closedFrom).toBeUndefined();
    expect(call.closedTo).toBeUndefined();

    await user.click(screen.getByRole("button", { name: "This week" }));
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ closedShortcut: "this_week" }),
    ));
    call = mockGetRequests.mock.calls[mockGetRequests.mock.calls.length - 1][0] as GetRequestsParams;
    expect(call.closedFrom).toBeUndefined();
    expect(call.closedTo).toBeUndefined();

    await user.click(screen.getByRole("button", { name: "Today" }));
    await waitFor(() => {
      const call = mockGetRequests.mock.calls.map(([q]) => q).find((q) => q.closedFrom);
      expect(call).toBeDefined();
      expect(call.closedShortcut).toBeUndefined();
      expect(call.closedFrom).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00\.000Z$/);
      expect(new Date(call.closedTo).getTime() - new Date(call.closedFrom).getTime()).toBe(24 * 60 * 60 * 1000);
    });
  });

  it("retains history view and date scope when searching or paginating — never returns to active queues", async () => {
    const user = userEvent.setup();
    renderRequests("owner");
    await enterHistory(user);
    await user.click(screen.getByRole("button", { name: "Yesterday" }));
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ view: "all_history", closedShortcut: "yesterday" }),
    ));

    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "Marcus{Enter}");

    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ view: "all_history", closedShortcut: "yesterday", q: "Marcus" }),
    ));
  });

  it("presents as history based on the server's listContext.isHistory, not client historyMode alone", async () => {
    const user = userEvent.setup();
    // Contrived: server returns isHistory:false even for a history-view request. Presentation
    // (subtitle, search placeholder) must follow the server's signal, not the client's own
    // navigation-intent flag — historyMode only decides which controls/query to show/send.
    mockGetRequests.mockImplementation(() => Promise.resolve(listResult([], false)));
    renderRequests("owner");

    await enterHistory(user);
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(
      expect.objectContaining({ view: "all_history" }),
    ));

    await waitFor(() => {
      expect(screen.queryByText("Closed and cancelled work — not part of your active queues.")).not.toBeInTheDocument();
    });
    expect(screen.getByLabelText("Search requests")).toHaveAttribute("placeholder", "Search requests…");
    // The demoted history chrome (client navigation intent) is still shown — only the
    // truthful content presentation follows the server.
    expect(screen.getByRole("button", { name: /Back to queues/ })).toBeInTheDocument();
  });

  it("returns to the default queue tab bar on Back to queues", async () => {
    const user = userEvent.setup();
    renderRequests("owner");
    await enterHistory(user);
    await screen.findByText("Closed and cancelled work — not part of your active queues.");

    await user.click(screen.getByRole("button", { name: /Back to queues/ }));

    expect(await screen.findByRole("tablist")).toBeInTheDocument();
    expect(screen.queryByText("Closed and cancelled work — not part of your active queues.")).not.toBeInTheDocument();
    // UI-004 amendment: tabs[0] is now Needs Attention (the locked Owner/Admin landing tab).
    await waitFor(() => expect(mockGetRequests).toHaveBeenCalledWith(expect.objectContaining({ view: "needs_attention" })));
  });
});
