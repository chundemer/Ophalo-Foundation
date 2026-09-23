import { useState, useMemo, useRef, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type KeepRequestDetailResult } from "../lib/apiClient";
import { NeedsShareBanner } from "../components/NeedsShareBanner";
import { ShareLinkModal } from "../components/ShareLinkModal";
import { QuickCapture } from "../components/QuickCapture";
import { getPublicBaseUrl } from "../lib/publicBaseUrl";
import {
  ALWAYS_HIDDEN_EVENT_TYPES,
  buildFollowUpDescription,
} from "./request-detail/helpers";
import {
  type AttentionHighlights,
  getAttentionResolutionHighlights,
} from "./request-detail/highlights";
import { type TimelineFilter, isCommunicationEvent } from "./request-detail/TimelineEvent";
import { FollowUpResolutionPanel } from "./request-detail/FollowUpResolutionPanel";
import { ClearAttentionSheet } from "./request-detail/DetailPanels";
import { OwnerReassignmentSheet, WatchersSheet } from "./request-detail/TeamSection";
import { LogContactModal } from "./request-detail/LogContactModal";
import { ServiceLocationModal } from "./request-detail/ServiceLocationModal";
import { RequestDetailHeader } from "./request-detail/RequestDetailHeader";
import { RequestDetailStates } from "./request-detail/RequestDetailStates";
import { RequestDetailContent } from "./request-detail/RequestDetailContent";
import { useBusinessTimeZone } from "../hooks/useBusinessTimeZone";

// ---------------------------------------------------------------------------
// RequestDetail page — controller
// ---------------------------------------------------------------------------

interface RequestDetailProps {
  requestId: string;
  focusPanel?: string;
  onBack: () => void;
  prevId?: string;
  nextId?: string;
  onNavigate?: (id: string) => void;
  // BL136 4f-i: opens the dedicated Actual Work Ticket Workspace route from the capture entry
  // point. Set on wide screens only; undefined below 1001px, where capture stays a full-bleed
  // modal on this page.
  onNavigateToActualWorkspace?: (requestId: string, visit?: "new" | "draft" | (string & {})) => void;
  // Step 5: set only by RequestWorkbenchShell's wide two-pane render. The Queue pane already
  // supplies navigation context in that layout, so the header's Back control is redundant/
  // ambiguous there — identity and Prev/Next stay, modal behavior is untouched.
  paneMode?: boolean;
}

