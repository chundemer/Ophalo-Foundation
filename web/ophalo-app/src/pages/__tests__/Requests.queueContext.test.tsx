import { useCallback, useRef, useState } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Requests } from "../Requests";
import { mockViewCounts } from "../../mocks/fixtures";
import type {
  AccountRole,
  KeepRequestListResult,
  KeepSetupResult,
  KeepRequestViewCounts,
} from "../../lib/apiClient";

// Session 3.5: a quiet, truthful description for each operational queue (My Work, Needs
// Attention, Watching, Ready to Close, Feedback Review) since All Work's command-center
// subtitle is absent there; and count continuity — the tab-bar/summary-pill viewCounts must
// never be replaced with null while an unvisited queue is still loading.
// UI-004 amendment (2026-08-21): Watching lives behind the Views disclosure; Ready to Close/
// Feedback Review/Actual Work Review live behind the Owner/Admin-only Office Review disclosure.

const mockGetRequests = vi.fn();
const mockGetAvailableRequests = vi.fn();
const mockGetGuidedSetup = vi.fn();
const mockGetSetup = vi.fn();
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

// Both Office Review members non-zero so they render as actionable (clickable) rows rather
// than collapsing into the quiet "No X" line — tests below need to select into them.
const officeReviewNonZeroCounts: KeepRequestViewCounts = {
  ...mockViewCounts,
  readyToClose: 1,
  feedbackReview: 1,
};

function listResult(view: string, viewCounts: KeepRequestViewCounts = officeReviewNonZeroCounts): KeepRequestListResult {
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

// App.tsx owns viewCounts as external state and feeds it back via onViewCountsUpdate (Session
// 3.5 first-visit count continuity); Office Review's readyToClose/feedbackReview inputs read
// this same prop, so tests need the same round trip rather than a static null/no-op.
function RequestsHarness({
  role,
  onViewCountsUpdate,
}: {
  role: AccountRole;
  onViewCountsUpdate?: (c: KeepRequestViewCounts | null) => void;
}) {
  const [viewCounts, setViewCounts] = useState<KeepRequestViewCounts | null>(null);
  const onUpdateRef = useRef(onViewCountsUpdate);
  onUpdateRef.current = onViewCountsUpdate;
  // App.tsx passes a useCallback-stable handler (see App.tsx:126) — a fresh identity per
  // render here would re-fire Requests.tsx's viewCounts effect on every unrelated re-render.
  const handleViewCountsUpdate = useCallback((c: KeepRequestViewCounts | null) => {
    setViewCounts(c);
    onUpdateRef.current?.(c);
  }, []);
  return (
    <Requests
      role={role}
      viewCounts={viewCounts}
      onViewCountsUpdate={handleViewCountsUpdate}
      onSelectRequest={() => {}}
      onNavigateSettings={() => {}}
      onStartCapture={() => {}}
    />
  );
}

function renderRequests(role: AccountRole = "owner", onViewCountsUpdate?: (c: KeepRequestViewCounts | null) => void) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RequestsHarness role={role} onViewCountsUpdate={onViewCountsUpdate} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockGetRequests.mockReset();
  mockGetAvailableRequests.mockReset();
  mockGetGuidedSetup.mockReset();
  mockGetSetup.mockReset();
  mockGetActualWorkReviewQueueCount.mockReset();
  mockGetAvailableRequests.mockResolvedValue({ requests: [], pageInfo: { limit: 50, hasMore: false, nextCursor: null } });
  mockGetGuidedSetup.mockResolvedValue(completeGuidedSetup);
  mockGetSetup.mockResolvedValue(mockBusinessSetup);
  mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 1 });
  mockGetRequests.mockImplementation((query: { view: string }) => Promise.resolve(listResult(query.view)));
});

async function openViews() {
  fireEvent.click(await screen.findByRole("button", { name: /^Views/ }));
}

async function openOfficeReview() {
  fireEvent.click(await screen.findByRole("button", { name: /^Office Review/ }));
}

