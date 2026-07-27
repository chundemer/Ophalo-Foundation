import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockRequestSummaries, mockViewCounts } from "../../mocks/fixtures";
import type { KeepRequestListResult, KeepSetupResult } from "../../lib/apiClient";

// GAP-043: retain the existing 50-row cursor model. Layer on a truthful numbered range
// ("Showing 1–50", never "of N" — there is no server total), an explicit end-of-results
// state, and post-page-change scroll+focus placement so keyboard/screen-reader users
// aren't stranded on a disabled or removed Previous/Next control.

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

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => { resolve = r; });
  return { promise, resolve };
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

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  // jsdom does not implement Element.scrollTo — the component already guards for its
  // absence, but stub it here so the "scroll on page change" assertion has something to spy on.
  HTMLElement.prototype.scrollTo = vi.fn();
});

const pageOneRow = mockRequestSummaries.find((r) => r.id === "mock-req-001")!;
const pageTwoRow = mockRequestSummaries.find((r) => r.id === "mock-req-002")!;

function listResult(
  requests: KeepRequestListResult["requests"],
  pageInfo: KeepRequestListResult["pageInfo"],
): KeepRequestListResult {
  return {
    requests,
    pageInfo,
    viewCounts: mockViewCounts,
    listContext: { view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false },
  };
}

describe("Requests — GAP-043 pagination affordances", () => {
  it("shows a truthful numbered range, never a fabricated total", async () => {
    mockGetRequests.mockResolvedValue(
      listResult([pageOneRow], { limit: 50, hasMore: true, nextCursor: "cursor-2" }),
    );
    renderRequests();

    expect(await screen.findByText("Showing 1–1")).toBeInTheDocument();
    expect(screen.queryByText(/of \d/)).not.toBeInTheDocument();
  });

  it("moves to page two on Next, updates the range, scrolls, and focuses the range heading", async () => {
    mockGetRequests.mockImplementation((query: { cursor?: string }) =>
      Promise.resolve(
        query.cursor === "cursor-2"
          ? listResult([pageTwoRow], { limit: 50, hasMore: false, nextCursor: null })
          : listResult([pageOneRow], { limit: 50, hasMore: true, nextCursor: "cursor-2" }),
      ),
    );
    renderRequests();

    await screen.findByText("Showing 1–1");
    const scrollSpy = HTMLElement.prototype.scrollTo as unknown as ReturnType<typeof vi.fn>;
    scrollSpy.mockClear();

    const nextButton = screen.getByRole("button", { name: /Next/ });
    nextButton.click();

    await waitFor(() => expect(screen.getByText("Showing 51–51")).toBeInTheDocument());
    expect(scrollSpy).toHaveBeenCalledWith(expect.objectContaining({ top: 0 }));
    expect(document.activeElement).toHaveAttribute("tabindex", "-1");
    expect(document.activeElement).toHaveTextContent("Showing 51–51");
    expect(screen.getByText("End of results")).toBeInTheDocument();

    const prevButton = screen.getByRole("button", { name: /Previous/ });
    expect(prevButton).not.toBeDisabled();
  });

  it("does not focus the heading until the new page has actually rendered", async () => {
    const gate = deferred<KeepRequestListResult>();
    mockGetRequests.mockResolvedValueOnce(
      listResult([pageOneRow], { limit: 50, hasMore: true, nextCursor: "cursor-2" }),
    );
    renderRequests();
    await screen.findByText("Showing 1–1");

    mockGetRequests.mockReturnValueOnce(gate.promise);
    const nextButton = screen.getByRole("button", { name: /Next/ });
    nextButton.click();

    // Still mid-flight: the old range heading may still show, but it must not hold focus —
    // focusing it now would announce the stale "Showing 1–1" range as if it were page two.
    await waitFor(() => expect(screen.getByRole("region")).toHaveAttribute("aria-busy", "true"));
    expect(document.activeElement).not.toHaveTextContent("Showing 1–1");
    expect(document.activeElement?.tagName).not.toBe("H2");

    gate.resolve(listResult([pageTwoRow], { limit: 50, hasMore: false, nextCursor: null }));

    await waitFor(() => expect(screen.getByText("Showing 51–51")).toBeInTheDocument());
    expect(document.activeElement).toHaveTextContent("Showing 51–51");
  });

  it("does not render a pager at all on a single, complete first page", async () => {
    mockGetRequests.mockResolvedValue(
      listResult([pageOneRow], { limit: 50, hasMore: false, nextCursor: null }),
    );
    renderRequests();

    await screen.findByText("Showing 1–1");
    expect(screen.queryByRole("button", { name: /Next/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Previous/ })).not.toBeInTheDocument();
  });
});
