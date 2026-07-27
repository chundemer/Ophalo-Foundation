import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockRequestSummaries, mockViewCounts } from "../../mocks/fixtures";
import type { KeepRequestListResult, KeepSetupResult, GetRequestsParams } from "../../lib/apiClient";

// GAP-046 / Build Log 094: applied-criteria visibility and recovery. The applied-criteria line
// and the live-region heading/range must report only submitted (q/statusFilter/history scope
// and date) criteria, never the unsubmitted draft; a filtered-empty state must read differently
// from a true-empty state and offer a single "Clear filters" recovery action.

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

function listResult(
  requests: KeepRequestListResult["requests"],
  isHistory = false,
): KeepRequestListResult {
  return {
    requests,
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: !isHistory, isHistory, isSearch: false },
  };
}

function renderRequests() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Requests
        role="owner"
        viewCounts={null}
        onViewCountsUpdate={() => {}}
        onSelectRequest={() => {}}
        onNavigateSettings={() => {}}
        onStartCapture={() => {}}
      />
    </QueryClientProvider>,
  );
}

const sampleRow = mockRequestSummaries.find((r) => r.id === "mock-req-001")!;

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  mockGetRequests.mockImplementation((query: GetRequestsParams) =>
    Promise.resolve(listResult(query.q || query.status ? [] : [sampleRow], query.view !== "default")),
  );
});

