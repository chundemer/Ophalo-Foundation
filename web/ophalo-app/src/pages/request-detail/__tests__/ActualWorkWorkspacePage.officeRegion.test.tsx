import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ActualWorkWorkspacePage } from "../../ActualWorkWorkspacePage";
import type { FinancialReviewOutcome } from "../useActualWorkFinancialReview";

// BL136 4f-ii: the capability-gated office region on the workspace read-only view — the existing
// `ActualWorkReviewCard` (real, not mocked here) composed against `useActualWorkWorkspace`'s
// `financialReview`, scoped to the one submitted visit.

const READ_ONLY_VISIT = {
  id: "aw-42",
  status: "Submitted",
  outcome: null,
  completionNote: null,
  visitNote: null,
  submittedAtUtc: "2026-08-20T10:00:00Z",
  superseded: false,
  lines: [
    {
      id: "l1",
      displayNameSnapshot: "Capacitor",
      unitOfMeasureSnapshot: "each",
      actualQuantity: 1,
      note: null,
      performedByAccountUserId: "u-tech",
      performerDisplayName: "Dana Tech",
    },
  ],
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
    lines: [
      {
        id: "l1",
        actualQuantity: 1,
        displayNameSnapshot: "Capacitor",
        isFinancialDataComplete: true,
        sellPriceResolved: false,
        directCostResolved: false,
        lineSalesTotal: 100,
        lineStandardExpectedDirectCostTotal: 40,
        lineMargin: 60,
      },
    ],
    blockers: [],
    ...overrides,
  };
}

const ok: FinancialReviewOutcome = { kind: "success" };
const financialReview = {
  state: { status: "loaded", visits: [financialDetail()] } as Record<string, unknown>,
  retry: vi.fn(),
  review: vi.fn((): Promise<FinancialReviewOutcome> => Promise.resolve(ok)),
  resolveLine: vi.fn((): Promise<FinancialReviewOutcome> => Promise.resolve(ok)),
  recordNoChargeDisposition: vi.fn((): Promise<FinancialReviewOutcome> => Promise.resolve(ok)),
  replace: vi.fn((): Promise<FinancialReviewOutcome> => Promise.resolve(ok)),
  mutatingVisitIds: new Set<string>(),
  isVisitMutating: () => false,
};

const workspace = {
  capture: {
    state: { status: "no-draft" } as Record<string, unknown>,
    createDraft: vi.fn(),
    // A successful replacement re-probes the retained capture hook onto the successor Draft.
    refetchDraft: vi.fn().mockImplementation(async () => {
      workspace.capture.state = { status: "draft", draft: { id: "aw-99", status: "Draft" } };
    }),
    replacementCorrection: false,
    conflictNotice: null,
    reconcileAfterConflict: vi.fn(),
    clearConflictNotice: vi.fn(),
    retryReconciliation: vi.fn(),
    markSubmitted: vi.fn(),
    setDefaultPerformer: vi.fn(),
    setVisitNote: vi.fn(),
    setZeroLineDisposition: vi.fn(),
    handOffToOffice: vi.fn(),
  },
  history: { state: { status: "loaded", submittedVisits: [READ_ONLY_VISIT] }, retry: vi.fn() },
  requestQuery: { data: { customerName: "Jane Doe", referenceCode: "R-100", status: "InProgress" } },
  submittedVisit: vi.fn().mockReturnValue(READ_ONLY_VISIT),
  financialReview,
};

vi.mock("../useActualWorkWorkspace", () => ({ useActualWorkWorkspace: () => workspace }));
vi.mock("../ActualWorkComposer", () => ({
  ActualWorkComposer: ({ draft }: { draft: { id: string } }) => (
    <div>SUCCESSOR COMPOSER {draft.id}</div>
  ),
}));

let meRole = "owner";
vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getMe: vi.fn().mockImplementation(() => Promise.resolve({ accountUserId: "u1", accountRole: meRole })),
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
      <ActualWorkWorkspacePage
        requestId="req-1"
        visit="aw-42"
        onExit={vi.fn()}
        onResolvedToDraft={vi.fn()}
      />
    </QueryClientProvider>,
  );
}

