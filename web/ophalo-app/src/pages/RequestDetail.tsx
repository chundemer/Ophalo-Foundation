import { useState, useMemo, useRef, useEffect, useCallback } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Phone, X, RefreshCw } from "lucide-react";
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
import { formatNaPhone } from "../components/quick-capture/utils";
import { useCopyFeedback } from "../hooks/useCopyFeedback";
import {
  FOCUS_RING,
  STATUS_CONFLICT_MESSAGE,
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
import { ResponsiveSheet } from "../components/keep/ResponsiveSheet";
import { CallHandoffQr } from "./request-detail/CallHandoffQr";
import { useHandoffMint } from "./request-detail/useHandoffMint";
import { RequestDetailHeader } from "./request-detail/RequestDetailHeader";
import { RequestDetailStates } from "./request-detail/RequestDetailStates";
import { RequestDetailContent } from "./request-detail/RequestDetailContent";

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

export function LogContactModal({
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
  const [dirty, setDirty] = useState(false);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);
  const { copiedId: phoneCopyState, failedId: phoneCopyFailed, copy: copyPhone } = useCopyFeedback();

  function attemptClose() {
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  useEffect(() => {
    if (!showDiscardConfirm) return;
    previousFocusRef.current = document.activeElement;
    keepEditingRef.current?.focus();

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        setShowDiscardConfirm(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = keepEditingRef.current;
      const last = discardRef.current;
      if (!first || !last) return;
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      const prior = previousFocusRef.current;
      if (prior instanceof HTMLElement) prior.focus();
    };
  }, [showDiscardConfirm]);

  const showPhone = channel === "phone" && !!detail.customerPhone;
  const showSms = channel === "sms" && !!detail.customerPhone;
  const showEmail = channel === "email" && !!detail.customerEmail;
  const publicBaseUrl = ((import.meta.env.VITE_PUBLIC_BASE_URL as string | undefined) ?? "").replace(/\/$/, "");
  const customerPageUrl = detail.pageToken ? `${publicBaseUrl}/keep/r/${detail.pageToken}` : null;
  const directMessage = customerPageUrl
    ? `${detail.businessName}: Regarding your request, please see ${customerPageUrl}`
    : `${detail.businessName}: Regarding your request.`;

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
    <ResponsiveSheet
      onClose={attemptClose}
      labelledBy="log-contact-dialog-heading"
      contentInert={showDiscardConfirm}
      header={
        <div className="flex items-center justify-between">
          <h2 id="log-contact-dialog-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">Contact customer</h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
      }
      overlay={
        showDiscardConfirm && (
          <div
            role="alertdialog"
            aria-modal="true"
            aria-label="Discard changes"
            className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
          >
            <div className="max-w-xs w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
              <p className="text-sm text-[var(--ophalo-ink)]">Discard this contact log?</p>
              <div className="flex items-center justify-end gap-3">
                <button
                  ref={keepEditingRef}
                  type="button"
                  onClick={() => setShowDiscardConfirm(false)}
                  className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
                >
                  Keep editing
                </button>
                <button
                  ref={discardRef}
                  type="button"
                  onClick={onClose}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium bg-[var(--ophalo-danger)] text-white hover:opacity-90 ${FOCUS_RING}`}
                >
                  Discard
                </button>
              </div>
            </div>
          </div>
        )
      }
    >
      <p className="text-xs text-[var(--ophalo-muted)] mb-4">
        Use your phone to call or text, then record what actually happened. Opening a call,
        text, or email draft does not update Keep.
      </p>

      {showPhone && (
        <div className="flex flex-col gap-2 mb-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
          <div className="flex flex-wrap items-center gap-3">
            <span className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
              <Phone className="h-3.5 w-3.5 text-[var(--keep-accent)] shrink-0" />
              {formatNaPhone(detail.customerPhone)}
            </span>
            <div className="flex items-center gap-2 ml-auto">
              <button
                type="button"
                onClick={() => void copyPhone(detail.customerPhone!, "phone")}
                className={`text-xs text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors ${FOCUS_RING}`}
              >
                {phoneCopyState === "phone" ? "Copied!" : phoneCopyFailed === "phone" ? "Couldn't copy" : "Copy"}
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
          {/* Desktop: QR handoff instead of direct tel: (ADR-443, GAP-020) */}
          <div className="hidden md:flex flex-col items-center gap-1.5 pt-2 border-t border-[var(--ophalo-border)]">
            <CallHandoffQr requestId={requestId} size={108} caption="Scan to call with your phone" />
          </div>
        </div>
      )}

      {showSms && detail.customerPhone && (
        <div className="flex flex-col gap-2 mb-4 rounded-lg border border-[var(--ophalo-border)] bg-[var(--ophalo-canvas)] px-3 py-2.5">
          <div className="flex flex-wrap items-center gap-3">
            <span className="flex items-center gap-1.5 text-sm font-semibold text-[var(--ophalo-ink)]">
              {formatNaPhone(detail.customerPhone)}
            </span>
            <span className="ml-auto text-xs text-[var(--ophalo-muted)]">Text includes the request-page link</span>
          </div>
          <div className="hidden md:flex flex-col items-center gap-1.5 pt-2 border-t border-[var(--ophalo-border)]">
            <SmsHandoffQr requestId={requestId} message={directMessage} />
            <p className="text-xs text-[var(--ophalo-muted)] text-center">Scan to open the text draft on your phone.</p>
          </div>
          <a
            href={`sms:${detail.customerPhone}?&body=${encodeURIComponent(directMessage)}`}
            className={`md:hidden inline-flex items-center justify-center rounded-lg border-2 border-[var(--ophalo-navy)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-navy)] ${FOCUS_RING}`}
          >
            Open text draft
          </a>
        </div>
      )}

      {showEmail && detail.customerEmail && (
        <a
          href={`mailto:${detail.customerEmail}?subject=${encodeURIComponent("Regarding your request")}&body=${encodeURIComponent(directMessage)}`}
          className={`mb-4 inline-flex w-full items-center justify-center rounded-lg border-2 border-[var(--ophalo-navy)] px-4 py-2 text-sm font-semibold text-[var(--ophalo-navy)] ${FOCUS_RING}`}
        >
          Open email draft with request link
        </a>
      )}

      <ExternalContactForm
        initialDirection={initialDirection as "outbound" | "inbound"}
        initialChannel={initialChannel}
        maxSummaryLength={detail.validation.externalContactSummaryMaxLength}
        loading={isSubmitting}
        disabled={conflictDisabled}
        error={error}
        onSubmit={(body) => void handleSubmit(body)}
        onCancel={attemptClose}
        onChannelChange={setChannel}
        onDirtyChange={setDirty}
      />
    </ResponsiveSheet>
  );
}

function SmsHandoffQr({ requestId, message }: { requestId: string; message: string }) {
  const mint = useCallback(() => api.createSmsHandoff(requestId, message), [requestId, message]);
  const { handoffUrl, isLoading, error, retry } = useHandoffMint(
    true,
    mint,
    "Could not create text link. Try again.",
  );

  if (isLoading) {
    return (
      <div
        className="flex items-center justify-center"
        style={{ height: 108, width: 108 }}
        role="status"
        aria-label="Preparing text link"
      >
        <RefreshCw className="h-5 w-5 animate-spin text-[var(--ophalo-muted)]" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col items-center gap-2 text-center" style={{ width: 108 }}>
        <p className="text-xs text-[var(--ophalo-danger)]">{error}</p>
        <button
          type="button"
          onClick={() => void retry()}
          className="text-xs font-medium text-[var(--keep-accent)] hover:underline"
        >
          Try again
        </button>
      </div>
    );
  }

  if (!handoffUrl) return null;
  return <div className="bg-white p-2 rounded-lg"><QRCode value={handoffUrl} size={108} /></div>;
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

export function ServiceLocationModal({ requestId, detail, onDetailUpdated, onClose }: ServiceLocationModalProps) {
  const [addressLine1, setAddressLine1] = useState(detail.serviceAddressLine1 ?? "");
  const [addressLine2, setAddressLine2] = useState(detail.serviceAddressLine2 ?? "");
  const [city, setCity] = useState(detail.serviceCity ?? "");
  const [state, setState] = useState(detail.serviceState ?? "");
  const [zip, setZip] = useState(detail.serviceZip ?? "");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [conflictDisabled, setConflictDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);
  const keepEditingRef = useRef<HTMLButtonElement>(null);
  const discardRef = useRef<HTMLButtonElement>(null);
  const previousFocusRef = useRef<Element | null>(null);

  const isEditing = !!(detail.serviceAddressLine1 || detail.serviceCity);

  const dirty =
    addressLine1 !== (detail.serviceAddressLine1 ?? "") ||
    addressLine2 !== (detail.serviceAddressLine2 ?? "") ||
    city !== (detail.serviceCity ?? "") ||
    state !== (detail.serviceState ?? "") ||
    zip !== (detail.serviceZip ?? "");

  function attemptClose() {
    if (dirty) {
      setShowDiscardConfirm(true);
      return;
    }
    onClose();
  }

  useEffect(() => {
    if (!showDiscardConfirm) return;
    previousFocusRef.current = document.activeElement;
    keepEditingRef.current?.focus();

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        setShowDiscardConfirm(false);
        return;
      }
      if (e.key !== "Tab") return;
      e.preventDefault();
      e.stopPropagation();
      const first = keepEditingRef.current;
      const last = discardRef.current;
      if (!first || !last) return;
      (document.activeElement === first ? last : first).focus();
    }
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      const prior = previousFocusRef.current;
      if (prior instanceof HTMLElement) prior.focus();
    };
  }, [showDiscardConfirm]);

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
    <ResponsiveSheet
      onClose={attemptClose}
      labelledBy="service-location-dialog-heading"
      contentInert={showDiscardConfirm}
      header={
        <div className="flex items-center justify-between">
          <h2 id="service-location-dialog-heading" className="text-base font-semibold text-[var(--ophalo-ink)]">
            {isEditing ? "Edit service location" : "Add service location"}
          </h2>
          <button
            type="button"
            onClick={attemptClose}
            className={`text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] p-1 rounded-md transition-colors ${FOCUS_RING}`}
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </button>
        </div>
      }
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={attemptClose}
            className={`px-3 py-1.5 text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] transition-colors rounded-md ${FOCUS_RING}`}
          >
            Cancel
          </button>
          <KeepButton
            type="submit"
            form="service-location-form"
            disabled={isSubmitting || conflictDisabled}
            className="min-h-[34px] px-4 py-1.5 text-sm"
          >
            {isSubmitting ? "Saving…" : "Save location"}
          </KeepButton>
        </div>
      }
      overlay={
        showDiscardConfirm && (
          <div
            role="alertdialog"
            aria-modal="true"
            aria-label="Discard changes"
            className="absolute inset-0 z-10 flex items-center justify-center bg-black/30 px-6"
          >
            <div className="max-w-xs w-full rounded-lg bg-[var(--ophalo-card)] shadow-xl p-4 flex flex-col gap-3">
              <p className="text-sm text-[var(--ophalo-ink)]">Discard your changes to this location?</p>
              <div className="flex items-center justify-end gap-3">
                <button
                  ref={keepEditingRef}
                  type="button"
                  onClick={() => setShowDiscardConfirm(false)}
                  className={`text-sm text-[var(--ophalo-muted)] hover:text-[var(--ophalo-ink)] rounded ${FOCUS_RING}`}
                >
                  Keep editing
                </button>
                <button
                  ref={discardRef}
                  type="button"
                  onClick={onClose}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium bg-[var(--ophalo-danger)] text-white hover:opacity-90 ${FOCUS_RING}`}
                >
                  Discard
                </button>
              </div>
            </div>
          </div>
        )
      }
    >
        <form id="service-location-form" onSubmit={handleSubmit} className="space-y-3">
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
        </form>
    </ResponsiveSheet>
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
  // BL136 4f-i: opens the dedicated Actual Work Ticket Workspace route from the capture entry
  // point. Set on wide screens only; undefined below 1001px, where capture stays a full-bleed
  // modal on this page.
  onNavigateToActualWorkspace?: (requestId: string, visit?: "new" | "draft") => void;
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
  const [businessUpdateDraft, setBusinessUpdateDraft] = useState("");
  const [businessUpdateDraftStatus, setBusinessUpdateDraftStatus] = useState("");
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
    setReviewSuccessMsg("Visit marked as reviewed.");
    reviewSuccessTimerRef.current = setTimeout(() => setReviewSuccessMsg(null), 4000);
    void queryClient.invalidateQueries({ queryKey: ["actual-work-review-queue"] });
    void queryClient.invalidateQueries({ queryKey: ["actual-work-review-queue-count"] });
  }

  function handleShareCleared() {
    setShareCleared(true);
    setShareModalOpen(false);
    void queryClient.invalidateQueries({ queryKey: ["request-detail", requestId] });
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
    <div className="flex flex-col h-full min-w-0 bg-[var(--ophalo-canvas)]">
      {/* Controller-owned overlays */}
      {contactModal && detail && (
        <LogContactModal
          requestId={requestId}
          detail={detail}
          initialDirection={contactModal.direction}
          initialChannel={contactModal.channel}
          onDetailUpdated={handleDetailUpdated}
          onClose={() => setContactModal(null)}
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
        customerUpdateDraft={businessUpdateDraft}
        onCustomerUpdateDraftChange={setBusinessUpdateDraft}
        customerUpdateDraftStatus={businessUpdateDraftStatus}
        onCustomerUpdateDraftStatusChange={setBusinessUpdateDraftStatus}
        reviewSuccessMsg={reviewSuccessMsg}
        timelineFilter={timelineFilter}
        onTimelineFilterChange={setTimelineFilter}
        displayedEvents={displayedEvents}
        onNavigate={onNavigate}
        onNavigateToActualWorkspace={onNavigateToActualWorkspace}
        canReviewActualWork={meQuery.data?.accountRole === "owner" || meQuery.data?.accountRole === "admin"}
        currentAccountUserId={meQuery.data?.accountUserId}
        focusPanel={focusPanel}
        onActualWorkReviewSuccess={handleActualWorkReviewSuccess}
      />}
    </div>
  );
}