describe("Requests — GAP-046 filter-state visibility and recovery", () => {
  it("shows no applied-criteria line in the untouched default queue, then a submitted-search line", async () => {
    const user = userEvent.setup();
    renderRequests();

    await screen.findByText("Showing 1–1");
    expect(screen.queryByText(/^Applied:/)).not.toBeInTheDocument();

    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "smith{Enter}");

    await waitFor(() =>
      expect(screen.getByText("Applied: Search “smith” · Status: All active statuses")).toBeInTheDocument(),
    );
  });

  it("shows the selected status label once a status filter is applied", async () => {
    const user = userEvent.setup();
    renderRequests();

    await screen.findByText("Showing 1–1");
    await user.selectOptions(screen.getByLabelText("Filter by status"), "in_progress");

    await waitFor(() =>
      expect(screen.getByText("Applied: Status: Active")).toBeInTheDocument(),
    );
    await waitFor(() =>
      expect(mockGetRequests).toHaveBeenCalledWith(expect.objectContaining({ status: "in_progress" })),
    );
  });

  it("does not let a changed-but-unsubmitted draft replace the submitted criteria in the line or live heading", async () => {
    const user = userEvent.setup();
    renderRequests();

    await screen.findByText("Showing 1–1");
    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "smith{Enter}");
    await waitFor(() =>
      expect(screen.getByText("Applied: Search “smith” · Status: All active statuses")).toBeInTheDocument(),
    );
    mockGetRequests.mockClear();

    fireEvent.change(searchInput, { target: { value: "smithers" } });

    expect(screen.getByText("Applied: Search “smith” · Status: All active statuses")).toBeInTheDocument();
    expect(screen.queryByText(/smithers/)).not.toBeInTheDocument();
    expect(mockGetRequests).not.toHaveBeenCalled();
  });

  it("renders filtered-empty copy distinct from the true-empty copy, and Clear filters recovers", async () => {
    const user = userEvent.setup();
    renderRequests();

    await screen.findByText("Showing 1–1");
    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "nomatch{Enter}");

    await screen.findByText("No matching requests for “nomatch”");
    expect(screen.getByText(/No requests match Search “nomatch”/)).toBeInTheDocument();
    expect(screen.queryByText("All promises covered")).not.toBeInTheDocument();

    mockGetRequests.mockClear();
    await user.click(screen.getByRole("button", { name: "Clear filters" }));

    await waitFor(() =>
      expect(mockGetRequests).toHaveBeenCalledWith(
        expect.objectContaining({ q: undefined, status: undefined, cursor: undefined }),
      ),
    );
    expect(screen.getByLabelText("Search requests")).toHaveFocus();
    expect(screen.queryByText(/^Applied:/)).not.toBeInTheDocument();
  });

  it("history wording includes search/scope/date but never status, and its recovery preserves scope/date", async () => {
    const user = userEvent.setup();
    renderRequests();

    await user.click(await screen.findByRole("button", { name: "History" }));
    expect(screen.queryByLabelText("Filter by status")).not.toBeInTheDocument();

    const scopeGroup = screen.getByRole("group", { name: "History scope" });
    await user.click(within(scopeGroup).getByRole("button", { name: "Closed" }));

    await waitFor(() =>
      expect(screen.getByText("Applied: Closed history · All time")).toBeInTheDocument(),
    );

    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "nomatch{Enter}");

    await screen.findByText("No matching history for “nomatch” · Closed history · All time");
    expect(screen.queryByText(/Status:/)).not.toBeInTheDocument();

    mockGetRequests.mockClear();
    await user.click(screen.getByRole("button", { name: "Clear filters" }));

    await waitFor(() =>
      expect(mockGetRequests).toHaveBeenCalledWith(
        expect.objectContaining({ view: "closed_history", q: undefined, cursor: undefined }),
      ),
    );
    await waitFor(() =>
      expect(screen.getByText("Applied: Closed history · All time")).toBeInTheDocument(),
    );
  });

  it("result and empty announcements carry criteria with a truthful range and no fabricated total", async () => {
    const user = userEvent.setup();
    renderRequests();

    await screen.findByText("Showing 1–1");
    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "smith{Enter}");

    // sampleRow query returns [] once q is set (per the mock above), so assert the empty
    // announcement here and the truthful-range assertion via the no-criteria case above.
    await screen.findByText("No matching requests for “smith”");
    expect(screen.queryByText(/of \d/)).not.toBeInTheDocument();
  });

  it("carries submitted criteria into a truthful non-empty filtered range, not just the empty case", async () => {
    const user = userEvent.setup();
    // Override: a submitted search that still resolves a row, so the live heading has both a
    // real range and criteria to combine — the earlier test only proves the empty-state case.
    mockGetRequests.mockImplementation(() => Promise.resolve(listResult([sampleRow])));
    renderRequests();

    await screen.findByText("Showing 1–1");
    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "smith{Enter}");

    await waitFor(() => expect(screen.getByText("Showing 1–1 results for “smith”")).toBeInTheDocument());
    expect(screen.queryByText(/of \d/)).not.toBeInTheDocument();
  });

  it("operational Clear filters resets an applied status filter, not just search", async () => {
    const user = userEvent.setup();
    renderRequests();

    await screen.findByText("Showing 1–1");
    await user.selectOptions(screen.getByLabelText("Filter by status"), "in_progress");
    await screen.findByText("No matching requests");
    mockGetRequests.mockClear();

    await user.click(screen.getByRole("button", { name: "Clear filters" }));

    await waitFor(() =>
      expect(mockGetRequests).toHaveBeenCalledWith(
        expect.objectContaining({ q: undefined, status: undefined, cursor: undefined }),
      ),
    );
    expect(screen.getByLabelText("Filter by status")).toHaveValue("");
  });

  it("history Clear filters preserves a non-default date scope, not just history scope", async () => {
    const user = userEvent.setup();
    renderRequests();

    await user.click(await screen.findByRole("button", { name: "History" }));
    const scopeGroup = screen.getByRole("group", { name: "History scope" });
    await user.click(within(scopeGroup).getByRole("button", { name: "Closed" }));
    const dateGroup = screen.getByRole("group", { name: "Date range" });
    await user.click(within(dateGroup).getByRole("button", { name: "Yesterday" }));

    await waitFor(() =>
      expect(mockGetRequests).toHaveBeenCalledWith(
        expect.objectContaining({ view: "closed_history", closedShortcut: "yesterday" }),
      ),
    );

    const searchInput = screen.getByLabelText("Search requests");
    await user.type(searchInput, "nomatch{Enter}");
    await screen.findByText("No matching history");
    mockGetRequests.mockClear();

    await user.click(screen.getByRole("button", { name: "Clear filters" }));

    await waitFor(() =>
      expect(mockGetRequests).toHaveBeenCalledWith(
        expect.objectContaining({ view: "closed_history", closedShortcut: "yesterday", q: undefined }),
      ),
    );
  });
});
