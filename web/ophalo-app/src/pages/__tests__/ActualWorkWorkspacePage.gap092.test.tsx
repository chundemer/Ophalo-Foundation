import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActualWorkWorkspacePage } from "../ActualWorkWorkspacePage";

// GAP-092 3: proves the ActualWorkWorkspacePage page-entry path actually supplies the resolved
// business timezone to its two ActualWorkReviewCard/ActualWorkFinancialReviewWorkspace render
// sites — not just that those components render correctly when given one directly (that's
// covered in their own test files). Both children are mocked to capture the exact timeZone prop
// they receive from the real page.

const READ_ONLY_VISIT = {
  id: "aw-42",
  status: "Submitted",
  outcome: null,
  completionNote: null,
  visitNote: null,
  submittedAtUtc: "2026-08-20T10:00:00Z",
  superseded: false,
  lines: [],
};

function financialDetail(overrides: Record<string, unknown> = {}) {
  return {
    id: "aw-42",
    submittedAtUtc: "2026-08-20T10:00:00Z",
    reviewedAtUtc: null,
    reviewedByDisplayName: null,
    reviewNote: null,
    outcome: null,
    completionNote: null,
    concurrencyVersion: "v1",
    hasIncompleteFinancialData: false,
    hasNoChargeDisposition: false,
    totalSalesPrice: 100,
    totalStandardExpectedDirectCost: 40,
    totalMargin: 60,
    lines: [],
    blockers: [],
    ...overrides,
  };
}

const financialReview = {
  state: { status: "loaded", visits: [financialDetail()] } as Record<string, unknown>,
  retry: vi.fn(),
  review: vi.fn(),
  resolveLine: vi.fn(),
  recordNoChargeDisposition: vi.fn(),
  replace: vi.fn(),
  mutatingVisitIds: new Set<string>(),
  isVisitMutating: () => false,
};

const workspace = {
  capture: { state: { status: "no-draft" } as Record<string, unknown>, createDraft: vi.fn(), refetchDraft: vi.fn(), reconcileAfterConflict: vi.fn(), clearConflictNotice: vi.fn(), retryReconciliation: vi.fn(), markSubmitted: vi.fn(), setDefaultPerformer: vi.fn(), setVisitNote: vi.fn(), setZeroLineDisposition: vi.fn(), handOffToOffice: vi.fn(), conflictNotice: null, replacementCorrection: false },
  history: { state: { status: "loaded", submittedVisits: [READ_ONLY_VISIT] }, retry: vi.fn() },
  requestQuery: { data: { customerName: "Jane Doe", referenceCode: "R-100", status: "InProgress" } as Record<string, unknown> },
  submittedVisit: vi.fn().mockReturnValue(READ_ONLY_VISIT),
  financialReview,
};

vi.mock("../request-detail/useActualWorkWorkspace", () => ({ useActualWorkWorkspace: () => workspace }));
vi.mock("../request-detail/ActualWorkComposer", () => ({ ActualWorkComposer: () => <div>MOCK COMPOSER</div> }));
vi.mock("../request-detail/LogContactModal", () => ({ LogContactModal: () => <div>CONTACT DRAWER</div> }));

let capturedFinancialWorkspaceTimeZone: string | null | undefined = "not-called";
vi.mock("../request-detail/ActualWorkFinancialReviewWorkspace", () => ({
  ActualWorkFinancialReviewWorkspace: ({ timeZone }: { timeZone: string | null }) => {
    capturedFinancialWorkspaceTimeZone = timeZone;
    return <div data-testid="captured-financial-workspace">captured</div>;
  },
}));

let capturedReviewCardTimeZone: string | null | undefined = "not-called";
vi.mock("../request-detail/ActualWorkReviewCard", () => ({
  ActualWorkReviewCard: ({ timeZone }: { timeZone: string | null }) => {
    capturedReviewCardTimeZone = timeZone;
    return <div data-testid="captured-review-card">captured</div>;
  },
}));

let meTimeZone: string | null = "America/Los_Angeles";
vi.mock("../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../lib/apiClient")>("../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getMe: vi.fn().mockImplementation(() =>
        Promise.resolve({ accountUserId: "u1", accountRole: "owner", timeZone: meTimeZone }),
      ),
      getActualWorkPendingReviewsForRequest: vi.fn().mockResolvedValue({ count: 0, items: [] }),
    },
  };
});

const originalMatchMedia = window.matchMedia;
function stubWideMatchMedia() {
  window.matchMedia = ((query: string) => ({
    matches: true,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia;
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <ActualWorkWorkspacePage requestId="req-1" visit="aw-42" onExit={vi.fn()} onResolvedToDraft={vi.fn()} onSwitchVisit={vi.fn()} />
    </QueryClientProvider>,
  );
}

describe("ActualWorkWorkspacePage — GAP-092 business-timezone page-entry wiring", () => {
  beforeEach(() => {
    stubWideMatchMedia();
    meTimeZone = "America/Los_Angeles";
    capturedFinancialWorkspaceTimeZone = "not-called";
    capturedReviewCardTimeZone = "not-called";
    workspace.submittedVisit.mockReturnValue(READ_ONLY_VISIT);
    financialReview.state = { status: "loaded", visits: [financialDetail()] };
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  it("passes the resolved business zone to ActualWorkFinancialReviewWorkspace (the wide, non-superseded, loaded-review render site)", async () => {
    renderPage();
    await screen.findByTestId("captured-financial-workspace");
    expect(capturedFinancialWorkspaceTimeZone).toBe("America/Los_Angeles");
  });

  it("passes null (never a device-local guess) to ActualWorkFinancialReviewWorkspace while ['me'] has not yet resolved a zone", async () => {
    meTimeZone = null;
    renderPage();
    await screen.findByTestId("captured-financial-workspace");
    expect(capturedFinancialWorkspaceTimeZone).toBeNull();
  });

  it("passes the resolved business zone to the second render site — ActualWorkReviewCard inside ReadOnlyVisit's office region, when financial review is not in the loaded state", async () => {
    // Financial detail not yet loaded (or errored) falls through to the read-only visit render,
    // whose office region still composes the real ActualWorkReviewCard against `financialReview`.
    financialReview.state = { status: "loading" };
    renderPage();
    await screen.findByTestId("captured-review-card");
    expect(capturedReviewCardTimeZone).toBe("America/Los_Angeles");
  });
});
