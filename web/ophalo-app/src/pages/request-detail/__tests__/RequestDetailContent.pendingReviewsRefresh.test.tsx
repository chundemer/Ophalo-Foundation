import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestDetailContent } from "../RequestDetailContent";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// BL138 Slice 1B-client: the request-scoped "Pending financial reviews (N)" card is composed by
// RequestDetailContent, gated on canReviewActualWork, and refreshed via a single
// `onFinancialReviewChanged` callback threaded through the section to the inline review card.

const mockPendingReload = vi.fn();
let pendingState: { status: string; count?: number; items?: unknown[] } = {
  status: "loaded",
  count: 1,
  items: [{ actualWorkId: "aw-1", submittedAtUtc: "2026-08-27T12:00:00Z", lineCount: 2, recorderDisplayName: "Dana", reviewStatus: "ReadyToReview" }],
};

vi.mock("../useActualWorkPendingReviews", () => ({
  useActualWorkPendingReviews: (_requestId: string, enabled: boolean) => ({
    state: enabled ? pendingState : { status: "hidden" },
    reload: mockPendingReload,
  }),
}));

vi.mock("../DetailHero", () => ({ TodayPromiseBanner: () => null, DetailHero: () => null }));
vi.mock("../DetailPanels", () => ({
  ProminentFeedbackCard: () => null,
  HeroAttentionBanner: () => null,
  OriginalRequestCard: () => null,
  RelatedWorkPanel: () => null,
  LogContactCard: () => null,
  ServiceLocationPanel: () => null,
  CustomerPanel: () => null,
  TriagePanel: () => null,
  CustomerSignalPanel: () => null,
  FeedbackSummaryCard: () => null,
  SourceMetaPanel: () => null,
  WorkControlsGroup: () => null,
}));
vi.mock("../CustomerContactStrip", () => ({ CustomerContactStrip: () => null }));
vi.mock("../RequestDetailAnchor", () => ({ RequestDetailAnchor: () => null }));
vi.mock("../MobileRequestAnchor", () => ({ MobileRequestAnchor: () => null, MobileActionRail: () => null }));
vi.mock("../MobileContactLocationCard", () => ({ MobileContactLocationCard: () => null }));
vi.mock("../TimingPanel", () => ({ TimingPanel: () => null }));
vi.mock("../BusinessSection", () => ({ CloseRequestCard: () => null, WorkDoneCard: () => null }));
vi.mock("../TeamSection", () => ({ TeamSection: () => null }));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => null }));
vi.mock("../RequestDetailActivity", () => ({ RequestDetailActivity: () => null }));
vi.mock("../ActualWorkCard", () => ({ ActualWorkCard: () => null }));
vi.mock("../ActualWorkHistoryCard", () => ({ ActualWorkHistoryCard: () => null }));
vi.mock("../ActualWorkComposer", () => ({ ActualWorkComposer: () => null }));
vi.mock("../useActualWorkFinancialReview", () => ({
  useActualWorkFinancialReview: (visits: unknown[]) => ({
    state: { status: "loaded", visits },
    retry: vi.fn(),
    review: vi.fn(),
    resolveLine: vi.fn(),
    recordNoChargeDisposition: vi.fn(),
    replace: vi.fn(),
    isVisitMutating: () => false,
  }),
}));
// Stand-in inline review card: surfaces the refresh/focus wiring the coordinator threads in.
vi.mock("../ActualWorkReviewCard", () => ({
  ActualWorkReviewCard: ({ onFinancialReviewChanged, onRetry, focusVisitId, onFocusVisitHandled }: {
    onFinancialReviewChanged?: () => void;
    onRetry?: () => void;
    focusVisitId?: string | null;
    onFocusVisitHandled?: () => void;
  }) => (
    <div>
      <button onClick={() => onFinancialReviewChanged?.()}>fire-financial-changed</button>
      <button onClick={() => onRetry?.()}>fire-retry-review</button>
      <span>focus-visit:{focusVisitId ?? "none"}</span>
      <button onClick={() => onFocusVisitHandled?.()}>fire-focus-handled</button>
    </div>
  ),
}));
vi.mock("../useActualWorkHistory", () => ({
  useActualWorkHistory: () => ({
    state: { status: "loaded", submittedVisits: [{ id: "aw-1", superseded: false }] },
    retry: vi.fn(),
  }),
}));
vi.mock("../useActualWorkCapture", () => ({
  useActualWorkCapture: () => ({
    state: { status: "no-draft", submittedCount: 1 },
    isModalOpen: false,
    startCapture: vi.fn(),
    createDraft: vi.fn(),
    openReplacementDraft: vi.fn(),
  }),
}));

