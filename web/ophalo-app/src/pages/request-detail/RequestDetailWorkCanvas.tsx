import { type KeyboardEvent, type ReactNode, type Ref } from "react";
import {
  type RequestDetailLayoutProps,
  ProminentFeedbackCard,
  HeroAttentionBanner,
  OriginalRequestCard,
  WorkControlsGroup,
} from "./DetailPanels";
import { TodayPromiseBanner } from "./DetailHero";
import { MobileContactLocationCard } from "./MobileContactLocationCard";
import { UnifiedComposer, type UnifiedComposerHandle } from "./UnifiedComposer";
import { MarkWorkDoneSecondarySlot } from "./PrimaryActionControl";
import { KeepButton } from "../../components/keep/KeepButton";
import { RequestCommunicationsWorkspace } from "./RequestCommunicationsWorkspace";

export type RequestWorkspaceTab = "work" | "communications";

interface RequestDetailWorkCanvasProps
  extends Pick<
    RequestDetailLayoutProps,
    | "requestId"
    | "detail"
    | "highlights"
    | "showProminentFeedbackCard"
    | "onDetailUpdated"
    | "onContactLaunched"
    | "onEditLocation"
    | "onRecordFollowUp"
    | "onCreateFollowUp"
    | "onReviewSuccess"
  > {
  isWide: boolean;
  onOpenClearAttention: () => void;
  onActivateCustomerUpdateComposer: () => void;
  composerRef: Ref<UnifiedComposerHandle>;
  customerUpdateDraft: string;
  onCustomerUpdateDraftChange: (value: string) => void;
  customerUpdateDraftStatus: string;
  onCustomerUpdateDraftStatusChange: (value: string) => void;
  reviewSuccessMsg: string | null;
  actualWorkSection: ReactNode;
  activityBlock: ReactNode;
  recordDetailsBlock: ReactNode;
  visitHistoryBlock?: ReactNode;
  requestAnchor?: ReactNode;
  requestMemoryRail?: ReactNode;
  activeWorkspaceTab: RequestWorkspaceTab;
  onWorkspaceTabChange: (tab: RequestWorkspaceTab) => void;
}

