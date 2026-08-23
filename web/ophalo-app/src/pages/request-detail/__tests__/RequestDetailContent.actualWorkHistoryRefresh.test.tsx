import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequestDetailContent } from "../RequestDetailContent";
import { mockRequestDetails } from "../../../mocks/fixtures";
import type { KeepRequestDetailResult } from "../../../lib/apiClient";

// GAP: a successful Actual Work submit must refresh the standalone submitted-history card
// (Batch 5c) alongside the capture hook's own markSubmitted — otherwise the history card goes
// stale until the next full page load.

const mockMarkSubmitted = vi.fn();
const mockHistoryRetry = vi.fn();

vi.mock("../DetailHero", () => ({
  TodayPromiseBanner: () => null,
  DetailHero: () => null,
}));
vi.mock("../DetailPanels", () => ({
  ProminentFeedbackCard: () => null,
  AttentionGuidanceCard: () => null,
  NextStepCard: () => null,
  OriginalRequestCard: () => null,
  RelatedWorkPanel: () => null,
  MarkHandledCard: () => null,
  LogContactCard: () => null,
  CustomerPanel: () => null,
  TriagePanel: () => null,
  CustomerSignalPanel: () => null,
  FeedbackSummaryCard: () => null,
  SourceMetaPanel: () => null,
  WorkControlsGroup: () => null,
}));
vi.mock("../CustomerContactStrip", () => ({ CustomerContactStrip: () => null }));
vi.mock("../RequestDetailAnchor", () => ({ RequestDetailAnchor: () => null }));
vi.mock("../TimingPanel", () => ({ TimingPanel: () => null }));
vi.mock("../BusinessSection", () => ({ CloseRequestCard: () => null, WorkDoneCard: () => null }));
vi.mock("../TeamSection", () => ({ TeamSection: () => null }));
vi.mock("../UnifiedComposer", () => ({ UnifiedComposer: () => null }));
vi.mock("../RequestDetailActivity", () => ({ RequestDetailActivity: () => null }));
vi.mock("../ActualWorkCard", () => ({ ActualWorkCard: () => null }));
vi.mock("../ActualWorkHistoryCard", () => ({ ActualWorkHistoryCard: () => null }));
vi.mock("../useActualWorkHistory", () => ({
  useActualWorkHistory: () => ({ state: { status: "loaded", submittedVisits: [] }, retry: mockHistoryRetry }),
}));
vi.mock("../useActualWorkCapture", () => ({
  useActualWorkCapture: () => ({
    state: { status: "draft", draft: { id: "aw-1", lines: [] }, submittedCount: 0 },
    isModalOpen: true,
    conflictNotice: null,
    startCapture: vi.fn(),
    closeModal: vi.fn(),
    refetchDraft: vi.fn(),
    reconcileAfterConflict: vi.fn(),
    clearConflictNotice: vi.fn(),
    retryReconciliation: vi.fn(),
    markSubmitted: mockMarkSubmitted,
    onDraftDiscarded: vi.fn(),
  }),
}));
vi.mock("../ActualWorkComposer", () => ({
  ActualWorkComposer: ({ onSubmitted }: { onSubmitted: () => void }) => (
    <button onClick={onSubmitted}>Submit visit</button>
  ),
}));

function baseDetail(): KeepRequestDetailResult {
  return mockRequestDetails["mock-req-001"];
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("RequestDetailContent — Actual Work submit refreshes history (Batch 5c)", () => {
  it("calls actualWorkHistory.retry alongside markSubmitted when the composer reports a successful submit", async () => {
    const user = userEvent.setup();
    render(
      <RequestDetailContent
        detail={baseDetail()}
        requestId="req-1"
        highlights={{}}
        showProminentFeedbackCard={false}
        onDetailUpdated={vi.fn()}
        onContactLaunched={vi.fn()}
        onEditLocation={vi.fn()}
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

    await user.click(screen.getByRole("button", { name: "Submit visit" }));

    expect(mockMarkSubmitted).toHaveBeenCalledTimes(1);
    expect(mockHistoryRetry).toHaveBeenCalledTimes(1);
  });
});

describe("RequestDetailContent — Proposed Scope removal (locked Workbench spec)", () => {
  it("never renders Proposed Scope capture/view in the Request Detail render path", () => {
    render(
      <RequestDetailContent
        detail={baseDetail()}
        requestId="req-1"
        highlights={{}}
        showProminentFeedbackCard={false}
        onDetailUpdated={vi.fn()}
        onContactLaunched={vi.fn()}
        onEditLocation={vi.fn()}
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

    // Proposed Scope is explicitly deferred from this pilot Workbench surface (locked spec,
    // 2026-08-22). None of this test file's mocks stub out a Proposed Scope module, so if it
    // were ever re-wired into RequestDetailContent, its real "Capture proposed scope" entry
    // point (or the draft/submitted states' own headings) would render here unmocked.
    expect(screen.queryByText(/proposed scope/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /proposed scope/i })).not.toBeInTheDocument();
  });
});
