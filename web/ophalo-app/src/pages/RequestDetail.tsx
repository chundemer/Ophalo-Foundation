import { useState, useMemo, useRef, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Phone, X } from "lucide-react";
import QRCode from "react-qr-code";
import {
  api,
  ApiError,
  type KeepRequestDetailResult,
  type UpdateServiceLocationBody,
} from "../lib/apiClient";
import { NeedsShareBanner } from "../components/NeedsShareBanner";
import { ShareLinkModal } from "../components/ShareLinkModal";
import { QuickCapture } from "../components/QuickCapture";
import { KeepButton } from "../components/keep/KeepButton";
import { ExternalContactForm } from "../components/ExternalContactForm";
import {
  FOCUS_RING,
  STATUS_CONFLICT_MESSAGE,
  ALWAYS_HIDDEN_EVENT_TYPES,
} from "./request-detail/helpers";
import {
  type AttentionHighlights,
  getAttentionResolutionHighlights,
} from "./request-detail/highlights";
import { type TimelineFilter, isCommunicationEvent, TimelineEvent } from "./request-detail/TimelineEvent";
import { TodayPromiseBanner, DetailHero } from "./request-detail/DetailHero";
import { FollowUpResolutionPanel } from "./request-detail/FollowUpResolutionPanel";
import { UnifiedComposer } from "./request-detail/UnifiedComposer";
import {
  OriginalRequestCard,
  AttentionGuidanceCard,
  ProminentFeedbackCard,
} from "./request-detail/DetailPanels";
import { CustomerContactStrip } from "./request-detail/CustomerContactStrip";
import { RequestDetailDesktopLayout } from "./request-detail/RequestDetailDesktopLayout";
import {
  RequestDetailMobileActions,
  RequestDetailMobileContext,
} from "./request-detail/RequestDetailMobileLayout";

// ---------------------------------------------------------------------------
// Log external contact modal — controller-owned overlay
// ---------------------------------------------------------------------------

interface LogContactModalProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  initialDirection: string;
  initialChannel: string;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