export function RequestDetailWorkCanvas({
  isWide,
  requestId,
  detail,
  highlights,
  showProminentFeedbackCard,
  onDetailUpdated,
  onContactLaunched,
  onEditLocation,
  onRecordFollowUp,
  onCreateFollowUp,
  onReviewSuccess,
  onOpenClearAttention,
  onActivateCustomerUpdateComposer,
  composerRef,
  customerUpdateDraft,
  onCustomerUpdateDraftChange,
  customerUpdateDraftStatus,
  onCustomerUpdateDraftStatusChange,
  reviewSuccessMsg,
  actualWorkSection,
  activityBlock,
  recordDetailsBlock,
  visitHistoryBlock,
  requestAnchor,
  requestMemoryRail,
  activeWorkspaceTab,
  onWorkspaceTabChange,
}: RequestDetailWorkCanvasProps) {
  const composer = (
    <div
      id="focus-panel-update"
      tabIndex={-1}
      className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)]"
    >
      <UnifiedComposer ref={composerRef} requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} customerUpdateDraft={customerUpdateDraft} onCustomerUpdateDraftChange={onCustomerUpdateDraftChange} customerUpdateDraftStatus={customerUpdateDraftStatus} onCustomerUpdateDraftStatusChange={onCustomerUpdateDraftStatusChange} highlight={highlights.sendUpdate} bare />
    </div>
  );

  const attention = (
    <div id="focus-panel-attention" className="space-y-3">
      <HeroAttentionBanner
        requestId={requestId}
        detail={detail}
        onDetailUpdated={onDetailUpdated}
        onOpenClearAttention={onOpenClearAttention}
        onRecordFollowUp={onRecordFollowUp}
        onContactLaunched={onContactLaunched}
        onActivateCustomerUpdateComposer={onActivateCustomerUpdateComposer}
      />
      <TodayPromiseBanner detail={detail} onRecordFollowUp={onRecordFollowUp} />
    </div>
  );

  const workModules = (
    <>
      {actualWorkSection}

      {detail.availableActions.markWorkDoneSecondary && (
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
          <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Request lifecycle</p>
          <p className="mb-3 mt-1 text-xs text-[var(--ophalo-muted)]">
            Marking work done changes only the request status — it does not notify the customer
            or complete internal financial review.
          </p>
          <MarkWorkDoneSecondarySlot requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
        </div>
      )}

      <div className="space-y-3">
        {showProminentFeedbackCard && <ProminentFeedbackCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />}
        {reviewSuccessMsg && <div role="status" aria-live="polite" className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm font-medium text-[var(--ophalo-success)]">{reviewSuccessMsg}</div>}
      </div>

      {!showProminentFeedbackCard && (
        <WorkControlsGroup
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          highlights={{ feedbackReview: "secondary" }}
          onReviewSuccess={onReviewSuccess}
        />
      )}

      {detail.availableActions.canCreateFollowUpRequest && (
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
          <p className="mb-1 text-sm font-semibold text-[var(--ophalo-ink)]">Follow-up work</p>
          <p className="mb-3 text-xs text-[var(--ophalo-muted)]">
            This request is closed. Start a new request for any additional work needed.
          </p>
          <KeepButton variant="secondary" onClick={onCreateFollowUp} className="w-full">
            Create follow-up request
          </KeepButton>
        </div>
      )}
    </>
  );

  function handleWorkspaceTabKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight" && event.key !== "Home" && event.key !== "End") return;
    event.preventDefault();
    const next: RequestWorkspaceTab = event.key === "ArrowLeft" || event.key === "Home"
      ? "work"
      : "communications";
    onWorkspaceTabChange(next);
    document.getElementById(`request-workspace-tab-${next}`)?.focus();
  }

  const workspaceTabs = (
    <div className="overflow-hidden rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm">
      <div role="tablist" aria-label="Request workspace" className="grid grid-cols-2">
        {(["work", "communications"] as const).map((tab) => (
          <button
            key={tab}
            id={`request-workspace-tab-${tab}`}
            type="button"
            role="tab"
            aria-selected={activeWorkspaceTab === tab}
            aria-controls={`request-workspace-panel-${tab}`}
            tabIndex={activeWorkspaceTab === tab ? 0 : -1}
            onClick={() => onWorkspaceTabChange(tab)}
            onKeyDown={handleWorkspaceTabKeyDown}
            className={`border-b-2 px-4 py-2.5 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--keep-accent)] ${activeWorkspaceTab === tab ? "border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]" : "border-transparent text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"}`}
          >
            {tab === "work" ? "Work" : "Communications"}
          </button>
        ))}
      </div>
    </div>
  );

  const activeWork = (
    <div className="min-w-0 space-y-5">
      {attention}

      {isWide ? (
        <>
          {workspaceTabs}
          <div
            id="request-workspace-panel-work"
            role="tabpanel"
            aria-labelledby="request-workspace-tab-work"
            hidden={activeWorkspaceTab !== "work"}
            className="space-y-5"
          >
            {workModules}
          </div>
          <div
            id="request-workspace-panel-communications"
            role="tabpanel"
            aria-labelledby="request-workspace-tab-communications"
            hidden={activeWorkspaceTab !== "communications"}
          >
            <RequestCommunicationsWorkspace detail={detail} composer={composer} />
          </div>
        </>
      ) : (
        <>
          {composer}
          <MobileContactLocationCard detail={detail} onContactLaunched={onContactLaunched} onEditLocation={onEditLocation} />
          <OriginalRequestCard detail={detail} />
          {workModules}
        </>
      )}

      {!isWide && activityBlock}
      {!isWide && recordDetailsBlock}
    </div>
  );

  return (
    <div data-request-detail-work-canvas className="min-h-0 min-w-0 flex-1 overflow-y-auto px-4 py-4 md:px-6">
      <div className={`mx-auto w-full ${isWide ? "max-w-[1440px]" : "max-w-4xl"}`}>
        {isWide && requestAnchor && (
          <div data-request-sticky-strip className="sticky top-0 z-20 -mx-1 bg-[var(--keep-request-canvas)] px-1 pb-3">
            {requestAnchor}
          </div>
        )}

        {isWide ? (
          <div data-request-three-column-workbench className="grid min-w-0 grid-cols-[minmax(0,1fr)_300px] items-start gap-5">
            {activeWork}
            {requestMemoryRail ?? (
              <aside data-request-work-canvas-secondary className="min-w-0 space-y-5">
                {recordDetailsBlock}
                {activityBlock}
                {visitHistoryBlock}
              </aside>
            )}
          </div>
        ) : (
          activeWork
        )}
      </div>
    </div>
  );
}