// Mirrors App.tsx: `onResolvedToDraft` swaps the route segment from `:visitId` to `draft`, keeping
// this page instance mounted.
function Harness() {
  const [visit, setVisit] = useState<"aw-42" | "draft">("aw-42");
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <ActualWorkWorkspacePage
        requestId="req-1"
        visit={visit}
        onExit={vi.fn()}
        onResolvedToDraft={() => setVisit("draft")}
      />
    </QueryClientProvider>
  );
}

describe("ActualWorkWorkspacePage — 4f-ii office region", () => {
  beforeEach(() => {
    meRole = "owner";
    stubWideMatchMedia();
    financialReview.state = { status: "loaded", visits: [financialDetail()] };
    financialReview.review.mockResolvedValue({ kind: "success" });
    financialReview.replace.mockResolvedValue(ok);
    workspace.submittedVisit.mockReturnValue(READ_ONLY_VISIT);
    workspace.capture.state = { status: "no-draft" };
    workspace.capture.refetchDraft.mockClear();
    vi.clearAllMocks();
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
  });

  it("hides the office region for a non-reviewer", async () => {
    meRole = "operator";
    renderPage();
    expect(await screen.findByText("Capacitor")).toBeInTheDocument();
    expect(screen.queryByText(/Internal financial review/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Complete internal financial review/i })).not.toBeInTheDocument();
  });

  it("shows the office region for an Owner: line-adjacent resolution + review controls", async () => {
    financialReview.state = {
      status: "loaded",
      visits: [
        financialDetail({
          hasIncompleteFinancialData: true,
          blockers: [
            {
              lineId: "l1",
              displayNameSnapshot: "Capacitor",
              sellPriceMissing: true,
              standardExpectedDirectCostMissing: false,
            },
          ],
        }),
      ],
    };
    renderPage();
    expect(await screen.findByText("Internal financial review")).toBeInTheDocument();
    expect(screen.getByText(/Resolve missing price · Capacitor/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Complete internal financial review/i })).toBeInTheDocument();
    expect(screen.getByText("Correct this visit")).toBeInTheDocument();
  });

  it("renders a reviewed visit read-only (no resolution or review controls)", async () => {
    financialReview.state = {
      status: "loaded",
      visits: [financialDetail({ reviewedAtUtc: "2026-08-21T09:00:00Z", reviewedByDisplayName: "Ada Owner" })],
    };
    renderPage();
    expect(await screen.findByText("Internal financial review")).toBeInTheDocument();
    expect(screen.getAllByText(/Financial review completed/).length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /Complete internal financial review/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/Resolve missing/i)).not.toBeInTheDocument();
  });

  it("confirms after a successful review that the customer request status is unchanged", async () => {
    financialReview.review.mockResolvedValue({ kind: "success" });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: /Complete internal financial review/i }));
    expect(
      await screen.findByText(
        "Internal financial review completed. The customer request status is unchanged.",
      ),
    ).toBeInTheDocument();
  });

  it("surfaces a concurrency-reconcile outcome from the review mutation", async () => {
    financialReview.review.mockResolvedValue({ kind: "reconciled", code: undefined });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: /Complete internal financial review/i }));
    await waitFor(() =>
      expect(screen.getByText(/already reviewed or changed/i)).toBeInTheDocument(),
    );
  });

  it("'Correct this visit' re-probes the retained capture hook, then hosts the successor Draft composer", async () => {
    financialReview.replace.mockResolvedValue({ kind: "replaced", successorActualWorkId: "aw-99" });
    render(<Harness />);

    await userEvent.click(await screen.findByText("Correct this visit"));
    await userEvent.type(
      screen.getByLabelText(/Correction reason/i),
      "missed the second capacitor",
    );
    await userEvent.click(screen.getByRole("button", { name: /Start correction/i }));

    await waitFor(() => expect(workspace.capture.refetchDraft).toHaveBeenCalled());
    expect(await screen.findByText("SUCCESSOR COMPOSER aw-99")).toBeInTheDocument();
  });
});
