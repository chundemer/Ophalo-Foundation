import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render } from "@testing-library/react";
import { RequestDetailContent } from "../RequestDetailContent";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// Mobile responsive-layout regression (2026-08-27): a viewport change from a wide desktop width
// to a Pixel-8-sized width must recompute `isWide` and switch the Request Detail layout WITHOUT
// a remount or browser refresh, and the detail flex chain must stay shrinkable so a wide child
// cannot hold the canvas — and the ResizeObserver target — stuck at the old width.
//
// jsdom has no layout engine, so this asserts the two testable halves of the fix:
//   1. the flex chain carries `min-w-0` (no stale intrinsic-width constraint);
//   2. the same mount flips wide -> narrow when the ResizeObserver reports the new width.
// Manual Chrome DevTools check: open a Request Detail with an active "Needs attention" banner,
// switch the Device Toolbar to Pixel 8 without refreshing — the attention card must wrap its
// primary action onto a second line and every card must fit within the viewport.

vi.mock("../DetailHero", () => ({ TodayPromiseBanner: () => null }));
vi.mock("../DetailPanels", () => ({
  ProminentFeedbackCard: () => null,
  HeroAttentionBanner: () => <div data-testid="section-attention" />,
  OriginalRequestCard: () => null,
  RelatedWorkPanel: () => null,
  CustomerSignalPanel: () => null,
  FeedbackSummaryCard: () => null,
  SourceMetaPanel: () => null,
  WorkControlsGroup: () => null,
}));
vi.mock("../RequestDetailAnchor", () => ({ RequestDetailAnchor: () => <div data-testid="desktop-anchor" /> }));
vi.mock("../MobileRequestAnchor", () => ({
  MobileRequestAnchor: () => <div data-testid="mobile-anchor" />,
  MobileActionRail: () => <div data-testid="mobile-action-rail" />,
}));
vi.mock("../MobileContactLocationCard", () => ({ MobileContactLocationCard: () => null }));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => null }));
vi.mock("../RequestDetailActivity", () => ({ RequestDetailActivity: () => null }));
vi.mock("../ActualWorkCard", () => ({ ActualWorkCard: () => null }));
vi.mock("../ActualWorkHistoryCard", () => ({ ActualWorkHistoryCard: () => null }));
vi.mock("../ActualWorkComposer", () => ({ ActualWorkComposer: () => null }));
vi.mock("../TeamSection", () => ({ TeamSection: () => null }));
vi.mock("../useActualWorkHistory", () => ({
  useActualWorkHistory: () => ({ state: { status: "loaded", submittedVisits: [] }, retry: vi.fn() }),
}));
vi.mock("../useActualWorkCapture", () => ({
  useActualWorkCapture: () => ({
    state: { status: "no-draft" },
    isModalOpen: false,
    conflictNotice: null,
    startCapture: vi.fn(),
    closeModal: vi.fn(),
    refetchDraft: vi.fn(),
    reconcileAfterConflict: vi.fn(),
    clearConflictNotice: vi.fn(),
    retryReconciliation: vi.fn(),
    markSubmitted: vi.fn(),
    onDraftDiscarded: vi.fn(),
  }),
}));

// Callback-capturing ResizeObserver stub (same pattern as RequestWorkbenchShell.test.tsx) so
// each test drives the observed width directly.
let roCallback: ResizeObserverCallback | null = null;
class StubResizeObserver {
  constructor(cb: ResizeObserverCallback) {
    roCallback = cb;
  }
  observe() {}
  disconnect() {}
  unobserve() {}
}

function fireWidth(width: number) {
  act(() => {
    roCallback?.(
      [{ contentRect: { width } } as ResizeObserverEntry],
      null as unknown as ResizeObserver,
    );
  });
}

function baseDetail(): KeepRequestDetailResult {
  return mockRequestDetails["mock-req-001"];
}

function renderContent() {
  return render(
    <RequestDetailContent
      detail={baseDetail()}
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
    />,
  );
}

describe("RequestDetailContent — responsive shrinkability", () => {
  beforeEach(() => {
    roCallback = null;
    vi.stubGlobal("ResizeObserver", StubResizeObserver);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("keeps the detail flex chain shrinkable (min-w-0) so a wide child cannot hold the canvas at a stale width", () => {
    const { container } = renderContent();

    const root = container.firstElementChild as HTMLElement;
    expect(root.className).toContain("min-w-0");

    const scrollRegion = container.querySelector("[data-request-detail-work-canvas]") as HTMLElement;
    expect(scrollRegion).not.toBeNull();
    expect(scrollRegion.className).toContain("min-w-0");
    expect(scrollRegion.className).toContain("overflow-y-auto");
  });

  it("recomputes isWide on a wide -> Pixel-8 width change on the same mount, with no remount or refresh", () => {
    const { container } = renderContent();

    // Wide desktop container width -> desktop workbench layout.
    fireWidth(1200);
    expect(container.querySelector('[data-testid="desktop-anchor"]')).not.toBeNull();
    expect(container.querySelector('[data-testid="mobile-anchor"]')).toBeNull();

    // Same mount, viewport resized to Pixel 8 (412 CSS px) -> mobile layout, without refresh.
    fireWidth(412);
    expect(container.querySelector('[data-testid="mobile-anchor"]')).not.toBeNull();
    expect(container.querySelector('[data-testid="desktop-anchor"]')).toBeNull();
    // Mobile-only surfaces mount in the narrow layout.
    expect(container.querySelector('[data-testid="mobile-action-rail"]')).not.toBeNull();
  });
});
