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

// RD-019A: the Work Canvas — the Workbench's sole vertical scroll surface (locked spec §1.2, §1.5,
// §5, §7.1). This component is layout-only: it owns the canvas structure and region order and
// nothing else. It fetches nothing and mutates nothing; it derives no action policy beyond reading
// one server-authored flag to place the quiet lifecycle action (RD-058B-2). The Actual Work,
// activity, and record-details regions arrive pre-built as props.
//
// Desktop module order: attention guidance -> primary execution column (Actual Work, quiet
// lifecycle action, communication) + supporting context column (record details, activity).
// Mobile keeps its focused single column: attention -> contact/location -> Customer Need -> Actual
// Work -> lifecycle action -> communication -> activity -> record details.
// Proposed Scope is explicitly deferred from this pilot Workbench (locked spec §1.7/§3).
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

  return (
    <div data-request-detail-work-canvas className="flex-1 min-h-0 min-w-0 overflow-y-auto px-4 py-5 md:px-6">
      <div className="w-full max-w-[1000px] space-y-5">
        {/* 1. Active attention guidance */}
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

        {/* Customer-message attention owns the composer above. In every other state it remains
            directly below attention, preserving one stable communication home. */}
        {!composeWithinAttention && composer}

        {/* 2. Contact/service location — mobile canvas only (Slice 3, 2026-08-26); desktop
            keeps this content solely in RequestDetailAnchor/CustomerContactStrip. */}
        {!isWide && (
          <MobileContactLocationCard detail={detail} onContactLaunched={onContactLaunched} onEditLocation={onEditLocation} />
        )}

        {/* Customer Need moves into the desktop Anchor in GAP-067. Narrow retains it here after
            contact/location, preserving the focused mobile reading order. */}
        {!isWide && <OriginalRequestCard detail={detail} />}

        <div data-request-work-canvas-context className={`grid items-start gap-5 ${isWide ? "grid-cols-[minmax(0,2fr)_minmax(280px,1fr)]" : "grid-cols-1"}`}>
          <div className="min-w-0 space-y-5">
            {/* 4. Work execution — Actual Work (capture, history, review, recovery), pre-built. */}
            {actualWorkSection}

            {/* 4b. Quiet contextual request-lifecycle action (RD-058B-2). Server-authored: renders
                only while `availableActions.markWorkDoneSecondary` is populated (active attention).
                It is deliberately below Actual Work and above the composer, never in the Anchor and
                never competing with the attention primary. The button's local confirm carries the
                full "does not notify / does not complete review / attention unresolved" advisory. */}
            {detail.availableActions.markWorkDoneSecondary && (
              <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4">
                <p className="text-sm font-semibold text-[var(--ophalo-ink)]">Request lifecycle</p>
                <p className="mt-1 mb-3 text-xs text-[var(--ophalo-muted)]">
                  Marking work done changes only the request status — it does not notify the customer
                  or complete internal financial review.
                </p>
                <MarkWorkDoneSecondarySlot requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} />
              </div>
            )}

            {/* Feedback and quiet lifecycle follow the primary operational work. Communication is
                deliberately above this grid, immediately after attention. */}
            <div className="space-y-3">
              {showProminentFeedbackCard && <ProminentFeedbackCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />}
              {reviewSuccessMsg && <div role="status" aria-live="polite" className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm text-[var(--ophalo-success)] font-medium">{reviewSuccessMsg}</div>}
            </div>

            {/* Actionable, stays visible (not lower-frequency record context) */}
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
                <p className="text-sm font-semibold text-[var(--ophalo-ink)] mb-1">Follow-up work</p>
                <p className="text-xs text-[var(--ophalo-muted)] mb-3">
                  This request is closed. Start a new request for any additional work needed.
                </p>
                <KeepButton variant="secondary" onClick={onCreateFollowUp} className="w-full">
                  Create follow-up request
                </KeepButton>
              </div>
            )}

          </div>

        {/* 6/7. Activity and lower-frequency record context use the quiet desktop secondary column
            to reduce scrolling. Desktop keeps its locked order
            (Record details above Activity, slice 5, 2026-08-23) — unchanged. Mobile's locked
            canvas order (Slice 3, 2026-08-26) puts Activity above Record details, so the two
            blocks below swap only when !isWide. Each still self-hides/collapses exactly as
            before; only their relative order changes. */}
          <aside data-request-work-canvas-secondary className="min-w-0 space-y-5">
        {isWide ? (
          <>
            {recordDetailsBlock}
            {activityBlock}
            {visitHistoryBlock}
          </>
        ) : (
          <>
            {activityBlock}
            {recordDetailsBlock}
          </>
        )}
          </aside>
        </div>
      </div>
    </div>
  );
}
