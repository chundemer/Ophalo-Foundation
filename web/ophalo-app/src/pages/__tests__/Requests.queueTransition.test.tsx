import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockRequestSummaries, mockViewCounts } from "../../mocks/fixtures";
import type { KeepRequestListResult, KeepSetupResult } from "../../lib/apiClient";

// GAP-041: a first-time queue selection must keep the header/tab bar/search row stable
// and show a fixed queue-agnostic skeleton, never blank the whole region or reuse the
// previous queue's real rows. The tab bar also needs a real roving-tabindex keyboard
// pattern (Left/Right/Home/End move focus+selection; Enter/Space stay native).
// GAP-026: the search box needs a visible, keyboard-usable clear affordance.

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

function listResult(requests: KeepRequestListResult["requests"]): KeepRequestListResult {
  return {
    requests,
    pageInfo: { limit: 50, hasMore: false, nextCursor: null },
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
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
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
});

describe("Requests — GAP-041 queue-transition stability", () => {
  it("shows a fixed skeleton on first load without blanking the header, tabs, or search row", async () => {
    const gate = deferred<KeepRequestListResult>();
    mockGetRequests.mockReturnValue(gate.promise);
    renderRequests();

    expect(await screen.findByRole("heading", { name: "Requests for Acme Plumbing" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /All work/ })).toBeInTheDocument();
    expect(screen.getByLabelText("Search requests")).toBeInTheDocument();
    const region = screen.getByRole("region", { name: "All work requests" });
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
    renderRequests();
    await screen.findByText(mockRequestSummaries[0].customerName);

    fireEvent.click(screen.getByRole("tab", { name: /Needs Attention/ }));
    await screen.findByText(mockRequestSummaries[1].customerName);

    fireEvent.click(screen.getByRole("tab", { name: /All work/ }));
    // Cached — must be present synchronously, no skeleton frame in between.
    expect(screen.getByText(mockRequestSummaries[0].customerName)).toBeInTheDocument();
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
