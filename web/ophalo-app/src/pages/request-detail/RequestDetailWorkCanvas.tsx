import { type ReactNode, type Ref } from "react";
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
}: RequestDetailWorkCanvasProps) {
  const composeWithinAttention =
    detail.effectiveAttention.level !== "none" &&
    detail.availableActions.primaryAction?.target === "customer_update_composer";

  const composer = (
    <div
      id="focus-panel-update"
      tabIndex={-1}
      className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] shadow-sm focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)]"
    >
      <UnifiedComposer ref={composerRef} requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} customerUpdateDraft={customerUpdateDraft} onCustomerUpdateDraftChange={onCustomerUpdateDraftChange} customerUpdateDraftStatus={customerUpdateDraftStatus} onCustomerUpdateDraftStatusChange={onCustomerUpdateDraftStatusChange} highlight={highlights.sendUpdate} bare />
    </div>
  );

  const attentionAndCommunication = (
    <>
      <div id="focus-panel-attention" className="space-y-3">
        <HeroAttentionBanner
          requestId={requestId}
          detail={detail}
          onDetailUpdated={onDetailUpdated}
          onOpenClearAttention={onOpenClearAttention}
          onRecordFollowUp={onRecordFollowUp}
          onContactLaunched={onContactLaunched}
          onActivateCustomerUpdateComposer={onActivateCustomerUpdateComposer}
          inlineComposer={composeWithinAttention ? composer : undefined}
        />
        <TodayPromiseBanner detail={detail} onRecordFollowUp={onRecordFollowUp} />
      </div>
      {!composeWithinAttention && composer}
    </>
  );

  const activeWork = (
    <div className="min-w-0 space-y-5">
      {attentionAndCommunication}

      {!isWide && (
        <MobileContactLocationCard detail={detail} onContactLaunched={onContactLaunched} onEditLocation={onEditLocation} />
      )}
      {!isWide && <OriginalRequestCard detail={detail} />}

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
