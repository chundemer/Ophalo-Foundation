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
  HeroAttentionBanner: () => null,
  OriginalRequestCard: () => null,
  RelatedWorkPanel: () => null,
  TriagePanel: () => null,
  CustomerSignalPanel: () => null,
  FeedbackSummaryCard: () => null,
  SourceMetaPanel: () => null,
  WorkControlsGroup: () => null,
}));
vi.mock("../CustomerContactStrip", () => ({ CustomerContactStrip: () => null }));
vi.mock("../RequestDetailAnchor", () => ({ RequestDetailAnchor: () => null }));
vi.mock("../MobileRequestAnchor", () => ({ MobileRequestAnchor: () => null, MobileActionRail: () => null }));
vi.mock("../TimingPanel", () => ({ TimingPanel: () => null }));
vi.mock("../BusinessSection", () => ({ CloseRequestCard: () => null, WorkDoneCard: () => null }));
vi.mock("../TeamSection", () => ({ TeamSection: () => null }));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => null }));
vi.mock("../RequestDetailActivity", () => ({ RequestDetailActivity: () => null }));
vi.mock("../ActualWorkCard", () => ({ ActualWorkCard: () => null }));
vi.mock("../ActualWorkHistoryCard", () => ({ ActualWorkHistoryCard: () => null }));
vi.mock("../useActualWorkHistory", () => ({
  useActualWorkHistory: () => ({ state: { status: "loaded", submittedVisits: [] }, retry: vi.fn() }),
}));
vi.mock("../useActualWorkCapture", () => ({
  useActualWorkCapture: () => ({
    state: { status: "idle" },
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