function LogContactModal({
  requestId,
  detail,
  initialDirection,
  initialChannel,
  onDetailUpdated,
  onClose,
}: LogContactModalProps) {
  const [channel, setChannel] = useState(initialChannel);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [phoneCopied, setPhoneCopied] = useState(false);
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    dialogRef.current?.focus();
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") { e.preventDefault(); onClose(); }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const showPhone = channel === "phone" && !!detail.customerPhone;

  async function handleSubmit(body: Parameters<typeof api.logExternalContact>[1]) {
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const updated = await api.logExternalContact(requestId, body, detail.version);
      onDetailUpdated(updated);
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not save contact log. Try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      onClick={onClose}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="log-contact-dialog-heading"
        tabIndex={-1}
        className="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-md p-5 focus:outline-none"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-1">
          <h2 id="log-contact-dialog-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">Log external contact</h2>
          <button
            type="button"
            onClick={onClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
        <p className="text-xs text-[var(--ophalo-muted)] mb-4">
          Save a Keep record of this contact. Opening the phone app is a separate step.
        </p>

        {showPhone && (
          <div className="flex flex-col gap-2 mb-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
            <div className="flex flex-wrap items-center gap-3">
              <span className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
                <Phone className="h-3.5 w-3.5 text-[var(--keep-accent)] shrink-0" />
                {detail.customerPhone}
              </span>
              <div className="flex items-center gap-2 ml-auto">
                <button
                  type="button"
                  onClick={async () => {
                    await navigator.clipboard.writeText(detail.customerPhone!);
                    setPhoneCopied(true);
                    setTimeout(() => setPhoneCopied(false), 2000);
                  }}
                  className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
                >
                  {phoneCopied ? "Copied!" : "Copy"}
                </button>
                {/* Mobile: direct tel: link (ADR-443) */}
                <span className="md:hidden text-[var(--ophalo-border)]">·</span>
                <a
                  href={`tel:${detail.customerPhone}`}
                  className={`md:hidden text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
                >
                  Call with phone app
                </a>
              </div>
            </div>
            {/* Desktop: QR handoff instead of direct tel: (ADR-443) */}
            <div className="hidden md:flex flex-col items-center gap-1.5 pt-2 border-t border-[var(--ophalo-border)]">
              <div className="bg-white p-2 rounded-lg">
                <QRCode value={`tel:${detail.customerPhone!}`} size={108} />
              </div>
              <p className="text-xs text-[var(--ophalo-muted)]">Scan to call with your phone</p>
            </div>
          </div>
        )}

        <ExternalContactForm
          initialDirection={initialDirection as "outbound" | "inbound"}
          initialChannel={initialChannel}
          maxSummaryLength={detail.validation.externalContactSummaryMaxLength}
          loading={isSubmitting}
          disabled={conflictDisabled}
          error={error}
          onSubmit={(body) => void handleSubmit(body)}
          onCancel={onClose}
          onChannelChange={setChannel}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Service location modal — controller-owned overlay
// ---------------------------------------------------------------------------

const US_STATES: [string, string][] = [
  ["AL","Alabama"],["AK","Alaska"],["AZ","Arizona"],["AR","Arkansas"],["CA","California"],
  ["CO","Colorado"],["CT","Connecticut"],["DE","Delaware"],["DC","Washington DC"],["FL","Florida"],
  ["GA","Georgia"],["HI","Hawaii"],["ID","Idaho"],["IL","Illinois"],["IN","Indiana"],
  ["IA","Iowa"],["KS","Kansas"],["KY","Kentucky"],["LA","Louisiana"],["ME","Maine"],
  ["MD","Maryland"],["MA","Massachusetts"],["MI","Michigan"],["MN","Minnesota"],["MS","Mississippi"],
  ["MO","Missouri"],["MT","Montana"],["NE","Nebraska"],["NV","Nevada"],["NH","New Hampshire"],
  ["NJ","New Jersey"],["NM","New Mexico"],["NY","New York"],["NC","North Carolina"],["ND","North Dakota"],
  ["OH","Ohio"],["OK","Oklahoma"],["OR","Oregon"],["PA","Pennsylvania"],["RI","Rhode Island"],
  ["SC","South Carolina"],["SD","South Dakota"],["TN","Tennessee"],["TX","Texas"],["UT","Utah"],
  ["VT","Vermont"],["VA","Virginia"],["WA","Washington"],["WV","West Virginia"],["WI","Wisconsin"],
  ["WY","Wyoming"],
];

interface ServiceLocationModalProps {
  requestId: string;
  detail: KeepRequestDetailResult;
  onDetailUpdated: (updated: KeepRequestDetailResult) => void;
  onClose: () => void;
}

function ServiceLocationModal({ requestId, detail, onDetailUpdated, onClose }: ServiceLocationModalProps) {
  const [addressLine1, setAddressLine1] = useState(detail.serviceAddressLine1 ?? "");
  const [addressLine2, setAddressLine2] = useState(detail.serviceAddressLine2 ?? "");
  const [city, setCity] = useState(detail.serviceCity ?? "");
  const [state, setState] = useState(detail.serviceState ?? "");
  const [zip, setZip] = useState(detail.serviceZip ?? "");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") { e.preventDefault(); onClose(); }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const isEditing = !!(detail.serviceAddressLine1 || detail.serviceCity);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || conflictDisabled) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const body: UpdateServiceLocationBody = {
        addressLine1: addressLine1.trim(),
        city: city.trim(),
        state: state.trim(),
      };
      if (addressLine2.trim()) body.addressLine2 = addressLine2.trim();
      if (zip.trim()) body.zip = zip.trim();
      const updated = await api.updateServiceLocation(requestId, body, detail.version);
      onDetailUpdated(updated);
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflictDisabled(true);
        setError(STATUS_CONFLICT_MESSAGE);
      } else {
        setError("Could not save location. Check fields and try again.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  const inputCls = `w-full rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2 text-sm text-[var(--ophalo-ink)] placeholder:text-[var(--ophalo-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--keep-accent)] focus:border-transparent ${FOCUS_RING}`;
  const labelCls = "block text-xs font-medium text-[var(--ophalo-muted)] mb-1";

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="service-location-dialog-heading"
        className="bg-[var(--ophalo-card)] rounded-xl shadow-xl w-full max-w-md p-5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h2 id="service-location-dialog-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
            {isEditing ? "Edit service location" : "Add service location"}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label htmlFor="sl-line1" className={labelCls}>
              Address line 1 <span className="text-[var(--ophalo-attention)]">*</span>
            </label>
            <input
              id="sl-line1"
              type="text"
              className={inputCls}
              value={addressLine1}
              onChange={(e) => setAddressLine1(e.target.value)}
              placeholder="123 Main St"
              required
              autoFocus
            />
          </div>
          <div>
            <label htmlFor="sl-line2" className={labelCls}>Address line 2</label>
            <input
              id="sl-line2"
              type="text"
              className={inputCls}
              value={addressLine2}
              onChange={(e) => setAddressLine2(e.target.value)}
              placeholder="Apt, unit, suite (optional)"
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="sl-city" className={labelCls}>
                City <span className="text-[var(--ophalo-attention)]">*</span>
              </label>
              <input
                id="sl-city"
                type="text"
                className={inputCls}
                value={city}
                onChange={(e) => setCity(e.target.value)}
                placeholder="City"
                required
              />
            </div>
            <div>
              <label htmlFor="sl-zip" className={labelCls}>ZIP</label>
              <input
                id="sl-zip"
                type="text"
                className={inputCls}
                value={zip}
                onChange={(e) => setZip(e.target.value)}
                placeholder="00000 (optional)"
                inputMode="numeric"
              />
            </div>
          </div>
          <div>
            <label htmlFor="sl-state" className={labelCls}>
              State <span className="text-[var(--ophalo-attention)]">*</span>
            </label>
            <select
              id="sl-state"
              className={inputCls}
              value={state}
              onChange={(e) => setState(e.target.value)}
              required
            >
              <option value="">Select state…</option>
              {US_STATES.map(([code, name]) => (
                <option key={code} value={code}>{name}</option>
              ))}
            </select>
          </div>

          {error && (
            <p className="text-xs text-[var(--ophalo-danger)]">{error}</p>
          )}

          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={onClose}
              className={`px-3 py-1.5 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded-md ${FOCUS_RING}`}
            >
              Cancel
            </button>
            <KeepButton
              type="submit"
              disabled={isSubmitting || conflictDisabled}
              className="min-h-[34px] px-4 py-1.5 text-sm"
            >
              {isSubmitting ? "Saving…" : "Save location"}
            </KeepButton>
          </div>
        </form>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Loading skeleton
// ---------------------------------------------------------------------------

function RequestDetailSkeleton() {
  const pulse = "animate-pulse motion-reduce:animate-none rounded bg-[var(--ophalo-canvas)]";
  return (
    <div
      aria-busy="true"
      aria-label="Loading request details"
      className="flex flex-1 min-h-0 overflow-hidden md:grid md:[grid-template-columns:minmax(0,7fr)_minmax(320px,3fr)]"
    >
      <div className="flex-1 md:flex-none overflow-y-auto px-4 md:px-6 py-5 space-y-4">
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5">
          <div className="flex gap-2 mb-3">
            <div className={`h-5 w-16 ${pulse}`} />
            <div className={`h-5 w-24 ${pulse}`} />
          </div>
          <div className={`h-8 w-56 ${pulse}`} />
        </div>
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4 space-y-3">
          <div className="flex gap-2">
            <div className={`h-8 w-36 ${pulse}`} />
            <div className={`h-8 w-28 ${pulse}`} />
          </div>
          <div className={`h-24 w-full ${pulse}`} />
        </div>
        <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-4 space-y-3">
          <div className={`h-4 w-20 ${pulse}`} />
          <div className={`h-3 w-48 ${pulse}`} />
        </div>
      </div>
      <div className="hidden md:flex md:flex-col border-l border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-4 py-5 gap-4">
        <div className={`h-24 w-full ${pulse}`} />
        <div className={`h-16 w-full ${pulse}`} />
        <div className={`h-16 w-full ${pulse}`} />
      </div>
    </div>
  );
}

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
}

export function RequestDetail({ requestId, focusPanel, onBack, prevId, nextId, onNavigate }: RequestDetailProps) {
  const [shareCleared, setShareCleared] = useState(false);
  const [shareModalOpen, setShareModalOpen] = useState(false);
  const [followUpPanelOpen, setFollowUpPanelOpen] = useState(false);
  const [followUpCaptureOpen, setFollowUpCaptureOpen] = useState(false);
  const [serviceLocationModalOpen, setServiceLocationModalOpen] = useState(false);
  const [contactModal, setContactModal] = useState<{ direction: string; channel: string } | null>(null);
  const [businessUpdateDraft, setBusinessUpdateDraft] = useState("");
  const [businessUpdateDraftStatus, setBusinessUpdateDraftStatus] = useState("");
  const [timelineFilter, setTimelineFilter] = useState<TimelineFilter>("communication");
  const [reviewSuccessMsg, setReviewSuccessMsg] = useState<string | null>(null);
  const reviewSuccessTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const queryClient = useQueryClient();

  const lastFocusRef = useRef<HTMLElement | null>(null);
  const serviceLocationFocusRef = useRef<HTMLElement | null>(null);

  const { data: detail, isLoading, isError, isFetching, error, refetch } = useQuery({
    queryKey: ["request-detail", requestId],
    queryFn: () => api.getRequestDetail(requestId),
  });

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

  function handleShareCleared() {
    setShareCleared(true);
    setShareModalOpen(false);
    void queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] });
  }

  function handleDetailUpdated(updated: KeepRequestDetailResult) {
    queryClient.setQueryData(["request-detail", requestId], updated);
    setShareCleared(false);
  }

  function handleContactLaunched(direction: string, channel: string) {
    lastFocusRef.current = document.activeElement as HTMLElement;
    setContactModal({ direction, channel });
  }

  function handleOpenServiceLocation() {
    serviceLocationFocusRef.current = document.activeElement as HTMLElement;
    setServiceLocationModalOpen(true);
  }

  const filterBtnCls = (active: boolean) =>
    `flex-1 px-3 py-1.5 text-xs font-semibold transition-colors ${FOCUS_RING} ${
      active
        ? "bg-[var(--ophalo-navy)] text-white"
        : "bg-[var(--ophalo-card)] text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)]"
    }`;

  return (
    <div className="flex flex-col h-full bg-[var(--ophalo-canvas)]">
      {/* Controller-owned overlays */}
      {contactModal && detail && (
        <LogContactModal
          requestId={requestId}
          detail={detail}
          initialDirection={contactModal.direction}
          initialChannel={contactModal.channel}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => { setContactModal(null); lastFocusRef.current?.focus(); }}
        />
      )}
      {serviceLocationModalOpen && detail && (
        <ServiceLocationModal
          requestId={requestId}
          detail={detail}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => { setServiceLocationModalOpen(false); serviceLocationFocusRef.current?.focus(); }}
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
            description: `Follow-up to closed request ${detail.referenceCode}: ${detail.description}`,
          }}
        />
      )}

      {/* Mobile NeedsShare banner */}
      {detail && needsShareEffective && canShare && (
        <NeedsShareBanner onOpenShareDrawer={() => setShareModalOpen(true)} />
      )}

      {/* Back breadcrumb + queue navigation */}
      <div className="flex items-center gap-2 px-4 py-3 bg-[var(--ophalo-card)] border-b border-[var(--ophalo-border)] shrink-0">
        <button
          type="button"
          onClick={onBack}
          className={`flex items-center gap-1 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] -ml-1 transition-colors ${FOCUS_RING}`}
        >
          <ChevronLeft className="h-4 w-4" />
          Requests
        </button>
        {detail && (
          <span className="text-sm text-[var(--ophalo-muted)] font-mono ml-1">
            {detail.referenceCode}
          </span>
        )}
        {onNavigate && (prevId !== undefined || nextId !== undefined) && (
          <div className="ml-auto flex items-center gap-1">
            <button
              type="button"
              disabled={!prevId}
              onClick={() => prevId && onNavigate(prevId)}
              aria-label="Previous request"
              className={`flex items-center gap-0.5 px-2 py-1 text-xs font-medium rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-40 disabled:cursor-not-allowed transition-colors ${FOCUS_RING}`}
            >
              <ChevronLeft className="h-3.5 w-3.5" />
              Prev
            </button>
            <button
              type="button"
              disabled={!nextId}
              onClick={() => nextId && onNavigate(nextId)}
              aria-label="Next request"
              className={`flex items-center gap-0.5 px-2 py-1 text-xs font-medium rounded text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] disabled:opacity-40 disabled:cursor-not-allowed transition-colors ${FOCUS_RING}`}
            >
              Next
              <ChevronRight className="h-3.5 w-3.5" />
            </button>
          </div>
        )}
      </div>

      {/* Loading skeleton */}
      {isLoading && <RequestDetailSkeleton />}

      {/* Error */}
      {isError && (
        <div className="flex flex-1 flex-col items-center justify-center gap-3 px-4">
          <span className="text-[var(--ophalo-muted)] text-sm text-center">
            {error instanceof ApiError && error.status === 403
              ? "You don't have access to this request."
              : error instanceof ApiError && error.status === 404
                ? "Request not found."
                : "Something went wrong loading this request."}
          </span>
          {!(error instanceof ApiError && (error.status === 403 || error.status === 404)) && (
            <button
              type="button"
              onClick={() => void refetch()}
              disabled={isFetching}
              className={`px-4 py-2 text-sm font-semibold rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] text-[var(--ophalo-ink)] hover:bg-[var(--ophalo-canvas)] transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${FOCUS_RING}`}
            >
              {isFetching ? "Retrying…" : "Retry"}
            </button>
          )}
        </div>
      )}

      {/* Main content */}
      {detail && (
        <div className="flex flex-1 min-h-0 overflow-hidden md:grid md:[grid-template-columns:minmax(0,7fr)_minmax(320px,3fr)]">
          {/* Left / main column — shared content with mobile layout injections */}
          <div className="flex-1 md:flex-none overflow-y-auto px-4 md:px-6 py-5 space-y-4">
            <TodayPromiseBanner
              detail={detail}
              onRecordFollowUp={() => setFollowUpPanelOpen(true)}
            />

            <DetailHero
              detail={detail}
              canRecordShareIntent={canShare}
              needsShare={needsShareEffective}
              onOpenShareDrawer={() => setShareModalOpen(true)}
            />

            <CustomerContactStrip
              phone={detail.customerPhone ?? null}
              email={detail.customerEmail ?? null}
              customerName={detail.customerName}
              pageToken={detail.pageToken ?? null}
              onContactLaunched={handleContactLaunched}
            />

            <OriginalRequestCard detail={detail} />

            <AttentionGuidanceCard detail={detail} highlights={highlights} />

            {/* Mobile: primary actions before the composer */}
            <div className="md:hidden space-y-4">
              <RequestDetailMobileActions
                requestId={requestId}
                detail={detail}
                highlights={highlights}
                showProminentFeedbackCard={showProminentFeedbackCard}
                onDetailUpdated={handleDetailUpdated}
                onContactLaunched={handleContactLaunched}
                onEditLocation={handleOpenServiceLocation}
                onRecordFollowUp={() => setFollowUpPanelOpen(true)}
                onCreateFollowUp={() => setFollowUpCaptureOpen(true)}
                onReviewSuccess={handleReviewSuccess}
              />
            </div>

            {/* Unified composer */}
            <div id="focus-panel-update">
              <UnifiedComposer
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
                customerUpdateDraft={businessUpdateDraft}
                onCustomerUpdateDraftChange={setBusinessUpdateDraft}
                customerUpdateDraftStatus={businessUpdateDraftStatus}
                onCustomerUpdateDraftStatusChange={setBusinessUpdateDraftStatus}
                highlight={highlights.sendUpdate}
              />
            </div>

            {/* Prominent feedback — only when opened from Feedback Review with unreviewed negative feedback */}
            {showProminentFeedbackCard && (
              <ProminentFeedbackCard
                requestId={requestId}
                detail={detail}
                onDetailUpdated={handleDetailUpdated}
                onReviewSuccess={handleReviewSuccess}
              />
            )}

            {/* Inline review success toast */}
            {reviewSuccessMsg && (
              <div
                role="status"
                aria-live="polite"
                className="rounded-xl border border-[var(--ophalo-success)] bg-[var(--ophalo-success-bg)] px-4 py-3 text-sm text-[var(--ophalo-success)] font-medium"
              >
                {reviewSuccessMsg}
              </div>
            )}

            {/* Activity timeline */}
            <div className="rounded-xl border border-[var(--ophalo-border)] bg-[var(--ophalo-card)] px-5 py-5">
              <div className="flex items-center justify-between mb-4 gap-3">
                <p className="text-base font-semibold text-[var(--ophalo-ink)] shrink-0">Activity</p>
                <div
                  className="flex rounded-lg border border-[var(--ophalo-border)] overflow-hidden shrink-0"
                  role="group"
                  aria-label="Activity filter"
                >
                  <button
                    type="button"
                    aria-pressed={timelineFilter === "communication"}
                    onClick={() => setTimelineFilter("communication")}
                    className={filterBtnCls(timelineFilter === "communication")}
                  >
                    Conversation &amp; notes
                  </button>
                  <button
                    type="button"
                    aria-pressed={timelineFilter === "all"}
                    onClick={() => setTimelineFilter("all")}
                    className={`border-l border-[var(--ophalo-border)] ${filterBtnCls(timelineFilter === "all")}`}
                  >
                    All activity
                  </button>
                </div>
              </div>

              {displayedEvents.length === 0 ? (
                <p className="text-sm text-[var(--ophalo-muted)]">
                  {timelineFilter === "communication"
                    ? "No customer updates or internal notes yet."
                    : "No activity yet."}
                </p>
              ) : (
                <div className="relative space-y-2 border-l border-[var(--ophalo-border)] pl-3 ml-4">
                  {displayedEvents.map((event, idx) => (
                    <TimelineEvent key={event.id} event={event} isFirst={idx === 0} />
                  ))}
                </div>
              )}
            </div>

            {/* Mobile: context panels after the timeline */}
            <div className="md:hidden space-y-4 pb-6">
              <RequestDetailMobileContext
                requestId={requestId}
                detail={detail}
                highlights={highlights}
                showProminentFeedbackCard={showProminentFeedbackCard}
                onDetailUpdated={handleDetailUpdated}
                onContactLaunched={handleContactLaunched}
                onEditLocation={handleOpenServiceLocation}
                onRecordFollowUp={() => setFollowUpPanelOpen(true)}
                onCreateFollowUp={() => setFollowUpCaptureOpen(true)}
                onReviewSuccess={handleReviewSuccess}
              />
            </div>
          </div>

          {/* Desktop sidebar */}
          <RequestDetailDesktopLayout
            requestId={requestId}
            detail={detail}
            highlights={highlights}
            showProminentFeedbackCard={showProminentFeedbackCard}
            onDetailUpdated={handleDetailUpdated}
            onContactLaunched={handleContactLaunched}
            onEditLocation={handleOpenServiceLocation}
            onRecordFollowUp={() => setFollowUpPanelOpen(true)}
            onCreateFollowUp={() => setFollowUpCaptureOpen(true)}
            onReviewSuccess={handleReviewSuccess}
          />
        </div>
      )}
    </div>
  );
}
