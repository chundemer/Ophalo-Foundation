import { describe, it, expect, vi } from "vitest";
import { render } from "@testing-library/react";
import { RequestDetailContent } from "../RequestDetailContent";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// Canvas max-width frame (three-row correction, 2026-08-22; narrowed to the mockup's reading
// measure in slice 4, 2026-08-23): the Work Canvas scroll region must constrain its readable
// content with a centered max-width wrapper (max-w-4xl mx-auto w-full), rather than rendering
// edge to edge at wide desktop widths.

vi.mock("../DetailHero", () => ({ TodayPromiseBanner: () => null }));
vi.mock("../DetailPanels", () => ({
  ProminentFeedbackCard: () => null,
  HeroAttentionBanner: () => <div data-testid="section-attention" />,
  OriginalRequestCard: () => <div data-testid="section-customer-need" />,
  RelatedWorkPanel: () => null,
  TriagePanel: () => null,
  CustomerSignalPanel: () => null,
  FeedbackSummaryCard: () => null,
  SourceMetaPanel: () => null,
  WorkControlsGroup: () => null,
  LogContactCard: () => null,
  ServiceLocationPanel: () => null,
}));
vi.mock("../CustomerContactStrip", () => ({ CustomerContactStrip: () => null }));
vi.mock("../RequestDetailAnchor", () => ({ RequestDetailAnchor: () => null }));
vi.mock("../MobileRequestAnchor", () => ({ MobileRequestAnchor: () => null, MobileActionRail: () => null }));
vi.mock("../MobileContactLocationCard", () => ({ MobileContactLocationCard: () => <div data-testid="section-contact-location" /> }));
vi.mock("../TimingPanel", () => ({ TimingPanel: () => null }));
vi.mock("../BusinessSection", () => ({ CloseRequestCard: () => null, WorkDoneCard: () => null }));
vi.mock("../TeamSection", () => ({ TeamSection: () => null }));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => <div data-testid="section-communication" /> }));
vi.mock("../RequestDetailActivity", () => ({ RequestDetailActivity: () => <div data-testid="section-activity" /> }));
vi.mock("../ActualWorkCard", () => ({ ActualWorkCard: () => <div data-testid="section-actual-work" /> }));
vi.mock("../ActualWorkHistoryCard", () => ({ ActualWorkHistoryCard: () => null }));
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

function baseDetail(): KeepRequestDetailResult {
  return mockRequestDetails["mock-req-001"];
}

describe("RequestDetailContent — Canvas max-width frame", () => {
  it("wraps the Canvas content in a centered max-width container inside the sole scroll region", () => {
    const { container } = render(
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

    const scrollRegion = container.querySelector(".overflow-y-auto");
    expect(scrollRegion).not.toBeNull();
    const maxWidthWrapper = scrollRegion?.querySelector(".max-w-4xl.mx-auto");
    expect(maxWidthWrapper).not.toBeNull();
  });
});

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

// Locked mobile canvas order (Slice 3, 2026-08-26, field-operations decision): Attention ->
// Contact/Service Location -> Customer Need -> Actual Work -> Communication -> Activity ->
// Record Details. The component only measures `isWide` via ResizeObserver, which jsdom's no-op
// stub never fires, so the component renders in its default (mobile, `!isWide`) mode here —
// exactly the mode this order applies to.
describe("RequestDetailContent — mobile canvas order (Slice 3)", () => {
  it("renders sections in the locked sequence, asserted by DOM order rather than a snapshot", () => {
    const { container } = renderContent();

    const recordDetails = container.querySelector("details");
    expect(recordDetails).not.toBeNull();
    if (recordDetails) recordDetails.setAttribute("data-testid", "section-record-details");

    const testIds = [
      "section-attention",
      "section-contact-location",
      "section-customer-need",
      "section-actual-work",
      "section-communication",
      "section-activity",
      "section-record-details",
    ];
    const positions = testIds.map((id) => {
      const el = container.querySelector(`[data-testid="${id}"]`);
      expect(el, `expected to find ${id}`).not.toBeNull();
      return { id, index: Array.from(container.querySelectorAll("[data-testid]")).indexOf(el!) };
    });

    for (let i = 1; i < positions.length; i++) {
      const prev = container.querySelector(`[data-testid="${positions[i - 1].id}"]`)!;
      const curr = container.querySelector(`[data-testid="${positions[i].id}"]`)!;
      // DOCUMENT_POSITION_FOLLOWING (4) means `curr` comes after `prev` in the DOM.
      expect(prev.compareDocumentPosition(curr) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
  });
});