export function RequestDetail({ requestId, focusPanel, onBack, prevId, nextId, onNavigate, onNavigateToActualWorkspace, paneMode }: RequestDetailProps) {
  const [shareCleared, setShareCleared] = useState(false);
  const [shareModalOpen, setShareModalOpen] = useState(false);
  const [followUpPanelOpen, setFollowUpPanelOpen] = useState(false);
  const [followUpCaptureOpen, setFollowUpCaptureOpen] = useState(false);
  const [serviceLocationModalOpen, setServiceLocationModalOpen] = useState(false);
  const [contactModal, setContactModal] = useState<{ direction: string; channel: string } | null>(null);
  const [clearAttentionOpen, setClearAttentionOpen] = useState(false);
  const [reassignOwnerOpen, setReassignOwnerOpen] = useState(false);
  const [watchersOpen, setWatchersOpen] = useState(false);
  const [timelineFilter, setTimelineFilter] = useState<TimelineFilter>("communication");
  const [reviewSuccessMsg, setReviewSuccessMsg] = useState<string | null>(null);
  const reviewSuccessTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const queryClient = useQueryClient();

  useEffect(() => {
    return () => {
      if (reviewSuccessTimerRef.current) clearTimeout(reviewSuccessTimerRef.current);
    };
  }, []);

  const { data: detail, isLoading, isError, isFetching, error, refetch } = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
  });

  // GAP-042: businessName is minimal authenticated workspace-shell context, sourced from the
  // shared ["me"] cache — every role that can reach Detail (Viewer included, via direct link).
  const meQuery = useQuery({ queryKey: ["me"], queryFn: api.getMe });
  const canReadBusinessPage = meQuery.data?.accountRole === "owner" || meQuery.data?.accountRole === "admin";
  // GAP-092 1b: joins the same ["me"] cache above; RequestDetailContent stays presentational.
  const { timeZone: businessTimeZone } = useBusinessTimeZone();
  const intakeQuery = useQuery({
    queryKey: ["intake"],
    queryFn: api.getIntake,
    enabled: canReadBusinessPage,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
  const publicBaseUrl = getPublicBaseUrl();
  const businessPageUrl = intakeQuery.data?.hasActiveLink && intakeQuery.data.publicSlug
    ? `${publicBaseUrl}/keep/s/${intakeQuery.data.publicSlug}`
    : null;

  const needsShareEffective = !shareCleared && (detail?.needsShare ?? false);
  const canShare = detail?.availableActions.canRecordShareIntent ?? false;

  const displayedEvents = useMemo(() => {
    if (!detail) return [];
    const base = detail.events.filter((e) => !ALWAYS_HIDDEN_EVENT_TYPES.has(e.eventType));
    const filtered = timelineFilter === "communication" ? base.filter(isCommunicationEvent) : base;
    return [...filtered].sort((a, b) => {
      const byDate = new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime();
      if (byDate !== 0) return byDate;
      return b.id.localeCompare(a.id);
    });
  }, [detail, timelineFilter]);

  const focusScrolledRef = useRef(false);

  useEffect(() => {
    focusScrolledRef.current = false;
  }, [requestId]);

  useEffect(() => {
    if (!focusPanel || !detail || focusScrolledRef.current) return;
    const el = document.getElementById(`focus-panel-${focusPanel}`);
    if (el) {
      el.scrollIntoView({ behavior: "smooth", block: "nearest" });
      focusScrolledRef.current = true;
    }
  }, [focusPanel, detail]);

  const focusHighlights = useMemo((): AttentionHighlights => {
    if (!focusPanel || !detail) return {};
    switch (focusPanel) {
      case "update": return { sendUpdate: "primary" };
      case "contact": return { logContact: "primary" };
      case "attention": return { markHandled: "primary" };
      case "feedback_review": return { feedbackReview: "primary" };
      default: return {};
    }
  }, [focusPanel, detail]);

  const highlights = useMemo(() => {
    const attention = detail ? getAttentionResolutionHighlights(detail) : {};
    return {
      sendUpdate: attention.sendUpdate ?? focusHighlights.sendUpdate,
      logContact: attention.logContact ?? focusHighlights.logContact,
      workControls: attention.workControls ?? focusHighlights.workControls,
      feedbackReview: attention.feedbackReview ?? focusHighlights.feedbackReview,
      markHandled: attention.markHandled ?? focusHighlights.markHandled,
    };
  }, [detail, focusHighlights]);

  const showProminentFeedbackCard = focusPanel === "feedback_review" &&
    !!detail &&
    detail.feedbackWasResolved === false &&
    detail.feedbackReviewedAtUtc == null &&
    !!detail.availableActions.canMarkFeedbackReviewed;

  function handleReviewSuccess() {
    if (reviewSuccessTimerRef.current) clearTimeout(reviewSuccessTimerRef.current);
    setReviewSuccessMsg("Feedback marked as reviewed.");
    reviewSuccessTimerRef.current = setTimeout(() => setReviewSuccessMsg(null), 4000);
    void queryClient.invalidateQueries({ queryKey: ["requests"] });
  }

  function handleActualWorkReviewSuccess() {
    if (reviewSuccessTimerRef.current) clearTimeout(reviewSuccessTimerRef.current);
    setReviewSuccessMsg("Internal financial review completed. The customer request status is unchanged.");
    reviewSuccessTimerRef.current = setTimeout(() => setReviewSuccessMsg(null), 4000);
    void queryClient.invalidateQueries({ queryKey: ["actual-work-review-queue"] });
    void queryClient.invalidateQueries({ queryKey: ["actual-work-review-queue-count"] });
  }

  function handleShareCleared() {
    setShareCleared(true);
    setShareModalOpen(false);
    void queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] });
  }

  function handleShareIntentRecorded() {
    // GAP-048: recordShareIntent returns no updated detail — refresh both the detail cache
    // and every cached queue so the server-cleared NeedsShare cue is visible immediately,
    // not just on the current detail view.
    void queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] });
    void queryClient.invalidateQueries({ queryKey: ["requests"] });
  }

  function handleDetailUpdated(updated: KeepRequestDetailResult) {
    queryClient.setQueryData(["request-detail", requestId], updated);
    // Detail mutations can clear an attention condition, which changes membership in the
    // request queues. Invalidate every cached queue so a visible Needs Attention list removes
    // the request immediately, without requiring a manual browser refresh.
    void queryClient.invalidateQueries({ queryKey: ["requests"] });
    setShareCleared(false);
  }

  function handleContactLaunched(direction: string, channel: string) {
    setContactModal({ direction, channel });
  }

  function handleOpenServiceLocation() {
    setServiceLocationModalOpen(true);
  }

  return (
    <div className="flex flex-col h-full min-w-0 bg-[var(--keep-request-canvas)]">
      {/* Controller-owned overlays */}
      {contactModal && detail && (
        <LogContactModal
          requestId={requestId}
          detail={detail}
          initialDirection={contactModal.direction}
          initialChannel={contactModal.channel}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setContactModal(null)}
          onShareIntentRecorded={handleShareIntentRecorded}
        />
      )}
      {serviceLocationModalOpen && detail && (
        <ServiceLocationModal
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setServiceLocationModalOpen(false)}
        />
      )}
      {clearAttentionOpen && detail && (
        <ClearAttentionSheet
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setClearAttentionOpen(false)}
        />
      )}
      {reassignOwnerOpen && detail && (
        <OwnerReassignmentSheet
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setReassignOwnerOpen(false)}
        />
      )}
      {watchersOpen && detail && (
        <WatchersSheet
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setWatchersOpen(false)}
        />
      )}
      {shareModalOpen && (
        <ShareLinkModal
          requestId={requestId}
          onClose={() => setShareModalOpen(false)}
          onShared={handleShareCleared}
        />
      )}
      {followUpPanelOpen && detail && (
        <FollowUpResolutionPanel
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setFollowUpPanelOpen(false)}
        />
      )}
      {followUpCaptureOpen && detail && (
        <QuickCapture
          onClose={() => setFollowUpCaptureOpen(false)}
          followUpPrefill={{
            phone: detail.customerPhone,
            name: detail.customerName,
            email: detail.customerEmail ?? undefined,
            ...buildFollowUpDescription(
              `Follow-up to closed request ${detail.referenceCode}: `,
              detail.description,
            ),
          }}
        />
      )}

      {/* Mobile NeedsShare banner */}
      {detail && needsShareEffective && canShare && (
        <NeedsShareBanner onOpenShareDrawer={() => setShareModalOpen(true)} />
      )}

      <RequestDetailHeader onBack={onBack} showBack={!paneMode} referenceCode={detail?.referenceCode} businessName={meQuery.data?.businessName} prevId={prevId} nextId={nextId} onNavigate={onNavigate} />
      <RequestDetailStates isLoading={isLoading} isError={isError} error={error} isFetching={isFetching} onRetry={() => void refetch()} />
      {detail && <RequestDetailContent
        requestId={requestId}
        detail={detail}
        highlights={highlights}
        showProminentFeedbackCard={showProminentFeedbackCard}
        onDetailUpdated={handleDetailUpdated}
        onRefreshDetail={() => void refetch()}
        onContactLaunched={handleContactLaunched}
        onEditLocation={handleOpenServiceLocation}
        onOpenReassignOwner={() => setReassignOwnerOpen(true)}
        onOpenWatchers={() => setWatchersOpen(true)}
        onOpenClearAttention={() => setClearAttentionOpen(true)}
        onRecordFollowUp={() => setFollowUpPanelOpen(true)}
        onCreateFollowUp={() => setFollowUpCaptureOpen(true)}
        onReviewSuccess={handleReviewSuccess}
        canRecordShareIntent={canShare}
        needsShare={needsShareEffective}
        onOpenShareDrawer={() => setShareModalOpen(true)}
        reviewSuccessMsg={reviewSuccessMsg}
        timeZone={businessTimeZone}
        timelineFilter={timelineFilter}
        onTimelineFilterChange={setTimelineFilter}
        displayedEvents={displayedEvents}
        onNavigate={onNavigate}
        onNavigateToActualWorkspace={onNavigateToActualWorkspace}
        canReviewActualWork={meQuery.data?.accountRole === "owner" || meQuery.data?.accountRole === "admin"}
        currentAccountUserId={meQuery.data?.accountUserId}
        focusPanel={focusPanel}
        onActualWorkReviewSuccess={handleActualWorkReviewSuccess}
        businessPageUrl={businessPageUrl}
      />}
    </div>
  );
}