function baseDetail(): KeepRequestDetailResult {
  return mockRequestDetails["mock-req-001"];
}

const commonProps = {
  requestId: "req-1",
  highlights: {},
  showProminentFeedbackCard: false,
  onDetailUpdated: vi.fn(),
  onContactLaunched: vi.fn(),
  onEditLocation: vi.fn(),
  onOpenReassignOwner: vi.fn(),
  onOpenWatchers: vi.fn(),
  onOpenClearAttention: vi.fn(),
  onRecordFollowUp: vi.fn(),
  onCreateFollowUp: vi.fn(),
  onReviewSuccess: vi.fn(),
  canRecordShareIntent: false,
  needsShare: false,
  onOpenShareDrawer: vi.fn(),
  customerUpdateDraft: "",
  onCustomerUpdateDraftChange: vi.fn(),
  customerUpdateDraftStatus: "idle",
  onCustomerUpdateDraftStatusChange: vi.fn(),
  reviewSuccessMsg: null,
  timelineFilter: "all" as const,
  onTimelineFilterChange: vi.fn(),
  displayedEvents: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  pendingState = {
    status: "loaded",
    count: 1,
    items: [{ actualWorkId: "aw-1", submittedAtUtc: "2026-08-27T12:00:00Z", lineCount: 2, recorderDisplayName: "Dana", reviewStatus: "ReadyToReview" }],
  };
});

describe("RequestDetailContent — pending financial reviews card (BL138 1B-client)", () => {
  it("does not render the card for a non-reviewer", () => {
    render(<RequestDetailContent {...commonProps} detail={baseDetail()} />);
    expect(screen.queryByText(/Pending financial reviews/)).not.toBeInTheDocument();
  });

  it("narrow: hands the visit id to the inline review card (not a click-time DOM lookup) and clears it once handled", async () => {
    render(<RequestDetailContent {...commonProps} detail={baseDetail()} canReviewActualWork />);
    expect(screen.getByText("focus-visit:none")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Review financials" }));
    expect(screen.getByText("focus-visit:aw-1")).toBeInTheDocument();

    // The inline card resolves the request (mounted/loaded) and reports back.
    await userEvent.click(screen.getByRole("button", { name: "fire-focus-handled" }));
    expect(screen.getByText("focus-visit:none")).toBeInTheDocument();
  });

  it("narrow: a manual retry on the inline review card also refreshes the pending projection", async () => {
    render(<RequestDetailContent {...commonProps} detail={baseDetail()} canReviewActualWork />);
    mockPendingReload.mockClear();
    await userEvent.click(screen.getByRole("button", { name: "fire-retry-review" }));
    expect(mockPendingReload).toHaveBeenCalledTimes(1);
  });

  it("wide viewport: the row routes to the visit's workspace deep link instead of scrolling", async () => {
    const originalMatchMedia = window.matchMedia;
    window.matchMedia = ((query: string) => ({
      matches: true, media: query, onchange: null,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
    })) as unknown as typeof window.matchMedia;
    const onNavigateToActualWorkspace = vi.fn();
    try {
      render(
        <RequestDetailContent
          {...commonProps}
          detail={baseDetail()}
          canReviewActualWork
          onNavigateToActualWorkspace={onNavigateToActualWorkspace}
        />,
      );
      await userEvent.click(screen.getByRole("button", { name: "Review financials" }));
      expect(onNavigateToActualWorkspace).toHaveBeenCalledWith("req-1", "aw-1");
    } finally {
      window.matchMedia = originalMatchMedia;
    }
  });

  it("refreshes the pending projection when the inline review card reports a change", async () => {
    render(<RequestDetailContent {...commonProps} detail={baseDetail()} canReviewActualWork />);
    mockPendingReload.mockClear();
    await userEvent.click(screen.getByRole("button", { name: "fire-financial-changed" }));
    expect(mockPendingReload).toHaveBeenCalledTimes(1);
  });
});
