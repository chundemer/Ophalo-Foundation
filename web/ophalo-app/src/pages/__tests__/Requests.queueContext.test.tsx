import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockViewCounts } from "../../mocks/fixtures";
import type { AccountRole, KeepRequestListResult, KeepSetupResult, KeepRequestViewCounts } from "../../lib/apiClient";

// Session 3.5: a quiet, truthful subtitle for each operational queue (Assigned to Me/My
// Promises, Needs Attention, Watching, Ready to Close, Feedback Review) since All work's
// command-center subtitle is absent there; and count continuity — the tab-bar/summary-pill
// viewCounts must never be replaced with null while an unvisited queue is still loading.

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

function listResult(view: string, viewCounts: KeepRequestViewCounts = mockViewCounts): KeepRequestListResult {
  return {
    requests: [],
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

function renderRequests(role: AccountRole = "owner", onViewCountsUpdate: (c: KeepRequestViewCounts | null) => void = () => {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Requests
        role={role}
        viewCounts={null}
        onViewCountsUpdate={onViewCountsUpdate}
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
  mockGetRequests.mockImplementation((query: { view: string }) => Promise.resolve(listResult(query.view)));
});

describe("Requests — GAP-027-adjacent queue subtitles (session 3.5)", () => {
  it("shows the locked subtitle for every Owner/Admin queue tab, and none for All work's own text is unaffected", async () => {
    renderRequests("owner");
    await screen.findByText("No active work needs company-wide attention right now.");

    fireEvent.click(screen.getByRole("tab", { name: "Assigned to Me" }));
    await waitFor(() => expect(screen.getByText("Requests currently assigned to you.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: "Needs Attention" }));
    await waitFor(() => expect(screen.getByText("Requests with customer promises needing attention now.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: "Watching" }));
    await waitFor(() => expect(screen.getByText("Requests you're watching.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: "Ready to Close" }));
    await waitFor(() => expect(screen.getByText("Resolved work ready for owner/admin closeout.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: "Feedback Review" }));
    await waitFor(() => expect(screen.getByText("Closed requests with customer feedback awaiting review.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: "All work" }));
    await waitFor(() =>
      expect(screen.getByText(
        "Open requests and feedback requiring review, ranked with customer promises needing attention first.",
      )).toBeInTheDocument(),
    );
  });

  it("never renders the All work subtitle on a non-All-work tab", async () => {
    const ALL_WORK_SUBTITLE =
      "Open requests and feedback requiring review, ranked with customer promises needing attention first.";
    renderRequests("owner");
    await screen.findByText("No active work needs company-wide attention right now.");

    for (const tabName of ["Assigned to Me", "Needs Attention", "Watching", "Ready to Close", "Feedback Review"]) {
      fireEvent.click(screen.getByRole("tab", { name: tabName }));
      await waitFor(() => expect(screen.queryByText(ALL_WORK_SUBTITLE)).not.toBeInTheDocument());
    }
  });

  it("shows the Operator-specific My Promises subtitle, and the same Needs Attention/Watching subtitles", async () => {
    renderRequests("operator");
    await screen.findByRole("tab", { name: "My Promises" });

    fireEvent.click(screen.getByRole("tab", { name: "My Promises" }));
    await waitFor(() =>
      expect(screen.getByText("Your active customer promises — the requests assigned to you.")).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole("tab", { name: "Needs Attention" }));
    await waitFor(() => expect(screen.getByText("Requests with customer promises needing attention now.")).toBeInTheDocument());

    expect(screen.queryByRole("tab", { name: "Ready to Close" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Feedback Review" })).not.toBeInTheDocument();
  });
});

describe("Requests — no duplicate empty-state heading (session 3.5 follow-up)", () => {
  it("Assigned to Me: has its own header subtitle, and renders exactly one visible empty-state heading", async () => {
    renderRequests("owner");
    await screen.findByText("No active work needs company-wide attention right now.");

    fireEvent.click(screen.getByRole("tab", { name: "Assigned to Me" }));
    await waitFor(() => expect(screen.getByText("Requests currently assigned to you.")).toBeInTheDocument());

    const matches = await screen.findAllByText("Nothing assigned to you");
    expect(matches).toHaveLength(2);
    const visible = matches.filter((el) => !el.className.includes("sr-only"));
    const hidden = matches.filter((el) => el.className.includes("sr-only"));
    expect(visible).toHaveLength(1);
    expect(hidden).toHaveLength(1);
    // The sr-only copy is the list-region heading/focus target; the visible one is the
    // centered empty-state heading with its detail directly beneath.
    expect(hidden[0].tagName).toBe("H2");
    expect(screen.getByText("Active requests assigned to you will appear here.")).toBeInTheDocument();
  });

  it("Feedback Review: same no-duplication behavior for another operational queue", async () => {
    renderRequests("owner");
    await screen.findByText("No active work needs company-wide attention right now.");

    fireEvent.click(screen.getByRole("tab", { name: "Feedback Review" }));
    await waitFor(() =>
      expect(screen.getByText("Closed requests with customer feedback awaiting review.")).toBeInTheDocument(),
    );

    const matches = await screen.findAllByText("No customer feedback");
    expect(matches).toHaveLength(2);
    const visible = matches.filter((el) => !el.className.includes("sr-only"));
    const hidden = matches.filter((el) => el.className.includes("sr-only"));
    expect(visible).toHaveLength(1);
    expect(hidden).toHaveLength(1);
    expect(hidden[0].tagName).toBe("H2");
  });
});

describe("Requests — first-visit count continuity (session 3.5)", () => {
  it("never replaces a known viewCounts with null while an unvisited tab is still loading", async () => {
    const updates: (KeepRequestViewCounts | null)[] = [];
    const secondCounts: KeepRequestViewCounts = { ...mockViewCounts, needsAttention: 9 };
    const d = deferred<KeepRequestListResult>();

    mockGetRequests.mockImplementation((query: { view: string }) =>
      query.view === "needs_attention" ? d.promise : Promise.resolve(listResult(query.view)),
    );

    renderRequests("owner", (c) => updates.push(c));
    await screen.findByText("No active work needs company-wide attention right now.");
    expect(updates.length).toBeGreaterThan(0);
    expect(updates.every((c) => c !== null)).toBe(true);
    updates.length = 0;

    fireEvent.click(screen.getByRole("tab", { name: "Needs Attention" }));

    // Still loading — no update at all, and definitely never a null overwrite.
    await new Promise((r) => setTimeout(r, 0));
    expect(updates).toEqual([]);

    d.resolve(listResult("needs_attention", secondCounts));

    await waitFor(() => expect(updates).toContainEqual(secondCounts));
    expect(updates.every((c) => c !== null)).toBe(true);
  });
});