describe("Requests — GAP-027-adjacent queue subtitles (session 3.5)", () => {
  it("shows the locked subtitle for every Owner/Admin queue tab/view/member, and none for All Work's own text is unaffected", async () => {
    renderRequests("owner");
    await screen.findByRole("tab", { name: /Needs Attention/ });

    fireEvent.click(screen.getByRole("tab", { name: /My Work/ }));
    await waitFor(() => expect(screen.getByText("Requests currently assigned to you.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: /Needs Attention/ }));
    await waitFor(() => expect(screen.getByText("Requests with customer promises needing attention now.")).toBeInTheDocument());

    await openViews();
    fireEvent.click(await screen.findByRole("button", { name: /Watching/ }));
    await waitFor(() => expect(screen.getByText("Requests you're watching.")).toBeInTheDocument());

    await openOfficeReview();
    fireEvent.click(await screen.findByRole("button", { name: /Ready to Close/ }));
    await waitFor(() => expect(screen.getByText("Resolved work ready for owner/admin closeout.")).toBeInTheDocument());

    await openOfficeReview();
    fireEvent.click(await screen.findByRole("button", { name: /Feedback Review/ }));
    await waitFor(() => expect(screen.getByText("Closed requests with customer feedback awaiting review.")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: /All Work/ }));
    await waitFor(() =>
      expect(screen.getByText(
        "Open requests and feedback requiring review, ranked with customer promises needing attention first.",
      )).toBeInTheDocument(),
    );
  });

  it("never renders the All Work subtitle on a non-All-Work tab/view/member", async () => {
    const ALL_WORK_SUBTITLE =
      "Open requests and feedback requiring review, ranked with customer promises needing attention first.";
    renderRequests("owner");
    await screen.findByRole("tab", { name: /Needs Attention/ });

    fireEvent.click(screen.getByRole("tab", { name: /My Work/ }));
    await waitFor(() => expect(screen.queryByText(ALL_WORK_SUBTITLE)).not.toBeInTheDocument());

    fireEvent.click(screen.getByRole("tab", { name: /Needs Attention/ }));
    await waitFor(() => expect(screen.queryByText(ALL_WORK_SUBTITLE)).not.toBeInTheDocument());

    await openViews();
    fireEvent.click(await screen.findByRole("button", { name: /Watching/ }));
    await waitFor(() => expect(screen.queryByText(ALL_WORK_SUBTITLE)).not.toBeInTheDocument());

    await openOfficeReview();
    fireEvent.click(await screen.findByRole("button", { name: /Ready to Close/ }));
    await waitFor(() => expect(screen.queryByText(ALL_WORK_SUBTITLE)).not.toBeInTheDocument());

    await openOfficeReview();
    fireEvent.click(await screen.findByRole("button", { name: /Feedback Review/ }));
    await waitFor(() => expect(screen.queryByText(ALL_WORK_SUBTITLE)).not.toBeInTheDocument());
  });

  it("shows the Operator-specific My Work subtitle, and the same Needs Attention subtitle; no Office Review", async () => {
    renderRequests("operator");
    await screen.findByRole("tab", { name: /My Work/ });

    fireEvent.click(screen.getByRole("tab", { name: /My Work/ }));
    await waitFor(() =>
      expect(screen.getByText("Your active customer promises — the requests assigned to you.")).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole("tab", { name: /Needs Attention/ }));
    await waitFor(() => expect(screen.getByText("Requests with customer promises needing attention now.")).toBeInTheDocument());

    expect(screen.queryByRole("tab", { name: /Ready to Close/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /Feedback Review/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Office Review/ })).not.toBeInTheDocument();
  });
});

describe("Requests — no duplicate empty-state heading (session 3.5 follow-up)", () => {
  it("My Work: has its own header subtitle, and renders exactly one visible empty-state heading", async () => {
    renderRequests("owner");
    await screen.findByRole("tab", { name: /Needs Attention/ });

    fireEvent.click(screen.getByRole("tab", { name: /My Work/ }));
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
    await screen.findByRole("tab", { name: /Needs Attention/ });

    await openOfficeReview();
    fireEvent.click(await screen.findByRole("button", { name: /Feedback Review/ }));
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
    const secondCounts: KeepRequestViewCounts = { ...officeReviewNonZeroCounts, default: 9 };
    const d = deferred<KeepRequestListResult>();

    // UI-004 amendment: the landing view is now Needs Attention, so that one must resolve
    // immediately here — the stall is on a tab visited afterward (All Work) instead.
    mockGetRequests.mockImplementation((query: { view: string }) =>
      query.view === "default" ? d.promise : Promise.resolve(listResult(query.view)),
    );

    renderRequests("owner", (c) => updates.push(c));
    await screen.findByRole("tab", { name: /Needs Attention/ });
    await waitFor(() => expect(updates.length).toBeGreaterThan(0));
    expect(updates.every((c) => c !== null)).toBe(true);
    updates.length = 0;

    fireEvent.click(screen.getByRole("tab", { name: /All Work/ }));

    // Still loading — no update at all, and definitely never a null overwrite.
    await new Promise((r) => setTimeout(r, 0));
    expect(updates).toEqual([]);

    d.resolve(listResult("default", secondCounts));

    await waitFor(() => expect(updates).toContainEqual(secondCounts));
    expect(updates.every((c) => c !== null)).toBe(true);
  });
});

describe("Requests — UI-004 amendment Office Review disclosure exclusivity and a11y", () => {
  it("opening Views closes Office Review, and vice versa; each returns focus to its own trigger on close", async () => {
    renderRequests("owner");
    await screen.findByRole("tab", { name: /Needs Attention/ });

    const officeReviewTrigger = await screen.findByRole("button", { name: /^Office Review/ });
    fireEvent.click(officeReviewTrigger);
    expect(await screen.findByRole("button", { name: /Ready to Close/ })).toBeInTheDocument();

    const viewsTrigger = await screen.findByRole("button", { name: /^Views/ });
    fireEvent.click(viewsTrigger);
    expect(await screen.findByRole("button", { name: /Watching/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Ready to Close/ })).not.toBeInTheDocument();

    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("button", { name: /Watching/ })).not.toBeInTheDocument());
    expect(document.activeElement).toBe(viewsTrigger);
  });

  it("collapses a zero-count Office Review member into one quiet, non-interactive line", async () => {
    mockGetRequests.mockImplementation((query: { view: string }) =>
      Promise.resolve(listResult(query.view, { ...mockViewCounts, readyToClose: 0, feedbackReview: 1 })),
    );
    renderRequests("owner");
    await screen.findByRole("tab", { name: /Needs Attention/ });

    await openOfficeReview();
    expect(await screen.findByRole("button", { name: /Feedback Review/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Ready to Close/ })).not.toBeInTheDocument();
    expect(screen.getByText("No Ready to Close")).toBeInTheDocument();
  });

  it("shows a Retry affordance (not a perpetual loading placeholder) when the Actual Work Review count fails, and recovers on retry", async () => {
    mockGetActualWorkReviewQueueCount.mockRejectedValue(new Error("network error"));
    renderRequests("owner");

    const retryButton = await screen.findByRole("button", { name: /couldn.t load counts/i });

    mockGetActualWorkReviewQueueCount.mockResolvedValue({ count: 1 });
    fireEvent.click(retryButton);

    await waitFor(() => expect(screen.getByRole("button", { name: /^Office Review/ })).toBeInTheDocument());
  });
});
