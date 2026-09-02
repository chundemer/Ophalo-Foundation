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
  requestQuery: { data: { customerName: "Jane Doe", referenceCode: "R-100", status: "InProgress" } as Record<string, unknown> },
  submittedVisit: vi.fn().mockReturnValue(READ_ONLY_VISIT),
  financialReview,
};

vi.mock("../useActualWorkWorkspace", () => ({ useActualWorkWorkspace: () => workspace }));
vi.mock("../ActualWorkComposer", () => ({
  ActualWorkComposer: ({ draft }: { draft: { id: string } }) => (
    <div>SUCCESSOR COMPOSER {draft.id}</div>
  ),
}));
// The shared Contact customer drawer is exercised by its own suite; here we only assert the
// workspace opens the real component with the right initial channel.
vi.mock("../../RequestDetail", () => ({
  LogContactModal: ({ initialChannel, initialDirection }: Record<string, string>) => (
    <div>
      CONTACT DRAWER {initialDirection}/{initialChannel}
    </div>
  ),
}));

const DEFAULT_REQUEST = { customerName: "Jane Doe", referenceCode: "R-100", status: "InProgress" };

let meRole = "owner";
// BL138 Slice 2: the request-scoped pending-review projection the wide workspace now composes.
// Default is empty so existing office-region assertions are unaffected (switcher stays hidden).
let pendingReviewsResult: { count: number; items: unknown[] } = { count: 0, items: [] };
vi.mock("../../../lib/apiClient", async () => {
  const actual = await vi.importActual<typeof import("../../../lib/apiClient")>("../../../lib/apiClient");
  return {
    ...actual,
    api: {
      ...actual.api,
      getMe: vi.fn().mockImplementation(() => Promise.resolve({ accountUserId: "u1", accountRole: meRole })),
      getActualWorkPendingReviewsForRequest: vi.fn().mockImplementation(() => Promise.resolve(pendingReviewsResult)),
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
        onSwitchVisit={vi.fn()}
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
        onSwitchVisit={vi.fn()}
      />
    </QueryClientProvider>
  );
}

describe("ActualWorkWorkspacePage — 4f-ii office region", () => {
  beforeEach(() => {
    meRole = "owner";
    pendingReviewsResult = { count: 0, items: [] };
    stubWideMatchMedia();
    financialReview.state = { status: "loaded", visits: [financialDetail()] };
    financialReview.review.mockResolvedValue({ kind: "success" });
    financialReview.replace.mockResolvedValue(ok);
    workspace.requestQuery.data = { ...DEFAULT_REQUEST };
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

  it("BL138 Slice 2: shows the pending-visit switcher only for 2+ pending visits and reloads it after a mutation", async () => {
    const { api } = await import("../../../lib/apiClient");
    pendingReviewsResult = {
      count: 2,
      items: [
        { actualWorkId: "aw-42", submittedAtUtc: "2026-08-20T10:00:00Z", lineCount: 1, recorderDisplayName: "Dana", reviewStatus: "ReadyToReview" },
        { actualWorkId: "aw-77", submittedAtUtc: "2026-08-21T10:00:00Z", lineCount: 2, recorderDisplayName: "Dana", reviewStatus: "NeedsCostPriceResolution" },
      ],
    };
    financialReview.review.mockResolvedValue({ kind: "success" });
    renderPage();
    expect(
      await screen.findByRole("navigation", { name: /pending financial reviews on this request/i }),
    ).toBeInTheDocument();
    const calls = (api.getActualWorkPendingReviewsForRequest as ReturnType<typeof vi.fn>).mock.calls.length;
    await userEvent.click(await screen.findByRole("button", { name: /Complete internal financial review/i }));
    await waitFor(() =>
      expect((api.getActualWorkPendingReviewsForRequest as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(calls),
    );
  });

  it("BL138 Slice 2: no switcher for a single pending visit", async () => {
    pendingReviewsResult = {
      count: 1,
      items: [
        { actualWorkId: "aw-42", submittedAtUtc: "2026-08-20T10:00:00Z", lineCount: 1, recorderDisplayName: "Dana", reviewStatus: "ReadyToReview" },
      ],
    };
    renderPage();
    await screen.findByRole("button", { name: /Complete internal financial review/i });
    expect(screen.queryByRole("navigation", { name: /pending financial reviews on this request/i })).toBeNull();
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

  it("renders the financial-review workspace header: title, pending status, submitted metadata", async () => {
    renderPage();
    expect(
      await screen.findByRole("heading", { level: 1, name: "Actual Work Financial Review — Visit #1" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Pending review")).toBeInTheDocument();
    expect(screen.getByText(/Submitted/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Back to Request/i })).toBeInTheDocument();
    // Context rail carries the job + customer identity without competing with the totals.
    expect(screen.getByText("Jane Doe")).toBeInTheDocument();
    expect(screen.getAllByText(/R-100/).length).toBeGreaterThan(0);
  });

  it("colors margin totals semantically: healthy at/above 15%, thin below, negative under zero", async () => {
    const toneClass = (nodes: HTMLElement[]) => nodes.map((n) => n.className).join(" ");
    // Default detail: sales 100 / margin 60 → 60% → healthy.
    const { unmount } = renderPage();
    expect(toneClass(await screen.findAllByText("60.0%"))).toContain("ophalo-success");
    unmount();

    financialReview.state = {
      status: "loaded",
      visits: [financialDetail({ totalSalesPrice: 100, totalMargin: 5 })],
    };
    const thin = renderPage();
    expect(toneClass(await screen.findAllByText("5.0%"))).toContain("ophalo-attention");
    thin.unmount();

    financialReview.state = {
      status: "loaded",
      visits: [financialDetail({ totalSalesPrice: 100, totalMargin: -10 })],
    };
    renderPage();
    expect(toneClass(await screen.findAllByText("-10.0%"))).toContain("ophalo-danger");
  });

  it("renders the line-item breakdown as a table with a totals row", async () => {
    renderPage();
    expect(await screen.findByRole("columnheader", { name: /Item description/i })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: /Margin \(%\)/i })).toBeInTheDocument();
    expect(screen.getAllByText("Totals").length).toBeGreaterThan(0);
  });

  it("shows the missing-cost warning and no totals row when financial data is incomplete", async () => {
    financialReview.state = {
      status: "loaded",
      visits: [
        financialDetail({
          hasIncompleteFinancialData: true,
          totalSalesPrice: null,
          totalMargin: null,
          lines: [
            {
              id: "l1",
              actualQuantity: 1,
              displayNameSnapshot: "Capacitor",
              isFinancialDataComplete: false,
              sellPriceResolved: false,
              directCostResolved: false,
              lineSalesTotal: null,
              lineStandardExpectedDirectCostTotal: null,
              lineMargin: null,
            },
          ],
          blockers: [
            { lineId: "l1", displayNameSnapshot: "Capacitor", sellPriceMissing: true, standardExpectedDirectCostMissing: false },
          ],
        }),
      ],
    };
    renderPage();
    expect(await screen.findByText(/Missing cost data/i)).toBeInTheDocument();
    expect(screen.queryByRole("cell", { name: "Totals" })).not.toBeInTheDocument();
    // Per-line badge in the margin cell + fail-closed disable of the primary action.
    expect(screen.getAllByText("Resolve cost").length).toBeGreaterThan(0);
    expect(
      screen.getByRole("button", { name: /Complete internal financial review/i }),
    ).toBeDisabled();
    expect(
      screen.getByText(/Resolve every line’s missing price or cost/i),
    ).toBeInTheDocument();
  });

  it("keeps the primary action enabled once every line has complete financial data", async () => {
    renderPage();
    expect(
      await screen.findByRole("button", { name: /Complete internal financial review/i }),
    ).toBeEnabled();
  });

  it("routes Call / Text / Email in the context card into the shared Contact customer drawer", async () => {
    workspace.requestQuery.data = {
      ...DEFAULT_REQUEST,
      customerPhone: "5125550177",
      customerEmail: "c@example.com",
    };
    renderPage();
    await screen.findByText("Internal financial review");

    await userEvent.click(screen.getByRole("button", { name: "Call" }));
    expect(await screen.findByText("CONTACT DRAWER outbound/phone")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Text" }));
    expect(await screen.findByText("CONTACT DRAWER outbound/sms")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Email" }));
    expect(await screen.findByText("CONTACT DRAWER outbound/email")).toBeInTheDocument();
  });

  it("hides Call / Text when no phone and Email when no email address is on file", async () => {
    workspace.requestQuery.data = { ...DEFAULT_REQUEST, customerEmail: "c@example.com" };
    renderPage();
    await screen.findByText("Internal financial review");
    expect(screen.queryByRole("button", { name: "Call" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Text" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Email" })).toBeInTheDocument();
  });

  it("tints the margin KPI cards by tone and leaves sales / cost cards neutral", async () => {
    renderPage();
    const marginPctValue = (await screen.findAllByText("60.0%")).find((n) =>
      n.className.includes("text-lg"),
    );
    expect(marginPctValue?.closest("div")?.className).toContain("ophalo-success-bg");
    expect(screen.getByText("Total sales price").closest("div")?.className).toContain(
      "bg-[var(--ophalo-card)]",
    );
  });
});
