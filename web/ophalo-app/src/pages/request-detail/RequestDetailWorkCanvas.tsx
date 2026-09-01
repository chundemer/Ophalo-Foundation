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
import { KeepButton } from "../../components/keep/KeepButton";

// RD-019A: the Work Canvas — the Workbench's sole vertical scroll surface (locked spec §1.2, §1.5,
// §5, §7.1). This component is layout-only: it owns the canvas structure and region order and
// nothing else. It fetches nothing, mutates nothing, and derives no action policy; the Actual Work,
// activity, and record-details regions arrive pre-built as props.
//
// Desktop module order: attention guidance -> Customer Need -> Actual Work context ->
// communication -> record details -> activity. Mobile (Slice 3, 2026-08-26) inserts a
// contact/service-location card after attention and swaps the last two: attention -> contact/
// location -> Customer Need -> Actual Work -> communication -> activity -> record details.
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
}: RequestDetailWorkCanvasProps) {
  return (
    <div data-request-detail-work-canvas className="flex-1 min-h-0 min-w-0 overflow-y-auto px-4 md:px-6 py-5">
      <div className="max-w-4xl mx-auto w-full space-y-3">
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
          />
          <TodayPromiseBanner detail={detail} onRecordFollowUp={onRecordFollowUp} />
        </div>

        {/* 2. Contact/service location — mobile canvas only (Slice 3, 2026-08-26); desktop
            keeps this content solely in RequestDetailAnchor/CustomerContactStrip. */}
        {!isWide && (
          <MobileContactLocationCard detail={detail} onContactLaunched={onContactLaunched} onEditLocation={onEditLocation} />
        )}

        {/* 3. Customer need — permanent, always mounted regardless of attention state
            (locked spec, 2026-08-24: decoupled from the conditional attention rail). */}
        <OriginalRequestCard detail={detail} />

        {/* 4. Work execution — Actual Work (capture, history, review, recovery), pre-built. */}
        {actualWorkSection}

        {/* 5. Communication — composer only; Follow-Up/Planned-For and priority moved to the
            Anchor's compact Internal Planning strip (locked 2026-08-24). Desktop's one Log Contact
            entry point lives in the Anchor; mobile's lives in the Contact/Location card above
            (Slice 3), not duplicated here. */}
        <div className="space-y-3">
          {showProminentFeedbackCard && <ProminentFeedbackCard requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} onReviewSuccess={onReviewSuccess} />}
          {reviewSuccessMsg && <div role="status" aria-live="polite" className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm text-[var(--ophalo-success)] font-medium">{reviewSuccessMsg}</div>}
          <div
            id="focus-panel-update"
            tabIndex={-1}
            className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)]"
          >
            <UnifiedComposer ref={composerRef} requestId={requestId} detail={detail} onDetailUpdated={onDetailUpdated} customerUpdateDraft={customerUpdateDraft} onCustomerUpdateDraftChange={onCustomerUpdateDraftChange} customerUpdateDraftStatus={customerUpdateDraftStatus} onCustomerUpdateDraftStatusChange={onCustomerUpdateDraftStatusChange} highlight={highlights.sendUpdate} bare />
          </div>
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

        {/* 6/7. Activity and lower-frequency record context. Desktop keeps its locked order
            (Record details above Activity, slice 5, 2026-08-23) — unchanged. Mobile's locked
            canvas order (Slice 3, 2026-08-26) puts Activity above Record details, so the two
            blocks below swap only when !isWide. Each still self-hides/collapses exactly as
            before; only their relative order changes. */}
        {isWide ? (
          <>
            {recordDetailsBlock}
            {activityBlock}
          </>
        ) : (
          <>
            {activityBlock}
            {recordDetailsBlock}
          </>
        )}
      </div>
    </div>
  );
}
