import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestDetailContent } from "../RequestDetailContent";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// BL136 4f-i: on wide screens the Actual Work capture entry point navigates to the dedicated
// workspace route and the in-page full-bleed composer modal is not rendered; below 1001px the
// existing modal behaviour is unchanged.

const mockCreateDraft = vi.fn().mockResolvedValue("created");
const mockStartCapture = vi.fn();

vi.mock("../DetailHero", () => ({ TodayPromiseBanner: () => null }));
vi.mock("../DetailPanels", () => ({
  ProminentFeedbackCard: () => null,
  HeroAttentionBanner: () => null,
  OriginalRequestCard: () => null,
  RelatedWorkPanel: () => null,
  CustomerSignalPanel: () => null,
  FeedbackSummaryCard: () => null,
  SourceMetaPanel: () => null,
  WorkControlsGroup: () => null,
}));
vi.mock("../RequestDetailAnchor", () => ({ RequestDetailAnchor: () => null }));
vi.mock("../MobileRequestAnchor", () => ({ MobileRequestAnchor: () => null, MobileActionRail: () => null }));
vi.mock("../MobileContactLocationCard", () => ({ MobileContactLocationCard: () => null }));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => null }));
vi.mock("../RequestDetailActivity", () => ({ RequestDetailActivity: () => null }));
vi.mock("../ActualWorkHistoryCard", () => ({ ActualWorkHistoryCard: () => null }));
vi.mock("../TeamSection", () => ({ TeamSection: () => null }));
vi.mock("../ActualWorkCard", () => ({
  ActualWorkCard: ({ onStartCapture }: { onStartCapture: (intent?: string) => void }) => (
    <button onClick={() => onStartCapture("transcribe")}>start-capture</button>
  ),
}));
vi.mock("../ActualWorkComposer", () => ({
  ActualWorkComposer: () => <div>IN-PAGE COMPOSER</div>,
}));
vi.mock("../useActualWorkHistory", () => ({
  useActualWorkHistory: () => ({ state: { status: "loaded", submittedVisits: [] }, retry: vi.fn() }),
}));
vi.mock("../useActualWorkCapture", () => ({
  useActualWorkCapture: () => ({
    state: { status: "draft", draft: { id: "d1", status: "Draft" } },
    isModalOpen: true,
    conflictNotice: null,
    replacementCorrection: false,
    createDraft: mockCreateDraft,
    startCapture: mockStartCapture,
    closeModal: vi.fn(),
    refetchDraft: vi.fn(),
    reconcileAfterConflict: vi.fn(),
    clearConflictNotice: vi.fn(),
    retryReconciliation: vi.fn(),
    markSubmitted: vi.fn(),
    onDraftDiscarded: vi.fn(),
    setDefaultPerformer: vi.fn(),
    setVisitNote: vi.fn(),
    setZeroLineDisposition: vi.fn(),
    handOffToOffice: vi.fn(),
    openReplacementDraft: vi.fn(),
  }),
}));
vi.mock("../useActualWorkFinancialReview", () => ({
  useActualWorkFinancialReview: () => ({ state: { status: "loaded", visits: [] }, retry: vi.fn(), replace: vi.fn() }),
}));

let roCallback: ResizeObserverCallback | null = null;
class StubResizeObserver {
  constructor(cb: ResizeObserverCallback) {
    roCallback = cb;
  }
  observe() {}
  disconnect() {}
  unobserve() {}
}
// Container width — drives Request Detail's *internal* layout only, not the route-vs-modal call.
function fireContainerWidth(width: number) {
  act(() => {
    roCallback?.([{ contentRect: { width } } as ResizeObserverEntry], null as unknown as ResizeObserver);
  });
}

// Viewport predicate — the route-vs-modal decision uses this (matches ActualWorkWorkspacePage).
const originalMatchMedia = window.matchMedia;
let viewportWide = true;
function stubMatchMedia() {
  window.matchMedia = ((query: string) => ({
    matches: viewportWide,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia;
}

function renderContent(
  onNavigateToActualWorkspace?: (id: string, visit?: "new" | "draft" | (string & {})) => void,
) {
  const detail: KeepRequestDetailResult = mockRequestDetails["mock-req-001"];
  return render(
    <RequestDetailContent
      detail={detail}
      requestId="req-1"
      highlights={{}}
      showProminentFeedbackCard={false}
      onDetailUpdated={vi.fn()}
      onContactLaunched={vi.fn()}
      onEditLocation={vi.fn()}
      onOpenReassignOwner={vi.fn()}
      onOpenWatchers={vi.fn()}
      onOpenClearAttention={vi.fn()}
      onRecordFollowUp={vi.fn()}
      onCreateFollowUp={vi.fn()}
      onReviewSuccess={vi.fn()}
      canRecordShareIntent={false}
      needsShare={false}
      onOpenShareDrawer={vi.fn()}
      customerUpdateDraft=""
      onCustomerUpdateDraftChange={vi.fn()}
      customerUpdateDraftStatus="idle"
      onCustomerUpdateDraftStatusChange={vi.fn()}
      reviewSuccessMsg={null}
      timelineFilter="all"
      onTimelineFilterChange={vi.fn()}
      displayedEvents={[]}
      onNavigateToActualWorkspace={onNavigateToActualWorkspace}
    />,
  );
}

describe("RequestDetailContent — Actual Work workspace routing", () => {
  beforeEach(() => {
    roCallback = null;
    viewportWide = true;
    stubMatchMedia();
    vi.stubGlobal("ResizeObserver", StubResizeObserver);
    vi.clearAllMocks();
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    window.matchMedia = originalMatchMedia;
  });

  it("wide viewport: capture entry creates the Draft then navigates to the workspace route; no in-page composer", async () => {
    const onNavigate = vi.fn();
    renderContent(onNavigate);
    fireContainerWidth(1200);
    await userEvent.click(screen.getByText("start-capture"));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith("req-1", "draft"));
    expect(mockCreateDraft).toHaveBeenCalledWith("transcribe");
    expect(mockStartCapture).not.toHaveBeenCalled();
    expect(screen.queryByText("IN-PAGE COMPOSER")).not.toBeInTheDocument();
  });

  it("narrow viewport: capture entry opens the in-page modal (startCapture), never navigates", async () => {
    viewportWide = false;
    stubMatchMedia();
    const onNavigate = vi.fn();
    renderContent(onNavigate);
    fireContainerWidth(800);
    await userEvent.click(screen.getByText("start-capture"));
    expect(mockStartCapture).toHaveBeenCalledWith("transcribe");
    expect(onNavigate).not.toHaveBeenCalled();
    expect(screen.getByText("IN-PAGE COMPOSER")).toBeInTheDocument();
  });

  it("two-pane wide viewport with a narrow detail container still routes to the workspace", async () => {
    // Workbench two-pane at ~1100px viewport: 360px queue pane leaves the detail container
    // < 1001px, but the viewport predicate is wide — the capture entry must still navigate.
    viewportWide = true;
    stubMatchMedia();
    const onNavigate = vi.fn();
    renderContent(onNavigate);
    fireContainerWidth(740);
    await userEvent.click(screen.getByText("start-capture"));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith("req-1", "draft"));
    expect(mockStartCapture).not.toHaveBeenCalled();
    expect(screen.queryByText("IN-PAGE COMPOSER")).not.toBeInTheDocument();
  });
});
